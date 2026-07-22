#!/usr/bin/env python3
"""Add the @objc facade sources to a target in WebRTCiOSSDK.xcodeproj.

Dropping a .swift file into the source directory is not enough: an Xcode target lists its files
explicitly in project.pbxproj, so a file that is not referenced there is simply not compiled.

Usage: add-facade.py <project.xcodeproj> <target> <file.swift> [file.swift ...]

Idempotent: re-running against a project that already has the files is a no-op, so a cached
checkout can be reused without accumulating duplicate build phase entries.

WHY NOT THE xcodeproj GEM
-------------------------
The obvious tool is Ruby's xcodeproj gem, and this script replaced one that used it. The gem meant
carrying a second language toolchain for 58 lines of work, plus a `gem install` step - which broke
twice: once because macOS ships a root-owned gem directory that a plain install cannot write to,
and once because the system Ruby is 2.6 and the script used a 2.7 method.

project.pbxproj is just a property list in OpenStep format. macOS ships plutil, which converts it
to XML, and Python's plistlib reads and writes that. Xcode and xcodebuild both accept a pbxproj
left in XML form, so it never has to be converted back. Nothing here is installed: plutil is part
of macOS and plistlib is in the standard library, and Python was already required by the build.
"""

from __future__ import annotations

import os
import plistlib
import subprocess
import sys
import uuid
from pathlib import Path


def load(pbxproj: Path) -> dict:
    """The project as a dict, whatever format it is stored in."""
    xml = subprocess.run(
        ["plutil", "-convert", "xml1", str(pbxproj), "-o", "-"],
        capture_output=True,
        check=True,
    ).stdout
    return plistlib.loads(xml)


def object_id(existing: set[str]) -> str:
    """A fresh 24-character uppercase hex id, which is what pbxproj uses to key its objects."""
    while True:
        candidate = uuid.uuid4().hex[:24].upper()
        if candidate not in existing:
            return candidate


def find_target(objects: dict, name: str) -> tuple[str, dict]:
    for key, value in objects.items():
        if value.get("isa") == "PBXNativeTarget" and value.get("name") == name:
            return key, value

    targets = sorted(
        v.get("name", "?") for v in objects.values() if v.get("isa") == "PBXNativeTarget"
    )
    sys.exit(f"error: target '{name}' not found. This project has: {', '.join(targets)}")


def source_phase(objects: dict, target: dict) -> dict:
    for phase_id in target.get("buildPhases", []):
        phase = objects.get(phase_id, {})
        if phase.get("isa") == "PBXSourcesBuildPhase":
            return phase

    sys.exit("error: the target has no PBXSourcesBuildPhase to add sources to")


def compiled_paths(objects: dict, phase: dict) -> set[str]:
    """Paths already compiled by the phase, so a re-run can skip them."""
    paths = set()
    for build_file_id in phase.get("files", []):
        file_ref = objects.get(objects.get(build_file_id, {}).get("fileRef", ""), {})
        if "path" in file_ref:
            paths.add(file_ref["path"])
    return paths


def main(argv: list[str]) -> int:
    if len(argv) < 4:
        print(__doc__, file=sys.stderr)
        return 2

    pbxproj = Path(argv[1]) / "project.pbxproj"
    target_name = argv[2]
    sources = [Path(p).resolve() for p in argv[3:]]

    if not pbxproj.exists():
        sys.exit(f"error: {pbxproj} does not exist")

    project = load(pbxproj)
    objects = project["objects"]

    _, target = find_target(objects, target_name)
    phase = source_phase(objects, target)
    already = compiled_paths(objects, phase)

    # Paths are relative to the directory holding the .xcodeproj, which is how the rest of the
    # project spells them.
    project_dir = pbxproj.parent.parent
    group = next(
        (v for v in objects.values()
         if v.get("isa") == "PBXGroup" and v.get("name") == "AntMediaNetFacade"),
        None,
    )

    if group is None:
        group_id = object_id(set(objects))
        group = {"isa": "PBXGroup", "children": [], "name": "AntMediaNetFacade", "sourceTree": "<group>"}
        objects[group_id] = group

        root = objects[project["rootObject"]]
        objects[root["mainGroup"]]["children"].append(group_id)

    added = []
    for source in sources:
        relative = os.path.relpath(source, project_dir)

        if relative in already:
            continue

        file_id = object_id(set(objects))
        objects[file_id] = {
            "isa": "PBXFileReference",
            "lastKnownFileType": "sourcecode.swift",
            "path": relative,
            "sourceTree": "<group>",
        }

        build_id = object_id(set(objects))
        objects[build_id] = {"isa": "PBXBuildFile", "fileRef": file_id}

        group["children"].append(file_id)
        phase.setdefault("files", []).append(build_id)
        added.append(relative)

    # Written as XML rather than converted back to OpenStep: Xcode and xcodebuild both read it,
    # and plutil cannot write OpenStep anyway.
    with pbxproj.open("wb") as handle:
        plistlib.dump(project, handle)

    if added:
        print(f"add-facade: added to {target_name}: {', '.join(added)}")
    else:
        print(f"add-facade: {target_name} already compiles the facade sources")

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
