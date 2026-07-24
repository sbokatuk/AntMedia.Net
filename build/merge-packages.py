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

    Usually read from the lib/<tfm>/ folders, but nuspec dependency groups are counted too:
    AntMedia.Net was a dependency-only metapackage with no lib/ at all until it grew the
    cross-platform client, and counting groups is what keeps the merge correct for any package
    shaped that way (a snupkg's nuspec, by contrast, declares no groups and is served by lib/).
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


def unexpected_assets(
    additional: zipfile.ZipFile, primary_names: set[str], carried: set[str]
) -> list[str]:
    """Assets in ADDITIONAL that the merge would silently drop.

    The merge deliberately carries only lib/<tfm>/ trees, because that is the only place these
    packages put per-framework payload today. If a future pass ever emits buildTransitive/,
    runtimes/, ref/ or anything else unique to it, dropping it with a green build would be the
    worst outcome - so it is an error instead. Packaging plumbing that legitimately differs
    between passes (the psmdcp has a fresh guid every pack) is exempt.
    """
    return sorted(
        name
        for name in additional.namelist()
        if name not in carried
        and name not in primary_names
        and not name.startswith(("_rels/", "package/"))
        and not name.endswith(".nuspec")
        and name != "[Content_Types].xml"
    )


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

        primary_names = set(primary.namelist())
        dropped = unexpected_assets(additional, primary_names, set(carried))
        if dropped:
            raise ValueError(
                f"{additional_path.name} carries assets outside lib/<tfm>/ that the merge does "
                f"not handle: {', '.join(dropped)}. Teach merge-packages.py about them rather "
                "than shipping a package with them silently missing."
            )

        additional_groups = dependency_groups(read_nuspec(additional))

        # A symbol package's nuspec declares no dependency groups - its target frameworks are its
        # lib/<tfm>/*.pdb trees - so it gets the pdbs carried across but no nuspec surgery, which
        # would otherwise bolt a <dependencies> element onto a nuspec that never has one.
        rewrite_nuspec = output_path.suffix != ".snupkg"

        with zipfile.ZipFile(output_path, "w", zipfile.ZIP_DEFLATED) as merged:
            for item in primary.infolist():
                data = primary.read(item.filename)
                if rewrite_nuspec and item.filename.endswith(".nuspec"):
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

    # The reverse gap: a package only the ADDITIONAL pass produced would otherwise vanish
    # without a word, since the loop above walks PRIMARY.
    primary_names = {p.name for p in packages}
    for orphan in sorted(
        p.name
        for ext in ("*.nupkg", "*.snupkg")
        for p in additional_dir.glob(ext)
        if p.name not in primary_names
    ):
        print(f"error: {orphan} exists only in {additional_dir}; nothing to merge it into", file=sys.stderr)
        failed = True

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
