#!/usr/bin/env python3
"""Pure traffic-admission policy for future protected Test canary; no deployment."""
import json
import sys


def verify(before, candidate, candidate_name=None):
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
    # Azure omits zero-weight entries. Prove zero effective weight from a complete
    # 100% named baseline allocation and the separately verified candidate identity.
    explicit=[v.get('revisionName') for v in traffic if v.get('weight')==0 and v.get('revisionName')!=bp.get('latestReadyRevisionName')]
    identity=candidate_name or cp.get('latestRevisionName') or (explicit[0] if len(explicit)==1 else None)
    if not identity or identity==bp.get('latestReadyRevisionName'):
        reasons.append('missing_zero_traffic_candidate')
    if candidate_name and cp.get('latestRevisionName')!=candidate_name:
        reasons.append('candidate_revision_identity_mismatch')
    if identity and any(v.get('weight',0)>0 and (v.get('latestRevision') or v.get('revisionName')==identity) for v in traffic):
        reasons.append('candidate_receives_traffic')
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
