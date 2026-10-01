"""Pure validation of Azure replica readiness; no remote calls or credentials."""

def service_replicas_ready(replicas, expected_names):
    if not isinstance(replicas, list) or not 1 <= len(replicas) <= 10:
        return False
    expected = set(expected_names)
    if not expected or len(expected) != len(expected_names):
        return False
    for replica in replicas:
        if not isinstance(replica, dict):
            return False
        properties = replica.get('properties')
        if not isinstance(properties, dict):
            return False
        containers = properties.get('containers')
        if not isinstance(containers, list) or len(containers) != len(expected):
            return False
        if not all(isinstance(c, dict) for c in containers):
            return False
        names = [c.get('name') for c in containers]
        if not all(isinstance(name, str) for name in names) or set(names) != expected:
            return False
        if not all(c.get('ready') is True and c.get('runningState') == 'Running' for c in containers):
            return False
    return True


# Use the documented, versioned ARM read rather than an extension-dependent
# CLI wrapper. Only the two expected Test services/revisions are addressable.
def read_service_replicas(name, revision):
    import json, re, subprocess
    from test_resources import APPS, ROOT
    if name not in APPS.values() or re.fullmatch(re.escape(name) + r'--svc-[1-9][0-9]{0,19}', revision or '') is None:
        raise ValueError('replica_read_scope_rejected')
    resource = ROOT + '/providers/Microsoft.App/containerApps/' + name + '/revisions/' + revision + '/replicas'
    command = ['az', 'rest', '--method', 'GET', '--url',
        'https://management.azure.com' + resource + '?api-version=2025-01-01',
        '--only-show-errors', '-o', 'json']
    try:
        result = subprocess.run(command, capture_output=True, text=True, timeout=25)
    except subprocess.TimeoutExpired:
        print('PULSE_REPLICA_READ=waiting category=timeout', flush=True)
        return []
    if result.returncode:
        # Azure CLI renders ARM's finite code as ERROR: (Code). Never export
        # raw stderr: it can include tenant/resource details or response bodies.
        text = (result.stderr or '')[-16384:]
        match = re.search(r'(?m)^ERROR: \(([A-Za-z0-9]{1,80})\)', text)
        code = match.group(1) if match else None
        if code is None and text.startswith('ERROR: '):
            # `az rest` renders HTTP errors as Not Found({"error": {...}}).
            # Parse only the top-level ARM error code, never message contents.
            start, end = text.find('{'), text.rfind('}')
            if 0 <= start < end:
                try:
                    envelope = json.loads(text[start:end+1])
                    error = envelope.get('error') if isinstance(envelope, dict) else None
                    candidate = error.get('code') if isinstance(error, dict) else None
                    if isinstance(candidate, str) and re.fullmatch(r'[A-Za-z0-9]{1,80}', candidate):
                        code = candidate
                except (ValueError, RecursionError):
                    pass
        waiting = {'ResourceNotFound', 'ContainerAppRevisionNotFound',
            'ContainerAppReplicaNotFound', 'ContainerAppReplicasNotFound',
            'RevisionNotFound', 'TooManyRequests', 'ServiceUnavailable',
            'GatewayTimeout', 'OperationTimedOut', 'ContainerAppRevisionNotReady'}
        denied = {'AuthorizationFailed', 'AuthenticationFailed', 'Unauthorized',
            'Forbidden', 'InvalidAuthenticationToken', 'LinkedAuthorizationFailed'}
        if code in denied:
            raise ValueError('replica_read_authorization_failed') from None
        if code in waiting:
            category = 'not_ready' if 'NotFound' in code or 'NotReady' in code else 'temporary_service_failure'
            print('PULSE_REPLICA_READ=waiting category=' + category, flush=True)
            return []
        raise ValueError('replica_read_unclassified_failure') from None
    if len(result.stdout.encode('utf-8')) > 1048576:
        raise ValueError('replica_read_response_budget')
    try:
        payload = json.loads(result.stdout)
    except (ValueError, RecursionError):
        raise ValueError('replica_read_invalid_json') from None
    if not isinstance(payload, dict) or set(payload) - {'value', 'nextLink'} or payload.get('nextLink'):
        raise ValueError('replica_read_invalid_collection')
    rows = payload.get('value')
    if not isinstance(rows, list) or len(rows) > 10:
        raise ValueError('replica_read_invalid_collection')
    return rows
