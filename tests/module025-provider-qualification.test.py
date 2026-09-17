#!/usr/bin/env python3
import base64
import copy
import contextlib
import io
import importlib.util
import json
import os
import re
from pathlib import Path
import tempfile
import subprocess
import yaml
from module025_qualification_workflow import deployment_projection, previous_acceptance_projection, GATE, NAMES, SHARED
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
            {'name': 'PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION', 'value': 'true'},
            {'name': 'PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED', 'value': 'true'},
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

    def test_azure_environment_representation_is_not_configuration_drift(self):
        # Azure EnvironmentVar has optional value and secretRef fields. Null
        # fields and ordering differ across API/CLI response representations.
        original = [{'name': 'DB', 'secretRef': 'database'}, {'name': 'MODE', 'value': 'test'}]
        returned = [{'name': 'MODE', 'value': 'test', 'secretRef': None},
            {'name': 'DB', 'secretRef': 'database', 'value': ''}]
        self.assertEqual(module.environment_contract(original), module.environment_contract(returned))
        for change in [original + [original[0]], [{'name': 'DB', 'secretRef': 'database', 'value': 'plaintext'}],
            [{'name': 'MODE', 'value': '$(OTHER)'}], [{'name': 'MODE', 'value': 'test', 'unknown': 'populated'}]]:
            with self.subTest(change=change), self.assertRaises(RuntimeError):
                module.environment_contract(change)

    def test_actual_sow_adapter_policy_dependencies_reach_both_provider_jobs(self):
        adapter = (ROOT / 'src/backend/ProjectTime.Api/Ai/Module025ExternalSowAdapter.cs').read_text()
        dependencies = set(re.findall(r'Environment.GetEnvironmentVariable\("([A-Z_]+)"\)', adapter))
        self.assertEqual(dependencies, {
            'PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION',
            'PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED'})
        for provider in ('claude', 'openai'):
            env = module.selected_environment(api_fixture(), provider)
            values = {item['name']: item for item in env}
            for name in dependencies:
                self.assertEqual(values[name], {'name': name, 'value': 'true'})
            module.require_existing_external_policy(env)

    def test_api_comparison_normalizes_only_environment_representation(self):
        before = api_fixture()['properties']['template']
        after = copy.deepcopy(before)
        after['containers'][0]['env'].reverse()
        for binding in after['containers'][0]['env']:
            binding.setdefault('value', None)
            binding.setdefault('secretRef', None)
        self.assertEqual(module.api_template_difference(before, after), [])
        for field, value, category in [('image', 'different', 'container_image'),
            ('command', ['changed'], 'container_command'), ('resources', {'cpu': 2}, 'container_resources'),
            ('unknown-secret-name', 'private-secret-value', 'container_other')]:
            changed = copy.deepcopy(after)
            changed['containers'][0][field] = value
            self.assertEqual(module.api_template_difference(before, changed), [category])
        changed = copy.deepcopy(after)
        changed['containers'][0]['env'][0]['value'] = 'private-secret-value'
        changed['containers'][0]['env'][0].pop('secretRef', None)
        self.assertEqual(module.api_template_difference(before, changed), ['container_env'])
        self.assertNotIn('private-secret-value', json.dumps(module.api_template_difference(before, changed)))
        changed['scale'] = {'minReplicas': 2}
        self.assertIn('template_other', module.api_template_difference(before, changed))

    def exercise(self, *, passed=True, fail_start=False, wrong_owner=False, drift=None, policy=None, api_after=None):
        calls, job = [], None
        api = api_fixture()
        if policy:
            field, value = policy
            env = api['properties']['template']['containers'][0]['env']
            item = next(x for x in env if x['name'] == field)
            if value is None: env.remove(item)
            else: item['value'] = value
        api_reads = 0
        result = {'passed': passed, 'called': True, 'provider': 'claude', 'fullLifecyclePassed': False, 'productionMutation': False}
        def az(*args, **kwargs):
            nonlocal job, api_reads
            calls.append(args)
            if args[:2] == ('account', 'show'): return {'id': 'sub'}
            if args[:2] == ('containerapp', 'show'):
                api_reads += 1
                if api_reads > 1 and api_after == 'error': raise RuntimeError('azure_operation_failed_containerapp')
                response = copy.deepcopy(api)
                if api_reads > 1 and api_after == 'revision': response['properties']['latestRevisionName'] = 'new-revision'
                if api_reads > 1 and api_after == 'template': response['properties']['template']['containers'][0]['image'] = 'new-image'
                return response
            if args[:2] == ('acr', 'build'): return {}
            if args[:3] == ('acr', 'repository', 'show'): return {'digest': 'sha256:' + 'a' * 64}
            if args[:3] == ('containerapp', 'secret', 'list'): return [{'name': 'database', 'value': 'synthetic-db'}, {'name': 'encryption', 'value': 'synthetic-key'}]
            if args[:3] == ('containerapp', 'job', 'list'): return [dict(job, name='m025q-12345-1')] if job else []
            if args[0] == 'rest':
                job = json.loads(Path(args[args.index('--body')+1][1:]).read_text())
                job['properties']['provisioningState'] = 'Succeeded'
                container = job['properties']['template']['containers'][0]
                container['env'] = list(reversed([{'value': None, 'secretRef': None, **x} for x in container['env']]))
                if drift == 'args': container['args'].append('--unauthorized')
                if drift == 'value': container['env'][0]['value'] = 'different'
                if drift == 'secret': next(x for x in container['env'] if x['secretRef'])['secretRef'] = 'different-secret'
                if drift == 'names': container['env'].append({'name': 'UNAPPROVED', 'value': 'different'})
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

    def test_missing_or_disabled_inherited_policy_fails_before_any_azure_mutation(self):
        for name in ('PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION',
            'PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED'):
            for value in (None, 'false', 'invalid'):
                with self.subTest(name=name, value=value):
                    code, report, calls = self.exercise(policy=(name, value))
                    self.assertEqual(code, 1)
                    self.assertFalse(report['called'])
                    self.assertEqual(report['stage'], 'read_test_configuration')
                    self.assertEqual(report['diagnostic'], 'sanitized_external_policy_missing' if value is None else 'sanitized_external_policy_disabled')
                    self.assertTrue(report['apiDeploymentUnchanged'])
                    self.assertTrue(all(x[:2] == ('containerapp', 'show') for x in calls))

    def test_application_read_failure_is_distinct_from_observed_revision_or_template_change(self):
        for state, diagnostic in [('error', 'azure_operation_failed_containerapp'),
            ('revision', 'api_revision_changed'), ('template', 'api_template_changed')]:
            with self.subTest(state=state):
                code, report, _ = self.exercise(api_after=state)
                self.assertEqual(code, 1)
                self.assertEqual(report['apiDeploymentUnchanged'], 'not_verified' if state == 'error' else False)
                self.assertEqual(report['apiDeploymentVerificationDiagnostic'], diagnostic)
                self.assertEqual(report['temporaryJobCleanup'], 'verified')

    def test_start_failure_cleans_up_without_repeating_start(self):
        code, report, _ = self.exercise(fail_start=True)
        self.assertEqual(code, 1)
        self.assertEqual(report['temporaryJobCleanup'], 'verified')

    def test_changed_job_ownership_refuses_start_and_deletion(self):
        code, report, calls = self.exercise(wrong_owner=True)
        self.assertEqual(code, 1)
        self.assertEqual(report['temporaryJobCleanup'], 'not_verified')
        self.assertFalse(any(x[:3] in [('containerapp', 'job', 'start'), ('containerapp', 'job', 'delete')] for x in calls))

    def test_real_configuration_drift_blocks_inference_but_cleans_owned_job(self):
        for drift, diagnostic in [('args', 'args'), ('value', 'env_bindings'),
            ('secret', 'env_bindings'), ('names', 'env_names')]:
            with self.subTest(drift=drift):
                code, report, calls = self.exercise(drift=drift)
                self.assertEqual(code, 1)
                self.assertFalse(report['called'])
                self.assertEqual(report['diagnostic'], 'qualification_job_' + diagnostic + '_mismatch')
                self.assertEqual(report['temporaryJobCleanup'], 'verified')
                self.assertFalse(any(x[:3] == ('containerapp', 'job', 'start') for x in calls))

    def test_prior_cleanup_is_limited_to_recorded_unstarted_owned_job(self):
        name = 'm025q-' + module.PRIOR_SCOPE
        for mutation in [None, 'run', 'source', 'image', 'environment', 'executed']:
            calls, deleted = [], False
            job = {'tags': {'projectpulse-scope': 'module025-one-phase-test',
                'projectpulse-run': module.PRIOR_SCOPE, 'projectpulse-source': module.PRIOR_SOURCE},
                'properties': {'environmentId': ENV, 'configuration': {'triggerType': 'Manual'},
                    'template': {'containers': [{'name': name, 'image': IMAGE}]}}}
            if mutation in ('run', 'source'): job['tags']['projectpulse-' + mutation] = 'unrelated'
            if mutation == 'image': job['properties']['template']['containers'][0]['image'] = 'other'
            if mutation == 'environment': job['properties']['environmentId'] = 'production'
            def az(*args, **kwargs):
                nonlocal deleted
                calls.append(args)
                if args[:3] == ('containerapp', 'job', 'list'): return [] if deleted else [{'name': name}, {'name': 'unrelated'}]
                if args[:3] == ('containerapp', 'job', 'show'): return job
                if args[:3] == ('acr', 'repository', 'show'): return {'digest': 'sha256:' + 'a' * 64}
                if args[:4] == ('containerapp', 'job', 'execution', 'list'): return [{'name': 'ran'}] if mutation == 'executed' else []
                if args[:3] == ('containerapp', 'job', 'delete'): deleted = True; return None
                raise AssertionError(args)
            with self.subTest(mutation=mutation), patch.object(module, 'az', az):
                if mutation:
                    with self.assertRaises(RuntimeError): module.cleanup_prior_unstarted_job(api_fixture(), 'testacr', 'test')
                    self.assertFalse(deleted)
                else:
                    self.assertEqual(module.cleanup_prior_unstarted_job(api_fixture(), 'testacr', 'test'), 'verified')
                    self.assertTrue(deleted)
                self.assertFalse(any(x[:3] == ('containerapp', 'job', 'start') for x in calls))

    def test_workflow_preserves_native_test_gate_and_manual_only_source(self):
        source = (ROOT / '.github/workflows/projectpulse-deploy-test.yml').read_text()
        doc = yaml.safe_load(source)
        base = yaml.safe_load(subprocess.check_output(['git', 'show',
            'dd6403e4ba89a8d15fa6308b85a0d20994d6cd13:.github/workflows/projectpulse-deploy-test.yml'], cwd=ROOT, text=True))
        # Exact equality proves every original deployment command, condition,
        # approval, concurrency and rollback is preserved in normal deploy mode.
        self.assertEqual(previous_acceptance_projection(doc), base)
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
