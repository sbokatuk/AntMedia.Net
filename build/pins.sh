#!/usr/bin/env bash
# Reads the native SDK pins out of Directory.Build.props and exports them.
#
# Directory.Build.props is the single source of truth: MSBuild reads it when building, and the
# fetch scripts, BuildNugets.sh and the CI cache keys read it through here. Sourced, not executed:
#
#   . "$(dirname "$0")/../build/pins.sh"
#
# Exports ANTMEDIA_VERSION, ANTMEDIA_ANDROID_REPOSITORY, ANTMEDIA_ANDROID_TAG,
# ANTMEDIA_ANDROID_COMMIT, ANTMEDIA_IOS_REPOSITORY, ANTMEDIA_IOS_COMMIT,
# ANTMEDIA_MAC_WEBRTC_RELEASE, ANTMEDIA_MAC_WEBRTC_SHA256 and ANTMEDIA_MAC_WEBRTC_URL,
# plus the antmedia_fail/antmedia_warn helpers the fetch scripts share.

# shellcheck disable=SC2034  # consumers use these; shellcheck cannot see across the source.

ANTMEDIA_REPO_ROOT="${ANTMEDIA_REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
ANTMEDIA_PROPS="${ANTMEDIA_REPO_ROOT}/Directory.Build.props"

# grep -P is unavailable on macOS, so this stays with sed. Reads the first occurrence only, which
# matters because Directory.Build.props mentions some of these names in comments.
antmedia_prop() {
    local name="$1" value
    value="$(sed -n "s|.*<${name}>\\([^<]*\\)</${name}>.*|\\1|p" "${ANTMEDIA_PROPS}" | head -1)"
    if [ -z "${value}" ]; then
        echo "error: <${name}> not found in ${ANTMEDIA_PROPS}" >&2
        return 1
    fi
    printf '%s' "${value}"
}

ANTMEDIA_VERSION="$(antmedia_prop AntMediaVersion)" || return 1 2>/dev/null || exit 1
ANTMEDIA_ANDROID_REPOSITORY="$(antmedia_prop AntMediaAndroidRepository)" || return 1 2>/dev/null || exit 1
ANTMEDIA_ANDROID_TAG="$(antmedia_prop AntMediaAndroidTag)" || return 1 2>/dev/null || exit 1
ANTMEDIA_ANDROID_COMMIT="$(antmedia_prop AntMediaAndroidCommit)" || return 1 2>/dev/null || exit 1
ANTMEDIA_IOS_REPOSITORY="$(antmedia_prop AntMediaIosRepository)" || return 1 2>/dev/null || exit 1
ANTMEDIA_IOS_COMMIT="$(antmedia_prop AntMediaIosCommit)" || return 1 2>/dev/null || exit 1
ANTMEDIA_MAC_WEBRTC_RELEASE="$(antmedia_prop AntMediaMacWebRtcRelease)" || return 1 2>/dev/null || exit 1
ANTMEDIA_MAC_WEBRTC_SHA256="$(antmedia_prop AntMediaMacWebRtcSha256)" || return 1 2>/dev/null || exit 1
# The url is derived here rather than kept in the props: a props copy would interpolate one
# property into another and hardcode the M-number, which is exactly the drift trap this avoids.
ANTMEDIA_MAC_WEBRTC_URL="https://github.com/stasel/WebRTC/releases/download/${ANTMEDIA_MAC_WEBRTC_RELEASE}/WebRTC-M${ANTMEDIA_MAC_WEBRTC_RELEASE%%.*}.xcframework.zip"

# Shared diagnostics: GitHub-annotated in CI, plain text locally, where the ::error:: syntax
# would just print literally.
antmedia_fail() {
    if [ -n "${GITHUB_ACTIONS:-}" ]; then
        echo "::error::$*" >&2
    else
        echo "error: $*" >&2
    fi
    exit 1
}

antmedia_warn() {
    if [ -n "${GITHUB_ACTIONS:-}" ]; then
        echo "::warning::$*" >&2
    else
        echo "warning: $*" >&2
    fi
}

export ANTMEDIA_REPO_ROOT ANTMEDIA_VERSION
export ANTMEDIA_ANDROID_REPOSITORY ANTMEDIA_ANDROID_TAG ANTMEDIA_ANDROID_COMMIT
export ANTMEDIA_IOS_REPOSITORY ANTMEDIA_IOS_COMMIT
export ANTMEDIA_MAC_WEBRTC_RELEASE ANTMEDIA_MAC_WEBRTC_SHA256 ANTMEDIA_MAC_WEBRTC_URL
