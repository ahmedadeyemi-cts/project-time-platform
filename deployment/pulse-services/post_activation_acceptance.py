"""Finalize a Test-only activation only after every existing application UAT passes.
This module is PR source, not a manual deployment entry point. It does not approve,
dispatch or modify any workflow. Failed acceptance restores the prior selection.
"""
import json
import os
import re
import shutil
from pathlib import Path

import cutover
from canonical_release import validate_context, validate_run, FINALIZATION_STEP, WORKFLOW, JOB
from service_images import context, existing_images


def canonical_finalization(run, jobs, source, number):
    cutover.check(run.get('id') == int(number) and run.get('head_sha') == source, 'run_identity_mismatch')
    cutover.check(run.get('path') == WORKFLOW and run.get('event') == 'workflow_dispatch'
                  and run.get('head_branch') == 'main', 'canonical_origin_required')
    cutover.check(run.get('status') == 'in_progress' and run.get('conclusion') is None
                  and run.get('run_attempt') == 1, 'active_first_attempt_required')
    cutover.check(run.get('actor', {}).get('login') in ('github-actions[bot]', 'ahmedadeyemi-cts'),
                  'authorized_release_actor_required')
    rows = jobs.get('jobs', [])
    cutover.check(len(rows) == 1 and rows[0].get('run_id') == int(number)
                  and rows[0].get('name') == JOB and rows[0].get('head_sha') == source,
                  'canonical_job_required')
    cutover.check(rows[0].get('status') == 'in_progress' and rows[0].get('conclusion') is None,
                  'active_job_required')
    active = [step for step in rows[0].get('steps', []) if step.get('status') == 'in_progress']
    cutover.check(len(active) == 1 and active[0].get('name') == FINALIZATION_STEP,
                  'finalization_step_required')


def finalize():
    source, safe = context()
    number = os.environ.get('GITHUB_RUN_ID', '')
    validate_context(os.environ, source, number)
    run = cutover.gh('actions/runs/' + number)
    jobs = cutover.gh('actions/runs/' + number + '/jobs?filter=latest&per_page=10')
    canonical_finalization(run, jobs, source, number)
    private = Path(os.environ['PULSE_PRIVATE_EVIDENCE'])
    expected = Path(os.environ['RUNNER_TEMP']).resolve() / 'pulse-services-private'
    cutover.check(not private.is_symlink() and private.resolve() == expected, 'private_directory_scope')
    cutover.SHA, cutover.RUN, cutover.PRIVATE, cutover.SAFE = source, number, private, safe
    if not private.is_dir():
        print('PULSE_SERVICE_ACCEPTANCE=FAILED activation_did_not_complete')
        return 1
    receipt = json.loads((safe / 'activation-receipt.json').read_text())
    cutover.check(receipt.get('sourceSha') == source and receipt.get('runId') == number,
                  'activation_receipt_identity_changed')
    try:
        validate_run(run, jobs, source, number, final=True)
        cutover.check(receipt.get('status') == 'pending_application_uat'
                      and receipt.get('servicesActivated') is True, 'native_activation_required')
        before = json.loads((private / 'preflight.json').read_text())['adminIdentity']
        cutover.check(cutover.local_admin(after=True) == before, 'post_uat_admin_identity_changed')
        build = json.loads((safe / 'build-identities.json').read_text())
        expected_images = json.loads((safe / 'registry-images.json').read_text())
        cutover.check(existing_images(build['fingerprint']) == expected_images, 'post_uat_images_changed')
        from release_phase import remove_success_job
        remove_success_job()
        receipt.update(status='passed', applicationUatPassed=True,
                       allConfiguredPostActivationGatesPassed=True)
        (safe / 'cutover.json').write_text(json.dumps(receipt, indent=2) + '\n')
        print('PULSE_SERVICE_ACCEPTANCE=PASS allConfiguredPostActivationGatesPassed=true')
        return 0
    except Exception as error:
        code = str(error) if isinstance(error, (ValueError, cutover.CutoverError)) and re.fullmatch('[a-z_]{1,100}', str(error)) else type(error).__name__
        receipt.update(status='failed', applicationUatPassed=False, diagnostic=code)
        try:
            if receipt.get('mode') != 'existing_verified':
                cutover.rollback()
            receipt['rollbackCompleted'] = True
            receipt['servicesActivated'] = receipt.get('mode') == 'existing_verified'
        except Exception:
            receipt['rollbackCompleted'] = False
        print('PULSE_SERVICE_ACCEPTANCE=FAILED diagnostic=' + code)
        return 1
    finally:
        (safe / 'activation-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
        shutil.rmtree(private)
