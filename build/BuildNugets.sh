#!/usr/bin/env bash
set -euo pipefail

# Packs every AntMedia.Net package for net8, net9 and net10.
#
# Usage:
#   ./build/BuildNugets.sh                            # version from Directory.Build.props
#   ./build/BuildNugets.sh 2.17.2-beta.4              # explicit package version
#   ./build/BuildNugets.sh 2.17.2-beta.4 android      # only AntMedia.Net.Android
#   ./build/BuildNugets.sh 2.17.2-beta.4 apple        # only AntMedia.Net.iOS + AntMedia.Net
#
# The scope argument exists for CI, which packs Android on a Linux runner and the Apple packages
# on a macOS one. It defaults to 'all', minus iOS when not running on macOS.
#
# Run the native fetch scripts first — native/android/fetch-android.sh and, on macOS,
# native/ios/fetch-ios.sh — or the bindings will pack without their native payload.
#
# Packages are written to ./artifacts.
#
# Each .NET SDK's workloads support only two target frameworks per platform (the .NET 9 band
# builds net8/net9, the .NET 10 band builds net10), so this runs two passes and merges them with
# build/merge-packages.py. global.json pins the .NET 9 SDK, and the SDK is resolved from the
# working directory, so the second pass runs from a scratch directory carrying its own global.json.
#
# On anything other than macOS the iOS packages are skipped: they need Xcode. Android packs
# everywhere.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
. "${SCRIPT_DIR}/pins.sh"

ROOT="${ANTMEDIA_REPO_ROOT}"
OUTPUT="${ROOT}/artifacts"

PASS1_BAND="net9"
PASS2_BAND="net10"
PASS2_SDK="10.0.100"

VERSION="${1:-}"
VERSION_ARG=""
if [ -n "${VERSION}" ]; then
    # Validated before being interpolated into MSBuild arguments and package file names.
    case "${VERSION}" in
        *[!A-Za-z0-9.+_-]*)
            echo "error: invalid version '${VERSION}'" >&2
            exit 1
            ;;
    esac
    VERSION_ARG="-p:Version=${VERSION}"
fi

SCOPE="${2:-all}"
case "${SCOPE}" in
    all|android|apple) ;;
    *)
        echo "error: scope must be all, android or apple (got '${SCOPE}')" >&2
        exit 1
        ;;
esac

PROJECTS=()

if [ "${SCOPE}" = "all" ] || [ "${SCOPE}" = "android" ]; then
    PROJECTS+=("${ROOT}/src/AntMedia.Net.Android/AntMedia.Net.Android.csproj")
fi

if [ "${SCOPE}" = "all" ] || [ "${SCOPE}" = "apple" ]; then
    if [ "$(uname -s)" = "Darwin" ]; then
        PROJECTS+=("${ROOT}/src/AntMedia.Net.iOS/AntMedia.Net.iOS.csproj")
        # The metapackage carries no assemblies, only per-target-framework dependencies on the
        # platform packages — but it still targets the iOS TFMs, so restoring it needs the iOS
        # workload and therefore macOS. It is packed alongside the iOS package for that reason.
        PROJECTS+=("${ROOT}/src/AntMedia.Net/AntMedia.Net.csproj")
    elif [ "${SCOPE}" = "apple" ]; then
        echo "::error::scope 'apple' requires macOS with Xcode" >&2
        exit 1
    else
        echo "==> not macOS: skipping the iOS and metapackage builds"
    fi
fi

PASS1_DIR="${OUTPUT}/.net9-pass"
PASS2_DIR="${OUTPUT}/.net10-pass"
rm -rf "${PASS1_DIR}" "${PASS2_DIR}"

SDK10_DIR="$(mktemp -d)"
trap 'rm -rf "${SDK10_DIR}"' EXIT
cat > "${SDK10_DIR}/global.json" <<EOF
{ "sdk": { "version": "${PASS2_SDK}", "rollForward": "latestFeature" } }
EOF

for project in "${PROJECTS[@]}"; do
    name="$(basename "${project}" .csproj)"

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
done

echo "==> merging target frameworks"
python3 "${SCRIPT_DIR}/merge-packages.py" "${PASS1_DIR}" "${PASS2_DIR}" "${OUTPUT}"

rm -rf "${PASS1_DIR}" "${PASS2_DIR}"

echo "==> packages in ${OUTPUT}:"
ls -1 "${OUTPUT}"/*.nupkg
