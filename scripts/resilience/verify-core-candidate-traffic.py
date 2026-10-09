#!/usr/bin/env python3
"""Pure traffic-admission policy for future protected Test canary; no deployment."""
import json
import sys


def verify(before, candidate):
    reasons = []
    bp = before.get('properties') or {}
    cp = candidate.get('properties') or {}
    if before.get('name') != 'ca-phd-test-api-westus3' or candidate.get('name') != before.get('name'):
        reasons.append('test_target_mismatch')
    if not bp.get('latestReadyRevisionName'):
        reasons.append('no_known_good_revision')
    traffic = ((cp.get('configuration') or {}).get('ingress') or {}).get('traffic') or []
    if not traffic or sum(v.get('weight', -1) for v in traffic) != 100:
        reasons.append('invalid_candidate_traffic')
    if any(v.get('weight') != 0 and v.get('revisionName') != bp.get('latestReadyRevisionName') for v in traffic):
        reasons.append('nonbaseline_revision_gets_traffic')
    if not any(v.get('weight') == 0 and v.get('revisionName') and v.get('revisionName') != bp.get('latestReadyRevisionName') for v in traffic):
        reasons.append('missing_zero_traffic_candidate')
    if (cp.get('configuration') or {}).get('activeRevisionsMode') != 'Multiple':
        reasons.append('revision_mode_not_multiple')
    return reasons


if __name__ == '__main__':
    data=json.load(sys.stdin)
    failures=verify(data['before'],data['candidate'])
    print('CORE_ZERO_TRAFFIC_CANDIDATE_POLICY=' + ('PASS' if not failures else 'DENIED'))
    for f in failures: print('DENIAL='+f)
    print('AZURE_MUTATION=NONE')
    sys.exit(int(bool(failures)))
