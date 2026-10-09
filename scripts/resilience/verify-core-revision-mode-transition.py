#!/usr/bin/env python3
"""Read-only policy: Single -> Multiple must preserve baseline revision and traffic."""
import json
import sys


def verify(before, after):
    errors=[]
    if before.get('name') != 'ca-phd-test-api-westus3' or after.get('name') != before.get('name'):
        errors.append('target_mismatch')
    b=before.get('properties') or {}
    a=after.get('properties') or {}
    bc=b.get('configuration') or {}
    ac=a.get('configuration') or {}
    ready=b.get('latestReadyRevisionName')
    if not ready or bc.get('activeRevisionsMode') != 'Single':
        errors.append('invalid_baseline')
    if ac.get('activeRevisionsMode') != 'Multiple':
        errors.append('candidate_not_multiple')
    if a.get('latestReadyRevisionName') != ready:
        errors.append('ready_revision_changed')
    traffic=(ac.get('ingress') or {}).get('traffic') or []
    if len(traffic) != 1 or traffic[0].get('weight') != 100 or not (traffic[0].get('revisionName') == ready or traffic[0].get('latestRevision') is True):
        errors.append('baseline_traffic_not_preserved')
    for field in ('secrets','registries','dapr'):
        if bc.get(field) != ac.get(field):
            errors.append('configuration_changed_'+field)
    return errors


if __name__ == '__main__':
    data=json.load(sys.stdin)
    failures=verify(data['before'],data['after'])
    print('CORE_REVISION_MODE_TRANSITION=' + ('PASS' if not failures else 'DENIED'))
    for failure in failures: print('DENIAL='+failure)
    print('AZURE_MUTATION=NONE')
    sys.exit(int(bool(failures)))
