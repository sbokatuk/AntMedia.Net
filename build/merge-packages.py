#!/usr/bin/env python3
"""Merge target-framework assets from one set of NuGet packages into another.

No single .NET SDK can build net8, net9 and net10 for a given platform: each SDK's workload
supports the current target framework and the previous one only. Verified for this repository:

                SDK 9 band                          SDK 10 band
    Android     net8.0-android34.0, net9.0-*35.0    net10.0-android36.0
    iOS         net8.0-ios18.0,     net9.0-ios18.0  net10.0-ios26.0

The packages are therefore built in two passes (see BuildNugets.sh) and merged here into one
package per id.

For every package in PRIMARY, any lib/<tfm>/ tree that exists in the matching ADDITIONAL package
but not in PRIMARY is copied across, and a dependency group is added to the nuspec so NuGet
advertises the new target framework. Everything else comes from PRIMARY unchanged.

Usage: merge-packages.py PRIMARY_DIR ADDITIONAL_DIR OUTPUT_DIR
"""

from __future__ import annotations

import re
import shutil
import sys
import zipfile
from pathlib import Path

BOM = b"\xef\xbb\xbf"


def read_nuspec(package: zipfile.ZipFile) -> str:
    """The package's .nuspec as text, or an empty string if it somehow has none."""
    for name in package.namelist():
        if name.endswith(".nuspec"):
            return package.read(name).decode("utf-8-sig")
    return ""


def dependency_groups(nuspec: str) -> dict[str, str]:
    """Maps each declared target framework to the raw XML of its <group> element.

    Kept as raw text so a group carrying <dependency> children can be copied across verbatim.
    """
    pattern = re.compile(
        r'<group\s+targetFramework="(?P<tfm>[^"]+)"\s*(?:/>|>(?P<body>.*?)</group>)',
        re.DOTALL,
    )
    return {match["tfm"]: match.group(0) for match in pattern.finditer(nuspec)}


def target_frameworks(package: zipfile.ZipFile) -> set[str]:
    """Every target framework the package serves.

    Usually read from the lib/<tfm>/ folders, but a dependency-only package - the AntMedia.Net
    metapackage - has no lib/ at all and declares its target frameworks solely as nuspec
    dependency groups. Ignoring those would silently drop the .NET 10 leg of the metapackage
    during the merge.
    """
    found = set()
    for name in package.namelist():
        parts = name.split("/")
        if len(parts) > 2 and parts[0] == "lib" and parts[2]:
            found.add(parts[1])

    return found | dependency_groups(read_nuspec(package)).keys()


def add_dependency_groups(nuspec: str, frameworks: list[str], source: dict[str, str]) -> str:
    """Add a <group targetFramework="..."> for each framework.

    Groups present in `source` (the additional package's nuspec) are copied verbatim so their
    <dependency> children survive; anything else becomes an empty group, which is all a package
    whose payload is lib/ assets needs in order to advertise the target framework.

    Edited as text rather than via ElementTree so that the rest of the nuspec - including
    attribute order and the xmlns declaration NuGet emits - is preserved byte for byte.
    """
    groups = "".join(
        "\n      " + source.get(tfm, f'<group targetFramework="{tfm}" />') for tfm in frameworks
    )

    if "</dependencies>" in nuspec:
        # Consume the existing indentation so the closing tag stays on its own tidy line.
        return re.sub(r"\s*</dependencies>", f"{groups}\n    </dependencies>", nuspec, count=1)

    # No <dependencies> element at all: add one at the end of <metadata>.
    if "</metadata>" in nuspec:
        return nuspec.replace(
            "</metadata>",
            f"    <dependencies>{groups}\n    </dependencies>\n  </metadata>",
            1,
        )

    raise ValueError("nuspec has no </metadata> element")


def merge(primary_path: Path, additional_path: Path, output_path: Path) -> list[str]:
    with zipfile.ZipFile(primary_path) as primary, zipfile.ZipFile(additional_path) as additional:
        missing = sorted(target_frameworks(additional) - target_frameworks(primary))
        if not missing:
            shutil.copy2(primary_path, output_path)
            return []

        carried = [
            name
            for name in additional.namelist()
            if any(name.startswith(f"lib/{tfm}/") for tfm in missing)
        ]

        additional_groups = dependency_groups(read_nuspec(additional))

        with zipfile.ZipFile(output_path, "w", zipfile.ZIP_DEFLATED) as merged:
            for item in primary.infolist():
                data = primary.read(item.filename)
                if item.filename.endswith(".nuspec"):
                    had_bom = data.startswith(BOM)
                    rewritten = add_dependency_groups(
                        data.decode("utf-8-sig"), missing, additional_groups
                    )
                    data = rewritten.encode("utf-8")
                    if had_bom:
                        data = BOM + data
                merged.writestr(item, data)

            for name in carried:
                merged.writestr(additional.getinfo(name), additional.read(name))

    return missing


def main(argv: list[str]) -> int:
    if len(argv) != 4:
        print(__doc__, file=sys.stderr)
        return 2

    primary_dir, additional_dir, output_dir = (Path(p) for p in argv[1:])
    output_dir.mkdir(parents=True, exist_ok=True)

    packages = sorted(p for ext in ("*.nupkg", "*.snupkg") for p in primary_dir.glob(ext))
    if not packages:
        print(f"error: no packages found in {primary_dir}", file=sys.stderr)
        return 1

    failed = False
    for package in packages:
        # Package file names are <id>.<version>.<ext>, identical across both passes.
        counterpart = additional_dir / package.name
        if not counterpart.exists():
            print(f"error: {package.name} has no counterpart in {additional_dir}", file=sys.stderr)
            failed = True
            continue

        added = merge(package, counterpart, output_dir / package.name)
        print(f"{package.name}: added {', '.join(added) if added else 'nothing'}")

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
