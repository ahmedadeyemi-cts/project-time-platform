#!/usr/bin/env python3
"""Verify native core deployment provenance and live API identity before read-only tests."""
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
REPOSITORY = 'ahmedadeyemi-cts/project-time-platform'
MAX_ARCHIVE = 262144


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


transport = load('core_security_transport', ROOT/'scripts/release-test/resolve-flowhive-installed-deployment.py')


def require(value, code):
    if not value:
        raise ValueError(code)


def validate(run, jobs, artifact, archive):
    require(run.get('workflow_id') == 315562561 and run.get('path') == '.github/workflows/projectpulse-deploy-test.yml', 'wrong_native_workflow')
    require((run.get('repository') or {}).get('full_name') == REPOSITORY, 'wrong_repository')
    require(run.get('event') == 'workflow_dispatch' and run.get('head_branch') == 'main', 'wrong_trigger')
    require(run.get('status') == 'completed' and run.get('conclusion') == 'success', 'release_not_successful')
    source = run.get('head_sha', '')
    require(isinstance(source, str) and re.fullmatch('[0-9a-f]{40}', source), 'invalid_source')
    number, attempt = run.get('id'), run.get('run_attempt')
    require(type(number) is int and number > 0 and type(attempt) is int and attempt > 0, 'invalid_attempt')
    selected = [j for j in jobs if j.get('name') == 'Validate, migrate, deploy, and verify protected Test']
    require(len(selected) == 1 and selected[0].get('conclusion') == 'success', 'native_job_not_successful')
    steps = selected[0].get('steps', [])
    for name in ('Admit exact merged main core release', 'Sign in for admitted core Test release',
                 'Stage, authenticate, validate recovery, and deploy core Test API'):
        matches = [s for s in steps if s.get('name') == name]
        require(len(matches) == 1 and matches[0].get('conclusion') == 'success', 'missing_core_step')
    provenance = artifact.get('workflow_run') or {}
    require(artifact.get('name') == f'core-test-release-{number}-{attempt}' and artifact.get('expired') is False, 'wrong_artifact_attempt')
    require(provenance.get('id') == number and provenance.get('head_sha') == source
            and provenance.get('head_branch') == 'main', 'wrong_artifact_provenance')
    require(len(archive) <= MAX_ARCHIVE and artifact.get('digest') == 'sha256:'+hashlib.sha256(archive).hexdigest(), 'wrong_artifact_digest')
    with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
        entries = bundle.infolist()
        require(len(entries) == 1 and entries[0].filename == 'core-test-release-summary.json'
                and entries[0].file_size <= MAX_ARCHIVE, 'unexpected_archive_entry')
        require((entries[0].external_attr >> 16) & 0o170000 != 0o120000, 'archive_link_forbidden')
        raw = bundle.read(entries[0])
        require(len(raw) <= MAX_ARCHIVE, 'summary_too_large')
        summary = json.loads(raw)
    require(summary.get('result') == 'PASS' and summary.get('phase') == 'complete', 'release_receipt_failed')
    require(summary.get('productionMutation') is False and summary.get('oracleMutation') is False, 'outside_core_scope')
    require(summary.get('releaseCommit') == source and summary.get('rollback') == 'PASS', 'source_or_recovery_not_verified')
    require(summary.get('deployedRevision') == f'ca-phd-test-api-westus3--cp-{number}-{attempt}', 'wrong_installed_revision')
    image = summary.get('deployedImage', '')
    require(isinstance(image, str) and re.fullmatch(r'acrphdtest7825cc\.azurecr\.io/project-health-dashboard-api@sha256:[0-9a-f]{64}', image), 'wrong_immutable_api_image')
    for name in ('candidateCanary', 'promotionCanary', 'installedCanary'):
        canary = summary.get(name) or {}
        require(canary.get('result') == 'PASS' and canary.get('sourceCommit') == source, 'canary_identity_not_verified')
    require('/api/core-release/source' in summary['installedCanary'].get('checks', []), 'immutable_source_probe_missing')
    return {'applicationSha': source, 'deploymentRunId': number, 'deploymentAttempt': attempt,
            'apiImage': image, 'apiRevision': summary['deployedRevision'], 'identityScope': 'api_only'}


def main():
    safe = Path(os.environ['SAFE_EVIDENCE_DIR'])
    private = Path(os.environ['EVIDENCE_DIR'])
    safe.mkdir(parents=True, exist_ok=True)
    private.mkdir(parents=True, exist_ok=True)
    identity = private/'flowhive-installed-identity.json'
    identity.unlink(missing_ok=True)
    report = {'status': 'failed', 'businessMutation': False, 'productionMutation': False,
              'identityScope': 'api_only', 'webIdentityVerified': False}
    try:
        require(os.environ.get('GITHUB_REPOSITORY') == REPOSITORY and os.environ.get('GITHUB_REF') == 'refs/heads/main', 'untrusted_verifier')
        number = os.environ.get('DEPLOYMENT_RUN_ID', '')
        require(re.fullmatch('[0-9]{1,20}', number), 'invalid_run_input')
        token = os.environ['GH_TOKEN']
        root = '/repos/'+REPOSITORY
        run = transport.api_json(root+'/actions/runs/'+number, token)
        require(str(run.get('id')) == number, 'run_identity_mismatch')
        verifier = os.environ.get('GITHUB_SHA', '')
        require(re.fullmatch('[0-9a-f]{40}', verifier), 'invalid_verifier_sha')
        require(transport.api_json(root+'/git/ref/heads/main', token)['object']['sha'] == verifier, 'stale_verifier')
        if verifier != run.get('head_sha'):
            comparison = transport.api_json(root+'/compare/'+run['head_sha']+'...'+verifier, token)
            require(comparison.get('status') == 'ahead' and comparison.get('merge_base_commit', {}).get('sha') == run['head_sha'], 'verifier_not_descendant')
        jobs = transport.inventory(root+'/actions/runs/'+number+'/attempts/'+str(run['run_attempt'])+'/jobs', 'jobs', token)
        artifacts = transport.inventory(root+'/actions/runs/'+number+'/artifacts', 'artifacts', token)
        name = f"core-test-release-{number}-{run['run_attempt']}"
        selected = [a for a in artifacts if a.get('name') == name and a.get('expired') is False]
        require(len(selected) == 1, 'missing_or_ambiguous_artifact')
        artifact = selected[0]
        require(type(artifact.get('id')) is int and artifact['id'] > 0, 'invalid_artifact_id')
        archive = transport.api_read(root+'/actions/artifacts/'+str(artifact['id'])+'/zip', token, MAX_ARCHIVE, 45)
        installed = validate(run, jobs, artifact, archive)
        latest = transport.api_json(root+'/actions/runs/'+number, token)
        require(all(latest.get(k) == run.get(k) for k in ('id', 'head_sha', 'run_attempt', 'status', 'conclusion', 'updated_at')), 'deployment_changed')
        sys.path.insert(0, str(ROOT/'scripts/resilience'))
        from core_test_canary import check, ORIGIN
        live = check(ORIGIN, source=installed['applicationSha'])
        require(live.get('result') == 'PASS' and live.get('sourceCommit') == installed['applicationSha'], 'live_api_identity_mismatch')
        report.update(status='passed', installed=installed, liveSource=live['sourceCommit'])
        identity.write_text(json.dumps(report)+'\n')
        boundaries = load('core_installed_boundaries', ROOT/'scripts/security/verify-installed-read-boundaries.py')
        result = boundaries.main()
    except Exception as error:
        report['diagnosticCode'] = str(error) if isinstance(error, (ValueError, transport.ResolutionError)) else type(error).__name__
        result = 1
    (safe/'core-installed-identity.json').write_text(json.dumps(report, indent=2)+'\n')
    print('CORE_INSTALLED_API_IDENTITY='+report['status'].upper())
    return result


if __name__ == '__main__':
    sys.exit(main())
