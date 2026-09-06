#!/usr/bin/env python3
"""Verify the coupled Oracle runtime and retain only allowlisted health evidence."""
import json
import http.client
import os
import re
import time
import urllib.error
import urllib.request
from pathlib import Path

URL = 'https://celarai.onenecklab.com/health'
FIELDS = ('gatewayVersion', 'sowTimeoutSeconds', 'sowModelAttemptSeconds', 'sowContextTokens')

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None

def fetch(token):
    request = urllib.request.Request(URL, headers={
        'Authorization': 'Bearer ' + token,
        'X-Pulse-AI-Privacy-Boundary': 'private_pulse_runtime_only',
        'Accept': 'application/json'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    try:
        response = opener.open(request, timeout=30)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        return response.code, response.read(65537)

def evaluate(status, raw, expected):
    actual = {key: None for key in FIELDS}
    parsed = False
    if len(raw) <= 65536:
        try:
            body = json.loads(raw)
            parsed = isinstance(body, dict)
        except (ValueError, UnicodeDecodeError):
            body = {}
        if parsed:
            value = body.get('gatewayVersion')
            if isinstance(value, str) and re.fullmatch(r'[0-9]{1,5}(\.[0-9]{1,5}){2}', value):
                actual['gatewayVersion'] = value
            for key in ('sowTimeoutSeconds', 'sowContextTokens'):
                value = body.get(key)
                if type(value) is int and 0 <= value <= 100000:
                    actual[key] = value
            value = body.get('sowModelAttemptSeconds')
            if isinstance(value, list) and len(value) == 3 and all(type(x) is int and 0 <= x <= 3600 for x in value):
                actual['sowModelAttemptSeconds'] = value
    matches = actual == expected
    code = ('runtime_verified' if status == 200 and matches else
            'runtime_authentication_rejected' if status == 401 else
            'runtime_access_rejected' if status == 403 else
            'runtime_transport_unavailable' if status == 0 else
            'runtime_http_failure' if status != 200 else
            'runtime_health_response_invalid' if not parsed else 'runtime_contract_mismatch')
    return dict(httpStatus=status, diagnosticCode=code, expected=expected, actual=actual)

def main():
    expected = {key: json.loads(Path('deployment/oracle-celar/release.json').read_text())[key] for key in FIELDS}
    evidence = Path(os.environ['EVIDENCE_DIR'])
    evidence.mkdir(parents=True, exist_ok=True)
    token = os.environ.get('RUNTIME_TOKEN', '').strip()
    if len(token) < 32:
        result = dict(httpStatus=0, diagnosticCode='runtime_token_missing', expected=expected, actual=None)
        (evidence / 'oracle-sow-runtime.json').write_text(json.dumps(result) + '\n')
        print(json.dumps(result))
        return 1
    for attempt in range(1, 11):
        try:
            status, raw = fetch(token)
        except (OSError, ValueError, urllib.error.URLError, http.client.HTTPException):
            status, raw = 0, b''
        result = evaluate(status, raw, expected)
        result['attempt'] = attempt
        safe = json.dumps(result)
        (evidence / 'oracle-sow-runtime.json').write_text(safe + '\n')
        with (evidence / 'oracle-sow-runtime-attempts.jsonl').open('a') as output:
            output.write(safe + '\n')
        print(safe, flush=True)
        if result['diagnosticCode'] == 'runtime_verified':
            return 0
        if status in (401, 403):
            break
        if attempt < 10:
            time.sleep(15)
    return 1

if __name__ == '__main__':
    raise SystemExit(main())
