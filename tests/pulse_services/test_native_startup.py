"""Startup and readiness regressions with synthetic metadata and mocked Azure."""
import ast, copy, errno, json, os, socket, subprocess, sys, types, unittest
from pathlib import Path
from unittest.mock import MagicMock, patch
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'deployment/pulse-services'))
import runtime
import cutover
from replica_readiness import service_replicas_ready
BASE='4a5a1e4e46c51fca2368f71949b69746e2982905'

def metadata():return {key:'SYNTHETIC-NOT-A-CREDENTIAL' for key in runtime.IDENTITY_VARIABLES}

class IdentityStartup(unittest.TestCase):
    def test_baseline_reproduces_false_failure_before_isolation(self):
        source=subprocess.check_output(['git','show',BASE+':deployment/pulse-services/runtime.py'],cwd=ROOT,text=True)
        original=types.ModuleType('old_runtime');exec(compile(source,'old_runtime','exec'),original.__dict__)
        with patch.dict(os.environ,metadata(),clear=True),patch.object(original.ctypes,'CDLL') as loader:
            with self.assertRaisesRegex(RuntimeError,'runtime_managed_identity_must_be_disabled'):original.harden()
            loader.assert_not_called()
    def test_metadata_can_exist_but_real_isolation_is_required(self):
        library=MagicMock();library.pulse_sandbox.return_value=0
        with patch.dict(os.environ,{**metadata(),'PULSE_SERVICE_TOKEN':'preserve-service-token'},clear=True),patch.object(runtime.ctypes,'CDLL',return_value=library),patch.object(runtime.socket,'socket',side_effect=PermissionError(errno.EPERM,'synthetic denial')) as create:
            runtime.harden()
            self.assertFalse(any(k in os.environ for k in runtime.IDENTITY_VARIABLES))
            self.assertEqual(os.environ['PULSE_SERVICE_TOKEN'],'preserve-service-token')
            self.assertEqual([call.args[0] for call in create.call_args_list],[socket.AF_INET,socket.AF_INET6])
            library.pulse_sandbox.assert_called_once_with(b'gateway')
    def test_failed_kernel_isolation_cannot_be_waived_by_metadata(self):
        library=MagicMock();library.pulse_sandbox.return_value=-1
        with patch.dict(os.environ,metadata(),clear=True),patch.object(runtime.ctypes,'CDLL',return_value=library),patch.object(runtime.socket,'socket') as create:
            with self.assertRaisesRegex(RuntimeError,'PULSE_ISOLATION_UNAVAILABLE'):runtime.harden()
            create.assert_not_called()
    def test_permitted_network_socket_is_a_terminal_failure(self):
        library=MagicMock();library.pulse_sandbox.return_value=0
        probe=MagicMock()
        with patch.object(runtime.ctypes,'CDLL',return_value=library),patch.object(runtime.socket,'socket',return_value=probe):
            with self.assertRaisesRegex(RuntimeError,'PULSE_NETWORK_ISOLATION_UNAVAILABLE'):runtime.harden()
            probe.close.assert_called_once()
    def test_different_socket_error_is_not_isolation_proof(self):
        library=MagicMock();library.pulse_sandbox.return_value=0
        with patch.object(runtime.ctypes,'CDLL',return_value=library),patch.object(runtime.socket,'socket',side_effect=OSError(errno.EMFILE,'synthetic')):
            with self.assertRaisesRegex(RuntimeError,'PULSE_NETWORK_ISOLATION_UNVERIFIED'):runtime.harden()
    def test_updater_exec_has_no_identity_metadata(self):
        captured=[]
        def execute(path,args):
            captured.append((path,args,dict(os.environ)))
            raise RuntimeError('exec_stop')
        with patch.dict(os.environ,metadata(),clear=True),patch.object(runtime.sys,'argv',['runtime.py','updater']),patch.object(runtime.os,'execv',side_effect=execute):
            with self.assertRaisesRegex(RuntimeError,'exec_stop'):runtime.start()
        self.assertEqual(captured[0][0],'/usr/local/bin/pulse-sandbox')
        self.assertEqual(captured[0][1][1],'updater')
        self.assertFalse(any(k in captured[0][2] for k in runtime.IDENTITY_VARIABLES))
    def test_invalid_mode_cannot_launch_a_process(self):
        with patch.object(runtime.sys,'argv',['runtime.py','other']),patch.object(runtime.os,'execv') as execute:
            with self.assertRaisesRegex(RuntimeError,'startup_mode_invalid'):runtime.start()
            execute.assert_not_called()

class ReplicaReadiness(unittest.TestCase):
    names=['documents','scanner','signature-updater']
    def replicas(self):return [{'properties':{'containers':[{'name':n,'ready':True,'runningState':'Running'} for n in self.names]}}]
    def test_all_expected_containers_must_be_ready(self):
        self.assertTrue(service_replicas_ready(self.replicas(),self.names))
        for name in self.names:
            for state in ('Waiting','Terminated'):
                rows=self.replicas();entry=next(c for c in rows[0]['properties']['containers'] if c['name']==name)
                entry['runningState']=state
                with self.subTest(name=name,state=state):self.assertFalse(service_replicas_ready(rows,self.names))
            rows=self.replicas();next(c for c in rows[0]['properties']['containers'] if c['name']==name)['ready']=False
            self.assertFalse(service_replicas_ready(rows,self.names))
    def test_empty_duplicate_unknown_and_nonboolean_ready_rejected(self):
        for rows in ([],{},None,[{}],[{'properties':{'containers':[]}}]):
            self.assertFalse(service_replicas_ready(rows,self.names))
        for field,value in (('name','other'),('name','scanner'),('ready',1),('ready','true'),('runningState',None)):
            rows=self.replicas();rows[0]['properties']['containers'][0][field]=value
            self.assertFalse(service_replicas_ready(rows,self.names))
        self.assertFalse(service_replicas_ready(self.replicas(),self.names+['scanner']))
    def test_revision_metadata_alone_cannot_start_acceptance(self):
        name=cutover.APPS['documents'];revision=name+'--svc-123'
        app={'properties':{'provisioningState':'Succeeded','template':{'containers':[{'name':n} for n in self.names]}}}
        not_ready=self.replicas();not_ready[0]['properties']['containers'][0]['ready']=False
        with patch.object(cutover,'get_app',return_value=app),patch.object(cutover,'revision_is_ready',return_value=True),patch.object(cutover,'read_service_replicas',side_effect=[not_ready,self.replicas()]) as azure,patch.object(cutover.time,'sleep') as sleep:
            self.assertIs(cutover.wait_app(name,expected_revision=revision),app)
            self.assertEqual(azure.call_count,2)
            self.assertEqual(azure.call_args.args,(name,revision))
            sleep.assert_called_once_with(10)
    def test_source_revision_guard_precedes_replica_read(self):
        app={'properties':{'provisioningState':'Succeeded'}}
        with patch.object(cutover,'get_app',return_value=app),patch.object(cutover,'revision_is_ready',return_value=False),patch.object(cutover,'read_service_replicas') as azure,patch.object(cutover.time,'monotonic',side_effect=[0,0,901]),patch.object(cutover.time,'sleep'):
            with self.assertRaisesRegex(cutover.CutoverError,'service_readiness_deadline'):cutover.wait_app(cutover.APPS['documents'],expected_revision='expected')
            azure.assert_not_called()
    def test_existing_api_wait_contract_is_unchanged(self):
        app={'properties':{'provisioningState':'Succeeded'}}
        with patch.object(cutover,'get_app',return_value=app),patch.object(cutover,'revision_is_ready',return_value=True),patch.object(cutover,'read_service_replicas') as azure:
            self.assertIs(cutover.wait_app(cutover.API,expected_revision='expected'),app)
            azure.assert_not_called()
    def test_other_cutover_functions_are_byte_identical_to_baseline(self):
        before=subprocess.check_output(['git','show',BASE+':deployment/pulse-services/cutover.py'],cwd=ROOT,text=True)
        after=(ROOT/'deployment/pulse-services/cutover.py').read_text()
        def functions(source):
            lines=source.splitlines(keepends=True)
            return {n.name:''.join(lines[n.lineno-1:n.end_lineno]) for n in ast.parse(source).body if isinstance(n,(ast.FunctionDef,ast.ClassDef))}
        original=functions(before);current=functions(after)
        self.assertEqual(set(current)-set(original),{'az_stage','recover_failed_orphan_services'})
        self.assertFalse(set(original)-set(current))
        for name in original:
            if name not in {'wait_app','prepare'}:self.assertEqual(current[name],original[name],name)
    def test_identity_authority_and_sandbox_source_remain_unchanged(self):
        for name in ('test_resources.py','canonical_release.py','secret_preservation.py','sandbox.c'):
            path='deployment/pulse-services/'+name
            self.assertEqual((ROOT/path).read_bytes(),subprocess.check_output(['git','show',BASE+':'+path],cwd=ROOT))

class BoundedReplicaReads(unittest.TestCase):
    name=cutover.APPS['documents']
    def revision(self):return self.name+'--svc-12345'
    def response(self,code=0,body=None,error=''):
        return types.SimpleNamespace(returncode=code,stdout=json.dumps(body if body is not None else {'value':[]}),stderr=error)
    def read(self):
        from replica_readiness import read_service_replicas
        return read_service_replicas(self.name,self.revision())
    def test_calls_only_fixed_versioned_test_replica_read(self):
        with patch('subprocess.run',return_value=self.response()) as process:
            self.assertEqual(self.read(),[])
        args=process.call_args.args[0]
        self.assertEqual(args,['az','rest','--method','GET','--url','https://management.azure.com'+cutover.ROOT+'/providers/Microsoft.App/containerApps/'+self.name+'/revisions/'+self.revision()+'/replicas?api-version=2025-01-01','--only-show-errors','-o','json'])
        self.assertEqual(process.call_args.kwargs['timeout'],25)
    def test_foreign_application_or_revision_rejected_before_network(self):
        from replica_readiness import read_service_replicas
        for name,revision in [('ca-prod',self.revision()),(self.name,'other--svc-12345'),(self.name,self.revision()+'/../../providers'),(self.name,self.name+'--svc-0')]:
            with patch('subprocess.run') as process,self.assertRaisesRegex(ValueError,'replica_read_scope_rejected'):
                read_service_replicas(name,revision)
            process.assert_not_called()
    def test_initial_not_found_is_unready_not_success(self):
        with patch('subprocess.run',return_value=self.response(1,error='ERROR: (ResourceNotFound) pending')):
            rows=self.read()
        self.assertEqual(rows,[]);self.assertFalse(service_replicas_ready(rows,['documents']))
    def test_actual_rest_not_found_envelope_is_unready(self):
        error='ERROR: Not Found('+json.dumps({'error':{'code':'ResourceNotFound','message':'PRIVATE_SENTINEL'}})+')'
        with patch('subprocess.run',return_value=self.response(1,error=error)):
            self.assertEqual(self.read(),[])
    def test_error_message_cannot_impersonate_transient_code(self):
        error='ERROR: Forbidden('+json.dumps({'error':{'code':'AuthorizationFailed','message':'ResourceNotFound'}})+')'
        with patch('subprocess.run',return_value=self.response(1,error=error)),self.assertRaisesRegex(ValueError,'replica_read_authorization_failed'):self.read()
        malformed='ERROR: Unknown({"message":"ResourceNotFound"})'
        with patch('subprocess.run',return_value=self.response(1,error=malformed)),self.assertRaisesRegex(ValueError,'replica_read_unclassified_failure'):self.read()
    def test_authorization_and_unclassified_errors_are_terminal(self):
        for code in ('AuthorizationFailed','Forbidden','InvalidAuthenticationToken','UnexpectedAzureFailure'):
            with patch('subprocess.run',return_value=self.response(1,error='ERROR: ('+code+') private details')),self.assertRaises(ValueError) as captured:self.read()
            self.assertNotIn('private details',str(captured.exception))
    def test_only_reads_can_be_retried_without_native_success(self):
        for code in ('ContainerAppRevisionNotFound','ContainerAppReplicaNotFound','TooManyRequests','ServiceUnavailable'):
            with patch('subprocess.run',return_value=self.response(1,error='ERROR: ('+code+') details')):
                self.assertEqual(self.read(),[])
    def test_timeout_keeps_service_unready(self):
        with patch('subprocess.run',side_effect=subprocess.TimeoutExpired(['az'],25)):
            self.assertEqual(self.read(),[])
    def test_raw_stderr_and_response_are_not_exported(self):
        import contextlib,io
        capture=io.StringIO()
        with patch('subprocess.run',return_value=self.response(1,error='ERROR: (ResourceNotFound) PRIVATE_SENTINEL')),contextlib.redirect_stdout(capture):self.read()
        self.assertNotIn('PRIVATE_SENTINEL',capture.getvalue())
    def test_invalid_oversized_or_paginated_collection_cannot_pass(self):
        for value in (None,[],{'value':None},{'value':[{}]*11},{'value':[],'nextLink':'https://untrusted.invalid'},{'value':[],'extra':'value'}):
            result=self.response();result.stdout=json.dumps(value)
            with patch('subprocess.run',return_value=result),self.assertRaisesRegex(ValueError,'replica_read_invalid_collection'):self.read()
        result=self.response();result.stdout='x'*1048577
        with patch('subprocess.run',return_value=result),self.assertRaisesRegex(ValueError,'replica_read_response_budget'):self.read()
    def test_positive_collection_still_requires_each_container_running(self):
        rows=[{'properties':{'containers':[{'name':'documents','ready':True,'runningState':'Waiting'}]}}]
        with patch('subprocess.run',return_value=self.response(body={'value':rows})):
            self.assertFalse(service_replicas_ready(self.read(),['documents']))
        rows[0]['properties']['containers'][0]['runningState']='Running'
        with patch('subprocess.run',return_value=self.response(body={'value':rows})):
            self.assertTrue(service_replicas_ready(self.read(),['documents']))
    def test_transient_missing_replicas_then_healthy_service(self):
        app={'properties':{'provisioningState':'Succeeded','template':{'containers':[{'name':'documents'}]}}}
        rows=[{'properties':{'containers':[{'name':'documents','ready':True,'runningState':'Running'}]}}]
        results=[self.response(1,error='ERROR: (ResourceNotFound) starting'),self.response(body={'value':rows})]
        with patch.object(cutover,'get_app',return_value=app),patch.object(cutover,'revision_is_ready',return_value=True),patch('subprocess.run',side_effect=results),patch.object(cutover.time,'sleep') as sleep:
            self.assertIs(cutover.wait_app(self.name,expected_revision=self.revision()),app)
            sleep.assert_called_once_with(10)

if __name__=='__main__':unittest.main(verbosity=2)
