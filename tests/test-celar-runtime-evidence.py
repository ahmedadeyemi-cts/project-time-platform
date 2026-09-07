import importlib.util
import json
from pathlib import Path

path = Path(__file__).resolve().parents[1] / 'scripts/release-test/collect-celar-runtime-evidence.py'
spec = importlib.util.spec_from_file_location('evidence', path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
private = 'PRIVATE_CUSTOMER_CONTENT_AND_SECRET'
raw = '\n'.join(json.dumps({'MESSAGE': message}) for message in [
    'Out of memory: killed process 123 ' + private,
    'model runner has unexpectedly stopped ' + private,
    'connection reset by peer ' + private,
    'context deadline exceeded ' + private])
result = module.journal_summary(raw)
assert result['signals']['oom'] == 1 and result['signals']['runner_exit'] == 1
assert result['signals']['connection_reset'] == 1 and result['signals']['timeout'] == 1
assert private not in json.dumps(result)
assert module.journal_summary(json.dumps({'MESSAGE': '[GIN] | 500 | 12m0s | POST /api/chat ' + private}))['inferenceHttpStatuses'] == {'500': 1}
assert module.journal_summary('invalid')['status'] == 'incomplete'
assert module.journal_summary('\n'.join(['{}'] * 5000))['status'] == 'incomplete'
assert module.journal_summary('')['entriesExamined'] == 0
service = module.service_summary('ActiveState=active\nSubState=running\nNRestarts=3\nEnvironment=' + private)
assert service['NRestarts'] == 3 and private not in json.dumps(service)
models = module.model_summary({'models': [{'name':'gemma3:4b', 'size':123, 'prompt':private}, {'name':private}]})
assert len(models['loadedModels']) == 1 and private not in json.dumps(models)
from types import SimpleNamespace
assert module.model_summary({})['status'] == 'incomplete'
assert models['status'] == 'incomplete' and models['omittedModels'] == 1
module.subprocess.run = lambda *a, **k: SimpleNamespace(returncode=0, stdout='', stderr='You are currently not seeing messages from other users')
assert module.command([]) == (None, 'read_permission_limited')
module.subprocess.run = lambda *a, **k: SimpleNamespace(returncode=1, stdout=private, stderr=private)
assert module.command([]) == (None, 'read_failed')
print('CELAR_RUNTIME_EVIDENCE_PRIVACY_AND_INCOMPLETENESS=PASS')
