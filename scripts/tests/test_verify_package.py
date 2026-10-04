import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest
import warnings
import zipfile


spec = importlib.util.spec_from_file_location("verify_package", Path(__file__).resolve().parents[1] / "verify-package.py")
verifier = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verifier)


class PackageVerificationTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name) / "Wording-0.3.0-win-x64.zip"
        executable = bytearray(128)
        executable[:2] = b"MZ"
        struct.pack_into("<I", executable, 0x3C, 64)
        executable[64:68] = b"PE\0\0"
        struct.pack_into("<H", executable, 68, 0x8664)
        self.files = {name: b"fixture" for name in verifier.REQUIRED}
        self.files["Wording.exe"] = bytes(executable)
        self.files["Wording.runtimeconfig.json"] = json.dumps({"runtimeOptions": {"includedFrameworks": [
            {"name": "Microsoft.NETCore.App"}, {"name": "Microsoft.WindowsDesktop.App"}]}})
        self.files["Wording.deps.json"] = json.dumps({"runtimeTarget": {"name": "net10.0/win-x64"},
            "libraries": {name + "/0.3.0": {} for name in ("Wording", "Wording.Core", "Wording.Infrastructure")}})

    def write_package(self, extra=None):
        with zipfile.ZipFile(self.path, "w") as archive:
            for name, content in self.files.items():
                archive.writestr(name, content)
            if extra:
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore", UserWarning)
                    archive.writestr(*extra)
        digest = hashlib.sha256(self.path.read_bytes()).hexdigest()
        Path(str(self.path) + ".sha256").write_text(f"{digest}  {self.path.name}\n", encoding="ascii")

    def test_complete_self_contained_package_passes(self):
        self.write_package()
        self.assertEqual([], verifier.verify_package(self.path, "0.3.0"))

    def test_missing_prompt_or_license_fails(self):
        for name in ("Prompts/practice-reading.txt", "docs/licenses/WebView2-LICENSE.txt"):
            with self.subTest(name=name):
                content = self.files.pop(name)
                self.write_package()
                self.assertTrue(any(name in error for error in verifier.verify_package(self.path, "0.3.0")))
                self.files[name] = content

    def test_wrong_hash_and_filename_fail(self):
        self.write_package()
        sidecar = Path(str(self.path) + ".sha256")
        sidecar.write_text("0" * 64 + f"  {self.path.name}\n", encoding="ascii")
        self.assertTrue(any("SHA-256" in e for e in verifier.verify_package(self.path, "0.3.0")))
        sidecar.write_text("0" * 64 + "  another.zip\n", encoding="ascii")
        self.assertTrue(any("name this ZIP" in e for e in verifier.verify_package(self.path, "0.3.0")))

    def test_user_data_and_unsafe_paths_fail(self):
        for name in ("wording.db", "wording.db-wal", "settings.json", "auth.json", ".env", "../outside.txt", "C:/outside.txt"):
            with self.subTest(name=name):
                self.write_package((name, b"fixture"))
                self.assertNotEqual([], verifier.verify_package(self.path, "0.3.0"))

    def test_stale_version_and_duplicate_entries_fail(self):
        self.write_package()
        self.assertTrue(any("version must match" in e for e in verifier.verify_package(self.path, "0.4.0")))
        self.write_package(("Wording.dll", b"duplicate"))
        self.assertTrue(any("duplicate" in e for e in verifier.verify_package(self.path, "0.3.0")))

    def test_framework_dependent_or_wrong_architecture_fails(self):
        self.files["Wording.runtimeconfig.json"] = json.dumps({"runtimeOptions": {"framework": {"name": "Microsoft.NETCore.App"}}})
        self.files["Wording.exe"] = b"not an executable"
        self.write_package()
        errors = verifier.verify_package(self.path, "0.3.0")
        self.assertTrue(any("include .NET" in e for e in errors))
        self.assertTrue(any("Windows executable" in e for e in errors))

    def test_invalid_json_and_corrupt_zip_are_reported(self):
        self.files["Wording.deps.json"] = "{invalid"
        self.write_package()
        self.assertNotEqual([], verifier.verify_package(self.path, "0.3.0"))
        self.path.write_bytes(b"corrupt archive")
        self.assertNotEqual([], verifier.verify_package(self.path, "0.3.0"))

    def test_non_x64_executable_and_runtime_target_fail(self):
        executable = bytearray(self.files["Wording.exe"])
        struct.pack_into("<H", executable, 68, 0x14C)  # A valid PE header for x86, rather than x64.
        self.files["Wording.exe"] = bytes(executable)
        dependencies = json.loads(self.files["Wording.deps.json"])
        dependencies["runtimeTarget"]["name"] = "net10.0/linux-x64"
        self.files["Wording.deps.json"] = json.dumps(dependencies)
        self.write_package()
        errors = verifier.verify_package(self.path, "0.3.0")
        self.assertTrue(any("must target Windows x64" in e for e in errors))
        self.assertTrue(any("must be win-x64" in e for e in errors))
