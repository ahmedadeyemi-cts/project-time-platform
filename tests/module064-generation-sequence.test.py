"""Regressions for the current observer and shared sequence; not live AI evidence."""
import copy
import importlib.util
import re
import unittest
from pathlib import Path
import yaml

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('sequence_live', ROOT / 'scripts/release-test/run-flowhive-psa-live-uat.py')
live = importlib.util.module_from_spec(spec)
spec.loader.exec_module(live)

def status():
    return {'executionContract': live.CONTRACT, 'attemptCount': 1, 'maximumAttempts': 2,
            'createdAt': '2026-09-21T00:00:00Z', 'deadlineAt': '2026-09-21T00:40:00Z',
            'phases': [{'number': i, 'total': 5, 'name': phase, 'status': 'completed',
                        'attemptCount': 1, 'taskCount': 2, 'startedAt': f'2026-09-21T00:{i:02d}:00Z',
                        'completedAt': f'2026-09-21T00:{i:02d}:20Z'} for i, phase in enumerate(live.PHASES, 1)]}

class SequenceRepairTests(unittest.TestCase):
    def test_contract_and_bounds_match_the_real_backend(self):
        policy = (ROOT / 'src/backend/ProjectTime.Api/Modules/ProjectFlowHiveExecutionPolicy.cs').read_text()
        self.assertEqual(live.CONTRACT, re.search(r'const string Contract = "([^"]+)"', policy)[1])
        self.assertEqual(live.BACKEND_BUDGET_SECONDS, int(re.search(r'OverallBudget = TimeSpan.FromMinutes\((\d+)\)', policy)[1]) * 60)
        self.assertEqual(live.MAXIMUM_ATTEMPTS, int(re.search(r'const int MaximumAttempts = (\d+)', policy)[1]))
        self.assertEqual(live.execution_checks(status()), 2400)

    def test_old_unknown_and_missing_contracts_are_rejected(self):
        for contract in ('flowhive-bounded-execution-v1-20260906', 'unknown', None):
            value = status(); value['executionContract'] = contract
            with self.assertRaisesRegex(live.GateError, 'bounded_execution_not_deployed'):
                live.execution_checks(value)

    def test_invalid_attempts_and_deadlines_are_rejected(self):
        for change in ({'attemptCount': True}, {'attemptCount': -1}, {'attemptCount': 3},
                       {'maximumAttempts': 3}, {'maximumAttempts': True},
                       {'deadlineAt': '2026-09-21T00:00:00Z'}, {'deadlineAt': '2026-09-21T00:41:00Z'},
                       {'deadlineAt': None}, {'createdAt': 'no timestamp'}):
            value = status(); value.update(change)
            with self.assertRaises(live.GateError): live.execution_checks(value)

    def test_real_phase_shape_retains_only_sanitized_timing_and_counts(self):
        value = status(); value['phases'][0]['untrusted_text'] = 'must not be retained'
        progress = live.sequential_phase_checks(value)
        self.assertEqual([p['name'] for p in progress], live.PHASES)
        self.assertTrue(all(p['elapsedSeconds'] == 20 for p in progress))
        self.assertNotIn('untrusted_text', progress[0])
        value['phases'][0].update(status='pending', startedAt=None, completedAt=None, taskCount=0, attemptCount=0)
        self.assertIsNone(live.sequential_phase_checks(value)[0]['elapsedSeconds'])

    def test_missing_reordered_or_invalid_phase_evidence_is_rejected(self):
        mutations = [lambda v: v.pop('phases'), lambda v: v['phases'].pop(),
                     lambda v: v['phases'].reverse(), lambda v: v['phases'][0].update(number=2),
                     lambda v: v['phases'][0].update(attemptCount=True),
                     lambda v: v['phases'][0].update(status='made_up'),
                     lambda v: v['phases'][0].update(attemptCount=5),
                     lambda v: v['phases'][0].update(taskCount=0),
                     lambda v: v['phases'][0].update(startedAt=None),
                     lambda v: v['phases'][0].update(completedAt='2026-09-21T00:00:00Z')]
        for mutation in mutations:
            value = status(); mutation(value)
            with self.assertRaises(live.GateError): live.sequential_phase_checks(value)

    def test_outer_workflow_can_observe_both_existing_application_budgets(self):
        workflow = yaml.safe_load((ROOT / '.github/workflows/flowhive-psa-installed-acceptance.yml').read_text())
        job = workflow['jobs']['verify-installed-release']
        sow = next(step for step in job['steps'] if step.get('id') == 'sow')
        sow_seconds = int(sow['env']['MODULE025_GENERATION_TIMEOUT_SECONDS'])
        engine = (ROOT / 'src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs').read_text()
        self.assertGreaterEqual(sow_seconds, int(re.search(r'const int DeadlineSeconds = (\d+)', engine)[1]))
        self.assertGreater(job['timeout-minutes'] * 60, live.BACKEND_BUDGET_SECONDS + live.OBSERVATION_GRACE_SECONDS + sow_seconds + 300)
        final = next(step for step in job['steps'] if step.get('name') == 'Require complete installed acceptance')['run']
        self.assertIn('steps.flowhive.outcome', final)
        self.assertIn('.status == "passed"', final)
        self.assertIn('.reviewedSaveReadback == true', final)

    def test_shared_router_does_not_reorder_or_replay_saved_targets(self):
        source = (ROOT / 'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs').read_text()
        policy = (ROOT / 'src/backend/ProjectTime.Api/Ai/CelarAiRouteExecutionPolicy.cs').read_text()
        self.assertNotIn('"deferred"', source)
        self.assertNotIn('.Concat(route.Targets.Where', policy)
        self.assertIn('requireSuccessfulRead: true', source)
        self.assertIn('module064_route_store_unavailable', source)
        self.assertNotIn('new[] { CelarAiCapabilityTargets.DeepSeek }.Concat(values)', source)
        self.assertIn('privatePositionVisited', source)
        self.assertIn('external_generation_approval_required', source)
        self.assertIn('if (privateResult.IsRefusal)', source)
        self.assertIn('if (result.IsRefusal)', source)

if __name__ == '__main__': unittest.main()
