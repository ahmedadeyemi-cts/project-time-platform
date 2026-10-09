#!/usr/bin/env python3
"""Retain Docker-loadable software; never publish to a registry or deploy services."""
from __future__ import annotations
import argparse
import gzip
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import tarfile
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent
BUILD = ROOT / 'build-images.py'
BASE_IMAGES = {'postgres': 'postgres:16-bookworm', 'caddy': 'caddy:2-alpine', 'ollama': 'ollama/ollama:latest'}
PACKAGES = {'pulse': ('api', 'web', 'postgres', 'caddy'), 'celar-ai': ('celar', 'ollama', 'caddy')}
VARIABLES = {'api': 'PULSE_API_IMAGE', 'web': 'PULSE_WEB_IMAGE', 'postgres': 'POSTGRES_IMAGE',
             'caddy': 'CADDY_IMAGE', 'celar': 'CELAR_GATEWAY_IMAGE', 'ollama': 'OLLAMA_IMAGE'}


def digest(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def head() -> str:
    value = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    if not re.fullmatch(r'[0-9a-f]{40}', value):
        raise ValueError('Exact source revision required')
    if subprocess.check_output(['git', 'diff', '--name-only', 'HEAD'], cwd=ROOT).strip():
        raise ValueError('Tracked checkout must be clean')
    return value


def inspect(tag: str) -> dict:
    return json.loads(subprocess.check_output(['docker', 'image', 'inspect', tag], text=True))[0]


def validate_archive(path: Path, tag: str, image_id: str) -> None:
    """Read but never extract image layers; verify Docker manifest and config identity."""
    wanted = {image_id.removeprefix('sha256:') + '.json',
              'blobs/sha256/' + image_id.removeprefix('sha256:')}
    manifest = None
    configs = set()
    with tarfile.open(path, 'r|gz') as archive:
        for item in archive:
            name = PurePosixPath(item.name)
            if name.is_absolute() or '..' in name.parts or item.issym() or item.islnk():
                raise ValueError('Unexpected outer image archive path')
            if item.name == 'manifest.json' or item.name in wanted:
                if not item.isfile() or item.size > 4 * 1024 * 1024:
                    raise ValueError('Invalid image metadata')
                stream = archive.extractfile(item)
                if stream is None:
                    raise ValueError('Missing image metadata')
                data = stream.read()
                if item.name == 'manifest.json':
                    manifest = json.loads(data)
                elif 'sha256:' + hashlib.sha256(data).hexdigest() == image_id:
                    configs.add(item.name)
    if not isinstance(manifest, list) or len(manifest) != 1:
        raise ValueError('Archive must describe exactly one image')
    if manifest[0].get('RepoTags') != [tag] or manifest[0].get('Config') not in configs:
        raise ValueError('Archived image differs from the tested image')


def save_image(component: str, output: Path, upstream: str | None = None) -> None:
    sha = head()
    tag = f'opencloud-candidate/{component}:{sha}'
    if upstream:
        subprocess.run(['docker', 'pull', '--platform', 'linux/amd64', upstream], check=True)
        origin = inspect(upstream)
        if not origin.get('RepoDigests'):
            raise ValueError('Upstream image has no resolved registry digest')
        subprocess.run(['docker', 'tag', origin['Id'], tag], check=True)
        version_args = {'postgres': ['postgres', '--version'], 'caddy': ['caddy', 'version'],
                        'ollama': ['--version']}[component]
        reply = subprocess.run(['docker', 'run', '--rm', '--network', 'none', tag, *version_args],
                               check=True, capture_output=True, text=True)
        evidence = {'upstreamReferenceAtBuild': upstream, 'resolvedRegistryDigests': origin['RepoDigests'],
                    'versionProbe': (reply.stdout + reply.stderr).strip(),
                    'versionMatchesCurrentInstallation': False,
                    'note': 'Packaging candidate; no match to the currently installed cloud service is claimed.'}
    else:
        report = output / (component + '-build.json')
        evidence = json.loads(report.read_text())
        if evidence.get('sourceCommit') != sha or evidence.get('component') != component:
            raise ValueError('Build evidence is not for this revision/component')
    current = inspect(tag)
    if current['Os'] != 'linux' or current['Architecture'] != 'amd64':
        raise ValueError('Only the tested Linux AMD64 platform may be exported')
    if not upstream and current['Id'] != evidence.get('localImageId'):
        raise ValueError('Image no longer matches its build evidence')
    output.mkdir(parents=True, exist_ok=True)
    target = output / (component + '.tar.gz')
    temporary = target.with_suffix('.partial')
    process = subprocess.Popen(['docker', 'image', 'save', tag], stdout=subprocess.PIPE)
    try:
        with temporary.open('wb') as stream, gzip.GzipFile(filename='', mode='wb', fileobj=stream,
                                                         compresslevel=1, mtime=0) as compressed:
            if process.stdout is None:
                raise RuntimeError('Image export stream unavailable')
            shutil.copyfileobj(process.stdout, compressed, 1024 * 1024)
        if process.wait() != 0:
            raise RuntimeError('Docker image export failed')
        temporary.replace(target)
    finally:
        if process.poll() is None:
            process.kill()
        process.wait()
        temporary.unlink(missing_ok=True)
    validate_archive(target, tag, current['Id'])
    subprocess.run(['docker', 'image', 'rm', tag], check=True)
    subprocess.run(['docker', 'image', 'load', '--input', str(target)], check=True)
    if inspect(tag)['Id'] != current['Id']:
        raise ValueError('Docker load round trip returned a different image')
    record = {'component': component, 'sourceCommit': sha, 'platform': 'linux/amd64',
              'imageTag': tag, 'localImageId': current['Id'], 'archive': target.name,
              'archiveSha256': digest(target), 'archiveBytes': target.stat().st_size,
              'archiveFormat': 'docker-save-gzip', 'dockerLoadVerified': True,
              'registryPublished': False, 'deployed': False, 'evidence': evidence}
    (output / (component + '-archive.json')).write_text(json.dumps(record, indent=2) + '\n')
    print('LOADABLE_IMAGE_ARCHIVE=PASS component=' + component, flush=True)


def assemble(package: str, inputs: Path, output: Path) -> None:
    import kit
    sha = head()
    state = kit.readiness()
    records = []
    files: dict[str, Path] = {}
    for component in PACKAGES[package]:
        matches = list(inputs.rglob(component + '-archive.json'))
        if len(matches) != 1:
            raise ValueError('Expected exactly one artifact for ' + component)
        record = json.loads(matches[0].read_text())
        archive = matches[0].parent / record['archive']
        if record['archive'] != component + '.tar.gz' or record['sourceCommit'] != sha:
            raise ValueError('Cross-revision or unexpected image archive')
        if record['platform'] != 'linux/amd64' or record['dockerLoadVerified'] is not True:
            raise ValueError('Unverified image archive')
        if digest(archive) != record['archiveSha256']:
            raise ValueError('Downloaded image archive checksum mismatch')
        records.append(record)
        files['images/' + archive.name] = archive
        files['evidence/' + matches[0].name] = matches[0]
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='image-package-') as temp:
        stage = Path(temp)
        configs = stage / 'configuration'
        kit.bundle(configs)
        with zipfile.ZipFile(configs / f'{package}-opencloud-handoff-{sha[:12]}.zip') as source:
            for item in source.infolist():
                path = PurePosixPath(item.filename)
                if path.is_absolute() or '..' in path.parts:
                    raise ValueError('Unexpected configuration archive member')
            source.extractall(stage / 'kit')
        for path in (stage / 'kit').rglob('*'):
            if path.is_file() and path.name != 'PACKAGE-MANIFEST.json':
                files[path.relative_to(stage / 'kit').as_posix()] = path
        files['load-images.py'] = ROOT / 'load-images.py'
        files['IMAGE-PACKAGE-README.md'] = ROOT / 'IMAGE-PACKAGE-README.md'
        env = stage / 'images.env'
        env.write_text('# Exact locally loaded image tags; not registry manifest digests.\nTARGET_PLATFORM=linux/amd64\n'
                       + ''.join(VARIABLES[r['component']] + '=' + r['imageTag'] + '\n' for r in records))
        files['images.env'] = env
        manifest = {'schema': 'pulse-opencloud-loadable-images-v1', 'package': package,
                    'sourceCommit': sha, 'platform': 'linux/amd64', 'includesImages': True,
                    'includesDatabaseSoftware': package == 'pulse', 'includesDatabaseContents': False,
                    'includesSecrets': False, 'includesModelWeights': False,
                    'includesLayaWorker': False, 'installableRelease': False,
                    'images': records, 'filesSha256': {name: digest(path) for name, path in sorted(files.items())},
                    'requiredBeforeInstallationAcceptance': state['requiredBeforeInstallationAcceptance']}
        filename = f'{package}-docker-images-linux-amd64-{sha[:12]}.zip'
        with zipfile.ZipFile(output / filename, 'w', zipfile.ZIP_STORED, allowZip64=True) as archive:
            for name, path in sorted(files.items()):
                archive.write(path, name)
            archive.writestr('PACKAGE-MANIFEST.json', json.dumps(manifest, indent=2) + '\n')
        (output / (filename + '.sha256')).write_text(digest(output / filename) + '  ' + filename + '\n')
        (output / (package + '-image-package-summary.json')).write_text(json.dumps(manifest, indent=2) + '\n')
        print('IMAGE_PACKAGE_ASSEMBLED=' + filename, flush=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=['save-built', 'dependencies', 'assemble'])
    parser.add_argument('--component', choices=['api', 'web', 'celar'])
    parser.add_argument('--package', choices=PACKAGES)
    parser.add_argument('--input', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.operation == 'save-built':
        if not args.component:
            parser.error('--component is required')
        save_image(args.component, args.output)
    elif args.operation == 'dependencies':
        for component, source in BASE_IMAGES.items():
            save_image(component, args.output, source)
    else:
        if not args.package or not args.input:
            parser.error('--package and --input are required')
        assemble(args.package, args.input, args.output)


if __name__ == '__main__':
    main()
