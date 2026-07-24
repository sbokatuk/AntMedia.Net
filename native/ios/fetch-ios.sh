#!/usr/bin/env bash
set -euo pipefail

# Builds WebRTCiOSSDK.xcframework from source with our @objc facade compiled into it, and stages
# it next to Ant Media's prebuilt WebRTC.xcframework for the binding project.
#
# Usage:
#   ./native/ios/fetch-ios.sh                  # commit from Directory.Build.props
#   ./native/ios/fetch-ios.sh -f              # rebuild even when already up to date
#   ./native/ios/fetch-ios.sh <commit-sha>     # build a different commit
#
# WHY NOT USE THE PREBUILT XCFRAMEWORK
# ------------------------------------
# WebRTC-iOS-SDK checks in a built WebRTCiOSSDK.xcframework, but it is useless to a .NET binding:
# the SDK is Swift and exposes only two empty @objc shells (AntMediaClient with just init, and
# Config) to Objective-C. Everything a caller needs is Swift-only and therefore invisible to
# Objective Sharpie. native/ios/Facade/AntMediaNetFacade.swift re-exposes that API as @objc types,
# and it has to be compiled *into* the framework to appear in the generated -Swift.h, hence this
# rebuild.
#
# Requires: macOS with Xcode, git and python3 - all either preinstalled or already needed by the
# build. Nothing is installed by this script.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
. "${SCRIPT_DIR}/../../build/pins.sh"

FORCE=false
COMMIT=""
for arg in "$@"; do
    case "${arg}" in
        -f|--force) FORCE=true ;;
        *) COMMIT="${arg}" ;;
    esac
done
COMMIT="${COMMIT:-${ANTMEDIA_IOS_COMMIT}}"

WORK_DIR="${ANTMEDIA_REPO_ROOT}/native/build/ios"
CHECKOUT="${WORK_DIR}/WebRTC-iOS-SDK"
DESTINATION="${ANTMEDIA_REPO_ROOT}/src/AntMedia.Net.iOS/lib"
PIN_FILE="${WORK_DIR}/pin.txt"

SCHEME="WebRTCiOSSDK"
TARGET="WebRTCiOSSDK"
PROJECT="${CHECKOUT}/WebRTCiOSSDK.xcodeproj"

# The facade and the scripts shape the output as much as the upstream commit does, so they are
# part of the up-to-date identity, mirroring the CI cache key.
INPUTS_HASH="$(cat "${BASH_SOURCE[0]}" "${SCRIPT_DIR}/add-facade.py" "${SCRIPT_DIR}/Facade/"*.swift \
    | shasum -a 256 | cut -d' ' -f1)"

if [ "${FORCE}" = false ] \
    && [ -d "${DESTINATION}/${SCHEME}.xcframework" ] \
    && [ -d "${DESTINATION}/WebRTC.xcframework" ] \
    && [ -f "${PIN_FILE}" ] \
    && grep -q "^commit=${COMMIT}$" "${PIN_FILE}" \
    && grep -q "^inputs=${INPUTS_HASH}$" "${PIN_FILE}"; then
    echo "==> ${DESTINATION} already built from ${COMMIT}; use -f to rebuild"
    exit 0
fi

if ! command -v xcodebuild >/dev/null 2>&1; then
    antmedia_fail "xcodebuild not found — the iOS native build requires macOS with Xcode"
fi

echo "==> Ant Media iOS SDK ${COMMIT}"

# Upstream has tagged this repository once (v2.10.0, 2020), so the pin is a commit. Fetching a
# bare sha needs an explicit refspec; --depth 1 keeps it to the one commit.
if [ ! -d "${CHECKOUT}/.git" ]; then
    rm -rf "${CHECKOUT}"
    mkdir -p "${CHECKOUT}"
    git -C "${CHECKOUT}" init -q
    git -C "${CHECKOUT}" remote add origin "${ANTMEDIA_IOS_REPOSITORY}"
fi

git -C "${CHECKOUT}" fetch --depth 1 origin "${COMMIT}"
git -C "${CHECKOUT}" checkout --force -q FETCH_HEAD
# Removes the previous run's facade copy and pbxproj edit so the build starts from pristine
# upstream sources; DerivedData lives outside the checkout.
git -C "${CHECKOUT}" clean -fdq

echo "==> injecting the @objc facade"
FACADE_DIR="${CHECKOUT}/WebRTCiOSSDK/AntMediaNet"
mkdir -p "${FACADE_DIR}"
cp "${SCRIPT_DIR}"/Facade/*.swift "${FACADE_DIR}/"

python3 "${SCRIPT_DIR}/add-facade.py" "${PROJECT}" "${TARGET}" "${FACADE_DIR}"/*.swift

DERIVED_DATA="${WORK_DIR}/DerivedData"
DEVICE_ARCHIVE="${WORK_DIR}/device.xcarchive"
SIMULATOR_ARCHIVE="${WORK_DIR}/simulator.xcarchive"
rm -rf "${DEVICE_ARCHIVE}" "${SIMULATOR_ARCHIVE}"

archive() {
    local destination="$1" archive_path="$2" log="$3"
    echo "==> archiving for ${destination}"
    # The full log goes to a file and only errors to the console; on failure the file has the
    # context the grep filter drops.
    if ! xcodebuild archive \
        -project "${PROJECT}" \
        -scheme "${SCHEME}" \
        -configuration Release \
        -destination "${destination}" \
        -archivePath "${archive_path}" \
        -derivedDataPath "${DERIVED_DATA}" \
        SKIP_INSTALL=NO \
        BUILD_LIBRARY_FOR_DISTRIBUTION=YES \
        CODE_SIGN_IDENTITY="" \
        CODE_SIGNING_REQUIRED=NO \
        CODE_SIGNING_ALLOWED=NO \
        > "${log}" 2>&1; then
        grep -E '(error:|BUILD FAILED)' "${log}" >&2 || true
        antmedia_fail "xcodebuild archive failed for ${destination}; full log: ${log}"
    fi
    grep -E 'BUILD SUCCEEDED' "${log}" || true
}

# SKIP_INSTALL=NO puts the framework in the archive rather than only in DerivedData;
# BUILD_LIBRARY_FOR_DISTRIBUTION emits the .swiftinterface that makes the framework usable from a
# different Swift compiler version than the one that built it.
archive "generic/platform=iOS" "${DEVICE_ARCHIVE}" "${WORK_DIR}/xcodebuild-device.log"
archive "generic/platform=iOS Simulator" "${SIMULATOR_ARCHIVE}" "${WORK_DIR}/xcodebuild-simulator.log"

DEVICE_FRAMEWORK="${DEVICE_ARCHIVE}/Products/Library/Frameworks/${SCHEME}.framework"
SIMULATOR_FRAMEWORK="${SIMULATOR_ARCHIVE}/Products/Library/Frameworks/${SCHEME}.framework"

for framework in "${DEVICE_FRAMEWORK}" "${SIMULATOR_FRAMEWORK}"; do
    if [ ! -d "${framework}" ]; then
        antmedia_fail "xcodebuild reported success but ${framework} is missing"
    fi
done

# The whole point of the rebuild: if the facade did not compile in, the binding would be generated
# against the same empty API as the prebuilt xcframework and would fail much later and less clearly.
HEADER="${DEVICE_FRAMEWORK}/Headers/${SCHEME}-Swift.h"
if ! grep -q 'AMSClient' "${HEADER}" 2>/dev/null; then
    antmedia_fail "${HEADER} does not declare AMSClient — the facade was not compiled into the framework"
fi

# The header is necessary but not sufficient. A Swift @objc class with no explicit Objective-C
# name is exported under its mangled name (_OBJC_CLASS_$__TtC12WebRTCiOSSDK9AMSClient) even though
# the header calls it AMSClient, while the .NET binding links against _OBJC_CLASS_$_AMSClient — so
# every consuming app fails at link time with an undefined symbol, and nothing before that notices.
for class in AMSClient AMSStreamInformation; do
    if ! nm -gU "${DEVICE_FRAMEWORK}/${SCHEME}" 2>/dev/null \
        | grep -q "_OBJC_CLASS_\\\$_${class}\$"; then
        antmedia_fail "${SCHEME} does not export _OBJC_CLASS_\$_${class}; the facade class needs an explicit @objc(${class}) name"
    fi
done

echo "==> creating the xcframework"
rm -rf "${WORK_DIR}/${SCHEME}.xcframework"
xcodebuild -create-xcframework \
    -framework "${DEVICE_FRAMEWORK}" \
    -framework "${SIMULATOR_FRAMEWORK}" \
    -output "${WORK_DIR}/${SCHEME}.xcframework"

mkdir -p "${DESTINATION}"
rm -rf "${DESTINATION}/${SCHEME}.xcframework" "${DESTINATION}/WebRTC.xcframework"
cp -R "${WORK_DIR}/${SCHEME}.xcframework" "${DESTINATION}/"
# Ant Media's build of libwebrtc, taken as-is: it is a plain Objective-C framework that needs no
# facade, and rebuilding libwebrtc from source is a multi-hour job with no benefit here.
cp -R "${CHECKOUT}/WebRTC.xcframework" "${DESTINATION}/"

# The date as well as the sha: upstream publishes no version for this SDK, so "how old is this
# pin" is a question only the commit date can answer. The up-to-date check reads it, and CI
# surfaces it in the build summary.
cat > "${PIN_FILE}" <<EOF
commit=$(git -C "${CHECKOUT}" rev-parse HEAD)
commit_date=$(git -C "${CHECKOUT}" show -s --format=%cs HEAD)
inputs=${INPUTS_HASH}
EOF

echo "==> staged in ${DESTINATION}:"
du -sh "${DESTINATION}"/*.xcframework
