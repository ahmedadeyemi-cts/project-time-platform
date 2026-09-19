#!/usr/bin/env python3
"""Authorize a read-only register check. Never dispatch a deployment/generation."""
import json
import os
import re
import subprocess
from pathlib import Path

REPOSITORY = 'ahmedadeyemi-cts/project-time-platform'


def require(condition, code):
    if not condition:
        raise RuntimeError(code)


def parse_request(command, actor, event, ref, sha, main):
    match = re.fullmatch(r'VERIFY MODULE025 REGISTER SHA ([0-9a-f]{40}) SOURCE RUN ([0-9]+)', command)
    require(actor == 'ahmedadeyemi-cts' and event == 'issue_comment' and ref == 'refs/heads/main',
            'register_check_untrusted_actor_or_ref')
    require(match is not None, 'register_check_command_invalid')
    require(match[1] == sha == main, 'register_check_stale_main')
    return match[2]


def verify_source(run, jobs, source_id):
    require(run.get('id') == int(source_id) and run.get('workflow_id') == 315562561
            and run.get('path') == '.github/workflows/projectpulse-deploy-test.yml'
            and run.get('status') == 'completed' and run.get('head_branch') == 'main',
            'register_check_source_run_invalid')
    require(len(jobs.get('jobs', [])) == 1 and jobs.get('total_count') == 1,
            'register_check_source_job_ambiguous')
    steps = {step['name']: step for step in jobs['jobs'][0].get('steps', [])}
    for name in ('Deploy immutable Test API image', 'Deploy immutable Test web image',
                 'Seal server-confirmed deployment identity',
                 'Verify Module 025 scoped deployment identity and lifecycle'):
        require(steps.get(name, {}).get('conclusion') == 'success', 'register_check_source_lifecycle_incomplete')
    # A later acceptance failure does not invalidate this retained synthetic
    # record, and must never be represented as a passing full deployment.
    return run['head_sha']


def main():
    require(os.environ['GITHUB_REPOSITORY'] == REPOSITORY, 'register_check_repository_invalid')
    def api(path):
        return json.loads(subprocess.check_output(['gh', 'api', f'repos/{REPOSITORY}/{path}'], text=True))
    current = api('git/ref/heads/main')['object']['sha']
    source_id = parse_request(os.environ['REQUEST_COMMENT'], os.environ['GITHUB_ACTOR'],
                              os.environ['GITHUB_EVENT_NAME'], os.environ['GITHUB_REF'],
                              os.environ['GITHUB_SHA'], current)
    run = api(f'actions/runs/{source_id}')
    source_sha = verify_source(run, api(f'actions/runs/{source_id}/jobs?filter=latest&per_page=100'), source_id)
    require(re.fullmatch(r'[0-9a-f]{40}', source_sha), 'register_check_source_sha_invalid')
    subprocess.run(['git', 'merge-base', '--is-ancestor', source_sha, 'HEAD'], check=True)
    with Path(os.environ['GITHUB_OUTPUT']).open('a') as output:
        output.write(f'source_run_id={source_id}\n')
    print('MODULE025_REGISTER_CHECK_AUTHORIZED=PASS generationPermitted=false deploymentPermitted=false')


if __name__ == '__main__':
    main()
