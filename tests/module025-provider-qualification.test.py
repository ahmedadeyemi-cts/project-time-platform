#!/usr/bin/env python3
import base64
import copy
import contextlib
import io
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('qualification', ROOT / 'scripts/release-test/run-module025-provider-qualification.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
UAMI = '/subscriptions/sub/resourceGroups/test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/pull'
ENV = '/subscriptions/sub/resourceGroups/test/providers/Microsoft.App/managedEnvironments/test'
IMAGE = 'testacr.azurecr.io/module025-qualification@sha256:' + 'a' * 64
SOURCE = 'b' * 40


def api_fixture():
    return {'id': '/subscriptions/sub/resourceGroups/test/providers/Microsoft.App/containerApps/api-test', 'location': 'westus',
        'identity': {'userAssignedIdentities': {UAMI: {}}}, 'properties': {
        'managedEnvironmentId': ENV, 'latestRevisionName': 'api-test--existing',
        'configuration': {'registries': [{'server': 'testacr.azurecr.io', 'identity': UAMI}], 'secrets': []},
        'template': {'containers': [{'name': 'api', 'image': 'unchanged', 'env': [
            {'name': 'PROJECTPULSE_ENVIRONMENT', 'value': 'test'},
            {'name': 'ConnectionStrings__DefaultConnection', 'secretRef': 'database'},
            {'name': 'PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY', 'secretRef': 'encryption'},
            {'name': 'PROJECTPULSE_CLAUDE_MODEL', 'value': 'approved-configured-model'},
            {'name': 'PROJECTPULSE_OPENAI_MODEL', 'value': 'other-model'},
            {'name': 'PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN', 'secretRef': 'private-token'},
            {'name': 'UNRELATED_MAIL_SECRET', 'secretRef': 'mail'}]}]}}}


def chunks(report):
    encoded = base64.b64encode(json.dumps(report).encode()).decode()
    pieces = [encoded[i:i+48] for i in range(0, len(encoded), 48)]
    return [{'Log': f'MODULE025_QUALIFICATION_CHUNK:{i+1}:{len(pieces)}:{piece}'} for i, piece in enumerate(pieces)]


class QualificationTest(unittest.TestCase):
    def setUp(self):
        self.env = patch.dict(os.environ, {'GITHUB_SHA': SOURCE, 'GITHUB_EVENT_NAME': 'workflow_dispatch',
            'GITHUB_REF': 'refs/heads/main', 'GITHUB_RUN_ATTEMPT': '1', 'GITHUB_RUN_ID': '12345',
            'QUALIFICATION_PROVIDER': 'claude', 'AZURE_RESOURCE_GROUP': 'test',
            'AZURE_API_APP': 'api-test', 'AZURE_ACR_NAME': 'testacr'})
        self.env.start()
        self.addCleanup(self.env.stop)

    def test_only_chosen_configuration_and_referenced_secrets_are_copied(self):
        api = api_fixture()
        secrets = [{'name': 'database', 'value': 'synthetic-db'}, {'name': 'encryption', 'value': 'synthetic-key'}, {'name': 'mail', 'value': 'must-not-copy'}]
        payload = module.build_payload(api, 'claude', IMAGE, 'm025q-12345-1', '12345-1', secrets)
        env = payload['properties']['template']['containers'][0]['env']
        self.assertNotIn('PROJECTPULSE_OPENAI_MODEL', [x['name'] for x in env])
        self.assertNotIn('must-not-copy', json.dumps(payload))
        self.assertNotIn('PRIVATE_INFERENCE', json.dumps(payload))
        config = payload['properties']['configuration']
        self.assertEqual(config['replicaRetryLimit'], 0)
        self.assertEqual(config['manualTriggerConfig'], {'replicaCompletionCount': 1, 'parallelism': 1})
        self.assertEqual(config['replicaTimeout'], 240)
        self.assertEqual(payload['properties']['template']['containers'][0]['args'][-2:], ['claude', '--use-module064-store'])

    def test_production_or_unowned_identity_and_mutable_image_rejected(self):
        for change in ['production', 'identity', 'image']:
            api = api_fixture()
            image = IMAGE
            if change == 'production': api['properties']['template']['containers'][0]['env'][0]['value'] = 'production'
            if change == 'identity': api['identity']['userAssignedIdentities'] = {}
            if change == 'image': image = 'testacr.azurecr.io/module025-qualification:latest'
            with self.subTest(change=change), self.assertRaises(RuntimeError):
                module.build_payload(api, 'claude', image, 'm025q-12345-1', '12345-1', [{'name': 'database', 'value': 'db'}, {'name': 'encryption', 'value': 'key'}])

    def test_evidence_transport_requires_complete_consistent_chunks(self):
        expected = {'passed': False, 'provider': 'claude', 'diagnostic': 'closed_code'}
        logs = chunks(expected)
        self.assertEqual(module.decode_report(list(reversed(logs))), expected)
        with self.assertRaises(RuntimeError): module.decode_report(logs[:-1])
        with self.assertRaises(RuntimeError): module.decode_report(logs + [{'Log': 'MODULE025_QUALIFICATION_CHUNK:1:999:AA=='}])

    def exercise(self, *, passed=True, fail_start=False, wrong_owner=False):
        calls, job = [], None
        api = api_fixture()
        result = {'passed': passed, 'called': True, 'provider': 'claude', 'fullLifecyclePassed': False, 'productionMutation': False}
        def az(*args, **kwargs):
            nonlocal job
            calls.append(args)
            if args[:2] == ('account', 'show'): return {'id': 'sub'}
            if args[:2] == ('containerapp', 'show'): return copy.deepcopy(api)
            if args[:2] == ('acr', 'build'): return {}
            if args[:3] == ('acr', 'repository', 'show'): return {'digest': 'sha256:' + 'a' * 64}
            if args[:3] == ('containerapp', 'secret', 'list'): return [{'name': 'database', 'value': 'synthetic-db'}, {'name': 'encryption', 'value': 'synthetic-key'}]
            if args[:3] == ('containerapp', 'job', 'list'): return [dict(job, name='m025q-12345-1')] if job else []
            if args[0] == 'rest':
                job = json.loads(Path(args[args.index('--body')+1][1:]).read_text())
                job['properties']['provisioningState'] = 'Succeeded'
                if wrong_owner: job['tags']['projectpulse-run'] = 'someone-else'
                return {}
            if args[:3] == ('containerapp', 'job', 'show'): return job
            if args[:3] == ('containerapp', 'job', 'start'):
                if fail_start: raise RuntimeError('azure_operation_failed_containerapp')
                return {'name': 'execution-one'}
            if args[:4] == ('containerapp', 'job', 'execution', 'list'): return [{'name': 'execution-one', 'properties': {'status': 'Succeeded' if passed else 'Failed'}}]
            if args[:4] == ('containerapp', 'job', 'logs', 'show'): return chunks(result)
            if args[:3] == ('containerapp', 'job', 'delete'): job = None; return {}
            raise AssertionError(args)
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {'RUNNER_TEMP': directory}), patch.object(module, 'az', az):
            with contextlib.redirect_stdout(io.StringIO()):
                code = module.main()
            report = json.loads((Path(directory) / 'module025-qualification-evidence/qualification.json').read_text())
            self.assertNotIn('synthetic-key', json.dumps(report))
        starts = [x for x in calls if x[:3] == ('containerapp', 'job', 'start')]
        self.assertLessEqual(len(starts), 1)
        self.assertFalse(any(x[:3] == ('containerapp', 'update') for x in calls))
        return code, report, calls

    def test_success_collects_provider_evidence_and_cleans_up_without_deploy(self):
        code, report, _ = self.exercise()
        self.assertEqual(code, 0)
        self.assertTrue(report['apiDeploymentUnchanged'])
        self.assertEqual(report['temporaryJobCleanup'], 'verified')
        self.assertFalse(report['fullLifecyclePassed'])

    def test_provider_failure_is_retained_without_retry(self):
        code, report, calls = self.exercise(passed=False)
        self.assertEqual(code, 1)
        self.assertTrue(report['called'])
        self.assertEqual(report['executionStatus'], 'Failed')
        self.assertEqual(report['temporaryJobCleanup'], 'verified')
        self.assertEqual(sum(x[:3] == ('containerapp', 'job', 'start') for x in calls), 1)

    def test_start_failure_cleans_up_without_repeating_start(self):
        code, report, _ = self.exercise(fail_start=True)
        self.assertEqual(code, 1)
        self.assertEqual(report['temporaryJobCleanup'], 'verified')

    def test_changed_job_ownership_refuses_start_and_deletion(self):
        code, report, calls = self.exercise(wrong_owner=True)
        self.assertEqual(code, 1)
        self.assertEqual(report['temporaryJobCleanup'], 'not_verified')
        self.assertFalse(any(x[:3] in [('containerapp', 'job', 'start'), ('containerapp', 'job', 'delete')] for x in calls))

    def test_workflow_preserves_native_test_gate_and_manual_only_source(self):
        source = (ROOT / '.github/workflows/module025-provider-qualification.yml').read_text()
        for marker in ['workflow_dispatch:', 'name: test', 'group: projectpulse-deploy-test', 'cancel-in-progress: false',
            '"$GITHUB_REF" == refs/heads/main', '"$GITHUB_RUN_ATTEMPT" == 1', 'persist-credentials: false',
            '"$(git rev-parse origin/main)" == "$GITHUB_SHA"', 'contents: read', 'id-token: write']:
            self.assertIn(marker, source)
        for forbidden in ['pull_request:', 'push:', 'schedule:', 'actions: write', 'contents: write', 'environment: production', 'recover_private_runtime']:
            self.assertNotIn(forbidden, source)


if __name__ == '__main__': unittest.main()
