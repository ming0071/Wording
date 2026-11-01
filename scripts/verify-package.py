#!/usr/bin/env python3
"""Verify installer bytes, real installation evidence, and the self-contained payload."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import struct
import xml.etree.ElementTree as ET

REQUIRED = {
    'Wording.exe', 'Wording.dll', 'Wording.deps.json', 'Wording.runtimeconfig.json',
    'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'e_sqlite3.dll', 'System.Speech.dll',
    'README.md', 'THIRD_PARTY_NOTICES.md', 'docs/scenario-practice.md', 'docs/configuration.md',
    'docs/licenses/WebView2-LICENSE.txt', 'docs/licenses/WebView2-NOTICE.txt',
    'Prompts/practice-common.txt', 'Prompts/practice-reading.txt', 'Prompts/practice-listening.txt',
    'Configuration/app-defaults.json', 'content/README.md', 'content/verb-phrases.md',
    'content/toeic-vocabulary.json', 'content/toeic-phrasal-verbs.json',
    'content/toeic-starter.json', 'content/vocabulary-example.json',
}
MANIFEST = 'package-manifest.json'

def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def windows_x64(path):
    with path.open('rb') as stream:
        header = stream.read(64)
        if len(header) < 64 or header[:2] != b'MZ':
            return False
        offset = struct.unpack_from('<I', header, 0x3c)[0]
        if offset > path.stat().st_size - 6:
            return False
        stream.seek(offset)
        pe = stream.read(6)
        return pe[:4] == b'PE\0\0' and struct.unpack_from('<H', pe, 4)[0] == 0x8664

def unsafe_name(name):
    path = PurePosixPath(name)
    lower = path.name.lower()
    return ('\\' in name or ':' in name or path.is_absolute() or '..' in path.parts
        or re.search(r'\.(?:db|sqlite)(?:-(?:wal|shm))?$', lower)
        or lower in {'auth.json', 'settings.json', '.env'} or lower.startswith('.env.')
        or lower.endswith(('.log', '.tmp')))

def verify_payload(directory, version, create_manifest=False):
    errors = []
    try:
        files = {path.relative_to(directory).as_posix(): path for path in directory.rglob('*') if path.is_file()}
        missing = REQUIRED - files.keys()
        if missing:
            errors.append('missing required files: ' + ', '.join(sorted(missing)))
        for name, path in files.items():
            if unsafe_name(name) or path.is_symlink():
                errors.append('user data or unsafe file in payload: ' + name)
        if errors:
            return errors
        if not windows_x64(directory / 'Wording.exe'):
            errors.append('Wording.exe must be a Windows x64 executable')
        config = json.loads((directory / 'Configuration/app-defaults.json').read_text(encoding='utf-8-sig'))
        if not isinstance(config, dict) or not {'Review', 'Shortcuts', 'Speech', 'Ai', 'Practice'} <= config.keys():
            errors.append('product defaults must contain the required configuration sections')
        runtime = json.loads((directory / 'Wording.runtimeconfig.json').read_text(encoding='utf-8-sig'))['runtimeOptions']
        frameworks = {item['name'] for item in runtime.get('includedFrameworks', [])}
        if not {'Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App'} <= frameworks or 'framework' in runtime or 'frameworks' in runtime:
            errors.append('payload must include .NET and WPF')
        dependencies = json.loads((directory / 'Wording.deps.json').read_text(encoding='utf-8-sig'))
        if not dependencies['runtimeTarget']['name'].endswith('/win-x64'):
            errors.append('runtime target must be win-x64')
        for project in ('Wording', 'Wording.Core', 'Wording.Infrastructure'):
            if f'{project}/{version}' not in dependencies['libraries']:
                errors.append(f'{project} version must match {version}')
        if errors:
            return errors
        if create_manifest:
            manifest = {'version': version, 'files': {name: digest(path) for name,path in sorted(files.items()) if name != MANIFEST}}
            (directory / MANIFEST).write_text(json.dumps(manifest, indent=2), encoding='utf-8')
        else:
            manifest = json.loads((directory / MANIFEST).read_text(encoding='utf-8'))
            if manifest['version'] != version or not REQUIRED <= manifest['files'].keys():
                errors.append('payload manifest version or required files mismatch')
            for name, checksum in manifest['files'].items():
                if unsafe_name(name) or name not in files or digest(files[name]) != checksum:
                    errors.append('payload content mismatch: ' + name)
    except (OSError, ValueError, KeyError, TypeError, struct.error) as error:
        errors.append('cannot validate payload: ' + str(error))
    return errors

def verify_package(path, version):
    errors = []
    try:
        if path.name != f'Wording-{version}-win-x64-Setup.exe' or not windows_x64(path):
            errors.append('package must be the versioned Windows x64 installer EXE')
        expected = re.fullmatch(r'([a-fA-F0-9]{64})  (.+)', Path(str(path)+'.sha256').read_text(encoding='ascii').strip())
        checksum = digest(path)
        if not expected or expected[2] != path.name or expected[1].lower() != checksum:
            errors.append('installer SHA-256 or checksum filename mismatch')
        evidence = json.loads((path.parent / 'installer-validation.json').read_text(encoding='utf-8-sig'))
        if evidence.get('version') != version or evidence.get('installerSha256') != checksum:
            errors.append('installation evidence does not match installer bytes/version')
        if not all(evidence.get(field) is True for field in ('installed', 'upgraded', 'uninstalled', 'dataPreserved')):
            errors.append('real install/upgrade/uninstall/data preservation evidence is incomplete')
        if evidence.get('filesVerified', 0) < len(REQUIRED):
            errors.append('installed payload evidence is incomplete')
    except (OSError, ValueError, KeyError, TypeError, struct.error) as error:
        errors.append('cannot validate installer: ' + str(error))
    return errors

def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('path', nargs='?', type=Path)
    parser.add_argument('--version', default=ET.parse(root/'Directory.Build.props').findtext('PropertyGroup/Version'))
    parser.add_argument('--directory', type=Path)
    parser.add_argument('--create-manifest', action='store_true')
    args = parser.parse_args()
    path = args.path or root/'artifacts'/f'Wording-{args.version}-win-x64-Setup.exe'
    errors = verify_payload(args.directory, args.version, args.create_manifest) if args.directory else verify_package(path, args.version)
    for error in errors:
        print('ERROR: ' + error)
    print(f"{'FAIL' if errors else 'PASS'}: {(args.directory or path).name}, {len(errors)} package errors")
    return int(bool(errors))

if __name__ == '__main__':
    raise SystemExit(main())
