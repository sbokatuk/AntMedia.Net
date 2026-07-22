#!/usr/bin/env bash
set -euo pipefail

# Builds WebRTCiOSSDK.xcframework for Mac Catalyst and stages it for the AntMedia.Net.Mac binding.
#
# Usage:
#   ./native/mac/fetch-mac.sh                  # commit from Directory.Build.props
#   ./native/mac/fetch-mac.sh <commit-sha>     # build a different commit
#
# HOW THIS DIFFERS FROM THE iOS BUILD
# The Ant Media commit is the same. The libwebrtc underneath is not.
#
# Ant Media's WebRTC.xcframework is a fork - it adds RTCAudioDeviceModule - published for iOS only,
# with no source, so no Catalyst slice can be produced from it. This build therefore links a stock
# community libwebrtc, which does ship a maccatalyst slice, and compiles native/mac/Shim beside the
# facade to supply the fork-only API that Ant Media's Swift code expects. Their source is not
# patched. The iOS and Android builds are untouched and keep using Ant Media's own libraries.
#
# Three build settings do the rest, and each is load-bearing:
#
#   SUPPORTS_MACCATALYST=YES      the target ships iOS-only by default, so xcodebuild reports
#                                 "Unable to find a destination matching the provided specifier"
#   IPHONEOS_DEPLOYMENT_TARGET    Catalyst derives its version from the *iOS* one, and
#                                 AVCaptureDevice needs Catalyst 14
#   ARCHS=arm64                   Ant Media picks its renderer with #if arch(arm64): Metal on
#                                 arm64, OpenGL otherwise. Catalyst has no OpenGL, so an x86_64
#                                 slice fails to link on RTCEAGLVideoView. Apple Silicon only.
#
# Requires: macOS with Xcode, git, python3, curl, unzip.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
. "${SCRIPT_DIR}/../../build/pins.sh"

COMMIT="${1:-${ANTMEDIA_IOS_COMMIT}}"
WORK_DIR="${ANTMEDIA_REPO_ROOT}/native/build/mac"
CHECKOUT="${WORK_DIR}/WebRTC-iOS-SDK"
DESTINATION="${ANTMEDIA_REPO_ROOT}/src/AntMedia.Net.Mac/lib"

SCHEME="WebRTCiOSSDK"
TARGET="WebRTCiOSSDK"
PROJECT="${CHECKOUT}/WebRTCiOSSDK.xcodeproj"
CATALYST_DEPLOYMENT_TARGET="14.0"

if ! command -v xcodebuild >/dev/null 2>&1; then
    echo "::error::xcodebuild not found — the Mac Catalyst build requires macOS with Xcode" >&2
    exit 1
fi

mkdir -p "${WORK_DIR}"

echo "==> stock libwebrtc ${ANTMEDIA_MAC_WEBRTC_RELEASE}"
WEBRTC_ZIP="${WORK_DIR}/webrtc-${ANTMEDIA_MAC_WEBRTC_RELEASE}.zip"
WEBRTC_DIR="${WORK_DIR}/webrtc-${ANTMEDIA_MAC_WEBRTC_RELEASE}"

if [ ! -d "${WEBRTC_DIR}/WebRTC.xcframework" ]; then
    curl -sfL "${ANTMEDIA_MAC_WEBRTC_URL}" -o "${WEBRTC_ZIP}"
    rm -rf "${WEBRTC_DIR}"
    mkdir -p "${WEBRTC_DIR}"
    unzip -q "${WEBRTC_ZIP}" -d "${WEBRTC_DIR}"
fi

# Fail here rather than at link time: without this slice the whole exercise is pointless, and the
# linker's complaint would be about missing symbols rather than a missing platform.
if ! /usr/libexec/PlistBuddy -c 'Print :AvailableLibraries' \
    "${WEBRTC_DIR}/WebRTC.xcframework/Info.plist" 2>/dev/null | grep -q maccatalyst; then
    echo "::error::${ANTMEDIA_MAC_WEBRTC_URL} has no maccatalyst slice" >&2
    exit 1
fi

echo "==> Ant Media iOS SDK ${COMMIT} (built for Mac Catalyst)"

if [ ! -d "${CHECKOUT}/.git" ]; then
    rm -rf "${CHECKOUT}"
    mkdir -p "${CHECKOUT}"
    git -C "${CHECKOUT}" init -q
    git -C "${CHECKOUT}" remote add origin "${ANTMEDIA_IOS_REPOSITORY}"
fi

git -C "${CHECKOUT}" fetch --depth 1 origin "${COMMIT}"
git -C "${CHECKOUT}" checkout --force -q FETCH_HEAD
git -C "${CHECKOUT}" clean -fdq

echo "==> swapping in stock libwebrtc"
rm -rf "${CHECKOUT}/WebRTC.xcframework"
cp -R "${WEBRTC_DIR}/WebRTC.xcframework" "${CHECKOUT}/"

echo "==> injecting the @objc facade and the stock-libwebrtc shim"
FACADE_DIR="${CHECKOUT}/WebRTCiOSSDK/AntMediaNet"
mkdir -p "${FACADE_DIR}"
cp "${SCRIPT_DIR}/../ios/Facade"/*.swift "${FACADE_DIR}/"
cp "${SCRIPT_DIR}/Shim"/*.swift "${FACADE_DIR}/"

python3 "${SCRIPT_DIR}/../ios/add-facade.py" "${PROJECT}" "${TARGET}" "${FACADE_DIR}"/*.swift

echo "==> enabling Mac Catalyst on the target"
python3 "${SCRIPT_DIR}/enable-catalyst.py" "${PROJECT}" "${TARGET}" "${CATALYST_DEPLOYMENT_TARGET}"

DERIVED_DATA="${WORK_DIR}/DerivedData"
ARCHIVE="${WORK_DIR}/maccatalyst.xcarchive"
rm -rf "${ARCHIVE}"

echo "==> archiving for Mac Catalyst (arm64)"
xcodebuild archive \
    -project "${PROJECT}" \
    -scheme "${SCHEME}" \
    -configuration Release \
    -destination 'generic/platform=macOS,variant=Mac Catalyst' \
    -archivePath "${ARCHIVE}" \
    -derivedDataPath "${DERIVED_DATA}" \
    ARCHS=arm64 \
    ONLY_ACTIVE_ARCH=NO \
    SKIP_INSTALL=NO \
    BUILD_LIBRARY_FOR_DISTRIBUTION=YES \
    CODE_SIGN_IDENTITY="" \
    CODE_SIGNING_REQUIRED=NO \
    CODE_SIGNING_ALLOWED=NO \
    | (grep -E '(error:|BUILD (SUCCEEDED|FAILED))' || true)

FRAMEWORK="${ARCHIVE}/Products/Library/Frameworks/${SCHEME}.framework"
if [ ! -d "${FRAMEWORK}" ]; then
    echo "::error::xcodebuild reported success but ${FRAMEWORK} is missing" >&2
    exit 1
fi

# Same guard as the iOS build: a Swift @objc class with no explicit Objective-C name is exported
# under its mangled name, and the .NET binding would fail to link against it in every consuming app.
for class in AMSClient AMSStreamInformation; do
    if ! nm -gU "${FRAMEWORK}/${SCHEME}" 2>/dev/null | grep -q "_OBJC_CLASS_\\\$_${class}\$"; then
        echo "::error::${SCHEME} does not export _OBJC_CLASS_\$_${class}" >&2
        exit 1
    fi
done

echo "==> creating the xcframework"
rm -rf "${WORK_DIR}/${SCHEME}.xcframework"
xcodebuild -create-xcframework \
    -framework "${FRAMEWORK}" \
    -output "${WORK_DIR}/${SCHEME}.xcframework"

mkdir -p "${DESTINATION}"
rm -rf "${DESTINATION}/${SCHEME}.xcframework" "${DESTINATION}/WebRTC.xcframework"
cp -R "${WORK_DIR}/${SCHEME}.xcframework" "${DESTINATION}/"

# Only the Catalyst slice, and only its arm64 half: the iOS slices come from Ant Media's own build
# in the iOS package, and x86_64 Catalyst cannot be supported at all (see ARCHS above). Shipping
# either would be tens of megabytes in every consuming app for something nothing can link against.
python3 "${SCRIPT_DIR}/strip-slices.py" \
    "${WEBRTC_DIR}/WebRTC.xcframework" "${DESTINATION}/WebRTC.xcframework" maccatalyst arm64

# NuGet packages cannot carry symlinks, and the macOS versioned framework layout is mostly
# symlinks - see flatten-frameworks.py for what that does to the consuming app's build.
for framework in "${DESTINATION}"/*.xcframework; do
    python3 "${SCRIPT_DIR}/flatten-frameworks.py" "${framework}"
done

cat > "${WORK_DIR}/pin.txt" <<EOF
commit=$(git -C "${CHECKOUT}" rev-parse HEAD)
commit_date=$(git -C "${CHECKOUT}" show -s --format=%cs HEAD)
webrtc=${ANTMEDIA_MAC_WEBRTC_RELEASE}
EOF

echo "==> staged in ${DESTINATION}:"
du -sh "${DESTINATION}"/*.xcframework
