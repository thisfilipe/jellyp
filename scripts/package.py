#!/usr/bin/env python3
"""Build an explicit, deterministic plugin archive. No publication or host changes."""
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DLL_NAME = "Jellyfin.Plugin.JellyP.dll"
GUID = "0928e334-8971-4205-a455-f42a76527f9e"
REPOSITORY = "thisfilipe/jellyp"


def metadata():
    project = ET.parse(ROOT / "Jellyfin.Plugin.JellyP/Jellyfin.Plugin.JellyP.csproj").getroot()
    version = project.findtext(".//Version")
    if not version or not re.fullmatch(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)", version):
        raise ValueError("Use a canonical four-part Jellyfin version in the plugin project.")
    if any(int(n) > 65534 for n in version.split(".")):
        raise ValueError("Assembly version components must be at most 65534.")
    dependency = next(p for p in project.findall(".//PackageReference") if p.get("Include") == "Jellyfin.Controller")
    abi = dependency.get("Version") + ".0"
    build = (ROOT / "build.yaml").read_text()
    for key, value in {"version": version, "targetAbi": abi, "guid": GUID, "name": "JellyP"}.items():
        if not re.search(r'^' + key + r': "' + re.escape(value) + r'"$', build, re.M):
            raise ValueError(f"build.yaml {key} does not match the project/plugin.")
    if GUID not in (ROOT / "Jellyfin.Plugin.JellyP/Plugin.cs").read_text():
        raise ValueError("Plugin GUID changed; preserve identity across releases.")
    return {
        "category": "General", "name": "JellyP", "guid": GUID,
        "version": version, "targetAbi": abi, "owner": "thisfilipe", "status": "Active",
        "autoUpdate": True, "assemblies": [DLL_NAME],
        "overview": "Configurable WebP artwork optimization and original-based policy synchronization.",
        "description": "Administrator-selected artwork policies, verified alpha and quality, resumable migration and optional preserved originals.",
    }


def validate_archive(archive, expected=None):
    expected = expected or metadata()
    with zipfile.ZipFile(archive) as package:
        if package.testzip() is not None or len(package.namelist()) != 3 or set(package.namelist()) != {DLL_NAME, "meta.json", "LICENSE"}:
            raise ValueError("Unexpected archive members or invalid CRC.")
        if json.loads(package.read("meta.json")) != expected:
            raise ValueError("Archive metadata does not match source version.")
        binary = package.read(DLL_NAME)
        for marker in (b"/home/", b"/tmp/jellyp", b"C:\\Users\\"):
            if marker in binary or marker.decode().encode("utf-16le") in binary:
                raise ValueError("Plugin binary contains a local build path; check PathMap.")


def build(output=None):
    meta = metadata()
    dll = ROOT / "Jellyfin.Plugin.JellyP/bin/Release/net10.0" / DLL_NAME
    if not dll.is_file():
        raise ValueError("Build the Release configuration before packaging.")
    output = output or ROOT / "artifacts"
    output.mkdir(parents=True, exist_ok=True)
    archive = output / f"JellyP_{meta['version']}.zip"
    files = {DLL_NAME: dll.read_bytes(), "meta.json": (json.dumps(meta, indent=2) + "\n").encode(), "LICENSE": (ROOT / "LICENSE").read_bytes()}
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as package:
        for name, data in sorted(files.items()):
            entry = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o644 << 16
            package.writestr(entry, data)
    validate_archive(archive, meta)
    checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
    Path(str(archive) + ".sha256").write_text(f"{checksum}  {archive.name}\n")
    return archive


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tag", help="Require tag v<project version> before a release build")
    parser.add_argument("--version", action="store_true", help="Print validated project version")
    args = parser.parse_args()
    meta = metadata()
    if args.tag is not None and args.tag != "v" + meta["version"]:
        parser.error("Tag does not match the project version.")
    if args.version:
        print(meta["version"])
    else:
        archive = build()
        print(archive)
        print("SHA256 " + hashlib.sha256(archive.read_bytes()).hexdigest())
