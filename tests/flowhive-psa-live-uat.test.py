"""Unit tests of acceptance decisions only. These fixtures are not live AI evidence."""
import copy
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch
spec = importlib.util.spec_from_file_location('live', Path(__file__).parents[1]/'scripts/release-test/run-flowhive-psa-live-uat.py')
live = importlib.util.module_from_spec(spec)
spec.loader.exec_module(live)


def fixture():
    tasks=[]
    for i,phase in enumerate(live.PHASES,1):
        tasks.append({'name': phase,'phase':phase,'isSummary':True,'wbsNumber':str(i)})
        tasks.append({'name':'Specific synthetic work for '+phase,'phase':phase,'isSummary':False,'wbsNumber':str(i)+'.1',
          'description':'Synthetic test-only activity for checking acceptance decisions.', 'isMilestone':False,'canonicalTaskId':None,
          'detailedSteps':['Identify the specific synthetic input','Validate the synthetic output artifact'],
          'outputs':['Synthetic output'],'acceptanceCriteria':['Synthetic approval'],'remainingEffortHours':2,'citationIds':[1]})
    plan={'projectId':live.PROJECT,'sourceKind':'celar_ai','sowVersion':'TEST-ONLY','milestones':[],
          'tasks':tasks,'celarAiCitationIds':[1]}
    schedule={'valid':True,'plannedHours':10,'tasks':[{'wbsNumber':r['wbsNumber'],'startDate':'2026-09-08','endDate':'2026-09-09'} for r in tasks]}
    return plan,schedule

class AcceptanceDecisions(unittest.TestCase):
    def ready_workspace(self):
        return {
            'sowEvidenceSummary': {'approvedSowScopeReady': True},
            'sowEvidence': [{
            'documentId': '11111111-1111-4111-8111-111111111111',
            'workRegisterDocumentId': '33333333-3333-4333-8333-333333333333',
            'activeVersionId': '22222222-2222-4222-8222-222222222222',
                'documentVersion': 'v3',
                'documentCategory': 'sow',
                'citationCount': 4,
                'readyForAiPlanner': True,
            }],
        }

    def test_sow_readiness_requires_server_summary_and_document_gate(self):
        item = live.ready_sow(self.ready_workspace())
        self.assertEqual(item['documentVersion'], 'v3')
        self.assertEqual(item['workRegisterDocumentId'], '33333333-3333-4333-8333-333333333333')
        for mutation in (
            lambda value: value.update(sowEvidenceSummary={'approvedSowScopeReady': False}),
            lambda value: value['sowEvidence'][0].update(readyForAiPlanner=False),
            lambda value: value['sowEvidence'][0].update(activeVersionId=None),
            lambda value: value['sowEvidence'][0].update(workRegisterDocumentId=None),
            lambda value: value['sowEvidence'][0].update(citationCount=0),
            lambda value: value['sowEvidence'][0].update(documentCategory='gsd'),
        ):
            value = copy.deepcopy(self.ready_workspace())
            mutation(value)
            with self.assertRaises(live.GateError):
                live.ready_sow(value)

    def test_sow_receipt_binds_identity_and_source_bytes(self):
        item = live.ready_sow(self.ready_workspace())
        receipt = live.sow_receipt(item, 'a' * 64)
        self.assertEqual(receipt['documentId'], item['documentId'])
        self.assertEqual(receipt['workRegisterDocumentId'], item['workRegisterDocumentId'])
        self.assertEqual(receipt['sourceFingerprint'], 'a' * 64)

    def test_sow_receipt_keeps_intake_and_work_register_identities_distinct(self):
        item = live.ready_sow(self.ready_workspace())
        self.assertNotEqual(item['documentId'], item['workRegisterDocumentId'])
        self.assertEqual(live.sow_receipt(item, 'b' * 64)['workRegisterDocumentId'],
                         '33333333-3333-4333-8333-333333333333')

    def test_planner_status_accepts_documented_nonterminal_202(self):
        self.assertTrue(live.planner_status_response_valid(200, {'terminal': True}))
        self.assertTrue(live.planner_status_response_valid(202, {'terminal': False, 'phase': 'inference'}))
        for code, value in [(200, {'terminal': False}), (201, {}), (204, {}),
                            (202, {'terminal': True}), (202, None), (202, [])]:
            self.assertFalse(live.planner_status_response_valid(code, value))

    def test_prior_planner_reconciliation_requires_matching_terminal_run(self):
        run_id = '11111111-1111-4111-8111-111111111111'
        report = {}
        with self.assertRaises(live.GateError) as nonterminal:
            live.planner_run_snapshot(202, {'runId': run_id, 'terminal': False, 'phase': 'inference'}, run_id, report)
        self.assertEqual(str(nonterminal.exception), 'prior_planner_run_nonterminal')
        self.assertEqual(report['priorPlannerRunReconciliation']['runId'], run_id)
        self.assertFalse(report['priorPlannerRunReconciliation']['terminal'])

        report = {}
        with self.assertRaises(live.GateError) as identity:
            live.planner_run_snapshot(200, {'runId': '22222222-2222-4222-8222-222222222222', 'terminal': True}, run_id, report)
        self.assertEqual(str(identity.exception), 'prior_planner_identity_mismatch')

        report = {}
        terminal = live.planner_run_snapshot(200, {'runId': run_id, 'terminal': True, 'status': 'completed'}, run_id, report)
        self.assertTrue(terminal['terminal'])
        self.assertTrue(report['priorPlannerRunReconciliation']['terminal'])

    def test_substantive_fixture(self):
        p,s=fixture(); self.assertEqual(live.plan_checks(p,s)['leafTasks'],5)
    def test_reject_incomplete_or_fabricated_success(self):
        for mutation in [lambda p:p.update(projectId='other'),lambda p:p.update(sourceKind='template'),
                         lambda p:p.update(milestones=[{}]),lambda p:p.update(celarAiCitationIds=[]),
                         lambda p:p['tasks'][1].update(detailedSteps=['one']),
                         lambda p:p['tasks'][1].update(citationIds=[9]),
                         lambda p:p['tasks'][1].update(remainingEffortHours=float('nan')),
                         lambda p:p['tasks'][1].update(remainingEffortHours=-1),
                         lambda p:p['tasks'][1].update(name='Plan phase'),
                         lambda p:p['tasks'][1].update(canonicalTaskId='adopted'),
                         lambda p:p['tasks'][1].update(acceptanceCriteria=[]),
                         lambda p:p['tasks'][1].update(phase='Implement')]:
            p,s=fixture();mutation(p)
            with self.assertRaises(live.GateError):live.plan_checks(p,s)
    def test_schedule_reconciliation(self):
        for mutation in [lambda s:s.update(valid=False),lambda s:s.update(plannedHours=11),
                         lambda s:s['tasks'][1].update(startDate='2026-10-01'),
                         lambda s:s['tasks'].pop(1)]:
            p,s=fixture();mutation(s)
            with self.assertRaises(live.GateError):live.plan_checks(p,s)
    def test_exact_atomic_saved_receipt(self):
        p,s=fixture();version='11111111-1111-4111-8111-111111111111'
        result={'plan':p,'workingDraft':{'persisted':True,'rowVersion':version,'workingRevision':3,
                                      'immutableVersionCreated':False,'baselineCreated':False}}
        workspace={'project':{'projectId':live.PROJECT},'workingCopy':{'plan':p,'schedule':s,
                    'rowVersion':version,'workingRevision':3,'validation':{'valid':True}}}
        live.receipt_checks(result,workspace)
        for mutation in [lambda w:w['project'].update(projectId='other'),
                         lambda w:w['workingCopy'].update(rowVersion='newer'),
                         lambda w:w['workingCopy'].update(workingRevision=4),
                         lambda w:w['workingCopy']['plan'].update(sourceKind='other')]:
            w=copy.deepcopy(workspace);mutation(w)
            with self.assertRaises(live.GateError):live.receipt_checks(result,w)
    def test_review_requires_persisted_project_matched_proposal(self):
        review = {'projectId': live.PROJECT, 'runId': '11111111-1111-4111-8111-111111111111',
                  'contract': 'flowhive-reviewed-regeneration-v1',
                  'currentPlan': {'projectId': live.PROJECT}, 'candidatePlan': {'projectId': live.PROJECT},
                  'candidateSchedule': {'valid': True}, 'candidateValidation': {'valid': True},
                  'expectedWorkingRowVersion': None}
        self.assertEqual(live.review_checks(review)[0]['projectId'], live.PROJECT)
        for mutation in [lambda r:r.update(contract='wrong'),
                         lambda r:r.update(candidateSchedule={'valid': False}),
                         lambda r:r.update(expectedWorkingRowVersion='not-a-uuid'),
                         lambda r:r.update(candidatePlan={'projectId': 'other'})]:
            candidate = copy.deepcopy(review); mutation(candidate)
            with self.assertRaises(live.GateError): live.review_checks(candidate)

    def test_existing_work_is_preserved_by_stable_identity(self):
        before, _ = fixture()
        leaves = [task for task in before['tasks'] if not task.get('isSummary')]
        for index, task in enumerate(leaves):
            task['clientTaskId'] = f'11111111-1111-4111-8111-{index + 1:012d}'
            task.update(status='in_progress', percentComplete=20, durationWorkingDays=2,
                        constraintType='start_no_earlier_than', constraintDate='2026-09-08',
                        comments='Existing comment', notes='Existing note')
        before['milestones'] = [{'clientMilestoneId': '22222222-2222-4222-8222-222222222222',
                                 'name': 'Existing gate', 'description': 'Existing milestone',
                                 'targetDate': '2026-09-30', 'acceptanceEvidence': 'Existing evidence',
                                 'citationIds': [1], 'isAssumption': False}]
        before['assignments'] = [{'taskWbs': leaves[0]['wbsNumber'], 'resourceUserId': '33333333-3333-4333-8333-333333333333',
                                  'resourceDisplayName': 'Existing owner', 'allocationPercent': 50, 'plannedHours': 4}]
        before['dependencies'] = [{'predecessorWbs': leaves[0]['wbsNumber'], 'successorWbs': leaves[1]['wbsNumber'],
                                   'type': 'FS', 'lagWorkingDays': 1}]
        after = copy.deepcopy(before)
        after['tasks'].append({'name': 'New reviewed work', 'phase': 'Release', 'isSummary': False,
                               'wbsNumber': '5.2', 'clientTaskId': '44444444-4444-4444-8444-444444444444'})
        result = live.preserved_work_checks(before, after)
        self.assertEqual(result['existingTaskCount'], 5)
        self.assertEqual(result['preservedMilestoneCount'], 1)
        self.assertEqual(result['preservedAssignmentCount'], 1)
        self.assertEqual(result['preservedDependencyCount'], 1)
    def test_network_rejects_unapproved_paths(self):
        for path in ['https://unapproved.invalid/', '//unapproved.invalid/api']:
            with self.assertRaises(live.GateError):live.Client().request(path)
    def test_timezones_and_deadlines(self):
        self.assertIsNotNone(live.iso('2026-09-08T00:00:00Z').tzinfo)
        for stamp in [None,'nonsense','2026-09-08T00:00:00']:
            with self.assertRaises(live.GateError):live.iso(stamp)
    def test_no_generation_retry_no_mocked_live_data(self):
        source=Path(spec.origin).read_text()
        self.assertEqual(source.count("client.start_posts += 1"),1)
        self.assertNotIn('route.fulfill(',source)
        self.assertNotIn('window.fetch =',source)
        self.assertIn("report['fullLiveAiAcceptance'] = False",source)
        self.assertIn("'modelName': None, 'actualInferenceRequests': 1",source)
        self.assertIn("/review-preview",source)
        self.assertIn("/apply-reviewed",source)
        self.assertIn("workingCopyUnchangedBeforeApply",source)
        self.assertIn("browserProposal",source)
        self.assertIn("browserApplied",source)
        self.assertNotIn('existing_milestones_require_pm_review_before_generation',source)
        self.assertNotIn('storage_state(',source)
    def test_error_evidence_never_contains_upstream_body(self):
        source=Path(spec.origin).read_text()
        self.assertNotIn('error.read(',source)
        self.assertIn("'unexpected_' + type(error).__name__",source)
        self.assertNotIn("'error': str(error)",source)

if __name__=='__main__':unittest.main()
