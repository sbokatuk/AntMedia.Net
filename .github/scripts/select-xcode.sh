#!/usr/bin/env bash
set -euo pipefail

# Selects the Xcode that carries the iOS SDK our net10 target framework is built against.
#
# Usage: select-xcode.sh [ios-sdk-version]     # defaults to 26.0
#
# WHY THIS IS PINNED
# The packages advertise net10.0-ios26.0, and .NET for iOS will only build that target framework
# against an Xcode carrying the iOS 26.0 SDK:
#
#     error : This version of .NET for iOS (26.0.11017) requires Xcode 26.0.
#             The current version of Xcode is 26.5.
#
# The runner image default is newer than that, so without this step the iOS smoke test cannot
# build the very target framework the package ships. Selecting it explicitly also means an image
# update that moves the default Xcode cannot silently change what the build produces.
#
# Resolved by glob rather than a hard-coded /Applications/Xcode_26.0.app: the images carry patch
# releases (26.0.1 today, 26.0.2 tomorrow) and a hard-coded path silently goes stale when they
# re-roll. Any 26.0.x carries the iOS 26.0 SDK, which is what the target framework needs.

IOS_SDK_VERSION="${1:-26.0}"

# Newest patch release first, so 26.0.10 would win over 26.0.9 rather than sorting lexically.
XCODE_APP="$(ls -d "/Applications/Xcode_${IOS_SDK_VERSION}"*.app 2>/dev/null | sort -V | tail -1 || true)"

if [ -z "${XCODE_APP}" ]; then
    echo "::error::no Xcode carrying the iOS ${IOS_SDK_VERSION} SDK is installed on this runner" >&2
    echo "available:" >&2
    ls -d /Applications/Xcode*.app >&2 || true
    exit 1
fi

sudo xcode-select -s "${XCODE_APP}"

echo "selected ${XCODE_APP}"
xcodebuild -version
