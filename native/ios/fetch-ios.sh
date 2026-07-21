#!/usr/bin/env bash
set -euo pipefail

# Builds WebRTCiOSSDK.xcframework from source with our @objc facade compiled into it, and stages
# it next to Ant Media's prebuilt WebRTC.xcframework for the binding project.
#
# Usage:
#   ./native/ios/fetch-ios.sh                  # commit from Directory.Build.props
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
# Requires: macOS with Xcode, git, and ruby with the xcodeproj gem (installed here if missing).

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
. "${SCRIPT_DIR}/../../build/pins.sh"

COMMIT="${1:-${ANTMEDIA_IOS_COMMIT}}"
WORK_DIR="${ANTMEDIA_REPO_ROOT}/native/build/ios"
CHECKOUT="${WORK_DIR}/WebRTC-iOS-SDK"
DESTINATION="${ANTMEDIA_REPO_ROOT}/src/AntMedia.Net.iOS/lib"

SCHEME="WebRTCiOSSDK"
TARGET="WebRTCiOSSDK"
PROJECT="${CHECKOUT}/WebRTCiOSSDK.xcodeproj"

if ! command -v xcodebuild >/dev/null 2>&1; then
    echo "::error::xcodebuild not found — the iOS native build requires macOS with Xcode" >&2
    exit 1
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

# Installed into a working-directory gem home rather than the system one: macOS ships a root-owned
# /Library/Ruby/Gems that a plain `gem install` cannot write to, and requiring sudo here would make
# the script unrunnable in CI and unpleasant locally. GEM_HOME is honoured both by `gem install`
# and by ruby's own gem resolution, so add-facade.rb below finds it.
export GEM_HOME="${WORK_DIR}/gems"
export PATH="${GEM_HOME}/bin:${PATH}"

if ! gem list -i xcodeproj >/dev/null 2>&1; then
    echo "==> installing the xcodeproj gem into ${GEM_HOME}"
    gem install --no-document --install-dir "${GEM_HOME}" xcodeproj
fi

ruby "${SCRIPT_DIR}/add-facade.rb" "${PROJECT}" "${TARGET}" "${FACADE_DIR}"/*.swift

DERIVED_DATA="${WORK_DIR}/DerivedData"
DEVICE_ARCHIVE="${WORK_DIR}/device.xcarchive"
SIMULATOR_ARCHIVE="${WORK_DIR}/simulator.xcarchive"
rm -rf "${DEVICE_ARCHIVE}" "${SIMULATOR_ARCHIVE}"

archive() {
    local destination="$1" archive_path="$2"
    echo "==> archiving for ${destination}"
    xcodebuild archive \
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
        | (grep -E '(error|warning: .*facade|BUILD)' || true)
}

# SKIP_INSTALL=NO puts the framework in the archive rather than only in DerivedData;
# BUILD_LIBRARY_FOR_DISTRIBUTION emits the .swiftinterface that makes the framework usable from a
# different Swift compiler version than the one that built it.
archive "generic/platform=iOS" "${DEVICE_ARCHIVE}"
archive "generic/platform=iOS Simulator" "${SIMULATOR_ARCHIVE}"

DEVICE_FRAMEWORK="${DEVICE_ARCHIVE}/Products/Library/Frameworks/${SCHEME}.framework"
SIMULATOR_FRAMEWORK="${SIMULATOR_ARCHIVE}/Products/Library/Frameworks/${SCHEME}.framework"

for framework in "${DEVICE_FRAMEWORK}" "${SIMULATOR_FRAMEWORK}"; do
    if [ ! -d "${framework}" ]; then
        echo "::error::xcodebuild reported success but ${framework} is missing" >&2
        exit 1
    fi
done

# The whole point of the rebuild: if the facade did not compile in, the binding would be generated
# against the same empty API as the prebuilt xcframework and would fail much later and less clearly.
HEADER="${DEVICE_FRAMEWORK}/Headers/${SCHEME}-Swift.h"
if ! grep -q 'AMSClient' "${HEADER}" 2>/dev/null; then
    echo "::error::${HEADER} does not declare AMSClient — the facade was not compiled into the framework" >&2
    exit 1
fi

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

printf 'commit=%s\n' "$(git -C "${CHECKOUT}" rev-parse HEAD)" > "${WORK_DIR}/pin.txt"

echo "==> staged in ${DESTINATION}:"
du -sh "${DESTINATION}"/*.xcframework
