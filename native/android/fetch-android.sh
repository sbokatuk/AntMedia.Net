#!/usr/bin/env bash
set -euo pipefail

# Builds the Ant Media WebRTC Android framework .aar and drops it where the binding project
# expects it (src/AntMedia.Net.Android/Jars/webrtc-android-framework.aar).
#
# Usage:
#   ./native/android/fetch-android.sh              # tag + commit from Directory.Build.props
#   ./native/android/fetch-android.sh -f           # rebuild even when already up to date
#   ./native/android/fetch-android.sh v2.17.1      # build a different tag (skips the commit pin)
#
# Upstream publishes io.antmedia:webrtc-android-framework to Maven Central, but not for every tag,
# so the .aar is built from source. The framework vendors both io.antmedia.* and org.webrtc Java
# sources plus jniLibs for arm64-v8a, armeabi-v7a, x86 and x86_64 — one .aar is the whole Android
# surface, and the x86_64 slice is what lets the emulator smoke test run.
#
# Requires: git, a JDK 17 (JAVA_HOME), and an Android SDK (ANDROID_HOME or ANDROID_SDK_ROOT).

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
. "${SCRIPT_DIR}/../../build/pins.sh"

FORCE=false
TAG=""
for arg in "$@"; do
    case "${arg}" in
        -f|--force) FORCE=true ;;
        *) TAG="${arg}" ;;
    esac
done

TAG="${TAG:-${ANTMEDIA_ANDROID_TAG}}"

# The commit pin applies whenever the *pinned* tag is being built — CI passes it explicitly, so
# this cannot key off whether an argument was given. A deliberately different tag builds an
# arbitrary upstream state the pin says nothing about.
EXPECTED_COMMIT=""
if [ "${TAG}" = "${ANTMEDIA_ANDROID_TAG}" ]; then
    EXPECTED_COMMIT="${ANTMEDIA_ANDROID_COMMIT}"
fi

WORK_DIR="${ANTMEDIA_REPO_ROOT}/native/build/android"
CHECKOUT="${WORK_DIR}/WebRTC-Android-SDK"
DESTINATION="${ANTMEDIA_REPO_ROOT}/src/AntMedia.Net.Android/Jars/webrtc-android-framework.aar"
PIN_FILE="${WORK_DIR}/pin.txt"

# Everything that shapes the output beyond the upstream sources themselves. A changed script or
# overlay invalidates the up-to-date check the same way it invalidates the CI cache key.
INPUTS_HASH="$(cat "${BASH_SOURCE[0]}" "${SCRIPT_DIR}/overlay/"* | shasum -a 256 | cut -d' ' -f1)"

# Skips the multi-minute gradle build when the staged .aar already matches the pins. CI gets the
# same effect from its cache key; this is for local re-runs.
if [ "${FORCE}" = false ] && [ -f "${DESTINATION}" ] && [ -f "${PIN_FILE}" ]; then
    if grep -q "^tag=${TAG}$" "${PIN_FILE}" \
        && { [ -z "${EXPECTED_COMMIT}" ] || grep -q "^commit=${EXPECTED_COMMIT}$" "${PIN_FILE}"; } \
        && grep -q "^inputs=${INPUTS_HASH}$" "${PIN_FILE}"; then
        echo "==> ${DESTINATION} already built from ${TAG}; use -f to rebuild"
        exit 0
    fi
fi

ANDROID_SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
if [ -z "${ANDROID_SDK}" ] || [ ! -d "${ANDROID_SDK}" ]; then
    antmedia_fail "ANDROID_HOME (or ANDROID_SDK_ROOT) must point at an Android SDK"
fi

# Upstream builds with AGP 7.4.2 / Gradle 7.5, which a JDK newer than 18 refuses deep inside
# gradle with an unrelated-looking "Unsupported class file major version" — so the check happens
# here, with a message that names the actual problem.
if ! command -v java >/dev/null 2>&1; then
    antmedia_fail "no java on PATH. Upstream's gradle build needs a JDK 17 - install one and set JAVA_HOME."
fi
JAVA_MAJOR="$(java -version 2>&1 | sed -n 's/.*version "\([0-9]*\).*/\1/p' | head -1)"
if [ -n "${JAVA_MAJOR}" ] && { [ "${JAVA_MAJOR}" -lt 11 ] || [ "${JAVA_MAJOR}" -gt 18 ]; }; then
    antmedia_fail "JDK ${JAVA_MAJOR} found, but upstream's AGP 7.4.2/Gradle 7.5 build needs 11-18 (17 is what CI uses). Point JAVA_HOME at a JDK 17."
fi

echo "==> Ant Media Android SDK ${TAG}"

# A shallow clone of just the pinned tag. Re-fetching into an existing checkout preserves the
# gradle build/ directories for incremental speed on local re-runs.
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

# Tags are mutable; the commit pin is not. Without this a re-pointed upstream tag would build -
# and execute the gradle scripts of - different sources with nothing failing anywhere.
if [ -n "${EXPECTED_COMMIT}" ] && [ "${COMMIT}" != "${EXPECTED_COMMIT}" ]; then
    antmedia_fail "tag ${TAG} resolves to ${COMMIT}, but Directory.Build.props pins ${EXPECTED_COMMIT}. If upstream re-pointed the tag on purpose, update AntMediaAndroidCommit; otherwise treat the tag as compromised."
fi

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
    antmedia_fail "gradle reported success but ${AAR} is missing"
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
    antmedia_warn "${SOURCES} not found; the binding will have no documentation"
fi

# The version the framework declares for itself, which is not always the tag name — v2.17.2 is
# built from sources that still say PUBLISH_VERSION = '2.17.1'.
SDK_VERSION="$(sed -n "s/.*PUBLISH_VERSION *= *'\\([^']*\\)'.*/\\1/p" \
    "${CHECKOUT}/webrtc-android-framework/build.gradle" | head -1)"

# What the staged .aar was built from. The up-to-date check above reads it, and CI surfaces it
# in the build summary.
cat > "${PIN_FILE}" <<EOF
tag=${TAG}
commit=${COMMIT}
publish_version=${SDK_VERSION}
inputs=${INPUTS_HASH}
EOF

echo "==> ${DESTINATION} ($(du -h "${DESTINATION}" | cut -f1), framework version ${SDK_VERSION:-unknown})"
