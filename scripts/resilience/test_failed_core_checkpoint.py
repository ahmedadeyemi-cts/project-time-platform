import copy,unittest
import failed_core_checkpoint as policy

class Tests(unittest.TestCase):
    def fixtures(self):
        run={'id':policy.RUN,'workflow_id':315562561,'head_sha':policy.SHA,'head_branch':'main','event':'workflow_dispatch','status':'completed','conclusion':'failure'}
        record={'result':'FAILED','releaseCommit':policy.SHA,'baselineRevision':policy.BASELINE,'baselineImage':policy.OLD_IMAGE,'baselineMode':'Single','failurePhase':'candidate_staging','failureReason':'zero_traffic_policy:missing_zero_traffic_candidate','rollback':'FAILED','productionMutation':False,'oracleMutation':False,'celarSowAcceptance':'PENDING_NOT_EXECUTED','baselineCanary':{'result':'PASS'}}
        app={'name':policy.APP,'properties':{'latestRevisionName':policy.CANDIDATE,'latestReadyRevisionName':policy.CANDIDATE,'template':{'containers':[{'image':policy.NEW_IMAGE}]},'configuration':{'activeRevisionsMode':'Multiple','ingress':{'traffic':[{'revisionName':policy.BASELINE,'weight':100}]}}}}
        baseline={'name':policy.BASELINE,'properties':{'active':True,'healthState':'Healthy','provisioningState':'Provisioned','template':{'containers':[{'image':policy.OLD_IMAGE}]}}}
        return run,record,app,baseline
    def test_exact_failed_run_and_live_pin_are_qualified(self):
        run,record,app,baseline=self.fixtures();policy.verify_run(run);policy.verify_record(record);policy.verify_live(app,baseline)
    def test_unknown_run_source_or_trigger_denied(self):
        run,_,_,_=self.fixtures()
        for key,value in [('id',1),('head_sha','a'*40),('event','push'),('status','in_progress'),('conclusion','success')]:
            wrong={**run,key:value}
            with self.assertRaises(RuntimeError):policy.verify_run(wrong)
    def test_wrong_baseline_or_acceptance_record_denied(self):
        _,record,_,_=self.fixtures()
        for key,value in [('baselineMode','Multiple'),('baselineImage',policy.NEW_IMAGE),('oracleMutation',True),('celarSowAcceptance','PASS')]:
            with self.assertRaises(RuntimeError):policy.verify_record({**record,key:value})
    def test_any_candidate_traffic_or_latest_pointer_denied(self):
        _,_,app,baseline=self.fixtures()
        for weights in [[{'revisionName':policy.BASELINE,'weight':99},{'revisionName':policy.CANDIDATE,'weight':1}],[{'latestRevision':True,'weight':100}]]:
            wrong=copy.deepcopy(app);wrong['properties']['configuration']['ingress']['traffic']=weights
            with self.assertRaises(RuntimeError):policy.verify_live(wrong,baseline)
    def test_unhealthy_or_changed_baseline_denied(self):
        _,_,app,baseline=self.fixtures();baseline['properties']['healthState']='Unhealthy'
        with self.assertRaises(RuntimeError):policy.verify_live(app,baseline)
if __name__=='__main__':unittest.main()
