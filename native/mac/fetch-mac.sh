#!/usr/bin/env bash
set -euo pipefail

# Builds WebRTCiOSSDK.xcframework for Mac Catalyst and stages it for the AntMedia.Net.Mac binding.
#
# Usage:
#   ./native/mac/fetch-mac.sh                  # commit from Directory.Build.props
#   ./native/mac/fetch-mac.sh -f              # rebuild even when already up to date
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

FORCE=false
COMMIT=""
for arg in "$@"; do
    case "${arg}" in
        -f|--force) FORCE=true ;;
        *) COMMIT="${arg}" ;;
    esac
done
COMMIT="${COMMIT:-${ANTMEDIA_IOS_COMMIT}}"

WORK_DIR="${ANTMEDIA_REPO_ROOT}/native/build/mac"
CHECKOUT="${WORK_DIR}/WebRTC-iOS-SDK"
DESTINATION="${ANTMEDIA_REPO_ROOT}/src/AntMedia.Net.Mac/lib"
PIN_FILE="${WORK_DIR}/pin.txt"

SCHEME="WebRTCiOSSDK"
TARGET="WebRTCiOSSDK"
PROJECT="${CHECKOUT}/WebRTCiOSSDK.xcodeproj"
CATALYST_DEPLOYMENT_TARGET="14.0"

INPUTS_HASH="$(cat "${BASH_SOURCE[0]}" \
    "${SCRIPT_DIR}/../ios/add-facade.py" \
    "${SCRIPT_DIR}/../ios/Facade/"*.swift \
    "${SCRIPT_DIR}/Shim/"*.swift \
    "${SCRIPT_DIR}/enable-catalyst.py" \
    "${SCRIPT_DIR}/strip-slices.py" \
    "${SCRIPT_DIR}/flatten-frameworks.py" \
    | shasum -a 256 | cut -d' ' -f1)"

if [ "${FORCE}" = false ] \
    && [ -d "${DESTINATION}/${SCHEME}.xcframework" ] \
    && [ -d "${DESTINATION}/WebRTC.xcframework" ] \
    && [ -f "${PIN_FILE}" ] \
    && grep -q "^commit=${COMMIT}$" "${PIN_FILE}" \
    && grep -q "^webrtc=${ANTMEDIA_MAC_WEBRTC_RELEASE}$" "${PIN_FILE}" \
    && grep -q "^inputs=${INPUTS_HASH}$" "${PIN_FILE}"; then
    echo "==> ${DESTINATION} already built from ${COMMIT}; use -f to rebuild"
    exit 0
fi

if ! command -v xcodebuild >/dev/null 2>&1; then
    antmedia_fail "xcodebuild not found — the Mac Catalyst build requires macOS with Xcode"
fi

mkdir -p "${WORK_DIR}"

echo "==> stock libwebrtc ${ANTMEDIA_MAC_WEBRTC_RELEASE}"
WEBRTC_ZIP="${WORK_DIR}/webrtc-${ANTMEDIA_MAC_WEBRTC_RELEASE}.zip"
WEBRTC_DIR="${WORK_DIR}/webrtc-${ANTMEDIA_MAC_WEBRTC_RELEASE}"

if [ ! -d "${WEBRTC_DIR}/WebRTC.xcframework" ]; then
    # -sS rather than -s so a failure prints curl's reason; --retry rides out the transient
    # errors a ~100 MB download from a release CDN occasionally hits.
    curl -sfSL --retry 3 "${ANTMEDIA_MAC_WEBRTC_URL}" -o "${WEBRTC_ZIP}"

    # GitHub release assets are mutable - deleting and re-uploading under the same name is
    # possible - so the pin in Directory.Build.props is what says this is the bytes we vetted.
    ACTUAL_SHA256="$(shasum -a 256 "${WEBRTC_ZIP}" | cut -d' ' -f1)"
    if [ "${ACTUAL_SHA256}" != "${ANTMEDIA_MAC_WEBRTC_SHA256}" ]; then
        rm -f "${WEBRTC_ZIP}"
        antmedia_fail "sha256 mismatch for ${ANTMEDIA_MAC_WEBRTC_URL}: got ${ACTUAL_SHA256}, pinned ${ANTMEDIA_MAC_WEBRTC_SHA256}. If the release was intentionally re-uploaded, re-vet it and update AntMediaMacWebRtcSha256; otherwise treat the asset as compromised."
    fi

    rm -rf "${WEBRTC_DIR}"
    mkdir -p "${WEBRTC_DIR}"
    unzip -q "${WEBRTC_ZIP}" -d "${WEBRTC_DIR}"
fi

# Fail here rather than at link time: without this slice the whole exercise is pointless, and the
# linker's complaint would be about missing symbols rather than a missing platform.
if ! /usr/libexec/PlistBuddy -c 'Print :AvailableLibraries' \
    "${WEBRTC_DIR}/WebRTC.xcframework/Info.plist" 2>/dev/null | grep -q maccatalyst; then
    antmedia_fail "${ANTMEDIA_MAC_WEBRTC_URL} has no maccatalyst slice"
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
XCODEBUILD_LOG="${WORK_DIR}/xcodebuild-maccatalyst.log"
# The full log goes to a file and only errors to the console; on failure the file has the
# context the grep filter drops.
if ! xcodebuild archive \
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
    > "${XCODEBUILD_LOG}" 2>&1; then
    grep -E '(error:|BUILD FAILED)' "${XCODEBUILD_LOG}" >&2 || true
    antmedia_fail "xcodebuild archive failed for Mac Catalyst; full log: ${XCODEBUILD_LOG}"
fi
grep -E 'BUILD SUCCEEDED' "${XCODEBUILD_LOG}" || true

FRAMEWORK="${ARCHIVE}/Products/Library/Frameworks/${SCHEME}.framework"
if [ ! -d "${FRAMEWORK}" ]; then
    antmedia_fail "xcodebuild reported success but ${FRAMEWORK} is missing"
fi

# Same guard as the iOS build: a Swift @objc class with no explicit Objective-C name is exported
# under its mangled name, and the .NET binding would fail to link against it in every consuming app.
for class in AMSClient AMSStreamInformation; do
    if ! nm -gU "${FRAMEWORK}/${SCHEME}" 2>/dev/null | grep -q "_OBJC_CLASS_\\\$_${class}\$"; then
        antmedia_fail "${SCHEME} does not export _OBJC_CLASS_\$_${class}"
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

# What the staged frameworks were built from. The up-to-date check reads it, and CI surfaces it
# in the build summary.
cat > "${PIN_FILE}" <<EOF
commit=$(git -C "${CHECKOUT}" rev-parse HEAD)
commit_date=$(git -C "${CHECKOUT}" show -s --format=%cs HEAD)
webrtc=${ANTMEDIA_MAC_WEBRTC_RELEASE}
inputs=${INPUTS_HASH}
EOF

echo "==> staged in ${DESTINATION}:"
du -sh "${DESTINATION}"/*.xcframework
