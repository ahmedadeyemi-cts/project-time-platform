"""Bounded authenticated core checks. Never generates SOWs or contacts Oracle."""
import json
import os
import urllib.error
import urllib.parse
import urllib.request

ORIGIN = 'https://phd-west-test.onenecklab.com'

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args):
        raise RuntimeError('canary_redirect_denied')


def check(base, source=None):
    parsed = urllib.parse.urlsplit(base)
    if parsed.scheme != 'https' or parsed.username or parsed.password or parsed.query or parsed.fragment:
        raise RuntimeError('canary_origin_denied')
    if base != ORIGIN and not ((parsed.hostname or '').startswith('ca-phd-test-api-westus3--') and (parsed.hostname or '').endswith('.azurecontainerapps.io')):
        raise RuntimeError('canary_target_denied')
    password = os.environ['TEST_LOGIN_PASSWORD']
    if len(password) < 12:
        raise RuntimeError('test_credential_missing')
    opener = urllib.request.build_opener(NoRedirect())
    token = None
    passed = []

    def request(path, payload=None, module='001', authenticated=False):
        headers = {'Accept': 'application/json', 'Cache-Control': 'no-cache',
                   'Origin': ORIGIN, 'Sec-Fetch-Site': 'same-origin',
                   'X-ProjectPulse-Module-Number': module}
        if authenticated:
            headers.update({'Authorization': 'Bearer '+token, 'X-ProjectPulse-Session': token})
        data = None
        if payload is not None:
            headers['Content-Type'] = 'application/json'
            data = json.dumps(payload).encode()
        req = urllib.request.Request(base+path, data=data, headers=headers)
        try:
            with opener.open(req, timeout=45) as reply:
                raw = reply.read(4*1024*1024+1)
                if reply.status != 200 or len(raw) > 4*1024*1024 or 'json' not in reply.headers.get('Content-Type',''):
                    raise RuntimeError('canary_response_contract')
                return json.loads(raw)
        except urllib.error.HTTPError as exc:
            raise RuntimeError('canary_http_'+str(exc.code)+' '+path) from None
        except (urllib.error.URLError, json.JSONDecodeError):
            raise RuntimeError('canary_transport_or_json '+path) from None

    try:
        for path, status in [('/health','healthy')]: # Existing public portal routes only /health; private candidate verifies all probes.
            result=request(path)
            if not isinstance(result,dict) or result.get('status') != status:
                raise RuntimeError('health_contract '+path)
            passed.append(path)
        version=request('/api/version')
        if version.get('component') != 'ProjectTime.Api':
            raise RuntimeError('immutable_source_identity_unverified')
        passed.append('/api/version')
        identity={}
        try:
            request('/api/security/context')
        except RuntimeError as denied:
            if not str(denied).startswith(('canary_http_401 ', 'canary_http_403 ')):
                raise
        else:
            raise RuntimeError('anonymous_core_access_not_denied')
        passed.append('anonymous_session_denied')
        login=request('/api/auth/local/login', {'username':'jason.mosier@ussignal.local','password':password})
        token=login.get('sessionToken')
        if login.get('provider') != 'LOCAL' or login.get('mustChangePassword') is not False or not isinstance(token,str) or not token:
            raise RuntimeError('local_session_contract')
        context=request('/api/security/context', authenticated=True)
        if not context.get('userId'):
            raise RuntimeError('authenticated_identity_missing')
        passed.append('/api/security/context')
        if source:
            identity=request('/api/core-release/source',authenticated=True)
            if identity.get('component')!='ProjectTime.Api' or identity.get('sourceCommit')!=source:
                raise RuntimeError('immutable_source_identity_unverified')
            passed.append('/api/core-release/source')
        for module,path in [('001','/api/assignments/available-tasks?weekStart=2026-08-16'),
                            ('001','/api/timesheet/work-queue?weekStart=2026-08-16'),
                            ('001A','/api/engineer-task-closeout/overview'),
                            ('019','/api/project-workspace/overview')]:
            result=request(path,module=module,authenticated=True)
            if not isinstance(result,(dict,list)):
                raise RuntimeError('core_read_contract '+path)
            passed.append(path)
        return {'result':'PASS','checks':passed,'sourceCommit':identity.get('sourceCommit'),
                'celarSowAcceptance':'PENDING_NOT_EXECUTED'}
    finally:
        if token:
            # Revoke even when a subsequent application check fails.
            request('/api/auth/session/logout', {}, authenticated=True)
