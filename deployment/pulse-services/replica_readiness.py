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
