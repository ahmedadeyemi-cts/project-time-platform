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
        with patch.object(cutover,'get_app',return_value=app),patch.object(cutover,'revision_is_ready',return_value=True),patch.object(cutover,'az',side_effect=[not_ready,self.replicas()]) as azure,patch.object(cutover.time,'sleep') as sleep:
            self.assertIs(cutover.wait_app(name,expected_revision=revision),app)
            self.assertEqual(azure.call_count,2)
            self.assertEqual(azure.call_args.args,('containerapp','replica','list','-g',cutover.GROUP,'-n',name,'--revision',revision))
            sleep.assert_called_once_with(10)
    def test_source_revision_guard_precedes_replica_read(self):
        app={'properties':{'provisioningState':'Succeeded'}}
        with patch.object(cutover,'get_app',return_value=app),patch.object(cutover,'revision_is_ready',return_value=False),patch.object(cutover,'az') as azure,patch.object(cutover.time,'monotonic',side_effect=[0,0,901]),patch.object(cutover.time,'sleep'):
            with self.assertRaisesRegex(cutover.CutoverError,'service_readiness_deadline'):cutover.wait_app(cutover.APPS['documents'],expected_revision='expected')
            azure.assert_not_called()
    def test_existing_api_wait_contract_is_unchanged(self):
        app={'properties':{'provisioningState':'Succeeded'}}
        with patch.object(cutover,'get_app',return_value=app),patch.object(cutover,'revision_is_ready',return_value=True),patch.object(cutover,'az') as azure:
            self.assertIs(cutover.wait_app(cutover.API,expected_revision='expected'),app)
            azure.assert_not_called()
    def test_other_cutover_functions_are_byte_identical_to_baseline(self):
        before=subprocess.check_output(['git','show',BASE+':deployment/pulse-services/cutover.py'],cwd=ROOT,text=True)
        after=(ROOT/'deployment/pulse-services/cutover.py').read_text()
        def functions(source):
            lines=source.splitlines(keepends=True)
            return {n.name:''.join(lines[n.lineno-1:n.end_lineno]) for n in ast.parse(source).body if isinstance(n,(ast.FunctionDef,ast.ClassDef))}
        original=functions(before);current=functions(after)
        self.assertEqual(set(original),set(current))
        for name in original:
            if name!='wait_app':self.assertEqual(current[name],original[name],name)
    def test_identity_authority_and_sandbox_source_remain_unchanged(self):
        for name in ('test_resources.py','canonical_release.py','secret_preservation.py','sandbox.c'):
            path='deployment/pulse-services/'+name
            self.assertEqual((ROOT/path).read_bytes(),subprocess.check_output(['git','show',BASE+':'+path],cwd=ROOT))

if __name__=='__main__':unittest.main(verbosity=2)
