#!/usr/bin/env python3
"""Rewrite the frameworks inside an xcframework from the versioned layout to the shallow one.

Usage: flatten-frameworks.py <xcframework>

Mac Catalyst frameworks are built in the macOS versioned layout: the real content lives under
Versions/A, and the top level is symlinks - WebRTC -> Versions/Current/WebRTC, Resources ->
Versions/Current/Resources, and Versions/Current -> A.

NuGet packages cannot carry symlinks. Every one of those becomes a real copy when the package is
built, and the framework arrives at the consumer with content in both places at once, which fails
the app build at signing time:

    WebRTC.framework: bundle format is ambiguous (could be app or framework)
    Failed to codesign '.../WebRTC.framework'

So the layout is flattened here, before packing: the top-level copies are kept - they are what
dyld resolves @rpath/WebRTC.framework/WebRTC to, and what a shallow bundle is - and Versions/ is
removed. Shallow is the layout iOS frameworks already use, and codesign accepts it on macOS too.
Doing it here rather than in the consuming build keeps it out of every app's build, and keeps the
package honest about what it ships.

Idempotent: an xcframework whose slices are already shallow is left alone.
"""

from __future__ import annotations

import re
import shutil
import subprocess
import sys
from pathlib import Path

# @rpath/WebRTC.framework/Versions/A/WebRTC — the install name a versioned framework is built with,
# and the path dyld would go looking for at launch.
VERSIONED = re.compile(r"^(@rpath/([^/]+)\.framework)/Versions/[^/]+/(.+)$")


def rewrite_install_names(framework: Path) -> None:
    """Point the binary's own id, and any framework it links, at the shallow paths.

    Flattening the directory layout is not enough on its own. Every Mach-O carries the paths it
    was built with, so a flattened framework still announces itself as
    @rpath/WebRTC.framework/Versions/A/WebRTC, and the app fails at launch rather than at build:

        dyld: Library not loaded: @rpath/WebRTCiOSSDK.framework/Versions/A/WebRTCiOSSDK
              tried: .../Contents/Frameworks/... (no such file)

    Only @rpath entries are touched, and only versioned ones, so system frameworks and absolute
    paths are left exactly as they are.
    """
    binary = framework / framework.stem

    subprocess.run(
        ["install_name_tool", "-id", f"@rpath/{framework.name}/{framework.stem}", str(binary)],
        check=True, capture_output=True)

    listing = subprocess.run(["otool", "-L", str(binary)],
                             check=True, capture_output=True, text=True).stdout

    for line in listing.splitlines()[1:]:
        dependency = line.strip().split(" (compatibility")[0]
        match = VERSIONED.match(dependency)
        if match:
            shallow = f"{match.group(1)}/{match.group(3)}"
            subprocess.run(["install_name_tool", "-change", dependency, shallow, str(binary)],
                           check=True, capture_output=True)

    # install_name_tool invalidates the signature it did not produce; the consuming app re-signs
    # every framework it bundles, but an ad-hoc signature here keeps the artifact loadable on its
    # own and keeps `codesign -v` from reporting a broken bundle mid-build.
    subprocess.run(["codesign", "--force", "--sign", "-", str(framework)],
                   check=True, capture_output=True)


def flatten(framework: Path) -> bool:
    versions = framework / "Versions"
    if not versions.is_dir():
        return False

    current = versions / "Current"
    if not current.exists():
        sys.exit(f"error: {framework} has Versions but no Versions/Current")

    # Replace each top-level symlink with the file or directory it points at. Resolving through
    # Current rather than reading the link target keeps this working whichever version is current.
    for entry in sorted(current.iterdir()):
        target = framework / entry.name
        if target.is_symlink():
            target.unlink()
        elif target.exists():
            # Already a real copy, courtesy of a previous NuGet round trip.
            continue

        if entry.is_dir():
            shutil.copytree(entry, target)
        else:
            shutil.copy2(entry, target)

    shutil.rmtree(versions)

    # The versioned layout keeps Info.plist and the privacy manifest under Resources/; a shallow
    # bundle keeps them at the root. Leaving them where they were gives a framework whose
    # Info.plist nothing can find, which surfaces much later as a bundle that fails to load.
    resources = framework / "Resources"
    if resources.is_dir():
        for entry in sorted(resources.iterdir()):
            entry.rename(framework / entry.name)
        resources.rmdir()

    rewrite_install_names(framework)
    return True


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2

    xcframework = Path(argv[1])
    frameworks = sorted(xcframework.glob("*/*.framework"))
    if not frameworks:
        sys.exit(f"error: no frameworks found in {xcframework}")

    flattened = [f for f in frameworks if flatten(f)]
    print(f"flatten-frameworks: {xcframework.name} — "
          + (f"flattened {len(flattened)}/{len(frameworks)} slice(s)" if flattened
             else "already shallow"))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
