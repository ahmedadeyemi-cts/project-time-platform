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
import subprocess
import yaml
from module025_qualification_workflow import deployment_projection, GATE, NAMES, SHARED
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
        source = (ROOT / '.github/workflows/projectpulse-deploy-test.yml').read_text()
        doc = yaml.safe_load(source)
        base = yaml.safe_load(subprocess.check_output(['git', 'show',
            'dd6403e4ba89a8d15fa6308b85a0d20994d6cd13:.github/workflows/projectpulse-deploy-test.yml'], cwd=ROOT, text=True))
        # Exact equality proves every original deployment command, condition,
        # approval, concurrency and rollback is preserved in normal deploy mode.
        self.assertEqual(deployment_projection(doc), base)
        self.assertEqual(doc[True]['workflow_dispatch']['inputs']['qualification_provider'], {
            'description': 'none deploys normally; claude/openai qualifies one Plan phase WITHOUT application deployment',
            'required': False, 'default': 'none', 'type': 'choice', 'options': ['none', 'claude', 'openai']})
        steps = doc['jobs']['deploy']['steps']
        self.assertEqual(len(steps), len(base['jobs']['deploy']['steps']) + len(NAMES))
        for step in steps:
            if step['name'] not in NAMES | SHARED:
                self.assertIn(GATE, step['if'], step['name'])
        guard = next(x for x in steps if x['name'] == 'Validate qualification-only selection')
        for marker in ['"$GITHUB_REF" == refs/heads/main', '"$GITHUB_RUN_ATTEMPT" == 1',
            '"$TARGET_RELEASE_BRANCH" == main', '"$TARGET_RELEASE_COMMIT" == "$GITHUB_SHA"',
            '"$RECOVER_PRIVATE_RUNTIME" != true', '"$ACCEPTANCE_SCOPE" == sow_role',
            '"$(git rev-parse origin/main)" == "$GITHUB_SHA"']:
            self.assertIn(marker, guard['run'])
        self.assertLess(steps.index(guard), next(i for i,x in enumerate(steps) if 'azure/login' in x.get('uses','')))
        for name in ['Compile the isolated one-phase runner', 'Qualify one phase in the Test private network']:
            self.assertEqual(next(x for x in steps if x['name'] == name)['if'],
                "inputs.qualification_provider == 'claude' || inputs.qualification_provider == 'openai'")
        self.assertFalse((ROOT / '.github/workflows/module025-provider-qualification.yml').exists())

    def test_real_shell_guard_rejects_unapproved_qualification_inputs(self):
        doc = yaml.safe_load((ROOT / '.github/workflows/projectpulse-deploy-test.yml').read_text())
        script = next(x['run'] for x in doc['jobs']['deploy']['steps'] if x['name'] == 'Validate qualification-only selection')
        with tempfile.TemporaryDirectory() as directory:
            git = Path(directory) / 'git'
            git.write_text('#!/bin/sh\nif [ "$1" = rev-parse ]; then printf "%s\\n" "$GITHUB_SHA"; fi\n')
            git.chmod(0o700)
            environment = dict(os.environ, PATH=directory + ':' + os.environ['PATH'],
                TARGET_RELEASE_BRANCH='main', TARGET_RELEASE_COMMIT=SOURCE, ACCEPTANCE_SCOPE='sow_role',
                RECOVER_PRIVATE_RUNTIME='false', EXPECTED_CONTROLLER_SHA='')
            for changes in [{}, {'QUALIFICATION_PROVIDER': 'openai'}, {'QUALIFICATION_PROVIDER': 'none'},
                {'TARGET_RELEASE_BRANCH': 'unapproved'}, {'TARGET_RELEASE_COMMIT': 'c' * 40},
                {'GITHUB_REF': 'refs/heads/feature'}, {'RECOVER_PRIVATE_RUNTIME': 'true'},
                {'ACCEPTANCE_SCOPE': 'full'}, {'EXPECTED_CONTROLLER_SHA': SOURCE},
                {'GITHUB_RUN_ATTEMPT': '2'}, {'QUALIFICATION_PROVIDER': 'unknown'}]:
                result = subprocess.run(['bash', '-c', script], env={**environment, **changes}, capture_output=True)
                with self.subTest(changes=changes):
                    self.assertEqual(result.returncode == 0, not changes or changes.get('QUALIFICATION_PROVIDER') in ('openai', 'none'))

    def test_qualification_cannot_accidentally_enter_deployment_steps(self):
        doc = yaml.safe_load((ROOT / '.github/workflows/projectpulse-deploy-test.yml').read_text())
        for step in doc['jobs']['deploy']['steps']:
            if step['name'] not in NAMES | SHARED:
                # These alternatives are both false for either provider input.
                for provider in ['claude', 'openai']:
                    self.assertFalse(provider == '' or provider == 'none')
                self.assertTrue(step['if'] == GATE or step['if'].startswith('${{ ' + GATE + ' && ('))


if __name__ == '__main__': unittest.main()
