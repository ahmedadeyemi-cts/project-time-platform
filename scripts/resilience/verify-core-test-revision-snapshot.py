#!/usr/bin/env python3
"""Validate a read-only Azure Test Container App revision snapshot before deployment design."""
import json
import re
import sys

API = 'ca-phd-test-api-westus3'
ACR = 'acrphdtest7825cc.azurecr.io/'


def verify(data):
    reasons = []
    if data.get('name') != API:
        reasons.append('wrong_test_app')
    props = data.get('properties') or {}
    config = props.get('configuration') or {}
    template = props.get('template') or {}
    containers = template.get('containers') or []
    traffic = (config.get('ingress') or {}).get('traffic') or []
    if not props.get('latestReadyRevisionName'):
        reasons.append('no_ready_revision')
    if len(containers) != 1:
        reasons.append('ambiguous_container_contract')
    else:
        image = containers[0].get('image') or ''
        if not image.startswith(ACR) or not re.search(r'@sha256:[0-9a-f]{64}$', image):
            reasons.append('image_not_immutable_test_acr')
    if not traffic or sum(x.get('weight', -1) for x in traffic) != 100:
        reasons.append('invalid_traffic_weights')
    if not all(x.get('revisionName') or x.get('latestRevision') is True for x in traffic):
        reasons.append('unresolvable_traffic_revision')
    if (template.get('scale') or {}).get('minReplicas', 0) < 1:
        reasons.append('zero_min_replicas')
    return reasons


def main():
    payload = json.load(sys.stdin)
    reasons = verify(payload)
    print('CORE_TEST_ROLLBACK_SNAPSHOT=' + ('PASS' if not reasons else 'DENIED'))
    for item in reasons:
        print('DENIAL=' + item)
    print('AZURE_MUTATION=NONE')
    return int(bool(reasons))


if __name__ == '__main__':
    sys.exit(main())
