#!/usr/bin/env python3
"""Validate and assemble configuration handoffs. Never starts or deploys services."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent
COMMON = ['README.md', 'HANDOFF.md', 'RUNBOOK.md', 'GITHUB_DEPLOYMENTS.md', 'RUNTIME_INVENTORY.md', 'readiness.json', 'kit.py']
FILES = {
    'pulse': ['compose.pulse.yaml', 'compose.integration-egress.yaml', '.env.example',
              'pulse-runtime.env.example', 'api-entrypoint.sh', 'schema-check.sql', 'Caddyfile'],
    'celar-ai': ['compose.celar-ai.yaml', '.env.example', 'Caddyfile', 'Dockerfile',
                 'runtime.py', 'clamd.conf', 'freshclam.conf'],
}

def source_sha() -> str:
    override = os.environ.get('PACKAGING_SOURCE_COMMIT', '')
    value = override or subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    if not re.fullmatch('[0-9a-f]{40}', value):
        raise ValueError('A full source commit is required')
    return value

def readiness() -> dict:
    data = json.loads((ROOT / 'readiness.json').read_text())
    if data['installableRelease'] is not False:
        raise ValueError('This candidate cannot certify an installable release')
    if len(data['requiredBeforeInstallationAcceptance']) < 8:
        raise ValueError('Required qualification items are missing')
    return data

def validate_compose() -> dict:
    if shutil.which('docker') is None:
        raise RuntimeError('Docker Compose is required; configuration validation was NOT RUN')
    with tempfile.TemporaryDirectory(prefix='pulse-compose-check-') as temporary:
        root = Path(temporary)
        (root / 'tls').mkdir()
        for name in ['pulse-runtime.env', 'tls/fullchain.pem', 'tls/private.key',
                     'database-owner-password', 'api-database-password', 'runtime-token']:
            (root / name).write_text('# synthetic syntax validation only\n')
        env = os.environ.copy()
        for key in ['PULSE_API_IMAGE','PULSE_WEB_IMAGE','POSTGRES_IMAGE','CADDY_IMAGE','CELAR_GATEWAY_IMAGE','OLLAMA_IMAGE','CELAR_LAYA_IMAGE']:
            env[key] = 'validation.invalid/image@sha256:' + '0' * 64
        env.update(PULSE_LOCAL_DIR=str(root), CELAR_LOCAL_DIR=str(root), TARGET_PLATFORM='linux/amd64', HTTPS_BIND_IP='0.0.0.0')
        models = {}
        for name in FILES:
            compose = ROOT / name / ('compose.' + name + '.yaml')
            command = ['docker','compose','--env-file',str(ROOT/name/'.env.example'),'-f',str(compose),'config','--format','json']
            result = subprocess.run(command, env=env, capture_output=True, text=True)
            if result.returncode:
                raise ValueError('Compose syntax validation failed for ' + name + ': ' + result.stderr[:2000])
            models[name] = json.loads(result.stdout)
        check_models(models)
        return {'composeSyntax': 'passed', 'networkAndStorageContracts': 'passed',
                'services': {name: len(model['services']) for name,model in models.items()},
                'containersStarted': 0, 'imagesPulled': 0}

def check_models(models: dict) -> None:
    for name, model in models.items():
        for service_name, service in model['services'].items():
            if service.get('privileged') or service.get('network_mode') == 'host':
                raise ValueError('Host privilege/network must not be exposed')
            for port in service.get('ports', []):
                if service_name != 'edge' or str(port['published']) != '443' or int(port['target']) != 8443:
                    raise ValueError('Only the controlled HTTPS edge may be published')
            for volume in service.get('volumes', []):
                if 'docker.sock' in str(volume) or volume.get('source') == '/':
                    raise ValueError('Host/container management mount prohibited')
        if not all(volume.get('external') is True for volume in model['volumes'].values()):
            raise ValueError('Persistent volumes must be externally owned')
    pulse = models['pulse']
    if pulse['networks']['database'].get('internal') is not True or pulse['networks']['application'].get('internal') is not True:
        raise ValueError('Database/API staging networks must remain internal')
    if set(pulse['services']['api']['networks']) != {'application', 'database'}:
        raise ValueError('Initial staging API must have no outbound integration network')
    celar = models['celar-ai']['services']
    for name in ['gateway','ollama','clamav']:
        if celar[name]['network_mode'] != 'service:edge':
            raise ValueError('Source loopback assumptions must be preserved')
    if celar['laya']['network_mode'] != 'none':
        raise ValueError('Laya must not obtain internet access')

def bundle(output: Path) -> list[str]:
    state = readiness()
    sha = source_sha()
    output.mkdir(parents=True, exist_ok=True)
    created = []
    for package, names in FILES.items():
        contents = {name: ROOT/name for name in COMMON}
        contents.update({package+'/'+name: ROOT/package/name for name in names})
        hashes = {}
        filename = f'{package}-opencloud-handoff-{sha[:12]}.zip'
        with zipfile.ZipFile(output/filename, 'w', zipfile.ZIP_DEFLATED) as archive:
            for relative, path in sorted(contents.items()):
                if path.is_symlink() or not path.is_file():
                    raise ValueError('Missing or unexpected package path: ' + relative)
                raw = path.read_bytes()
                archive.writestr(relative, raw)
                hashes[relative] = hashlib.sha256(raw).hexdigest()
            manifest = {'package': package, 'sourceCommit': sha,
                'kind': 'configuration-and-build-handoff', 'includesImages': False,
                'includesDatabase': False, 'includesSecrets': False, 'includesModelWeights': False,
                'installableRelease': state['installableRelease'], 'filesSha256': hashes}
            archive.writestr('PACKAGE-MANIFEST.json', json.dumps(manifest, sort_keys=True, indent=2)+'\n')
        created.append(filename)
    (output/'SHA256SUMS').write_text(''.join(hashlib.sha256((output/name).read_bytes()).hexdigest()+'  '+name+'\n' for name in created))
    return created

def host_report() -> dict:
    result = {'readOnly': True, 'containersStarted': 0, 'readiness': readiness(), 'host': {}}
    for name, command in [('architecture',['uname','-m']),('docker',['docker','version','--format','{{.Server.Version}}']),('compose',['docker','compose','version','--short'])]:
        try:
            reply = subprocess.run(command, capture_output=True, text=True, timeout=10)
            result['host'][name] = reply.stdout.strip() if reply.returncode == 0 else 'unavailable'
        except (OSError, subprocess.TimeoutExpired):
            result['host'][name] = 'unavailable'
    return result

def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('operation', choices=['validate','bundle','plan','check-host'])
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    if args.operation == 'validate':
        readiness()
        print(json.dumps(validate_compose(), indent=2))
    elif args.operation == 'bundle':
        if args.output is None:
            parser.error('bundle requires --output')
        print(json.dumps({'created': bundle(args.output), 'installableRelease': False}, indent=2))
    elif args.operation == 'check-host':
        print(json.dumps(host_report(), indent=2))
    else:
        print(json.dumps(readiness(), indent=2))

if __name__ == '__main__':
    main()
