#!/usr/bin/env bash
set -euo pipefail

# Installs the Android smoke-test app against a packed AntMedia.Net.Android package and runs it on
# the emulator that the calling workflow step has already booted. The app reports results to
# logcat under the AntMediaE2E tag; this script turns them into an exit code.
#
# Usage: run-android-device-tests.sh <package-version> [target-framework]

VERSION="${1:?a package version is required}"
TARGET_FRAMEWORK="${2:-net10.0-android36.0}"

PACKAGE_NAME="com.sbokatuk.antmedia.devicetests"
LOG_FILE="device-tests-logcat.txt"
# CI emulators are x86_64; override when running this against a local arm64 emulator or device.
DEVICE_RID="${ANTMEDIA_DEVICE_RID:-android-x64}"
POLL_ATTEMPTS=60
POLL_INTERVAL=5

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="${REPO_ROOT}/tests/AntMedia.Net.Android.DeviceTests/AntMedia.Net.Android.DeviceTests.csproj"

# The .NET 9 band builds net8/net9 and the .NET 10 band builds net10, so pick the SDK that owns
# the requested target framework. The SDK is resolved from the working directory, and the
# repository's global.json pins .NET 9, hence the scratch directory.
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

# NuGet caches by package id + version, so rebuilding a version that was already restored once
# silently reuses the stale copy. CI versions are unique, but locally you will re-pack the same
# version repeatedly and test yesterday's bits without this.
rm -rf "${HOME}/.nuget/packages/antmedia.net.android/${VERSION}"

TRIMMING="${ANTMEDIA_TRIMMING:-none}"

echo "==> installing device tests (version=${VERSION}, tfm=${TARGET_FRAMEWORK}, sdk=${sdk_version}, trimming=${TRIMMING})"

if [ "${TRIMMING}" = "none" ]; then
    ( cd "${SDK_DIR}" && dotnet build "${PROJECT}" \
        --configuration Release \
        -p:AntMediaPackageVersion="${VERSION}" \
        -p:AntMediaDeviceTargetFramework="${TARGET_FRAMEWORK}" \
        -p:AntMediaTrimming="${TRIMMING}" \
        -p:RuntimeIdentifier="${DEVICE_RID}" \
        -t:Install )
else
    # PublishTrimmed is honoured on publish, not on build: `dotnet build -t:Install` produces an
    # untrimmed APK whatever the property says, so asking for trimming and then using the build
    # path would report a pass without the linker ever having run over the binding. Publishing
    # produces the trimmed, signed APK, which adb then installs.
    ( cd "${SDK_DIR}" && dotnet publish "${PROJECT}" \
        --configuration Release \
        -f "${TARGET_FRAMEWORK}" \
        -p:AntMediaPackageVersion="${VERSION}" \
        -p:AntMediaDeviceTargetFramework="${TARGET_FRAMEWORK}" \
        -p:AntMediaTrimming="${TRIMMING}" \
        -p:RuntimeIdentifier="${DEVICE_RID}" )

    APK="$(find "$(dirname "${PROJECT}")/bin/Release/${TARGET_FRAMEWORK}/${DEVICE_RID}/publish" \
        -name '*-Signed.apk' | head -1)"
    if [ -z "${APK}" ]; then
        echo "::error::publish succeeded but no signed APK was produced" >&2
        exit 1
    fi

    echo "==> installing ${APK}"
    adb install -r "${APK}"
fi

echo "==> launching"
adb logcat -c
adb shell am start -n "${PACKAGE_NAME}/.MainActivity"

echo "==> waiting for results"
for _ in $(seq 1 "${POLL_ATTEMPTS}"); do
    if adb logcat -d -s "AntMediaE2E:*" | grep -q "ANTMEDIA_E2E_DONE"; then
        break
    fi
    sleep "${POLL_INTERVAL}"
done

adb logcat -d -s "AntMediaE2E:*" | tee "${LOG_FILE}"

if ! grep -q "ANTMEDIA_E2E_DONE PASS" "${LOG_FILE}"; then
    # No verdict usually means the app died before reporting, so keep the crash trace.
    echo "==> no passing verdict; capturing crash output"
    adb logcat -d -s AndroidRuntime:E DEBUG:F "${PACKAGE_NAME}:*" | tee -a "${LOG_FILE}"
    echo "::error::Ant Media Android device smoke tests failed or timed out"
    exit 1
fi

echo "==> device smoke tests passed"
