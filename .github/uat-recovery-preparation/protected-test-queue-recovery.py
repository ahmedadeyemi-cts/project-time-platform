"""Fail-closed recovery of the reviewed, never-started Protected Test runs.

No application credentials, environment approvals, deployment inputs or cloud
resources are changed. A fixed new concurrency generation remains serialized;
old exact-main releases are provably stale before Azure authentication.
"""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request

REPO = 'ahmedadeyemi-cts/project-time-platform'
BASE = 'af060cbc311dcc7cce89c3ae2cc0bff040f5f6ea'
WORKFLOW = 315562561
CONTROLLER = '.github/workflows/projectpulse-deploy-test.yml'
CONTROLLER_SHA256 = '@@CONTROLLER_SHA256@@'
NEW_GROUP = 'projectpulse-deploy-test-recovery-20260921'
TARGET = 35645759101
KNOWN = {
    33654881418: ('3d02b4cb62683b96554186f9d3a782fc1f21ca54', 'fix/shared-project-document-planning-20260819', '2026-09-02T16:26:03Z'),
    34377182662: ('af5fcb463384096f668345ac7cc9bd00efef0a33', 'main', '2026-09-09T16:30:42Z'),
    34495606530: ('9f30078c2c407d4d3576ccefd663a145be50c6c4', 'main', '2026-09-10T15:26:05Z'),
    35364203547: ('669b467b932d3d029bde2c5bba03e0f63ba474f4', 'main', '2026-09-18T15:43:56Z'),
    35374125567: ('245b0915d895d83f1ceaed32460ad95a4a3d79be', 'main', '2026-09-18T17:24:14Z'),
    35461429670: ('57c8d0264bdd828e6b3b53a8c5cb8b1b841e8f61', 'main', '2026-09-19T18:30:44Z'),
    35473939797: ('c15ef12d5ce1bc54c15d8b31c87a50daa94bad17', 'main', '2026-09-19T22:39:09Z'),
    35524946948: ('045b66ca01baa68b2f5b3f6eb9e063c23c335981', 'main', '2026-09-20T17:09:14Z'),
    TARGET: (BASE, 'main', '2026-09-21T19:35:24Z'),
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def validate_snapshot(run_id: int, run: dict, jobs: dict, pending: list) -> None:
    require(type(run_id) is int and run_id in KNOWN, 'Unknown active Test run; recovery is not authorized.')
    sha, branch, created = KNOWN[run_id]
    expected = {
        'id': run_id, 'workflow_id': WORKFLOW, 'run_attempt': 1,
        'event': 'workflow_dispatch', 'head_sha': sha, 'head_branch': branch,
        'created_at': created, 'updated_at': created, 'status': 'queued', 'conclusion': None,
    }
    require(all(key in run and run[key] == value for key, value in expected.items()),
            f'Run {run_id} is no longer the reviewed never-started run.')
    require(type(jobs.get('total_count')) is int and jobs['total_count'] == 0
            and type(jobs.get('jobs')) is list and jobs['jobs'] == [],
            f'Run {run_id} has jobs or incomplete job evidence.')
    require(type(pending) is list and pending == [], f'Run {run_id} has pending or unknown approvals.')


def request(method: str, path: str):
    token = os.environ.get('GH_TOKEN', '')
    require(bool(token), 'The existing Actions token is required.')
    require(path.startswith(f'/repos/{REPO}/'), 'Only this repository is authorized.')
    req = urllib.request.Request('https://api.github.com' + path, method=method, headers={
        'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'protected-test-reviewed-recovery',
    })
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            body = response.read(8 * 1024 * 1024 + 1)
            require(len(body) <= 8 * 1024 * 1024, 'GitHub evidence exceeds the bounded response size.')
            return response.status, json.loads(body) if body else None
    except urllib.error.HTTPError as error:
        # 409 specifically means the exact zero-job run cannot be cancelled.
        # Permission errors and every other failure stop; no token substitution.
        if method == 'POST' and path == f'/repos/{REPO}/actions/runs/{TARGET}/cancel' and error.code == 409:
            return 409, None
        raise RuntimeError(f'GitHub {method} failed with HTTP {error.code}; recovery stopped.') from None


def get(path: str):
    return request('GET', f'/repos/{REPO}/' + path)[1]


def current_main() -> str:
    return get('git/ref/heads/main')['object']['sha']


def read_snapshot(run_id: int):
    run = get(f'actions/runs/{run_id}')
    jobs = get(f'actions/runs/{run_id}/jobs?filter=all&per_page=1')
    pending = get(f'actions/runs/{run_id}/pending_deployments')
    return run, jobs, pending


def verify_context(release: str) -> None:
    require(re.fullmatch('[0-9a-f]{40}', release) is not None and release != BASE, 'A new descendant release is required.')
    require(os.environ.get('GITHUB_REPOSITORY') == REPO and os.environ.get('GITHUB_REF') == 'refs/heads/main',
            'Recovery must execute in this repository on main.')
    require(os.environ.get('GITHUB_WORKFLOW_REF') == f'{REPO}/.github/workflows/module025-protected-uat-control.yml@refs/heads/main',
            'Only the existing protected supervisor may perform recovery.')
    require(os.environ.get('GITHUB_EVENT_NAME') in {'push', 'issue_comment'}, 'Unexpected recovery trigger.')
    require(os.environ.get('GITHUB_SHA') == release == subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
            'Recovery checkout and executing release differ.')
    require(current_main() == release, 'Main changed before recovery.')
    subprocess.run(['git', 'merge-base', '--is-ancestor', BASE, release], check=True)
    require(hashlib.sha256(Path(CONTROLLER).read_bytes()).hexdigest() == CONTROLLER_SHA256,
            'The deployment controller is not the exact reviewed queue-generation change.')


def main() -> None:
    require(len(sys.argv) == 2, 'Supply the authorized release SHA.')
    release = sys.argv[1]
    verify_context(release)
    active = set()
    # Inspect all pages, not only the latest 100 runs. Unknown active runs stop
    # before any cancellation or the existing supervisor enables admissions.
    for page in range(1, 101):
        result = get(f'actions/workflows/{WORKFLOW}/runs?per_page=100&page={page}')
        runs = result.get('workflow_runs')
        require(type(runs) is list, 'Incomplete workflow-run evidence.')
        for run in runs:
            if run.get('status') != 'completed':
                require(type(run.get('id')) is int, 'Active run identity is missing.')
                active.add(run['id'])
        if len(runs) < 100:
            break
    else:
        raise RuntimeError('Workflow history exceeded the bounded pagination budget; no runs were changed.')
    for run_id in sorted(active):
        require(run_id in KNOWN, f'Executable or unreviewed Test run {run_id} exists; no recovery changes were made.')
        validate_snapshot(run_id, *read_snapshot(run_id))
    require(current_main() == release, 'Main changed during the active-run inspection.')
    outcome = 'already_terminal_or_absent'
    if TARGET in active:
        # Recheck immediately before the single, exact authorized cancellation.
        validate_snapshot(TARGET, *read_snapshot(TARGET))
        status, _ = request('POST', f'/repos/{REPO}/actions/runs/{TARGET}/cancel')
        require(status in {202, 409}, 'Unexpected cancellation response.')
        outcome = 'cancellation_not_available_for_zero_job_run' if status == 409 else 'cancellation_requested'
        if status == 202:
            for _ in range(10):
                time.sleep(2)
                snapshot = read_snapshot(TARGET)
                if snapshot[0].get('status') == 'completed':
                    require(snapshot[0].get('head_sha') == BASE and snapshot[0].get('conclusion') == 'cancelled'
                            and snapshot[1].get('total_count') == 0 and snapshot[2] == [],
                            'Cancellation did not produce the expected zero-job terminal result.')
                    outcome = 'cancelled_and_verified'
                    break
                validate_snapshot(TARGET, *snapshot)
        if outcome != 'cancelled_and_verified':
            validate_snapshot(TARGET, *read_snapshot(TARGET))
    require(current_main() == release, 'Main changed before the protected supervisor could continue.')
    evidence = {'release': release, 'knownZeroJobRuns': sorted(active), 'targetRun': TARGET,
                'cancellation': outcome, 'concurrencyGroup': NEW_GROUP,
                'productionMutation': False, 'deploymentsDispatchedByRecovery': 0}
    print('PROTECTED_TEST_REVIEWED_QUEUE_RECOVERY=' + json.dumps(evidence, sort_keys=True), flush=True)
    summary = os.environ.get('GITHUB_STEP_SUMMARY')
    if summary:
        with open(summary, 'a', encoding='utf-8') as handle:
            handle.write('\n### Reviewed Protected Test queue recovery\n```json\n' + json.dumps(evidence, indent=2) + '\n```\n')


if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, OSError, ValueError, subprocess.CalledProcessError, KeyError, TypeError) as error:
        print(f'STOP: {error}', file=sys.stderr)
        sys.exit(1)
