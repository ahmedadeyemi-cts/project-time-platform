import importlib.util
import json
from pathlib import Path
from unittest.mock import patch
import tempfile
import os
import contextlib
import io

root = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('preflight', root / 'scripts/release-test/verify-oracle-sow-runtime.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
expected = {k: json.loads((root / 'deployment/oracle-celar/release.json').read_text())[k] for k in m.FIELDS}
assert m.evaluate(200, json.dumps(expected).encode(), expected)['diagnosticCode'] == 'runtime_verified'
stale = dict(expected, gatewayVersion='1.1.4')
assert m.evaluate(200, json.dumps(stale).encode(), expected)['diagnosticCode'] == 'runtime_contract_mismatch'
for code, name in [(401,'runtime_authentication_rejected'),(403,'runtime_access_rejected'),(503,'runtime_http_failure'),(0,'runtime_transport_unavailable')]:
    assert m.evaluate(code, b'{}', expected)['diagnosticCode'] == name
assert m.evaluate(200, b'<html>error</html>', expected)['diagnosticCode'] == 'runtime_health_response_invalid'
secret='synthetic-secret-that-must-never-be-printed'
malicious = {k: secret for k in m.FIELDS} | {'error':secret,'apiKey':secret}
assert secret not in json.dumps(m.evaluate(401,json.dumps(malicious).encode(),expected))
assert m.NoRedirect().redirect_request(None,None,302,'',{},'https://other.example') is None
with tempfile.TemporaryDirectory() as tmp, patch.dict(os.environ, EVIDENCE_DIR=tmp, RUNTIME_TOKEN=secret), patch.object(m, 'fetch', return_value=(401,json.dumps(malicious).encode())) as fetch, patch.object(m.time,'sleep') as sleep:
    output=io.StringIO()
    with contextlib.redirect_stdout(output): assert m.main() == 1
    assert fetch.call_count == 1 and sleep.call_count == 0
    for p in Path(tmp).iterdir():
        assert secret not in p.read_text()
        assert 'runtime_authentication_rejected' in p.read_text()
    assert secret not in output.getvalue()
print('ORACLE_RUNTIME_PREFLIGHT_EVIDENCE=PASS')

with tempfile.TemporaryDirectory() as tmp, patch.dict(os.environ, EVIDENCE_DIR=tmp, RUNTIME_TOKEN=secret), patch.object(m, 'fetch', side_effect=m.http.client.IncompleteRead(secret.encode())) as fetch, patch.object(m.time,'sleep') as sleep:
    output=io.StringIO()
    with contextlib.redirect_stdout(output): assert m.main() == 1
    assert fetch.call_count == 30 and sleep.call_count == 29
    assert secret not in output.getvalue()
    result=json.loads((Path(tmp)/'oracle-sow-runtime.json').read_text())
    assert result['diagnosticCode'] == 'runtime_transport_unavailable' and result['attempt'] == 30

# Export-only acceptance must not contact Celar or read its credential.
export_context=dict(ACCEPTANCE_SCOPE='sow_exports',GITHUB_REPOSITORY='ahmedadeyemi-cts/project-time-platform',
 GITHUB_REF='refs/heads/main',GITHUB_EVENT_NAME='workflow_dispatch',TARGET_RELEASE_BRANCH='main',
 GITHUB_SHA='a'*40,TARGET_RELEASE_COMMIT='a'*40,RUNTIME_TOKEN='')
for invalid in [None,('GITHUB_REPOSITORY','other/repo'),('GITHUB_REF','refs/heads/other'),
 ('GITHUB_EVENT_NAME','push'),('TARGET_RELEASE_BRANCH','other'),('TARGET_RELEASE_COMMIT','b'*40),('GITHUB_SHA','invalid')]:
 with tempfile.TemporaryDirectory() as tmp, patch.dict(os.environ,dict(export_context,EVIDENCE_DIR=tmp)), patch.object(m,'fetch') as fetch:
  if invalid:os.environ[invalid[0]]=invalid[1]
  with contextlib.redirect_stdout(io.StringIO()):assert m.main()==(1 if invalid else 0)
  fetch.assert_not_called()
  result=json.loads((Path(tmp)/'oracle-sow-runtime.json').read_text())
  assert result['celarAcceptance']=='PENDING_NOT_EXECUTED' and result['oracleMutation'] is False
  assert result['status']==('rejected' if invalid else 'not_required')
print('GENERATION_FREE_EXPORT_PREFLIGHT=PASS positive=1 negative=6 retained_full_runtime_checks=PASS')
