#!/usr/bin/env python3
"""Copy an xcframework, keeping only the slices whose identifier matches a filter.

Usage: strip-slices.py <source.xcframework> <destination.xcframework> <substring>

The stock libwebrtc build ships iOS, iOS simulator, macOS and Mac Catalyst slices in one
xcframework — around 100 MB. AntMedia.Net.Mac serves Mac Catalyst only; the iOS slices come from
Ant Media's own build in AntMedia.Net.iOS, and macOS is not a target at all. Shipping the lot would
add tens of megabytes to every consumer for platforms this package does not serve.

Info.plist is rewritten to list only the slices kept, because an xcframework advertising a slice
that is not on disk fails to resolve at build time rather than being quietly ignored.
"""

from __future__ import annotations

import plistlib
import shutil
import sys
from pathlib import Path


def main(argv: list[str]) -> int:
    if len(argv) != 4:
        print(__doc__, file=sys.stderr)
        return 2

    source, destination, keep = Path(argv[1]), Path(argv[2]), argv[3]

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
        shutil.copytree(source / library["LibraryIdentifier"],
                        destination / library["LibraryIdentifier"])

    info["AvailableLibraries"] = kept
    with (destination / "Info.plist").open("wb") as handle:
        plistlib.dump(info, handle)

    dropped = [lib["LibraryIdentifier"] for lib in libraries if lib not in kept]
    print(f"strip-slices: kept {', '.join(lib['LibraryIdentifier'] for lib in kept)}"
          + (f"; dropped {', '.join(dropped)}" if dropped else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
