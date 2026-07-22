#!/usr/bin/env bash
set -euo pipefail

# Builds the Mac Catalyst smoke-test app against a packed AntMedia.Net.Mac package and runs it.
# The app prints its verdict to stdout and exits; this script turns that into an exit code.
#
# Usage: run-mac-device-tests.sh <package-version> [target-framework]
#
# Simpler than the iOS and Android runners, because a Mac Catalyst app is a macOS app: there is no
# simulator to boot and no install step. It does still go through `open` rather than exec'ing the
# binary — see the note on capture permissions below.

VERSION="${1:?a package version is required}"
TARGET_FRAMEWORK="${2:-net10.0-maccatalyst26.0}"

# Where the log ends up, for the caller and for CI's artifact upload.
LOG_FILE="device-tests-maccatalyst.log"

# Where the app actually writes it. `open --stdout` hands the descriptor over through
# LaunchServices, which cannot open a path inside a TCC-protected directory - Documents, Desktop,
# Downloads - and fails the whole launch with a bare
#
#     _LSOpenURLsWithCompletionHandler() failed with error -10810.
#
# that says nothing about the log file. A clone under ~/Documents is enough to hit it, so the app
# writes to a temp file and it is copied out at the end.
LAUNCH_LOG="$(mktemp -t antmedia-mac-e2e)"

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="${REPO_ROOT}/tests/AntMedia.Net.Mac.DeviceTests/AntMedia.Net.Mac.DeviceTests.csproj"

if [ "$(uname -s)" != "Darwin" ]; then
    echo "::error::the Mac Catalyst smoke test requires macOS" >&2
    exit 1
fi

if [ "$(uname -m)" != "arm64" ]; then
    echo "::error::AntMedia.Net.Mac is Apple Silicon only; see the package's own description" >&2
    exit 1
fi

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
trap 'rm -rf "${SDK_DIR}" "${LAUNCH_LOG}"' EXIT
printf '{ "sdk": { "version": "%s", "rollForward": "latestFeature" } }\n' "${sdk_version}" \
    > "${SDK_DIR}/global.json"

# See the other runners: NuGet would otherwise serve a cached copy of a version we just re-packed.
rm -rf "${HOME}/.nuget/packages/antmedia.net.mac/${VERSION}" \
       "${HOME}/.nuget/packages/antmedia.net/${VERSION}"

# Debug, like the iOS runner: a Release build trims and AOT-compiles, which buys nothing here and
# costs most of an hour. Debug still links the native frameworks, which is the part that matters.
CONFIGURATION="Debug"

echo "==> building device tests (version=${VERSION}, tfm=${TARGET_FRAMEWORK})"
( cd "${SDK_DIR}" && dotnet build "${PROJECT}" \
    --configuration "${CONFIGURATION}" \
    -f "${TARGET_FRAMEWORK}" \
    -p:AntMediaPackageVersion="${VERSION}" \
    -p:AntMediaDeviceTargetFramework="${TARGET_FRAMEWORK}" \
    -p:AntMediaTrimming="${ANTMEDIA_TRIMMING:-none}" )

APP="$(find "$(dirname "${PROJECT}")/bin/${CONFIGURATION}/${TARGET_FRAMEWORK}/maccatalyst-arm64" \
    -maxdepth 1 -name '*.app' -type d | head -1)"
if [ -z "${APP}" ]; then
    echo "::error::build succeeded but no .app bundle was produced" >&2
    exit 1
fi
echo "==> built ${APP}"

# Launched through LaunchServices rather than exec'd directly, and that is load-bearing when
# ANTMEDIA_TEST_SERVER is set: macOS only shows the camera and microphone prompts for an app it
# launched, and a binary started from a shell inherits the terminal's (absent) grants instead. The
# publish then succeeds at every signalling step and silently sends no media.
#
# `open -n` returns immediately, so the wait below is on the app's own exit rather than on open's.
echo "==> launching"
open -n \
    ${ANTMEDIA_TEST_SERVER:+--env ANTMEDIA_TEST_SERVER="${ANTMEDIA_TEST_SERVER}"} \
    --stdout "${LAUNCH_LOG}" \
    --stderr "${LAUNCH_LOG}" \
    "${APP}"

BINARY="${APP}/Contents/MacOS/$(basename "${APP}" .app)"

for _ in $(seq 1 60); do
    sleep 5
    grep -q "ANTMEDIA_E2E_DONE" "${LAUNCH_LOG}" && break
    if ! pgrep -f "${BINARY}" > /dev/null; then
        echo "==> the app exited without printing a verdict"
        break
    fi
done

pkill -f "${BINARY}" 2>/dev/null || true

cp "${LAUNCH_LOG}" "${LOG_FILE}"
cat "${LOG_FILE}"

if ! grep -q "ANTMEDIA_E2E_DONE PASS" "${LOG_FILE}"; then
    echo "::error::Ant Media Mac Catalyst smoke tests failed or timed out"
    exit 1
fi

echo "==> Mac Catalyst smoke tests passed"
