#!/usr/bin/env bash
set -euo pipefail

# Builds the iOS smoke-test app against a packed AntMedia.Net.iOS package, installs it on a
# simulator and runs it. The app prints its verdict to stdout and exits; this script turns that
# into an exit code.
#
# Usage: run-ios-device-tests.sh <package-version> [target-framework]
#
# Unlike the Android runner, this boots the simulator itself — there is no equivalent of the
# emulator-runner action, and simctl gives a cleaner handle on the app's stdout than mlaunch does.

VERSION="${1:?a package version is required}"
# One of the package's own target frameworks, so the smoke test proves what actually ships. It
# only builds against an Xcode carrying the matching iOS SDK; CI selects it with
# .github/scripts/select-xcode.sh, and locally you may need DEVELOPER_DIR set to the same.
TARGET_FRAMEWORK="${2:-net10.0-ios26.0}"

BUNDLE_ID="com.sbokatuk.antmedia.devicetests"
LOG_FILE="device-tests-simulator.log"

# Seconds from launch to the app's first line of output, then from that line to its exit. Two limits
# because almost all the time goes before the first line, and it varies. On GitHub's macos-15 runners
# that wait was 13s to 261s, and one run was killed at 300s with its launch still unfinished, while
# the checks took about 3s. The second limit also covers the live check, which can take 90s more.
STARTUP_TIMEOUT="${ANTMEDIA_STARTUP_TIMEOUT:-900}"
CHECKS_TIMEOUT="${ANTMEDIA_CHECKS_TIMEOUT:-300}"
# How often the log reports on an app that has not printed anything yet.
REPORT_INTERVAL=30

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="${REPO_ROOT}/tests/AntMedia.Net.iOS.DeviceTests/AntMedia.Net.iOS.DeviceTests.csproj"

if [ "$(uname -s)" != "Darwin" ]; then
    echo "::error::the iOS smoke test requires macOS" >&2
    exit 1
fi

# GitHub's macOS runners are arm64, but keep this derived rather than hard-coded so the script
# also works on an Intel Mac.
case "$(uname -m)" in
    arm64) DEVICE_RID="${ANTMEDIA_DEVICE_RID:-iossimulator-arm64}" ;;
    *)     DEVICE_RID="${ANTMEDIA_DEVICE_RID:-iossimulator-x64}" ;;
esac

case "${TARGET_FRAMEWORK}" in
    net10.0-*) sdk_major=10 ;;
    *)         sdk_major=9 ;;
esac

sdk_version="$(dotnet --list-sdks | grep "^${sdk_major}\." | tail -1 | cut -d' ' -f1)"
if [ -z "${sdk_version}" ]; then
    echo "::error::no .NET ${sdk_major} SDK installed, cannot build ${TARGET_FRAMEWORK}"
    exit 1
fi

SDK_DIR="$(mktemp -d)"
trap 'rm -rf "${SDK_DIR}"' EXIT
printf '{ "sdk": { "version": "%s", "rollForward": "latestFeature" } }\n' "${sdk_version}" \
    > "${SDK_DIR}/global.json"

# See the Android runner: NuGet would otherwise serve a cached copy of a version we just re-packed.
rm -rf "${HOME}/.nuget/packages/antmedia.net.ios/${VERSION}"

# Debug, not Release. An iOS Release build trims and AOT-compiles, which for an app carrying the
# ~27 MB WebRTC framework takes the better part of an hour on a CI runner — and buys nothing here,
# since this app is never shipped and the binding's trimming behaviour is not what the smoke test
# is checking. Debug still links the native frameworks, which is the part that matters.
#
# Held in a variable because it appears twice: the build and the path the .app is looked up under.
CONFIGURATION="Debug"

echo "==> building device tests (version=${VERSION}, tfm=${TARGET_FRAMEWORK}, rid=${DEVICE_RID})"
( cd "${SDK_DIR}" && dotnet build "${PROJECT}" \
    --configuration "${CONFIGURATION}" \
    -f "${TARGET_FRAMEWORK}" \
    -p:AntMediaPackageVersion="${VERSION}" \
    -p:AntMediaDeviceTargetFramework="${TARGET_FRAMEWORK}" \
    -p:AntMediaTrimming="${ANTMEDIA_TRIMMING:-none}" \
    -p:RuntimeIdentifier="${DEVICE_RID}" )

APP="$(find "$(dirname "${PROJECT}")/bin/${CONFIGURATION}/${TARGET_FRAMEWORK}/${DEVICE_RID}" \
    -maxdepth 1 -name '*.app' -type d | head -1)"
if [ -z "${APP}" ]; then
    echo "::error::build succeeded but no .app bundle was produced" >&2
    exit 1
fi
echo "==> built ${APP}"
APP_NAME="$(basename "${APP}" .app)"

# Newest available iPhone simulator. Pinning a specific device name would break every time the
# runner image drops that model.
UDID="$(xcrun simctl list devices available --json \
    | python3 -c '
import json, sys
devices = json.load(sys.stdin)["devices"]
candidates = [
    device
    for runtime, entries in sorted(devices.items())
    if "iOS" in runtime
    for device in entries
    if device.get("isAvailable") and "iPhone" in device["name"]
]
print(candidates[-1]["udid"] if candidates else "")
')"

if [ -z "${UDID}" ]; then
    echo "::error::no available iPhone simulator on this runner" >&2
    xcrun simctl list devices available >&2
    exit 1
fi

echo "==> booting simulator ${UDID}"
# 'boot' fails if the device is already booted, which is fine and not worth failing the run over.
xcrun simctl boot "${UDID}" 2>/dev/null || true
xcrun simctl bootstatus "${UDID}" -b

cleanup() {
    xcrun simctl shutdown "${UDID}" >/dev/null 2>&1 || true
    rm -rf "${SDK_DIR}"
}
trap cleanup EXIT

echo "==> installing"
xcrun simctl install "${UDID}" "${APP}"

# A simulator app is an ordinary host process, so this works whatever state the simulator is in.
find_app_pid() {
    pgrep -f "/Devices/${UDID}/data/Containers/Bundle/Application/.*/${APP_NAME}\.app/" | head -1 || true
}

report_progress() {
    local app_state="no app process yet"
    if [ -n "${app_pid}" ]; then
        app_state="app process ${app_pid}, $(ps -o state=,time= -p "${app_pid}" \
            | awk '{ print "state", $1, "cpu", $2 }' || echo exited)"
    fi
    echo "==> ${elapsed}s since launch, no output: ${app_state}; busiest:" \
        "$(ps -Arco pcpu=,comm= | awk 'NR <= 3 { print $2, $1 "%" }' | paste -sd ',' -)"
}

# simctl passes signals on to the app, so one that ignores SIGTERM would leave simctl waiting.
stop_launch() {
    kill -TERM "${launch_pid}" 2>/dev/null || true
    for _ in 1 2 3 4 5 6 7 8 9 10; do
        kill -0 "${launch_pid}" 2>/dev/null || return 0
        sleep 1
    done
    kill -KILL "${launch_pid}" 2>/dev/null || true
}

# The simulator's own account of the launch. CoreSimulatorBridge retries a launch that fails, so its
# attempts say where the time went before the app existed. 'Requesting launch' stays out of the
# predicate because that message spells out the app's environment.
print_launch_timeline() {
    echo "==> launch timeline"
    xcrun simctl spawn "${UDID}" log show --start "@${launch_started}" --style compact --predicate \
        "(process == 'CoreSimulatorBridge' AND (eventMessage BEGINSWITH 'Beginning launch' OR eventMessage BEGINSWITH 'Launch ' OR eventMessage BEGINSWITH 'Error Launching')) OR (process == 'SpringBoard' AND eventMessage CONTAINS '${BUNDLE_ID}' AND (eventMessage CONTAINS 'Bootstrap success' OR eventMessage CONTAINS 'Process exited'))" \
        2>/dev/null | cut -c1-200 | tee -a "${LOG_FILE}" || true
}

echo "==> launching"
# --console-pty streams the app's stdout and blocks until it exits, so the app's own
# Environment.Exit call is what ends this step. macOS has no coreutils timeout, so the guard against
# a hang before that point is the loop below, which also says what the wait was spent on.
#
# simctl buffers its own "<bundle id>: <pid>" line until it exits, so anything in the log file while
# it runs came from the app. A unix time, because `log show` reads its other formats in the
# simulator's time zone.
launch_started="$(date +%s)"
xcrun simctl launch --console-pty "${UDID}" "${BUNDLE_ID}" > "${LOG_FILE}" 2>&1 &
launch_pid=$!

app_pid=""
app_started_after=""
first_output_after=""
hang=""
next_report="${REPORT_INTERVAL}"

while kill -0 "${launch_pid}" 2>/dev/null; do
    elapsed=$(( $(date +%s) - launch_started ))

    if [ -z "${app_pid}" ]; then
        app_pid="$(find_app_pid)"
        if [ -n "${app_pid}" ]; then
            app_started_after="${elapsed}"
            echo "==> app process ${app_pid} started ${elapsed}s after launch"
        fi
    fi

    if [ -z "${first_output_after}" ]; then
        if [ -s "${LOG_FILE}" ]; then
            first_output_after="${elapsed}"
            echo "==> first output ${elapsed}s after launch"
        elif [ "${elapsed}" -ge "${STARTUP_TIMEOUT}" ]; then
            hang="no output ${elapsed}s after launch"
        elif [ "${elapsed}" -ge "${next_report}" ]; then
            report_progress
            next_report=$(( next_report + REPORT_INTERVAL ))
        fi
    elif [ $(( elapsed - first_output_after )) -ge "${CHECKS_TIMEOUT}" ]; then
        hang="no verdict $(( elapsed - first_output_after ))s after the first output"
    fi

    [ -z "${hang}" ] || break
    sleep 1
done

if [ -n "${hang}" ]; then
    echo "==> ${hang}; stopping the launch"
    # Taken first, because the signal below reaches the app too, and limited with perl's alarm so
    # that a stuck sample cannot keep the launch from being stopped.
    if [ -n "${app_pid}" ]; then
        perl -e 'alarm 120; exec @ARGV' sample "${app_pid}" 3 -mayDie 2>&1 \
            | sed '/^Binary Images:/,$d' || true
    fi
    stop_launch
fi

set +e
wait "${launch_pid}"
status=$?
set -e
echo "==> finished $(( $(date +%s) - launch_started ))s after launch"

cat "${LOG_FILE}"
print_launch_timeline

if [ "${status}" -ne 0 ]; then
    echo "==> simctl exited with status ${status}"
fi

if ! grep -q "ANTMEDIA_E2E_DONE PASS" "${LOG_FILE}"; then
    # The timeline as well as the poll, which can miss a process that dies within a second. simctl
    # waits for the launch to finish before it hands signals on, so a kill that leaves status 143
    # and no "<bundle id>: <pid>" line also means no process.
    if [ -n "${app_started_after}" ]; then
        echo "==> the app process started ${app_started_after}s after launch (pid ${app_pid})"
    elif grep -q "Launch successful" "${LOG_FILE}"; then
        echo "==> the app process started, and was gone before the next poll"
    else
        echo "==> the app process never started: the launch did not get that far"
    fi

    echo "==> no passing verdict; capturing the simulator's log of the app"
    xcrun simctl spawn "${UDID}" log show --start "@${launch_started}" --predicate "process CONTAINS 'DeviceTests'" \
        2>/dev/null | tail -100 | tee -a "${LOG_FILE}" || true
    echo "::error::Ant Media iOS simulator smoke tests failed or timed out"
    exit 1
fi

echo "==> simulator smoke tests passed"
