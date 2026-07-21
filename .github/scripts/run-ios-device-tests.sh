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
TARGET_FRAMEWORK="${2:-net10.0-ios26.0}"

BUNDLE_ID="com.sbokatuk.antmedia.devicetests"
LOG_FILE="device-tests-simulator.log"

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

echo "==> building device tests (version=${VERSION}, tfm=${TARGET_FRAMEWORK}, rid=${DEVICE_RID})"
( cd "${SDK_DIR}" && dotnet build "${PROJECT}" \
    --configuration Release \
    -f "${TARGET_FRAMEWORK}" \
    -p:AntMediaPackageVersion="${VERSION}" \
    -p:AntMediaDeviceTargetFramework="${TARGET_FRAMEWORK}" \
    -p:RuntimeIdentifier="${DEVICE_RID}" )

APP="$(find "$(dirname "${PROJECT}")/bin/Release/${TARGET_FRAMEWORK}/${DEVICE_RID}" \
    -maxdepth 1 -name '*.app' -type d | head -1)"
if [ -z "${APP}" ]; then
    echo "::error::build succeeded but no .app bundle was produced" >&2
    exit 1
fi
echo "==> built ${APP}"

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

echo "==> launching"
# --console-pty streams the app's stdout and blocks until it exits, so the app's own
# Environment.Exit call is what ends this step. macOS has no coreutils timeout, so the guard
# against a hang before that point is a watchdog that kills the launch.
xcrun simctl launch --console-pty "${UDID}" "${BUNDLE_ID}" > "${LOG_FILE}" 2>&1 &
launch_pid=$!

( sleep 300; kill -TERM "${launch_pid}" 2>/dev/null ) &
watchdog_pid=$!
# Detached so the shell does not print a "Terminated" job notice over the test output when the
# watchdog is killed on the happy path.
disown "${watchdog_pid}" 2>/dev/null || true

set +e
wait "${launch_pid}"
status=$?
set -e
kill "${watchdog_pid}" 2>/dev/null || true

cat "${LOG_FILE}"

if [ "${status}" -ne 0 ]; then
    echo "==> the app exited with status ${status} (killed by the watchdog if it ran for 300s)"
fi

if ! grep -q "ANTMEDIA_E2E_DONE PASS" "${LOG_FILE}"; then
    echo "==> no passing verdict; capturing the simulator's crash log"
    xcrun simctl spawn "${UDID}" log show --last 2m --predicate "process CONTAINS 'DeviceTests'" \
        2>/dev/null | tail -100 | tee -a "${LOG_FILE}" || true
    echo "::error::Ant Media iOS simulator smoke tests failed or timed out"
    exit 1
fi

echo "==> simulator smoke tests passed"
