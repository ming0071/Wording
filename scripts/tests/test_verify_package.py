import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('verify_package', Path(__file__).resolve().parents[1]/'verify-package.py')
verifier = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verifier)

class PackageVerificationTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.path = self.root/'Wording-0.3.0-win-x64-Setup.exe'
        executable = bytearray(128)
        executable[:2] = b'MZ'
        struct.pack_into('<I', executable, 0x3c, 64)
        executable[64:68] = b'PE\0\0'
        struct.pack_into('<H', executable, 68, 0x8664)
        self.executable = bytes(executable)

    def write_package(self):
        self.path.write_bytes(self.executable)
        checksum = hashlib.sha256(self.executable).hexdigest()
        Path(str(self.path)+'.sha256').write_text(f'{checksum}  {self.path.name}\n', encoding='ascii')
        self.evidence = dict(version='0.3.0', installerSha256=checksum, installed=True, upgraded=True,
            uninstalled=True, dataPreserved=True, filesVerified=len(verifier.REQUIRED))
        self.write_evidence()

    def write_evidence(self):
        (self.root/'installer-validation.json').write_text(json.dumps(self.evidence), encoding='utf-8')

    def payload(self):
        directory = self.root/'payload'
        for name in verifier.REQUIRED:
            file = directory/name
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_bytes(b'fixture')
        (directory/'Wording.exe').write_bytes(self.executable)
        (directory/'Configuration/app-defaults.json').write_text(json.dumps({key:{} for key in ('Review','Shortcuts','Speech','Ai','Practice')}))
        (directory/'Wording.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'includedFrameworks':[
            {'name':'Microsoft.NETCore.App'},{'name':'Microsoft.WindowsDesktop.App'}]}}))
        (directory/'Wording.deps.json').write_text(json.dumps({'runtimeTarget':{'name':'net10.0/win-x64'},
            'libraries':{name+'/0.3.0':{} for name in ('Wording','Wording.Core','Wording.Infrastructure')}}))
        return directory

    def test_verified_installer_passes(self):
        self.write_package()
        self.assertEqual([],verifier.verify_package(self.path,'0.3.0'))

    def test_checksum_and_name_must_match(self):
        self.write_package()
        Path(str(self.path)+'.sha256').write_text('0'*64+'  other.exe')
        self.assertTrue(any('SHA-256' in error for error in verifier.verify_package(self.path,'0.3.0')))

    def test_invalid_executable_architecture_or_version_fails(self):
        self.write_package()
        self.assertTrue(verifier.verify_package(self.path,'0.4.0'))
        self.path.write_bytes(b'not an exe')
        self.assertTrue(verifier.verify_package(self.path,'0.3.0'))
        changed = bytearray(self.executable)
        struct.pack_into('<H',changed,68,0x14c)
        self.path.write_bytes(changed)
        self.assertTrue(verifier.verify_package(self.path,'0.3.0'))

    def test_all_real_install_checks_are_required_for_these_exact_bytes(self):
        for field in ('installed','upgraded','uninstalled','dataPreserved','installerSha256','version','filesVerified'):
            with self.subTest(field=field):
                self.write_package()
                self.evidence.pop(field)
                self.write_evidence()
                self.assertTrue(verifier.verify_package(self.path,'0.3.0'))

    def test_payload_contains_all_materials_and_detects_changes(self):
        directory = self.payload()
        self.assertEqual([],verifier.verify_payload(directory,'0.3.0',True))
        self.assertEqual([],verifier.verify_payload(directory,'0.3.0'))
        (directory/'content/toeic-starter.json').write_text('changed')
        self.assertTrue(any('content mismatch' in error for error in verifier.verify_payload(directory,'0.3.0')))

    def test_payload_requires_licenses_materials_and_no_private_data(self):
        directory = self.payload()
        (directory/'content/README.md').unlink()
        self.assertTrue(any('missing required' in error for error in verifier.verify_payload(directory,'0.3.0',True)))
        (directory/'content/README.md').write_text('restored')
        for name in ('wording.db','wording.db-wal','settings.json','auth.json','.env','diagnostics.log'):
            with self.subTest(name=name):
                file=directory/name
                file.write_text('private')
                self.assertTrue(any('user data' in error for error in verifier.verify_payload(directory,'0.3.0',True)))
                file.unlink()

    def test_payload_rejects_bad_runtime_version_and_manifest_paths(self):
        directory = self.payload()
        self.assertTrue(verifier.verify_payload(directory,'0.4.0',True))
        self.assertEqual([],verifier.verify_payload(directory,'0.3.0',True))
        manifest = directory/verifier.MANIFEST
        value=json.loads(manifest.read_text())
        value['files']['../outside']='0'*64
        manifest.write_text(json.dumps(value))
        self.assertTrue(verifier.verify_payload(directory,'0.3.0'))
        (directory/'Wording.runtimeconfig.json').write_text('{broken')
        self.assertTrue(verifier.verify_payload(directory,'0.3.0',True))

if __name__ == '__main__':
    unittest.main()
