"""Synthetic acceptance-evidence regressions. No network, secrets or live login."""
import ast
import copy
import importlib.util
import json
import os
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASE = '163c949ad5f5be7e3b17cdf073f5923b92894892'
def load(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module
DIAG = load('diagnostic_test_subject', 'scripts/security/safe_uat_diagnostics.py')
PUB = load('publisher_test_subject', 'scripts/security/publish-safe-uat-evidence.py')


def model(provider='openai'):
    return {'result': {'modelProvider': provider,
            'answer': {'directConclusion': 'Light is refracted.', 'detailedAnalysis': ['Then reflected.']},
            'targetDecisions': [{'target': 'celar_ai', 'outcome': 'skipped', 'reasonCode': 'provider_deadline_exceeded'},
                                {'target': provider, 'outcome': 'used'}]},
            'providerConfiguration': {'targets': ['celar_ai', provider]}}

def public_fact():
    return {'result': {'answer': {'directConclusion': 'There are 50 states.'},
                       'sources': [{'sourceCode': 'census_state_codes', 'statusCode': 200}]},
            'reliability': {'assessment': {'passed': True}}}


class DiagnosticTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.source = Path(self.tmp.name) / 'private'; self.source.mkdir()
        self.destination = Path(self.tmp.name) / 'public'

    def write(self, name, obj):
        (self.source / name).write_text(json.dumps(obj))

    def project(self):
        return DIAG.project(self.source)

    def test_previous_silent_failure_now_retains_finite_diagnostics(self):
        value = model('local')
        value['result']['answer'] = {'directConclusion': 'Synthetic fallback.', 'detailedAnalysis': []}
        value['result']['targetDecisions'] = []
        self.write('module064-provider-routing-smoke.json', value)
        PUB.publish(self.source, self.destination)
        obj = json.loads((self.destination / 'security-safe-uat-summary.json').read_text())
        diag = obj['functionalUatDiagnostics']
        self.assertFalse(diag['recordedPredicates']['model_routing']['provider_permitted_by_unchanged_acceptance'])
        self.assertEqual(diag['acceptanceVerdict'], 'not_inferred')
        self.assertEqual({p.name for p in self.destination.iterdir()}, {'security-safe-uat-summary.json'})

    def test_correct_content_never_becomes_an_acceptance_verdict(self):
        self.write('module064-provider-routing-smoke.json', model())
        diag = self.project()
        self.assertTrue(all(diag['recordedPredicates']['model_routing'].values()))
        self.assertEqual(diag['acceptanceVerdict'], 'not_inferred')
        self.assertEqual(diag['deadlineVerdict'], 'not_recorded')
        self.assertIs(diag['artifactPresenceIsNotAcceptance'], True)

    def test_gemini_reports_contract_difference_without_relaxing_it(self):
        self.write('module064-provider-routing-smoke.json', model('gemini'))
        data = self.project()
        flags = data['recordedPredicates']['model_routing']
        self.assertTrue(flags['refraction_explanation_present'])
        self.assertFalse(flags['provider_permitted_by_unchanged_acceptance'])
        self.assertEqual(data['providerEvidence']['selectedProvider'], 'gemini')

    def test_public_fact_requires_source_and_reliability(self):
        for key in ('text', 'source', 'reliability'):
            obj = public_fact()
            if key == 'text': obj['result']['answer']['directConclusion'] = 'Unknown count.'
            if key == 'source': obj['result']['sources'] = []
            if key == 'reliability': obj['reliability']['assessment']['passed'] = False
            with self.subTest(key=key): self.assertFalse(all(DIAG.public_fact_predicates(obj).values()))
        self.assertTrue(all(DIAG.public_fact_predicates(public_fact()).values()))

    def test_saved_provider_order_rejection(self):
        obj = model(); obj['result']['targetDecisions'].reverse()
        self.assertFalse(DIAG.model_predicates(obj)['saved_provider_order_preserved'])
        obj = model(); obj['result']['targetDecisions'][0]['target'] = 'foreign'
        self.assertFalse(DIAG.model_predicates(obj)['saved_provider_order_preserved'])

    def test_private_strings_are_not_reflected_anywhere(self):
        secret = 'SENTINEL_customer_email_session_password_project'
        obj = model(); obj['result']['modelProvider'] = secret
        obj['result']['answer']['directConclusion'] = secret
        obj['result']['answer']['detailedAnalysis'] = [secret]
        obj['result']['targetDecisions'] += [{'target': secret, 'outcome': secret, 'reasonCode': secret}]
        obj['result']['targetDecisions'][0]['reasonCode'] = secret
        obj['serverError'] = secret
        self.write('module064-provider-routing-smoke.json', obj)
        self.write('coordinator-login-redacted.json', {'email': secret, 'token': secret, 'sessionToken': secret})
        self.write('financial-secret.json', {'data': secret})
        (self.source / 'raw-response.log').write_text(secret)
        data = json.dumps(self.project())
        self.assertNotIn(secret, data); self.assertNotIn('raw-response', data)
        self.assertNotIn('financial-secret', data)
        self.assertNotIn('sha256', data)

    def test_planner_reconciliation_projects_only_finite_safe_state(self):
        secret = 'SENTINEL_customer_document_text'
        self.write('flowhive-planner-reconciliation.json', {
            'status':'passed', 'projectId':secret, 'assignedPmVerified':True,
            'workingCopy':{'rowVersion':'11111111-1111-1111-1111-111111111111',
                'workingRevision':42,'taskCount':27,'milestoneCount':3,
                'sowEvidencePresent':True,'approvedSowScopeReady':True,'readySowCount':1,
                'private':secret},
            'priorPlanner':{'httpStatus':200,'runId':secret,'terminal':True,'status':'completed',
                'phase':'candidate_review_required','candidateAvailable':True,'workingDraftPersisted':False,
                'private':secret},
            'latestPlanner':{'httpStatus':200,'runIdPresent':True,'terminal':True,'private':secret},
            'private':secret})
        projected=self.project()['recordedPredicates']['flowhive_planner_reconciliation']
        self.assertEqual(projected['reconciliation_status'],'passed')
        self.assertTrue(projected['assigned_pm_verified'])
        self.assertEqual(projected['working_copy_task_bucket'],'multiple')
        self.assertEqual(projected['ready_sow_bucket'],'one')
        self.assertEqual(projected['prior_planner_status'],'completed')
        self.assertEqual(projected['prior_planner_phase'],'candidate_review_required')
        self.assertTrue(projected['prior_planner_terminal'])
        self.assertTrue(projected['prior_candidate_available'])
        self.assertFalse(projected['prior_working_draft_persisted'])
        self.assertEqual(projected['latest_planner_http_status'],'200')
        self.assertTrue(projected['latest_planner_terminal'])
        self.assertNotIn(secret,json.dumps(projected))
        self.assertNotIn('rowVersion',json.dumps(projected))
        self.assertNotIn('runId',json.dumps(projected))
        self.assertNotIn('workingRevision',json.dumps(projected))

    def test_planner_reconciliation_unknown_values_do_not_escape(self):
        secret='SENTINEL_private_diagnostic'
        self.write('flowhive-planner-reconciliation.json', {
            'status':'blocked','diagnosticCode':secret,
            'workingCopy':{'taskCount':-1,'readySowCount':'2'},
            'priorPlanner':{'status':secret,'phase':secret,'terminal':False},
            'latestPlanner':{'httpStatus':418}})
        projected=self.project()['recordedPredicates']['flowhive_planner_reconciliation']
        self.assertEqual(projected['reconciliation_status'],'blocked')
        self.assertEqual(projected['working_copy_task_bucket'],'unrecognized')
        self.assertEqual(projected['ready_sow_bucket'],'unrecognized')
        self.assertEqual(projected['prior_planner_status'],'unrecognized')
        self.assertEqual(projected['prior_planner_phase'],'unrecognized')
        self.assertEqual(projected['latest_planner_http_status'],'unrecognized')
        self.assertEqual(projected['diagnostic_code'],'none')
        self.assertNotIn(secret,json.dumps(projected))

    def test_unknown_reason_is_not_disclosed(self):
        obj = model(); obj['result']['targetDecisions'][0]['reasonCode'] = 'SENTINEL_arbitrary_error'
        self.write('module064-provider-routing-smoke.json', obj)
        rows = self.project()['providerEvidence']['recordedDecisions']
        self.assertEqual(rows[0]['reasonCodes'], [])

    def test_known_reason_is_exact_evidence_not_inferred_cause(self):
        self.write('module064-provider-routing-smoke.json', model())
        obj = self.project()
        self.assertEqual(obj['providerEvidence']['recordedDecisions'][0]['reasonCodes'], ['provider_deadline_exceeded'])
        self.assertNotIn('failureCause', obj)

    def test_missing_evidence_is_not_failure(self):
        self.assertIsNone(self.project())
        PUB.publish(self.source, self.destination)
        obj = json.loads((self.destination / 'security-safe-uat-summary.json').read_text())
        self.assertNotIn('functionalUatDiagnostics', obj)

    def test_invalid_json_only_emits_fixed_state(self):
        p = self.source / 'module064-provider-routing-smoke.json'
        for data in (b'SENTINEL_private_malformed', b'{"x":NaN}', b'{"x":1,"x":2}', b'\xff'):
            p.write_bytes(data)
            obj = self.project()
            self.assertEqual(obj['artifactObservations'][0]['artifactState'], 'invalid_json')
            self.assertEqual(obj['recordedPredicates'], {})
            self.assertNotIn('SENTINEL', json.dumps(obj))

    def test_invalid_schema_does_not_fabricate_predicates(self):
        self.write('module064-provider-routing-smoke.json', ['not', 'an', 'object'])
        obj = self.project()
        self.assertEqual(obj['artifactObservations'][0]['artifactState'], 'invalid_schema')
        self.assertEqual(obj['recordedPredicates'], {})

    def test_malformed_answer_shape_is_unknown(self):
        obj = model(); obj['result']['answer']['detailedAnalysis'] = {'private': 'SENTINEL'}
        self.assertIsNone(DIAG.model_predicates(obj)['refraction_explanation_present'])
        self.assertIsNone(DIAG.model_predicates(obj)['reflection_explanation_present'])

    def test_symlink_files_fail_before_publication(self):
        for target in ('missing', str(ROOT / 'package.json')):
            link = self.source / 'module064-provider-routing-smoke.json'; link.symlink_to(target)
            with self.assertRaises(ValueError): PUB.publish(self.source, self.destination)
            self.assertFalse(self.destination.exists()); link.unlink()

    def test_source_directory_symlink_rejected(self):
        link = Path(self.tmp.name) / 'alias'; link.symlink_to(self.source, target_is_directory=True)
        with self.assertRaises(ValueError): DIAG.project(link)

    def test_fifo_rejected_without_waiting_for_writer(self):
        os.mkfifo(self.source / 'module064-provider-routing-smoke.json')
        with self.assertRaises(ValueError): self.project()

    def test_oversized_file_bounded(self):
        p = self.source / 'module064-provider-routing-smoke.json'
        with p.open('wb') as f: f.truncate(DIAG.MAX_BYTES + 1)
        obj = self.project()
        self.assertEqual(obj['artifactObservations'][0]['artifactState'], 'over_budget')
        self.assertEqual(obj['recordedPredicates'], {})

    def test_depth_limit_cannot_publish_error_text(self):
        (self.source / 'module064-provider-routing-smoke.json').write_text('[' * 1200 + '0' + ']' * 1200)
        self.assertEqual(self.project()['artifactObservations'][0]['artifactState'], 'invalid_json')

    def test_quoted_brackets_do_not_trigger_nesting_limit(self):
        obj = model(); obj['privateIgnoredField'] = '[' * 200 + '\"\\' + ']' * 200
        self.write('module064-provider-routing-smoke.json', obj)
        self.assertEqual(self.project()['artifactObservations'][0]['artifactState'], 'recorded')

    def test_http_projection_discards_paths_headers_and_counts(self):
        secret = 'SENTINEL_private_financial_identity'
        rows = [{'name': 'Project Management summary', 'path': secret, 'httpStatus': '403', 'curlExit': 0,
                 'contentType': secret, 'bodyBytes': 92746123, 'session': secret},
                {'name': secret, 'httpStatus': '200', 'curlExit': 0}]
        (self.source / 'uat-http-diagnostics.ndjson').write_text('\n'.join(json.dumps(x) for x in rows))
        obj = self.project()['httpEvidence']
        self.assertEqual(obj['observations'], [{'stage': 'project_management', 'httpStatus': '403', 'transportSucceeded': True}])
        self.assertNotIn(secret, json.dumps(obj)); self.assertNotIn('92746123', json.dumps(obj))

    def test_unknown_http_status_and_transport_types_are_not_reflected(self):
        row = {'name': 'Project Management summary', 'httpStatus': 'SENTINEL', 'curlExit': False}
        (self.source / 'uat-http-diagnostics.ndjson').write_text(json.dumps(row))
        observed = self.project()['httpEvidence']['observations'][0]
        self.assertEqual(observed['httpStatus'], 'unrecognized'); self.assertFalse(observed['transportSucceeded'])

    def test_http_record_budget_is_enforced(self):
        p = self.source / 'uat-http-diagnostics.ndjson'; p.write_text('{}\n' * 513)
        self.assertEqual(self.project()['httpEvidence'], {'state': 'over_budget', 'observations': []})

    def test_http_records_match_actual_multiline_jq_writer(self):
        workflow = (ROOT / '.github/workflows/projectpulse-deploy-test.yml').read_text()
        self.assertIn("'{name:$name,path:$path,attempt:$attempt,curlExit:$curlExit,httpStatus:$httpStatus,contentType:$contentType,bodyBytes:$bodyBytes}'", workflow)
        chunks = []
        for name, status in [('Project Management summary', '200'), ('Customer directory', '403')]:
            chunks.append(subprocess.check_output(['jq', '-n', '--arg', 'name', name,
                '--arg', 'path', 'SENTINEL_private_path', '--argjson', 'attempt', '1',
                '--argjson', 'curlExit', '0', '--arg', 'httpStatus', status,
                '--arg', 'contentType', 'SENTINEL_private_type', '--argjson', 'bodyBytes', '987654',
                '{name:$name,path:$path,attempt:$attempt,curlExit:$curlExit,httpStatus:$httpStatus,contentType:$contentType,bodyBytes:$bodyBytes}']))
        (self.source / 'uat-http-diagnostics.ndjson').write_bytes(b''.join(chunks))
        result = self.project()['httpEvidence']
        self.assertEqual(result, {'state': 'recorded', 'observations': [
            {'stage': 'project_management', 'httpStatus': '200', 'transportSucceeded': True},
            {'stage': 'customers', 'httpStatus': '403', 'transportSucceeded': True}]})
        self.assertNotIn('SENTINEL', json.dumps(result)); self.assertNotIn('987654', json.dumps(result))

    def test_http_pretty_record_budget_counts_objects_not_lines(self):
        row = {'name': 'Customer directory', 'httpStatus': '200', 'curlExit': 0}
        p = self.source / 'uat-http-diagnostics.ndjson'
        p.write_text(''.join(json.dumps(row, indent=2) + '\n' for _ in range(512)))
        result = self.project()['httpEvidence']
        self.assertEqual(result['state'], 'recorded'); self.assertEqual(len(result['observations']), 512)
        with p.open('a') as stream: stream.write(json.dumps(row, indent=2))
        self.assertEqual(self.project()['httpEvidence'], {'state': 'over_budget', 'observations': []})

    def test_http_invalid_stream_discards_all_partial_observations(self):
        valid = json.dumps({'name': 'Customer directory', 'httpStatus': '200', 'curlExit': 0}).encode()
        p = self.source / 'uat-http-diagnostics.ndjson'
        for suffix in (b'SENTINEL', b'{', b'{"name":"x","name":"y"}', b'{"x":NaN}', b'[]', b'null', b'\xff'):
            with self.subTest(suffix=suffix):
                p.write_bytes(valid + b'\n' + suffix)
                self.assertEqual(self.project()['httpEvidence'], {'state': 'invalid_json', 'observations': []})

    def test_http_record_separator_is_required(self):
        row = b'{"name":"Customer directory","httpStatus":"200","curlExit":0}'
        (self.source / 'uat-http-diagnostics.ndjson').write_bytes(row + row)
        self.assertEqual(self.project()['httpEvidence'], {'state': 'invalid_json', 'observations': []})

    def test_http_depth_is_bounded_for_each_pretty_record(self):
        (self.source / 'uat-http-diagnostics.ndjson').write_text('{"private":' + '['*65 + '0' + ']'*65 + '}')
        self.assertEqual(self.project()['httpEvidence'], {'state': 'invalid_json', 'observations': []})

    def test_http_escaped_brackets_do_not_change_record_boundaries(self):
        row = {'name': 'Customer directory', 'httpStatus': '200', 'curlExit': 0,
               'private': '[{}]\n' * 100 + '\\"SENTINEL'}
        (self.source / 'uat-http-diagnostics.ndjson').write_text(json.dumps(row, indent=2) + '\n')
        result = self.project()['httpEvidence']
        self.assertEqual(len(result['observations']), 1); self.assertNotIn('SENTINEL', json.dumps(result))

    def test_publisher_modes_and_original_summary_preserved(self):
        self.write('uat-summary.json', {'status': 'passed', 'productionMutation': False, 'private': 'SENTINEL'})
        self.write('module064-provider-routing-smoke.json', model('local'))
        PUB.publish(self.source, self.destination)
        obj = json.loads((self.destination / 'security-safe-uat-summary.json').read_text())
        self.assertEqual(obj['reports']['uat-summary.json']['status'], 'passed')
        self.assertFalse(obj['rawResponsesPublished']); self.assertFalse(obj['screenshotsPublished'])
        self.assertEqual(self.destination.stat().st_mode & 0o777, 0o700)
        self.assertEqual((self.destination / 'security-safe-uat-summary.json').stat().st_mode & 0o777, 0o600)
        self.assertNotIn('SENTINEL', json.dumps(obj))

    def test_all_paths_are_fixed_leaf_json_files(self):
        names = [name for _, name in DIAG.ARTIFACTS]
        self.assertEqual(len(names), len(set(names)))
        for name in names:
            self.assertEqual(Path(name).name, name); self.assertTrue(name.endswith('.json'))
        node = ast.parse((ROOT / 'scripts/security/safe_uat_diagnostics.py').read_text())
        imports = {alias.name for n in ast.walk(node) if isinstance(n, ast.Import) for alias in n.names}
        self.assertFalse(imports & {'subprocess', 'socket', 'requests', 'http', 'urllib'})


class SourceParityTests(unittest.TestCase):
    def test_acceptance_and_runtime_sources_remain_byte_identical(self):
        paths = ['.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/module025-protected-uat-control.yml',
                 'scripts/validate-deployment-concurrency-governance.mjs', 'tests/security-deployment-boundaries.test.py']
        for path in paths:
            old = subprocess.check_output(['git', 'show', BASE + ':' + path], cwd=ROOT)
            self.assertEqual((ROOT / path).read_bytes(), old)
        changed = subprocess.check_output(['git', 'diff', '--name-only', BASE], cwd=ROOT, text=True).splitlines()
        self.assertFalse(any(p.startswith(('src/', 'database/', 'deployment/')) for p in changed))

    def test_model_projection_matches_actual_jq_gate(self):
        source = (ROOT / '.github/workflows/projectpulse-deploy-test.yml').read_text()
        expression = re.search(r"jq -e '([^']*)'\s+\"\$EVIDENCE_DIR/module064-provider-routing-smoke.json\"", source).group(1)
        cases = [model(p) for p in DIAG.PROVIDERS]
        for key in ('content', 'used', 'order', 'target'):
            obj = model()
            if key == 'content': obj['result']['answer'] = {'directConclusion': 'Fallback', 'detailedAnalysis': []}
            if key == 'used': obj['result']['targetDecisions'] = []
            if key == 'order': obj['result']['targetDecisions'].reverse()
            if key == 'target': obj['result']['targetDecisions'][0]['target'] = 'outside'
            cases.append(obj)
        for obj in cases:
            actual = subprocess.run(['jq', '-e', expression], input=json.dumps(obj), text=True, capture_output=True)
            self.assertEqual(all(DIAG.model_predicates(obj).values()), actual.returncode == 0)

    def test_public_fact_projection_matches_actual_jq_gate(self):
        source = (ROOT / '.github/workflows/projectpulse-deploy-test.yml').read_text()
        expression = re.search(r"jq -e '([^']*)'\s+\"\$EVIDENCE_DIR/module064-chat-routing-smoke.json\"", source).group(1)
        for text in ('50', 'There are fifty states.', '500', 'Unknown', 'FIFTY'):
            for verified in (True, False):
                obj = public_fact(); obj['result']['answer']['directConclusion'] = text
                obj['reliability']['assessment']['passed'] = verified
                actual = subprocess.run(['jq', '-e', expression], input=json.dumps(obj), text=True, capture_output=True)
                self.assertEqual(all(DIAG.public_fact_predicates(obj).values()), actual.returncode == 0)


if __name__ == '__main__':
    unittest.main(verbosity=2)
