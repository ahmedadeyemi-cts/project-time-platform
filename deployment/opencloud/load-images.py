#!/usr/bin/env python3
"""Verify package and optionally load images. Never starts services or modifies data."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess

ROOT = Path(__file__).resolve().parent


def safe_file(name: str) -> Path:
    relative = PurePosixPath(name)
    if relative.is_absolute() or '..' in relative.parts:
        raise ValueError('Unsafe package path')
    file = ROOT.joinpath(*relative.parts)
    if not file.is_file() or file.is_symlink() or not file.resolve().is_relative_to(ROOT.resolve()):
        raise ValueError('Missing or unsafe package file: ' + name)
    return file


def verify() -> dict:
    manifest = json.loads(safe_file('PACKAGE-MANIFEST.json').read_text())
    if manifest.get('schema') != 'pulse-opencloud-loadable-images-v1' or manifest.get('includesImages') is not True:
        raise ValueError('This is not a loadable image package')
    sha = manifest.get('sourceCommit', '')
    if not re.fullmatch(r'[0-9a-f]{40}', sha) or manifest.get('platform') != 'linux/amd64':
        raise ValueError('Invalid source revision/platform')
    hashes = manifest.get('filesSha256', {})
    for name, expected in hashes.items():
        with safe_file(name).open('rb') as stream:
            observed = hashlib.file_digest(stream, 'sha256').hexdigest()
        if observed != expected:
            raise ValueError('Checksum mismatch: ' + name)
    expected_components = {'pulse': {'api', 'web', 'postgres', 'caddy'}, 'celar-ai': {'celar', 'ollama', 'caddy'}}
    components = expected_components.get(manifest.get('package'))
    records = manifest.get('images', [])
    if components is None or len(records) != len(components) or {r['component'] for r in records} != components:
        raise ValueError('Image inventory is incomplete or unexpected')
    for record in records:
        name = 'images/' + record['component'] + '.tar.gz'
        if record['archive'] != record['component'] + '.tar.gz' or hashes.get(name) != record['archiveSha256']:
            raise ValueError('Image archive is not covered by the package checksum')
        if record['sourceCommit'] != sha or record['imageTag'] != f"opencloud-candidate/{record['component']}:{sha}":
            raise ValueError('Invalid release image tag')
        if not re.fullmatch(r'sha256:[0-9a-f]{64}', record['localImageId']) or record['dockerLoadVerified'] is not True:
            raise ValueError('Invalid verified image identity')
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--load', action='store_true', help='Explicitly import images into the local Docker daemon')
    args = parser.parse_args()
    manifest = verify()
    print('PACKAGE_CHECKSUMS=PASS')
    print('INSTALLATION_ACCEPTANCE=PENDING; this operation never starts containers')
    if not args.load:
        print('IMAGES_LOADED=0; use --load only on the intended Docker host')
        return
    server = json.loads(subprocess.check_output(['docker', 'info', '--format', '{{json .}}'], text=True))
    if server.get('OSType') != 'linux' or server.get('Architecture') not in ('amd64', 'x86_64'):
        raise RuntimeError('This package was tested for Linux AMD64 only')
    # Preflight every destination tag before importing any image.
    for record in manifest['images']:
        existing = subprocess.run(['docker', 'image', 'inspect', record['imageTag']], capture_output=True, text=True)
        if existing.returncode == 0 and json.loads(existing.stdout)[0]['Id'] != record['localImageId']:
            raise RuntimeError('Refusing to overwrite a conflicting local release tag')
    for record in manifest['images']:
        path = safe_file('images/' + record['archive'])
        subprocess.run(['docker', 'image', 'load', '--input', str(path)], check=True)
        loaded = json.loads(subprocess.check_output(['docker', 'image', 'inspect', record['imageTag']], text=True))[0]
        if loaded['Id'] != record['localImageId'] or loaded['Os'] != 'linux' or loaded['Architecture'] != 'amd64':
            raise RuntimeError('Loaded image does not match the manifest')
        print('VERIFIED_IMAGE=' + record['component'])
    print('IMAGES_LOADED=' + str(len(manifest['images'])))
    print('CONTAINERS_STARTED=0; complete the installation prerequisites in IMAGE-PACKAGE-README.md')


if __name__ == '__main__':
    main()
