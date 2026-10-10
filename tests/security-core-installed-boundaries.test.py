import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('core_boundary', ROOT/'scripts/security/verify-core-installed-read-boundaries.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def fixtures():
    source = 'a'*40
    run = {'id': 123, 'run_attempt': 1, 'workflow_id': 315562561,
           'path': '.github/workflows/projectpulse-deploy-test.yml',
           'repository': {'full_name': module.REPOSITORY}, 'event': 'workflow_dispatch',
           'head_branch': 'main', 'head_sha': source, 'status': 'completed', 'conclusion': 'success'}
    jobs = [{'name': 'Validate, migrate, deploy, and verify protected Test', 'conclusion': 'success',
             'steps': [{'name': n, 'conclusion': 'success'} for n in (
                 'Admit exact merged main core release', 'Sign in for admitted core Test release',
                 'Stage, authenticate, validate recovery, and deploy core Test API')]}]
    artifact = {'id': 1, 'name': 'core-test-release-123-1', 'expired': False,
                'workflow_run': {'id': 123, 'head_sha': source, 'head_branch': 'main'}}
    summary = {'result': 'PASS', 'phase': 'complete', 'productionMutation': False,
               'oracleMutation': False, 'releaseCommit': source, 'rollback': 'PASS',
               'deployedRevision': 'ca-phd-test-api-westus3--cp-123-1',
               'deployedImage': 'acrphdtest7825cc.azurecr.io/project-health-dashboard-api@sha256:'+'b'*64}
    for name in ('candidateCanary', 'promotionCanary', 'installedCanary'):
        summary[name] = {'result': 'PASS', 'sourceCommit': source, 'checks': ['/api/core-release/source']}
    return run, jobs, artifact, summary


def archive(summary, artifact, name='core-test-release-summary.json'):
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, 'w') as bundle:
        bundle.writestr(name, json.dumps(summary))
    data = buffer.getvalue()
    artifact['digest'] = 'sha256:'+hashlib.sha256(data).hexdigest()
    return data


class InstalledCoreSecurity(unittest.TestCase):
    def test_verified_core_receipt_is_api_only(self):
        run, jobs, artifact, summary = fixtures()
        result = module.validate(run, jobs, artifact, archive(summary, artifact))
        self.assertEqual(result['applicationSha'], run['head_sha'])
        self.assertEqual(result['identityScope'], 'api_only')
        self.assertNotIn('webImage', result)

    def test_untrusted_or_incomplete_release_is_rejected(self):
        mutations = [
            lambda r,j,a,s: r.update(workflow_id=1),
            lambda r,j,a,s: r.update(path='.github/workflows/other.yml'),
            lambda r,j,a,s: r.update(repository={'full_name': 'foreign/repo'}),
            lambda r,j,a,s: r.update(event='pull_request'),
            lambda r,j,a,s: r.update(head_branch='feature'),
            lambda r,j,a,s: r.update(status='in_progress'),
            lambda r,j,a,s: r.update(conclusion='failure'),
            lambda r,j,a,s: r.update(id=True),
            lambda r,j,a,s: j.clear(),
            lambda r,j,a,s: j[0]['steps'][0].update(conclusion='skipped'),
            lambda r,j,a,s: a.update(expired=True),
            lambda r,j,a,s: a.update(name='core-test-release-123-2'),
            lambda r,j,a,s: a['workflow_run'].update(head_sha='c'*40),
            lambda r,j,a,s: s.update(result='BLOCKED'),
            lambda r,j,a,s: s.update(productionMutation=True),
            lambda r,j,a,s: s.update(oracleMutation=True),
            lambda r,j,a,s: s.update(releaseCommit='c'*40),
            lambda r,j,a,s: s.update(rollback='NOT_EXECUTED'),
            lambda r,j,a,s: s.update(deployedRevision='ca-phd-test-api-westus3--cp-999-1'),
            lambda r,j,a,s: s.update(deployedImage='foreign.azurecr.io/api@sha256:'+'b'*64),
            lambda r,j,a,s: s['installedCanary'].update(sourceCommit='c'*40),
            lambda r,j,a,s: s['installedCanary'].update(checks=[]),
        ]
        for index, mutation in enumerate(mutations):
            with self.subTest(case=index):
                run, jobs, artifact, summary = fixtures()
                mutation(run, jobs, artifact, summary)
                data = archive(summary, artifact)
                with self.assertRaises(ValueError):
                    module.validate(run, jobs, artifact, data)
        print('CORE_INSTALLED_SECURITY_NEGATIVES=PASS cases='+str(len(mutations)))

    def test_tampered_or_unexpected_archive_is_rejected(self):
        run, jobs, artifact, summary = fixtures()
        data = archive(summary, artifact)
        with self.assertRaises(ValueError):
            module.validate(run, jobs, artifact, data+b'tampered')
        for name in ('../core-test-release-summary.json', 'other.json'):
            data = archive(summary, artifact, name)
            with self.assertRaises(ValueError):
                module.validate(run, jobs, artifact, data)


if __name__ == '__main__':
    unittest.main()
