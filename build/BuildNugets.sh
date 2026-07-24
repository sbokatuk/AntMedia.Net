#!/usr/bin/env bash
set -euo pipefail

# Packs every AntMedia.Net package for net8, net9 and net10.
#
# Usage:
#   ./build/BuildNugets.sh                            # <props version>-local.<timestamp>
#   ./build/BuildNugets.sh 2.17.2-beta.4              # explicit package version
#   ./build/BuildNugets.sh 2.17.2-beta.4 android      # only AntMedia.Net.Android
#   ./build/BuildNugets.sh 2.17.2-beta.4 apple        # only the Apple bindings + AntMedia.Net
#
# The no-argument default is deliberately never a released version: packing plain 2.17.2 locally
# would collide with the published 2.17.2 in the global package cache, and the sample and device
# tests would silently restore the published bits instead of the freshly packed ones.
#
# The scope argument exists for CI, which packs Android on a Linux runner and the Apple packages
# on a macOS one. It defaults to 'all', minus iOS when not running on macOS.
#
# Run the native fetch scripts first — native/android/fetch-android.sh and, on macOS,
# native/ios/fetch-ios.sh and native/mac/fetch-mac.sh — or the bindings will pack without
# their native payload.
#
# Packages are written to ./artifacts.
#
# Each .NET SDK's workloads support only two target frameworks per platform (the .NET 9 band
# builds net8/net9, the .NET 10 band builds net10), so this runs two passes and merges them with
# build/merge-packages.py. global.json pins the .NET 9 SDK, and the SDK is resolved from the
# working directory, so the second pass runs from a scratch directory carrying its own global.json.
#
# On anything other than macOS the Apple packages are skipped: they need Xcode. Android packs
# everywhere.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
. "${SCRIPT_DIR}/pins.sh"

ROOT="${ANTMEDIA_REPO_ROOT}"
OUTPUT="${ROOT}/artifacts"

PASS1_BAND="net9"
PASS2_BAND="net10"
PASS2_SDK="10.0.100"

VERSION="${1:-}"
if [ -z "${VERSION}" ]; then
    VERSION="${ANTMEDIA_VERSION}-local.$(date +%Y%m%d%H%M%S)"
    echo "==> no version given; packing as ${VERSION}"
fi

# Validated before being interpolated into MSBuild arguments and package file names.
case "${VERSION}" in
    *[!A-Za-z0-9.+_-]*)
        echo "error: invalid version '${VERSION}'" >&2
        exit 1
        ;;
esac
VERSION_ARG="-p:Version=${VERSION}"

SCOPE="${2:-all}"
case "${SCOPE}" in
    all|android|apple) ;;
    *)
        echo "error: scope must be all, android or apple (got '${SCOPE}')" >&2
        exit 1
        ;;
esac

IS_MACOS=false
[ "$(uname -s)" = "Darwin" ] && IS_MACOS=true

PLATFORM_PROJECTS=()
PACK_METAPACKAGE=false

if [ "${SCOPE}" = "all" ] || [ "${SCOPE}" = "android" ]; then
    PLATFORM_PROJECTS+=("${ROOT}/src/AntMedia.Net.Android/AntMedia.Net.Android.csproj")
fi

if [ "${SCOPE}" = "all" ] || [ "${SCOPE}" = "apple" ]; then
    if [ "${IS_MACOS}" = true ]; then
        PLATFORM_PROJECTS+=("${ROOT}/src/AntMedia.Net.iOS/AntMedia.Net.iOS.csproj")
        PLATFORM_PROJECTS+=("${ROOT}/src/AntMedia.Net.Mac/AntMedia.Net.Mac.csproj")
        PACK_METAPACKAGE=true
    elif [ "${SCOPE}" = "apple" ]; then
        echo "::error::scope 'apple' requires macOS with Xcode" >&2
        exit 1
    else
        echo "==> not macOS: skipping the Apple and metapackage builds"
    fi
fi

# Scratch directories for the two passes, deliberately *outside* artifacts/: NuGet folder sources
# search subdirectories, so a pass directory under artifacts/ would let the metapackage restore
# resolve an unmerged single-target-framework package and fail with NU1202.
WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

PASS1_DIR="${WORK}/net9-pass"
PASS2_DIR="${WORK}/net10-pass"

SDK10_DIR="${WORK}/sdk10"
mkdir -p "${SDK10_DIR}"
cat > "${SDK10_DIR}/global.json" <<EOF
{ "sdk": { "version": "${PASS2_SDK}", "rollForward": "latestFeature" } }
EOF

# Packs one project in both SDK bands, then merges the two packages into artifacts/.
pack_and_merge() {
    local project="$1" name directory
    name="$(basename "${project}" .csproj)"
    directory="$(dirname "${project}")"

    # From clean, every time. An incremental pack of a project that both targets Android and
    # depends on an Android *binding* package rolls the binding's extracted library projects
    # (obj/*/lp/**) into this project's own .aar on the second run - which took AntMedia.Net from
    # 131 KB to 20 MB, silently, and only ever locally, because CI always starts empty.
    rm -rf "${directory}/obj" "${directory}/bin"

    echo "==> packing ${name} (${PASS1_BAND} band)"
    dotnet pack "${project}" \
        -c Release \
        -p:AntMediaSdkBand="${PASS1_BAND}" \
        ${VERSION_ARG} \
        -o "${PASS1_DIR}"

    echo "==> packing ${name} (${PASS2_BAND} band)"
    ( cd "${SDK10_DIR}" && dotnet pack "${project}" \
        -c Release \
        -p:AntMediaSdkBand="${PASS2_BAND}" \
        ${VERSION_ARG} \
        -o "${PASS2_DIR}" )

    echo "==> merging ${name}"
    python3 "${SCRIPT_DIR}/merge-packages.py" "${PASS1_DIR}" "${PASS2_DIR}" "${OUTPUT}"

    rm -rf "${PASS1_DIR}" "${PASS2_DIR}"
}

for project in "${PLATFORM_PROJECTS[@]}"; do
    pack_and_merge "${project}"
done

if [ "${PACK_METAPACKAGE}" = true ]; then
    # Order matters, and each step depends on the previous one's output being in artifacts/:
    #
    #   AntMedia.Net       pinned dependency on all three platform bindings
    #   AntMedia.Net.Maui  pinned dependency on AntMedia.Net
    #
    # They are PackageReferences rather than ProjectReferences so the packed dependency graph is
    # exactly what a consumer restores - which means each has to be packed before the next one
    # can restore. NuGet would otherwise resolve a stale copy, or the last published release.
    #
    # With scope 'apple' the Android package must be placed in artifacts/ by the caller; CI
    # downloads it from the pack-android job.
    pack_and_merge "${ROOT}/src/AntMedia.Net/AntMedia.Net.csproj"
    pack_and_merge "${ROOT}/src/AntMedia.Net.Maui/AntMedia.Net.Maui.csproj"
fi

echo "==> packages in ${OUTPUT}:"
ls -1 "${OUTPUT}"/*.nupkg
