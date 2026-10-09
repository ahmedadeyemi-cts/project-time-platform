import base64,json,os,unittest
from unittest.mock import patch
import private_core_canary as canary

SHA='a'*40
PASSWORD='not-a-real-test-password'
CHECKS=['/health','/health/live','/health/ready','/health/source','anonymous_session_denied','/api/security/context','/api/assignments/available-tasks?weekStart=2026-08-16','/api/timesheet/work-queue?weekStart=2026-08-16','/api/engineer-task-closeout/overview','/api/project-workspace/overview']
class Process:
    def __init__(self):self.returncode=None
    def poll(self):return self.returncode
    def terminate(self):self.returncode=0
    def wait(self,timeout=None):return 0
class Tests(unittest.TestCase):
    def transport(self,result):
        wire=b'PULSE_CANARY_READY\r\r\n'+b'PULSE_CANARY_RESULT='+base64.b64encode(json.dumps(result).encode())+b'\r\r\n'
        with patch.dict(os.environ,{'TEST_LOGIN_PASSWORD':PASSWORD}),patch.object(canary,'ready_replica',return_value='ca-phd-test-api-westus3--cc-123-1-abc-xyz'),patch.object(canary.pty,'openpty',return_value=(100,101)),patch.object(canary.subprocess,'Popen',return_value=Process()) as process,patch.object(canary.os,'close'),patch.object(canary.os,'read',return_value=wire),patch.object(canary.os,'write') as write,patch.object(canary.select,'select',return_value=([100],[],[])):
            value=canary.check_revision('ca-phd-test-api-westus3--cc-123-1','api',SHA)
            self.assertNotIn(PASSWORD,str(process.call_args))
            self.assertIn(PASSWORD,write.call_args.args[1].decode())
            self.assertNotIn(PASSWORD,json.dumps(value))
            return value
    def test_real_terminal_crlf_and_stdin_only_secret(self):
        value=self.transport({'result':'PASS','sourceCommit':SHA,'checks':CHECKS,'unexpectedSecret':PASSWORD})
        self.assertEqual(set(value),{'result','sourceCommit','checks','celarSowAcceptance'})
    def test_incomplete_application_checks_are_denied(self):
        with self.assertRaisesRegex(RuntimeError,'checks_incomplete'):self.transport({'result':'PASS','sourceCommit':SHA,'checks':[]})
    def test_failed_authentication_is_denied(self):
        with self.assertRaisesRegex(RuntimeError,'private_canary_local_login'):self.transport({'result':'FAILED','failureReason':'local_login'})
    def test_only_running_ready_replica_is_selected(self):
        rev='ca-phd-test-api-westus3--cc-123-1'
        running={'name':rev+'-abc-xyz','properties':{'runningState':'Running','containers':[{'name':'api','runningState':'Running','ready':True,'started':True}]}}
        pending={'name':rev+'-abc-old','properties':{'runningState':'Running','containers':[{'name':'api','runningState':'Running','ready':False,'started':True}]}}
        with patch.object(canary.subprocess,'check_output',return_value=json.dumps([pending,running]).encode()):
            self.assertEqual(canary.ready_replica(rev,'api'),running['name'])
        running['name']='production--abc'
        with patch.object(canary.subprocess,'check_output',return_value=json.dumps([running]).encode()):
            with self.assertRaisesRegex(RuntimeError,'replica_identity'):canary.ready_replica(rev,'api')
    def test_attach_retry_is_bounded_and_application_failure_is_not_retried(self):
        args=('ca-phd-test-api-westus3--cc-123-1','api',SHA)
        with patch.object(canary,'ready_replica',return_value=args[0]+'-abc-xyz'),patch.object(canary.time,'sleep'),patch.object(canary,'terminal_check',side_effect=[RuntimeError('private_canary_attach_not_ready'),{'result':'PASS'}]) as call:
            self.assertEqual(canary.check_revision(*args),{'result':'PASS'})
            self.assertEqual(call.call_count,2)
        for reason,attempts in [('private_canary_attach_not_ready',3),('private_canary_terminal_failed',1),('private_canary_local_login',1)]:
            with patch.object(canary,'ready_replica',return_value=args[0]+'-abc-xyz'),patch.object(canary.time,'sleep'),patch.object(canary,'terminal_check',side_effect=RuntimeError(reason)) as call:
                with self.assertRaisesRegex(RuntimeError,reason):canary.check_revision(*args)
                self.assertEqual(call.call_count,attempts)
    def test_non_candidate_revision_never_executes(self):
        with patch.object(canary.subprocess,'Popen') as process:
            with self.assertRaises(AssertionError):canary.check_revision('production--cp-1-1','api',SHA)
            process.assert_not_called()
if __name__=='__main__':unittest.main()
