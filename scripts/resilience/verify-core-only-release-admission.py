#!/usr/bin/env python3
"""Offline, fail-closed admission preflight; cannot authorize or deploy anything."""
import argparse
import re
import subprocess
import sys


def git(*args):
    return subprocess.check_output(['git', *args], text=True, stderr=subprocess.DEVNULL).strip()


def evaluate(release_sha, main_sha, ref, environment, oracle_acceptance):
    reasons = []
    if not re.fullmatch(r'[a-f0-9]{40}', release_sha):
        reasons.append('release_sha_invalid')
    if not re.fullmatch(r'[a-f0-9]{40}', main_sha):
        reasons.append('main_sha_invalid')
    if release_sha != main_sha:
        reasons.append('release_not_exact_main')
    if ref != 'refs/heads/main':
        reasons.append('not_main_ref')
    if environment != 'test':
        reasons.append('not_protected_test')
    if oracle_acceptance != 'NOT_EXECUTED':
        reasons.append('oracle_acceptance_must_remain_not_executed')
    return reasons


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--release-sha', required=True)
    p.add_argument('--main-sha', required=True)
    p.add_argument('--ref', required=True)
    p.add_argument('--environment', required=True)
    p.add_argument('--oracle-acceptance', default='NOT_EXECUTED')
    args = p.parse_args()
    reasons = evaluate(args.release_sha, args.main_sha, args.ref, args.environment, args.oracle_acceptance)
    print('PULSE_CORE_ADMISSION_PREFLIGHT=' + ('PASS' if not reasons else 'DENIED'))
    for reason in reasons:
        print('DENIAL=' + reason)
    print('DEPLOYMENT_AUTHORIZATION=NOT_GRANTED')
    print('PRODUCTION_MUTATION=NONE')
    return int(bool(reasons))


if __name__ == '__main__':
    sys.exit(main())
