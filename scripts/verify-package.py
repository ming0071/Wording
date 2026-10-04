#!/usr/bin/env python3
"""Verify a self-contained Windows release ZIP and its SHA-256 sidecar."""

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import struct
import xml.etree.ElementTree as ET
import zipfile


REQUIRED = {
    "Wording.exe", "Wording.dll", "Wording.deps.json", "Wording.runtimeconfig.json",
    "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "e_sqlite3.dll", "System.Speech.dll",
    "README.md", "THIRD_PARTY_NOTICES.md", "docs/scenario-practice.md", "docs/configuration.md",
    "docs/licenses/WebView2-LICENSE.txt", "docs/licenses/WebView2-NOTICE.txt",
    "Prompts/practice-common.txt", "Prompts/practice-reading.txt", "Prompts/practice-listening.txt",
    "Configuration/app-defaults.json",
}


def verify_package(path, version):
    """Return validation errors without extracting or changing the archive."""
    errors = []
    try:
        checksum = Path(str(path) + ".sha256").read_text(encoding="ascii").strip()
        expected = re.fullmatch(r"([0-9a-fA-F]{64})  (.+)", checksum)
        if not expected or expected[2] != path.name:
            errors.append("checksum must name this ZIP and contain a SHA-256 hash")
        else:
            with path.open("rb") as stream:
                actual = hashlib.file_digest(stream, "sha256").hexdigest()
            if actual != expected[1].lower():
                errors.append("ZIP SHA-256 does not match the checksum")
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            if archive.testzip() is not None:
                errors.append("ZIP contains corrupt entries")
            duplicates = [name for name, count in Counter(names).items() if count > 1]
            if duplicates:
                errors.append(f"duplicate ZIP entries: {duplicates}")
            missing = REQUIRED - set(names)
            if missing:
                errors.append(f"missing required files: {sorted(missing)}")
            if "Configuration/app-defaults.json" in names:
                config = json.loads(archive.read("Configuration/app-defaults.json"))
                if not isinstance(config, dict) or not {"Review", "Shortcuts", "Speech", "Ai", "Practice"} <= config.keys():
                    errors.append("product defaults must contain the required configuration sections")
            for name in names:
                entry = PurePosixPath(name)
                if "\\" in name or ":" in name or entry.is_absolute() or ".." in entry.parts:
                    errors.append(f"unsafe archive path: {name}")
                lower = entry.name.lower()
                if (re.search(r"\.(?:db|sqlite)(?:-(?:wal|shm))?$", lower)
                        or lower in {"auth.json", "settings.json", ".env"}
                        or lower.startswith(".env.") or lower.endswith((".log", ".tmp"))):
                    errors.append(f"user data or temporary file in package: {name}")
            if "Wording.runtimeconfig.json" in names:
                runtime = json.loads(archive.read("Wording.runtimeconfig.json"))["runtimeOptions"]
                frameworks = {item["name"] for item in runtime.get("includedFrameworks", [])}
                if not {"Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App"} <= frameworks or "framework" in runtime or "frameworks" in runtime:
                    errors.append("package must include .NET and WPF instead of requiring installed frameworks")
            if "Wording.deps.json" in names:
                dependencies = json.loads(archive.read("Wording.deps.json"))
                if not dependencies["runtimeTarget"]["name"].endswith("/win-x64"):
                    errors.append("runtime target must be win-x64")
                for project in ("Wording", "Wording.Core", "Wording.Infrastructure"):
                    if f"{project}/{version}" not in dependencies["libraries"]:
                        errors.append(f"{project} version must match {version}")
            if "Wording.exe" in names:
                executable = archive.read("Wording.exe")
                if len(executable) < 64 or executable[:2] != b"MZ":
                    errors.append("Wording.exe must be a Windows executable")
                else:
                    offset = struct.unpack_from("<I", executable, 0x3C)[0]
                    if offset + 6 > len(executable) or executable[offset:offset + 4] != b"PE\0\0" or struct.unpack_from("<H", executable, offset + 4)[0] != 0x8664:
                        errors.append("Wording.exe must target Windows x64")
    except (OSError, UnicodeError, zipfile.BadZipFile, json.JSONDecodeError, KeyError, TypeError, ValueError) as error:
        errors.append(f"cannot validate package: {error}")
    return errors


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", nargs="?", type=Path)
    parser.add_argument("--version", default=ET.parse(root / "Directory.Build.props").findtext("PropertyGroup/Version"))
    args = parser.parse_args()
    path = args.path or root / "artifacts" / f"Wording-{args.version}-win-x64.zip"
    errors = verify_package(path, args.version)
    for error in errors:
        print(f"ERROR: {error}")
    print(f"{'FAIL' if errors else 'PASS'}: {path.name}, {len(errors)} package errors")
    return int(bool(errors))


if __name__ == "__main__":
    raise SystemExit(main())
