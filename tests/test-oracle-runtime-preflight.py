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
