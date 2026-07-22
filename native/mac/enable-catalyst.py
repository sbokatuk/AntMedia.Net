#!/usr/bin/env python3
"""Turn on Mac Catalyst for a target in WebRTCiOSSDK.xcodeproj.

Usage: enable-catalyst.py <project.xcodeproj> <target> <ios-deployment-target>

Two settings, both needed, neither obvious from the failure it produces:

  SUPPORTS_MACCATALYST        The target declares iOS platforms only, so without this xcodebuild
                              does not fail to *build* - it fails to resolve the destination:
                              "Unable to find a destination matching the provided destination
                              specifier", which reads like a typo in the -destination argument.

  IPHONEOS_DEPLOYMENT_TARGET  Mac Catalyst derives its availability from the iOS deployment
                              target, not the macOS one. Ant Media's project sits at iOS 13, and
                              AVCaptureDevice is Catalyst 14+, so the build fails with a dozen
                              "'AVCaptureDevice' is only available in Mac Catalyst 14.0 or newer".
                              Raising MACOSX_DEPLOYMENT_TARGET instead does nothing at all.

Uses plutil and plistlib rather than Ruby's xcodeproj gem, like native/ios/add-facade.py.
Idempotent.
"""

from __future__ import annotations

import plistlib
import subprocess
import sys
from pathlib import Path


def main(argv: list[str]) -> int:
    if len(argv) != 4:
        print(__doc__, file=sys.stderr)
        return 2

    pbxproj = Path(argv[1]) / "project.pbxproj"
    target_name = argv[2]
    deployment_target = argv[3]

    if not pbxproj.exists():
        sys.exit(f"error: {pbxproj} does not exist")

    xml = subprocess.run(
        ["plutil", "-convert", "xml1", str(pbxproj), "-o", "-"],
        capture_output=True,
        check=True,
    ).stdout
    project = plistlib.loads(xml)
    objects = project["objects"]

    target = next(
        (v for v in objects.values()
         if v.get("isa") == "PBXNativeTarget" and v.get("name") == target_name),
        None,
    )
    if target is None:
        sys.exit(f"error: target '{target_name}' not found in {pbxproj}")

    def configurations(owner: dict) -> list[dict]:
        return [objects[i] for i in objects[owner["buildConfigurationList"]]["buildConfigurations"]]

    changed = []
    for configuration in configurations(target):
        settings = configuration["buildSettings"]
        settings["SUPPORTS_MACCATALYST"] = "YES"
        settings["SUPPORTED_PLATFORMS"] = "iphoneos iphonesimulator macosx"
        settings["IPHONEOS_DEPLOYMENT_TARGET"] = deployment_target
        changed.append(configuration["name"])

    # The project-level configurations too: a target that inherits rather than overrides would
    # otherwise keep the old deployment target.
    for configuration in configurations(objects[project["rootObject"]]):
        configuration["buildSettings"]["IPHONEOS_DEPLOYMENT_TARGET"] = deployment_target

    with pbxproj.open("wb") as handle:
        plistlib.dump(project, handle)

    print(
        f"enable-catalyst: {target_name} [{', '.join(changed)}] "
        f"-> Mac Catalyst, iOS deployment target {deployment_target}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
