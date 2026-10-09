#!/usr/bin/env python3
"""Read-only core health verifier: no Oracle, credentials, or Azure mutations."""
import json
import os
import sys
import urllib.error
import urllib.request

BASE = os.getenv('PULSE_TEST_BASE', 'https://phd-west-test.onenecklab.com').rstrip('/')
EXPECTED_SOURCE = os.getenv('PULSE_EXPECTED_SOURCE', '').strip()


def get(path):
    request = urllib.request.Request(BASE + path, headers={'Accept': 'application/json', 'Cache-Control': 'no-cache'})
    try:
        with urllib.request.urlopen(request, timeout=15) as result:
            body = result.read(65537)
            if len(body) > 65536:
                raise ValueError('response_too_large')
            if 'json' not in result.headers.get('Content-Type', '').lower():
                raise ValueError('not_json_content_type')
            return result.status, json.loads(body)
    except (urllib.error.URLError, json.JSONDecodeError, ValueError) as exc:
        raise RuntimeError(type(exc).__name__ + ': ' + str(exc)[:100]) from None


def main():
    failures = []
    checks = [('/health', 'healthy'), ('/health/live', 'alive'), ('/health/ready', 'ready')]
    for path, expected in checks:
        try:
            code, body = get(path)
            if code != 200 or body.get('status') != expected:
                raise ValueError('unexpected_response_contract')
            print(path, 'PASS')
        except Exception as exc:
            failures.append(path)
            print(path, 'FAIL', str(exc))
    try:
        code, body = get('/api/version')
        source = body.get('sourceCommit') or body.get('source') or body.get('commit')
        if EXPECTED_SOURCE and source != EXPECTED_SOURCE:
            raise ValueError('source_commit_unverified')
        print('/api/version', 'PASS' if code == 200 else 'FAIL', 'source_identity=' + ('present' if source else 'not_exposed'))
    except Exception as exc:
        failures.append('/api/version')
        print('/api/version', 'FAIL', str(exc))
    print('PULSE_CORE_ONLY_RESULT=' + ('PASS' if not failures else 'BLOCKED'))
    print('CELAR_AI_SOW_ACCEPTANCE=NOT_EXECUTED')
    print('PRODUCTION_MUTATION=NONE')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
