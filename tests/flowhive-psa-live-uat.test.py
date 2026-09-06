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
        self.assertIn("'modelName': None, 'actualInferenceRequests': None",source)
        self.assertNotIn('storage_state(',source)
    def test_error_evidence_never_contains_upstream_body(self):
        source=Path(spec.origin).read_text()
        self.assertNotIn('error.read(',source)
        self.assertIn("'unexpected_' + type(error).__name__",source)
        self.assertNotIn("'error': str(error)",source)

if __name__=='__main__':unittest.main()
