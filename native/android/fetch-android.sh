#!/usr/bin/env bash
set -euo pipefail

# Builds the Ant Media WebRTC Android framework .aar and drops it where the binding project
# expects it (src/AntMedia.Net.Android/Jars/webrtc-android-framework.aar).
#
# Usage:
#   ./native/android/fetch-android.sh              # tag from Directory.Build.props
#   ./native/android/fetch-android.sh v2.17.1      # build a different tag
#
# Upstream publishes io.antmedia:webrtc-android-framework to Maven Central, but not for every tag,
# so the .aar is built from source. The framework vendors both io.antmedia.* and org.webrtc Java
# sources plus jniLibs for arm64-v8a, armeabi-v7a, x86 and x86_64 — one .aar is the whole Android
# surface, and the x86_64 slice is what lets the emulator smoke test run.
#
# Requires: git, a JDK (17), and an Android SDK (ANDROID_HOME or ANDROID_SDK_ROOT).

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
. "${SCRIPT_DIR}/../../build/pins.sh"

TAG="${1:-${ANTMEDIA_ANDROID_TAG}}"
WORK_DIR="${ANTMEDIA_REPO_ROOT}/native/build/android"
CHECKOUT="${WORK_DIR}/WebRTC-Android-SDK"
DESTINATION="${ANTMEDIA_REPO_ROOT}/src/AntMedia.Net.Android/Jars/webrtc-android-framework.aar"

ANDROID_SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
if [ -z "${ANDROID_SDK}" ] || [ ! -d "${ANDROID_SDK}" ]; then
    echo "::error::ANDROID_HOME (or ANDROID_SDK_ROOT) must point at an Android SDK" >&2
    exit 1
fi

echo "==> Ant Media Android SDK ${TAG}"

# A shallow clone of just the pinned tag. Re-fetching into an existing checkout keeps the CI cache
# useful when only the tag moved.
if [ -d "${CHECKOUT}/.git" ]; then
    git -C "${CHECKOUT}" fetch --depth 1 origin "refs/tags/${TAG}:refs/tags/${TAG}" --force
else
    rm -rf "${CHECKOUT}"
    mkdir -p "${WORK_DIR}"
    git clone --depth 1 --branch "${TAG}" "${ANTMEDIA_ANDROID_REPOSITORY}" "${CHECKOUT}"
fi

git -C "${CHECKOUT}" checkout --force "tags/${TAG}"
# Drops the previous run's overlay and any build leftovers, so a re-run is not affected by what
# the last one wrote. Build outputs live under build/, which is preserved for incremental speed.
git -C "${CHECKOUT}" clean -fd -e build -e '**/build'

COMMIT="$(git -C "${CHECKOUT}" rev-parse HEAD)"
echo "==> commit ${COMMIT}"

cp "${SCRIPT_DIR}/overlay/settings.gradle" "${CHECKOUT}/settings.gradle"

# scripts/deploy-variables.gradle reads local.properties and copies every key into project ext,
# so this file carries the SDK location and nothing else — a stray signing key here would be
# picked up by the publish plugin.
printf 'sdk.dir=%s\n' "${ANDROID_SDK}" > "${CHECKOUT}/local.properties"

echo "==> assembling"
# assembleRelease only: 'build' would additionally run lint and the unit tests, which need the
# test fixtures upstream keeps out of the release path and add several minutes for no benefit here.
( cd "${CHECKOUT}" && ./gradlew --no-daemon :webrtc-android-framework:assembleRelease )

AAR="${CHECKOUT}/webrtc-android-framework/build/outputs/aar/webrtc-android-framework-release.aar"
if [ ! -f "${AAR}" ]; then
    echo "::error::gradle reported success but ${AAR} is missing" >&2
    exit 1
fi

mkdir -p "$(dirname "${DESTINATION}")"
cp "${AAR}" "${DESTINATION}"

# A jar of the Java sources, purely for their javadoc: the binding generator extracts it and emits
# real XML documentation, so IntelliSense shows what a member does instead of nothing. Built here
# with `jar` rather than through gradle's androidSourcesJar task, which is defined in
# publish-remote.gradle alongside the signing and Maven configuration and drags that in with it.
SOURCES="${CHECKOUT}/webrtc-android-framework/src/main/java"
if [ -d "${SOURCES}" ]; then
    ( cd "${SOURCES}" && jar cf "${DESTINATION%.aar}-sources.jar" . )
    echo "==> sources jar: $(du -h "${DESTINATION%.aar}-sources.jar" | cut -f1)"
else
    echo "::warning::${SOURCES} not found; the binding will have no documentation" >&2
fi

# The version the framework declares for itself, which is not always the tag name — v2.17.2 is
# built from sources that still say PUBLISH_VERSION = '2.17.1'. Recorded for the build summary.
SDK_VERSION="$(sed -n "s/.*PUBLISH_VERSION *= *'\\([^']*\\)'.*/\\1/p" \
    "${CHECKOUT}/webrtc-android-framework/build.gradle" | head -1)"

cat > "${WORK_DIR}/pin.txt" <<EOF
tag=${TAG}
commit=${COMMIT}
publish_version=${SDK_VERSION}
EOF

echo "==> ${DESTINATION} ($(du -h "${DESTINATION}" | cut -f1), framework version ${SDK_VERSION:-unknown})"
