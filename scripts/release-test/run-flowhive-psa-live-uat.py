#!/usr/bin/env python3
"""One real SOW-to-working-copy run in the approved Protected Test environment.

Never mocks an API/model, retries a generation POST, publishes a customer link,
creates a baseline or adopts canonical tasks. Artifacts contain allowlisted
identifiers and aggregate measurements, never sessions, SOW text or task text.
A functional pass is NOT semantic SOW acceptance or a model-speed SLO claim.
"""
from __future__ import annotations
import asyncio
import hashlib
import json
import math
import os
import re
import sys
import time
from datetime import date, datetime, timezone
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse
from urllib.request import HTTPRedirectHandler, Request, build_opener

ORIGIN = 'https://phd-west-test.onenecklab.com'
PROJECT = '0ea25cb8-1a7f-4baf-ba7b-2dd76215be49'
LOGIN = 'heather.schrock@ussignal.local'
PHASES = ['Plan', 'Design', 'Implement', 'Validate', 'Release']
CONTRACT = 'flowhive-bounded-execution-v1-20260906'
MAX_BODY = 12 * 1024 * 1024
TERMINAL_OK = {'completed', 'completed_with_schedule_overrun'}

class GateError(Exception):
    """Safe fixed diagnostic code only, never an upstream response body."""


def need(ok: bool, code: str) -> None:
    if not ok:
        raise GateError(code)


def uid(value: object) -> bool:
    return isinstance(value, str) and re.fullmatch(r'[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}', value) is not None


def iso(value: object) -> datetime:
    need(isinstance(value, str), 'timestamp_missing')
    try:
        result = datetime.fromisoformat(value.replace('Z', '+00:00'))
        need(result.tzinfo is not None, 'timestamp_timezone_missing')
        return result
    except (ValueError, TypeError):
        raise GateError('timestamp_invalid') from None


def plan_checks(plan: dict, schedule: dict, project: str = PROJECT) -> dict:
    need(isinstance(plan, dict) and plan.get('projectId') == project, 'generated_plan_wrong_project')
    need(plan.get('sourceKind') == 'celar_ai', 'generated_plan_not_ai')
    need(isinstance(plan.get('sowVersion'), str) and bool(plan['sowVersion']), 'sow_version_missing')
    need(plan.get('milestones', []) == [], 'unexpected_project_milestones')
    rows = plan.get('tasks')
    need(isinstance(rows, list) and 5 <= len(rows) <= 1000, 'task_count_outside_review_bounds')
    leaves = [r for r in rows if not r.get('isSummary')]
    need(len(leaves) >= 5, 'detailed_work_missing')
    need([r.get('phase') for r in rows if r.get('isSummary')] == PHASES, 'phase_summaries_invalid')
    need({r.get('phase') for r in leaves} == set(PHASES), 'five_phase_work_missing')
    need(all(not r.get('isMilestone') for r in leaves), 'work_replaced_by_milestones')
    need(all(r.get('canonicalTaskId') is None for r in leaves), 'unexpected_canonical_adoption')
    keys = [r.get('wbsNumber') for r in rows]
    need(all(isinstance(k, str) and k for k in keys) and len(set(keys)) == len(keys), 'wbs_keys_invalid')
    titles = [' '.join(str(r.get('name', '')).casefold().split()) for r in leaves]
    need(all(len(s) >= 8 for s in titles) and len(set(titles)) == len(titles), 'duplicate_or_empty_work')
    filler = re.compile(r'^(?:plan|design|implement|validate|release)(?: phase| activities| work packages?| scope)?$', re.I)
    need(all(not filler.fullmatch(s) for s in titles), 'generic_phase_filler')
    references = plan.get('celarAiCitationIds') or []
    need(isinstance(references, list) and references and all(isinstance(x, int) and x > 0 for x in references), 'plan_citations_missing')
    available = set(references)
    hours = 0.0
    for row in leaves:
        need(len(str(row.get('description') or '').strip()) >= 30, 'task_description_missing')
        need(isinstance(row.get('detailedSteps'), list) and len(row['detailedSteps']) >= 2, 'task_steps_missing')
        need(all(isinstance(s, str) and len(s.strip()) >= 10 for s in row['detailedSteps']), 'task_steps_too_generic')
        need(bool(row.get('outputs')) and bool(row.get('acceptanceCriteria')), 'deliverable_or_acceptance_missing')
        cites = row.get('citationIds') or []
        need(isinstance(cites, list) and cites and all(isinstance(x, int) and x in available for x in cites), 'task_citations_invalid')
        effort = row.get('remainingEffortHours')
        need(type(effort) in (float, int) and math.isfinite(effort) and effort > 0, 'task_effort_invalid')
        hours += effort
    need(isinstance(schedule, dict) and schedule.get('valid') is True, 'schedule_not_valid')
    by_wbs = {r.get('wbsNumber'): r for r in schedule.get('tasks') or []}
    need(len(by_wbs) == len(schedule.get('tasks') or []), 'duplicate_schedule_rows')
    for row in leaves:
        dates = by_wbs.get(row['wbsNumber'])
        need(isinstance(dates, dict), 'scheduled_task_missing')
        try:
            start, end = date.fromisoformat(dates['startDate']), date.fromisoformat(dates['endDate'])
            need(start <= end, 'task_dates_reversed')
        except (KeyError, TypeError, ValueError):
            raise GateError('task_dates_invalid') from None
    need(type(schedule.get('plannedHours')) in (float, int) and math.isclose(schedule['plannedHours'], hours, abs_tol=0.01), 'schedule_effort_not_reconciled')
    # Source-specific semantic coverage and commercial estimate approval remain separate gates.
    return {'leafTasks': len(leaves), 'summaryTasks': len(rows) - len(leaves),
            'phaseTaskCounts': {p: sum(r['phase'] == p for r in leaves) for p in PHASES},
            'estimatedHours': round(hours, 2), 'citedTasks': len(leaves), 'projectMilestones': 0}


def receipt_checks(result: dict, workspace: dict, project: str = PROJECT) -> dict:
    need(workspace.get('project', {}).get('projectId') == project, 'readback_wrong_project')
    saved = result.get('workingDraft') or {}
    working = workspace.get('workingCopy') or {}
    need(saved.get('persisted') is True and uid(saved.get('rowVersion')), 'save_receipt_missing')
    need(type(saved.get('workingRevision')) is int and saved['workingRevision'] > 0, 'save_revision_invalid')
    need(saved.get('immutableVersionCreated') is False and saved.get('baselineCreated') is False, 'unexpected_immutable_publication')
    need(working.get('rowVersion') == saved['rowVersion'] and working.get('workingRevision') == saved['workingRevision'], 'readback_receipt_mismatch')
    need(working.get('plan') == result.get('plan'), 'readback_plan_mismatch')
    need((working.get('validation') or {}).get('valid') is True, 'readback_validation_failed')
    return working


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise GateError('redirect_refused')


class Client:
    def __init__(self):
        self.opener = build_opener(NoRedirect())
        self.token = ''
        self.start_posts = 0
        self.status_reads = 0

    def request(self, path: str, method: str = 'GET', data=None, *, timeout: float = 25, authenticated=True):
        need(path.startswith('/') and not path.startswith('//') and '#' not in path, 'request_path_invalid')
        headers = {'Accept': 'application/json', 'Cache-Control': 'no-cache'}
        if authenticated and self.token:
            headers['X-ProjectPulse-Session'] = self.token
        payload = None
        if data is not None:
            payload = json.dumps(data).encode('utf-8')
            headers['Content-Type'] = 'application/json'
        request_started = time.monotonic()
        try:
            with self.opener.open(Request(ORIGIN + path, data=payload, headers=headers, method=method), timeout=max(0.1, timeout)) as response:
                parts = []
                length = 0
                read_deadline = request_started + timeout
                while True:
                    need(time.monotonic() < read_deadline, 'response_body_deadline_exceeded')
                    chunk = response.read1(min(65536, MAX_BODY + 1 - length))
                    if not chunk:
                        break
                    parts.append(chunk)
                    length += len(chunk)
                    need(length <= MAX_BODY, 'response_size_exceeded')
                raw = b''.join(parts)
                if not raw:
                    return response.status, None
                try:
                    value = json.loads(raw)
                except (ValueError, UnicodeError):
                    raise GateError('response_not_json') from None
                return response.status, value
        except HTTPError as error:
            # Do not expose upstream diagnostics or potentially private response bodies.
            return error.code, None
        except (URLError, TimeoutError, OSError):
            raise GateError('network_read_failed') from None

    def get(self, path: str):
        code, value = self.request(path)
        need(code == 200 and isinstance(value, dict), 'authorized_get_failed_' + str(code))
        return value


async def browser_readback(session: dict, expected: dict, report: dict) -> None:
    from playwright.async_api import async_playwright
    plan = expected['plan']
    leaves = [x for x in plan['tasks'] if not x.get('isSummary')]
    dates = {x['wbsNumber']: x for x in expected['schedule']['tasks']}
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        context = await browser.new_context(viewport={'width': 1600, 'height': 1000}, ignore_https_errors=False)
        blocked = []
        page_errors = []
        # Safety filter only: successful responses always come from the real deployed API.
        # Never fulfill synthetic API responses or alter page fetch behavior.
        async def restrict(route):
            req = route.request
            parsed = urlparse(req.url)
            if parsed.netloc != urlparse(ORIGIN).netloc:
                if req.method not in ('GET', 'HEAD', 'OPTIONS'):
                    blocked.append('cross_origin_write'); await route.abort(); return
            if parsed.path.startswith('/api/project-flowhive') and req.method not in ('GET', 'HEAD', 'OPTIONS'):
                if parsed.path not in ('/api/project-flowhive/schedule/calculate', '/api/project-flowhive/planning/validate'):
                    blocked.append('unexpected_flowhive_mutation'); await route.abort(); return
            await route.continue_()
        await context.route('**/*', restrict)
        await context.add_init_script("""(() => {
            if (location.origin === ORIGIN) {
                localStorage.setItem('projectPulseAuthSession', JSON.stringify(SESSION));
                localStorage.removeItem('projectPulseViewAsUser');
            }
        })();""".replace('ORIGIN', json.dumps(ORIGIN)).replace('SESSION', json.dumps(session)))
        page = await context.new_page()
        page.on('pageerror', lambda _: page_errors.append('browser_page_error'))
        page.set_default_timeout(45000)
        try:
            for cycle in range(2):
                if cycle == 0:
                    await page.goto(ORIGIN + '/#project-flowhive', wait_until='domcontentloaded', timeout=45000)
                else:
                    await page.reload(wait_until='domcontentloaded', timeout=45000)
                center = page.locator('.project-flowhive-center[data-module="066"]')
                await center.wait_for(state='visible')
                await center.get_by_role('button', name='Planner', exact=True).click()
                await center.get_by_label('Canonical project', exact=True).select_option(PROJECT)
                await center.get_by_label('Plan name', exact=True).wait_for()
                await center.get_by_role('button', name='Load working copy', exact=True).click()
                await center.get_by_role('heading', name='AI Planner work breakdown', exact=True).wait_for()
                # Wait for actual named task fields from saved readback, without logging their content.
                await page.wait_for_function("""([names]) => {
                    const values = new Set(Array.from(document.querySelectorAll('.flowhive-work-row input')).map(x => x.value));
                    return names.every(x => values.has(x));
                }""", arg=[[x['name'] for x in leaves]])
                table = center.locator('.flowhive-work-row')
                need(await table.count() == len(leaves), 'browser_wbs_row_count_mismatch')
                for i, task in enumerate(leaves):
                    date_fields = table.nth(i).locator('input[type="date"]')
                    need(await date_fields.count() == 2, 'browser_task_date_fields_missing')
                    need(await date_fields.nth(0).input_value() == dates[task['wbsNumber']]['startDate'], 'browser_task_start_mismatch')
                    need(await date_fields.nth(1).input_value() == dates[task['wbsNumber']]['endDate'], 'browser_task_finish_mismatch')
                need(not await center.locator('.flowhive-milestone-list').count(), 'browser_unexpected_milestones')
                need(not blocked, 'browser_unexpected_write_blocked')
                need(not page_errors, 'browser_runtime_error')
            report['browser'] = {'realDeployedPage': True, 'reloadVerified': True,
                                 'wbsTasksVerified': len(leaves), 'generationPosts': 0,
                                 'unexpectedWritesBlocked': len(blocked), 'pageErrors': len(page_errors)}
        finally:
            await context.close()
            await browser.close()


def run(approval: dict, report: dict) -> None:
    need(os.environ.get('BASE') == ORIGIN, 'unapproved_public_origin')
    need(approval.get('sha') == os.environ.get('TARGET_RELEASE_COMMIT'), 'unapproved_release')
    need(approval.get('projectId') == PROJECT and approval.get('projectManagerLogin') == LOGIN, 'unapproved_project_or_pm')
    need(os.environ.get('PSA_RELEASE_AUTHORIZED') == 'true', 'main_admission_missing')
    password = os.environ.pop('TEST_LOGIN_PASSWORD', '')
    need(len(password) >= 12, 'test_login_secret_missing')
    client = Client()
    started = time.monotonic()
    base = '/api/project-flowhive/projects/' + PROJECT
    run_id = None
    complete = False
    try:
        # Health was verified with exact cloud image/source tags by the canonical controller.
        health = client.get('/health')
        need(health.get('status') == 'healthy', 'test_health_failed')
        code, _ = client.request(base + '/enterprise', authenticated=False)
        need(code in (401, 403), 'anonymous_project_access_not_denied')
        report['anonymousAccessDenied'] = True
        code, session = client.request('/api/auth/local/login', 'POST', {'username': LOGIN, 'password': password}, authenticated=False)
        password = ''
        need(code == 200 and isinstance(session, dict) and session.get('provider') == 'LOCAL' and session.get('mustChangePassword') is False,
             'pm_login_failed')
        client.token = session.get('sessionToken') or ''
        need(bool(client.token), 'pm_session_missing')
        workspace = client.get(base + '/enterprise')
        need(workspace.get('project', {}).get('projectId') == PROJECT, 'project_identity_mismatch')
        access = workspace.get('access') or {}
        need(access.get('isViewAs') is False and access.get('actualUserId') == access.get('effectiveUserId'), 'view_as_or_actor_mismatch')
        need(access.get('isProjectManagerOwner') is True and access.get('canEditPlanner') is True, 'assigned_pm_authority_missing')
        need(access.get('actualUserId') == workspace['project'].get('projectManagerUserId'), 'pm_project_ownership_mismatch')
        report['assignedPmVerified'] = True
        need(bool(workspace.get('sowEvidence')), 'existing_sow_missing')
        latest_code, latest = client.request(base + '/ai-planner/runs/latest')
        need(latest_code in (200, 409), 'latest_run_read_failed')
        if latest_code == 200:
            need(isinstance(latest, dict) and (not latest.get('runId') or latest.get('terminal') is True), 'another_planner_operation_active')
        # A 409 on this read is an obsolete generated-evidence projection. The
        # new POST still atomically refuses any active run or unauthorized source.
        report['priorEvidenceProjectionStale'] = latest_code == 409
        working = workspace.get('workingCopy') or {}
        before = working.get('plan')
        if before:
            need(before.get('projectId') == PROJECT, 'stored_plan_wrong_project')
            need(not before.get('milestones'), 'existing_milestones_require_pm_review_before_generation')
            need(not before.get('assignments') or all(x.get('resourceUserId') is None for x in before['assignments']), 'assigned_plan_requires_review_before_replacement')
            need(uid(working.get('rowVersion')), 'starting_revision_missing')
        body = {'plan': before, 'requestedOutcome': 'Create a detailed SOW-grounded work breakdown in Plan, Design, Implement, Validate, and Release with task-specific steps, estimates, dependencies, risks, assumptions, acceptance, operational handoff, and closeout. Do not automatically create project milestones.',
                'detailLevel': 'comprehensive', 'retryTerminalDocumentProcessing': False,
                'expectedWorkingRowVersion': working.get('rowVersion'), 'hasWorkingCopyExpectation': True}
        # Exactly one generation POST: unknown transport outcome is investigated, never reposted.
        generation_start = time.monotonic()
        client.start_posts += 1
        code, result = client.request(base + '/ai-planner/runs', 'POST', body, timeout=30)
        need(code in (200, 202) and isinstance(result, dict), 'generation_start_failed_' + str(code))
        run_id = result.get('runId')
        need(uid(run_id) and result.get('projectId') == PROJECT, 'run_identity_missing')
        report['runId'] = run_id
        report['acknowledgementSeconds'] = round(time.monotonic() - generation_start, 3)
        deadline = generation_start + 330  # Backend failure ceiling is 300s; bounded observation grace only.
        stage_started = time.monotonic()
        stage = str(result.get('phase') or '')
        report['stages'] = []
        failures = 0
        while True:
            need(result.get('runId') == run_id and result.get('projectId') == PROJECT, 'status_identity_changed')
            need(result.get('executionContract') == CONTRACT, 'bounded_execution_not_deployed')
            need(type(result.get('attemptCount')) is int and 0 <= result['attemptCount'] <= 2, 'orchestration_budget_exceeded')
            need(result.get('maximumAttempts') == 2, 'orchestration_budget_changed')
            need((iso(result['deadlineAt']) - iso(result['createdAt'])).total_seconds() <= 301, 'backend_deadline_not_bounded')
            new_stage = str(result.get('phase') or '')
            if new_stage != stage:
                report['stages'].append({'stage': re.sub('[^a-z0-9_]', '', stage.lower())[:80], 'observedSeconds': round(time.monotonic() - stage_started, 3)})
                stage, stage_started = new_stage, time.monotonic()
            if result.get('terminal') is True:
                complete = True
                break
            need(time.monotonic() < deadline, 'status_observation_deadline_exceeded')
            time.sleep(min(2, max(0, deadline - time.monotonic())))
            client.status_reads += 1
            try:
                code, next_result = client.request(base + '/ai-planner/runs/' + run_id,
                                                   timeout=min(25, max(0.1, deadline - time.monotonic())))
                if code in (408, 429, 500, 502, 503, 504):
                    failures += 1
                    need(failures < 3, 'status_transient_budget_exceeded')
                    continue
                need(code == 200 and isinstance(next_result, dict), 'status_read_failed_' + str(code))
                result, failures = next_result, 0
            except GateError as error:
                if str(error) != 'network_read_failed':
                    raise
                failures += 1
                need(failures < 3, 'status_network_budget_exceeded')
        report['generationSeconds'] = round(time.monotonic() - generation_start, 3)
        report['terminalStatus'] = re.sub('[^a-z_]', '', str(result.get('status', '')))[:80]
        report['orchestrationAttempts'] = result['attemptCount']
        need(result.get('status') in TERMINAL_OK, 'planner_terminal_failure')
        need((result.get('planningEvidence') or {}).get('sourceGrounded') is True, 'grounded_plan_not_proven')
        generated = result.get('plan') or {}
        report['planQualityChecks'] = plan_checks(generated, result.get('schedule'))
        provider = str(generated.get('celarAiProviderCode') or '')
        report['providerCode'] = re.sub('[^A-Za-z0-9_.:-]', '', provider)[:100]
        need(provider in {'celar_ai', 'deepseek_v4'}, 'configured_private_inference_not_proven')
        correlation = str(generated.get('celarAiCorrelationId') or '')
        need(bool(correlation), 'ai_correlation_missing')
        report['aiCorrelationFingerprint'] = hashlib.sha256(correlation.encode()).hexdigest()
        # Model and per-provider transport attempts are not present in this API contract.
        # Do not invent them from config or confuse orchestrationAttempts with model calls.
        report['modelInvocationTelemetry'] = {'status': 'requires_correlated_provider_telemetry',
                                              'modelName': None, 'actualInferenceRequests': None}
        report['semanticSowAcceptance'] = 'requires_scope_exclusions_and_estimate_review'
        after = client.get(base + '/enterprise')
        saved = receipt_checks(result, after)
        plan_checks(saved['plan'], saved.get('schedule'))
        if before:
            need(generated['projectStartDate'] == before['projectStartDate'] and generated['projectEndDate'] == before['projectEndDate'], 'requested_dates_changed')
            need(saved['rowVersion'] != working['rowVersion'], 'saved_revision_not_advanced')
        report['savedReceipt'] = {'rowVersion': saved['rowVersion'], 'workingRevision': saved['workingRevision']}
        need(after.get('customerShares') == workspace.get('customerShares'), 'customer_shares_changed')
        need(after.get('statusReports') == workspace.get('statusReports'), 'status_publication_changed')
        report['sowVersionFingerprint'] = hashlib.sha256(generated['sowVersion'].encode()).hexdigest()
        asyncio.run(browser_readback(session, saved, report))
        last = client.get(base + '/enterprise')
        receipt_checks(result, last)
        report['status'] = 'passed'
        report['functionalLifecycleVerified'] = True
        report['fullLiveAiAcceptance'] = False
    finally:
        password = ''
        if run_id and not complete and client.token:
            try:
                code, _ = client.request(base + '/ai-planner/runs/' + run_id + '/cancel', 'POST', {})
                report['cancellationRequested'] = code in (200, 202)
            except GateError:
                report['cancellationRequested'] = False
        if client.token:
            try:
                client.request('/api/auth/session/logout', 'POST', {})
            except GateError:
                pass
            client.token = ''
        report['generationStartPosts'] = client.start_posts
        report['statusReads'] = client.status_reads
        report['elapsedSeconds'] = round(time.monotonic() - started, 3)


def main() -> int:
    report = {'contract': 'flowhive-psa-live-functional-uat-v1', 'status': 'failed',
              'functionalLifecycleVerified': False, 'fullLiveAiAcceptance': False,
              'sourceCommit': os.environ.get('TARGET_RELEASE_COMMIT', ''),
              'environment': 'test', 'productionMutation': False, 'mockedApiOrModel': False,
              'customerPublicationRequested': False, 'canonicalTaskAdoptionRequested': False}
    directory = Path(os.environ.get('EVIDENCE_DIR', '/tmp/flowhive-psa-evidence'))
    directory.mkdir(parents=True, exist_ok=True)
    try:
        approval = json.loads((Path(__file__).resolve().parents[2] / '.github/flowhive-psa-protected-test-candidate.json').read_text())
        run(approval, report)
    except GateError as error:
        report['diagnosticCode'] = str(error)
    except Exception as error:
        # Exception type only; browser/network messages can contain customer values or tokens.
        report['diagnosticCode'] = 'unexpected_' + type(error).__name__
    finally:
        (directory / 'flowhive-psa-live-uat.json').write_text(json.dumps(report, indent=2) + '\n')
    print('FLOWHIVE_PSA_LIVE_FUNCTIONAL_UAT=' + ('PASS' if report['status'] == 'passed' else 'FAIL'))
    print('FLOWHIVE_PSA_FULL_LIVE_AI_ACCEPTANCE=NOT_YET_ESTABLISHED')
    return 0 if report['status'] == 'passed' else 1

if __name__ == '__main__':
    sys.exit(main())
