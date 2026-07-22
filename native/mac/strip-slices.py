#!/usr/bin/env python3
"""Copy an xcframework, keeping only the slices and architectures it is actually shipped for.

Usage: strip-slices.py <source.xcframework> <destination.xcframework> <substring> [<arch>]

The stock libwebrtc build ships iOS, iOS simulator, macOS and Mac Catalyst slices in one
xcframework — around 100 MB. AntMedia.Net.Mac serves Mac Catalyst only; the iOS slices come from
Ant Media's own build in AntMedia.Net.iOS, and macOS is not a target at all. Shipping the lot would
add tens of megabytes to every consumer for platforms this package does not serve.

<arch> thins the surviving slices further, with lipo. The Catalyst slice is a fat x86_64+arm64
binary, and this package is arm64-only — WebRTCiOSSDK cannot be built for x86_64 Catalyst at all,
because Ant Media renders through OpenGL when arch != arm64 and Mac Catalyst has no OpenGL. Half
the bytes are therefore for an architecture nothing here can link against.

Info.plist is rewritten to match on both counts: an xcframework advertising a slice that is not on
disk fails to resolve at build time rather than being quietly ignored, and one advertising an
architecture its binary no longer contains is selected and then fails at link time.
"""

from __future__ import annotations

import plistlib
import shutil
import subprocess
import sys
from pathlib import Path


def thin(slice_dir: Path, identifier: str, arch: str) -> str:
    """lipo the slice's framework binary down to one architecture. Returns the new identifier."""
    framework = next(slice_dir.glob("*.framework"))
    # Versions/Current/<name> in a Catalyst framework, <name> in an iOS one; the symlinked layout
    # means resolve() lands on the same file either way.
    binary = (framework / framework.stem).resolve()

    subprocess.run(["lipo", "-thin", arch, str(binary), "-output", str(binary)], check=True)

    # The identifier encodes the architectures, and Xcode does not parse it — but a directory
    # called ios-x86_64_arm64-maccatalyst holding an arm64-only binary is a trap for the next
    # person, so it is renamed to match.
    platform = identifier.split("-")[0]
    variant = identifier.split("-")[2:]
    renamed = "-".join([platform, arch, *variant])
    if renamed != identifier:
        slice_dir.rename(slice_dir.parent / renamed)
    return renamed


def main(argv: list[str]) -> int:
    if len(argv) not in (4, 5):
        print(__doc__, file=sys.stderr)
        return 2

    source, destination, keep = Path(argv[1]), Path(argv[2]), argv[3]
    arch = argv[4] if len(argv) == 5 else None

    with (source / "Info.plist").open("rb") as handle:
        info = plistlib.load(handle)

    libraries = info.get("AvailableLibraries", [])
    kept = [lib for lib in libraries if keep in lib["LibraryIdentifier"]]

    if not kept:
        available = ", ".join(lib["LibraryIdentifier"] for lib in libraries)
        sys.exit(f"error: no slice matching '{keep}' in {source}. Available: {available}")

    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)

    for library in kept:
        identifier = library["LibraryIdentifier"]
        shutil.copytree(source / identifier, destination / identifier, symlinks=True)

        if arch is not None:
            if arch not in library["SupportedArchitectures"]:
                sys.exit(f"error: {identifier} has no {arch} slice "
                         f"({', '.join(library['SupportedArchitectures'])})")
            library["LibraryIdentifier"] = thin(destination / identifier, identifier, arch)
            library["SupportedArchitectures"] = [arch]

    info["AvailableLibraries"] = kept
    with (destination / "Info.plist").open("wb") as handle:
        plistlib.dump(info, handle)

    dropped = [lib["LibraryIdentifier"] for lib in libraries if lib not in kept]
    print(f"strip-slices: kept {', '.join(lib['LibraryIdentifier'] for lib in kept)}"
          + (f"; dropped {', '.join(dropped)}" if dropped else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
