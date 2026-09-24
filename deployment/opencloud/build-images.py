#!/usr/bin/env python3
"""Build candidate images from an exact tracked checkout; never publish/deploy."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tarfile
import tempfile

ROOT = Path(__file__).resolve().parents[2]

def run(*args: str, **kwargs):
    return subprocess.run(args, check=True, **kwargs)

def render_api(source: str, sha: str) -> str:
    if not re.fullmatch('[0-9a-f]{40}', sha):
        raise ValueError('Exact source commit required')
    replacements = [
        ('WORKDIR /src', 'ARG SOURCE_REVISION\nWORKDIR /src'),
        ('/p:UseAppHost=false', '/p:UseAppHost=false \\\n    /p:ProjectPulseSourceRevision=${SOURCE_REVISION}'),
        ('USER $APP_UID', 'COPY --chmod=0555 deployment/opencloud/pulse/api-entrypoint.sh /usr/local/bin/opencloud-api-entrypoint\nUSER $APP_UID'),
        ('ENTRYPOINT ["dotnet", "ProjectTime.Api.dll"]', 'ENTRYPOINT ["/usr/local/bin/opencloud-api-entrypoint"]'),
    ]
    for old, new in replacements:
        if source.count(old) != 1:
            raise ValueError('Canonical API recipe changed; review packaging adaptation')
        source = source.replace(old, new, 1)
    return source

def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('component', choices=['api', 'web', 'celar'])
    parser.add_argument('--output', required=True, type=Path)
    options = parser.parse_args()
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    if not re.fullmatch('[0-9a-f]{40}', sha):
        raise ValueError('Exact tracked source required')
    if subprocess.check_output(['git', 'diff', '--name-only', 'HEAD'], cwd=ROOT).strip():
        raise ValueError('Build requires an unchanged tracked checkout')
    options.output.mkdir(parents=True, exist_ok=True)
    tag = f'opencloud-candidate/{options.component}:{sha}'
    with tempfile.TemporaryDirectory(prefix='pulse-build-') as temporary:
        folder = Path(temporary)
        archive = folder / 'source.tar'
        with archive.open('wb') as stream:
            run('git', 'archive', '--format=tar', sha, cwd=ROOT, stdout=stream)
        context = folder / 'context'
        context.mkdir()
        with tarfile.open(archive) as tar:
            tar.extractall(context, filter='data')
        canonical = context / 'deployment/containers/api/Dockerfile'
        if options.component == 'api':
            dockerfile = context / 'opencloud-api.Dockerfile'
            dockerfile.write_text(render_api(canonical.read_text(), sha))
        elif options.component == 'web':
            dockerfile = context / 'deployment/containers/web/Dockerfile'
        else:
            dockerfile = context / 'deployment/opencloud/celar-ai/Dockerfile'
        run('docker', 'build', '--platform', 'linux/amd64', '--build-arg', 'SOURCE_REVISION=' + sha,
            '--label', 'org.opencontainers.image.revision=' + sha,
            '-f', str(dockerfile), '-t', tag, str(context))
        inspect = json.loads(subprocess.check_output(['docker', 'image', 'inspect', tag], text=True))[0]
        if inspect['Config']['Labels'].get('org.opencontainers.image.revision') != sha:
            raise ValueError('Image source label mismatch')
        checks = []
        if options.component == 'celar':
            run('docker', 'run', '--rm', '--network', 'none', tag, 'self-test')
            checks.append('real image native prerequisites and fail-closed gateway authentication')
            run('docker', 'run', '--rm', '--network', 'none', '--entrypoint', 'python3',
                '-v', f'{context}:/source:ro', '-w', '/source', tag, 'tests/laya/test_gateway.py')
            checks.append('existing synthetic HTTP and real Unix-socket contract tests')
        elif options.component == 'api':
            run('docker', 'run', '--rm', '--network', 'none', '--entrypoint', 'dotnet', tag, '--info')
            checks.append('published API runtime executable')
        else:
            run('docker', 'run', '--rm', '--network', 'none', tag, 'nginx', '-t')
            checks.append('real nginx entrypoint/configuration syntax')
        report = {
            'component': options.component, 'sourceCommit': sha,
            'platform': inspect['Os'] + '/' + inspect['Architecture'],
            'localImageId': inspect['Id'], 'registryManifestDigests': inspect.get('RepoDigests', []),
            'dockerfileSha256': hashlib.sha256(dockerfile.read_bytes()).hexdigest(),
            'published': False, 'deployed': False, 'checksPassed': checks,
            'notRun': ['image vulnerability qualification', 'full restored-database runtime',
                'live models and Laya classification', 'OpenCloud end-to-end acceptance'],
            'note': 'localImageId is NOT a deployable registry manifest digest. Do not substitute it in Compose.'
        }
        (options.output / (options.component + '-build.json')).write_text(json.dumps(report, indent=2) + '\n')
        print('OPENCloud_CANDIDATE_BUILD=PASS component=' + options.component)

if __name__ == '__main__':
    main()
