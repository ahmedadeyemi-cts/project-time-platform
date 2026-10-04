"""Finite, content-free projection of locally recorded functional-UAT evidence.

This module cannot authorize, execute or pass acceptance. It performs no network
operations and reads no workflow logs. Output values are fixed enums/booleans;
raw answers, request paths, identities, tokens, exception text and source hashes
are deliberately absent. A missing artifact is not a failed test or a passed one.
"""
import json
import os
import re
import stat
from pathlib import Path

MAX_BYTES = 4 * 1024 * 1024
PROVIDERS = ('deepseek_v4', 'celar_ai', 'claude', 'openai', 'gemini', 'copilot', 'local')
ACCEPTED_MODEL_PROVIDERS = frozenset(PROVIDERS[:4])
# These are filenames emitted by the existing protected workflow, never input paths.
ARTIFACTS = (
    ('coordinator_login', 'coordinator-login-redacted.json'),
    ('engineer_login', 'engineer-login-redacted.json'),
    ('project_manager_login', 'project-manager-login-redacted.json'),
    ('public_fact', 'module064-chat-routing-smoke.json'),
    ('model_routing', 'module064-provider-routing-smoke.json'),
    ('project_management', 'project-management-summary.json'),
    ('project_intake', 'project-intake-overview.json'),
    ('financial_portfolio', 'project-financial-portfolio.json'),
    ('cost_alerts', 'cost-alerts.json'),
    ('customers', 'customer-directory.json'),
    ('project_manager_modules', 'project-manager-module-availability.json'),
    ('project_manager_work_register', 'project-manager-work-register.json'),
    ('project_manager_approval_access', 'project-manager-approval-access.json'),
    ('opportunities', 'opportunities.json'),
    ('engineer_logout', 'engineer-logout.json'),
    ('flowhive_portfolio', 'flowhive-portfolio.json'),
    ('flowhive_workspace', 'flowhive-enterprise.json'),
    ('flowhive_plan', 'flowhive-ai-planner.json'),
    ('flowhive_planner_reconciliation', 'flowhive-planner-reconciliation.json'),
    ('flowhive_working_copy', 'flowhive-enterprise-after.json'),
    ('forge_plan', 'project-forge-ai-draft.json'),
    ('forge_workspace', 'project-forge-review-workspace.json'),
    ('authentication_audit', 'audit-authentication.json'),
    ('current_public_fact', 'celar-president.json'),
    ('company_public_fact', 'celar-us-signal-ceo.json'),
    ('authorized_project_count', 'celar-kevin-project-count.json'),
)
# Do not echo an arbitrary server message or reason. Recognize exact known codes.
REASONS = ('provider_deadline_exceeded', 'provider_circuit_open',
           'private_runtime_unavailable', 'private_model_unavailable',
           'private_model_http_504_private_runtime_timeout')
OUTCOMES = ('used', 'skipped', 'failed', 'refused', 'unavailable', 'timeout')
HTTP_STAGES = {
    'Project Management summary': 'project_management',
    'Project Intake overview': 'project_intake',
    'Project financial portfolio': 'financial_portfolio',
    'Cost alert workflow': 'cost_alerts',
    'Customer directory': 'customers',
    'Project Manager module availability': 'project_manager_modules',
    'Project Manager Work Register overview': 'project_manager_work_register',
    'Project Manager Approval access': 'project_manager_approval_access',
    'Opportunity directory': 'opportunities',
    'Project Manager FlowHive portfolio': 'flowhive_portfolio',
    'FlowHive preserved working copy': 'flowhive_working_copy',
    'Project Forge persisted review workspace': 'forge_workspace',
    'Audit history': 'authentication_audit',
}
HTTP_CODES = frozenset(('000', '200', '201', '202', '204', '400', '401', '403',
                       '404', '408', '409', '422', '423', '429', '500', '502', '503', '504'))
PLANNER_STATUSES = frozenset(('queued','processing','generating','completed',
    'completed_with_schedule_overrun','needs_attention','failed'))
PLANNER_PHASES = frozenset(('queued','private_document_processing','authority_index_and_citations',
    'source_evidence_ready','ai_route_retry','safety_refusal','evidence_review','persist_working_draft',
    'resolve_authoritative_sow','deadline_exceeded','working_draft_ready','candidate_review_required',
    'phase_semantics_invalid','authority_changed','working_copy_changed','project_archived','cancelled'))
PLANNER_DIAGNOSTICS = frozenset(('pm_login_email_missing','pm_login_secret_missing','pm_login_failed',
    'pm_login_contract_failed','pm_session_missing','flowhive_portfolio_read_failed','pm_portfolio_scope_invalid',
    'pm_portfolio_actor_mismatch','pm_project_not_in_authorized_portfolio','flowhive_workspace_read_failed',
    'flowhive_project_identity_mismatch','flowhive_actor_mismatch','assigned_pm_authority_missing',
    'working_copy_identity_missing','prior_planner_status_read_failed','prior_planner_identity_mismatch',
    'prior_planner_status_pair_invalid','prior_planner_run_nonterminal','latest_planner_read_failed',
    'latest_planner_payload_invalid','another_planner_operation_active','planner_read_failed'))

def _count_bucket(value, *, one_label='one', many_label='multiple'):
    if type(value) is not int or value < 0:
        return 'unrecognized'
    if value == 0:
        return 'zero'
    if value == 1:
        return one_label
    return many_label

def planner_reconciliation_predicates(obj):
    working = _dict(obj.get('workingCopy'))
    prior = _dict(obj.get('priorPlanner'))
    latest = _dict(obj.get('latestPlanner'))
    status = prior.get('status')
    phase = prior.get('phase')
    diagnostic = obj.get('diagnosticCode')
    latest_http = latest.get('httpStatus')
    return {
        'reconciliation_status': 'passed' if obj.get('status') == 'passed' else 'blocked',
        'assigned_pm_verified': obj.get('assignedPmVerified') is True,
        'working_copy_present': isinstance(working.get('rowVersion'), str) and len(working.get('rowVersion')) == 36,
        'working_copy_task_bucket': _count_bucket(working.get('taskCount'), many_label='multiple'),
        'sow_evidence_present': working.get('sowEvidencePresent') is True,
        'approved_sow_scope_ready': working.get('approvedSowScopeReady') is True,
        'ready_sow_bucket': _count_bucket(working.get('readySowCount'), many_label='multiple'),
        'prior_planner_status': status if isinstance(status, str) and status in PLANNER_STATUSES else 'unrecognized',
        'prior_planner_phase': phase if isinstance(phase, str) and phase in PLANNER_PHASES else 'unrecognized',
        'prior_planner_terminal': prior.get('terminal') is True,
        'prior_candidate_available': prior.get('candidateAvailable') is True,
        'prior_working_draft_persisted': prior.get('workingDraftPersisted') is True,
        'latest_planner_http_status': str(latest_http) if type(latest_http) is int and latest_http in (200,409) else 'unrecognized',
        'latest_planner_terminal': latest.get('terminal') is True if 'terminal' in latest else None,
        'diagnostic_code': diagnostic if isinstance(diagnostic, str) and diagnostic in PLANNER_DIAGNOSTICS else 'none',
    }


def _unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('duplicate_key')
        result[key] = value
    return result


def _constant(_):
    raise ValueError('nonfinite_number')


def _check_depth(data):
    # Bound nesting before the JSON parser allocates deeply nested containers.
    # Braces inside escaped JSON strings are content, not structural depth.
    depth = 0
    quoted = False
    escaped = False
    for char in data:
        if quoted:
            if escaped:
                escaped = False
            elif char == 92:
                escaped = True
            elif char == 34:
                quoted = False
        elif char == 34:
            quoted = True
        elif char in (91, 123):
            depth += 1
            if depth > 64:
                raise ValueError('json_depth')
        elif char in (93, 125):
            depth -= 1


def _load(data):
    _check_depth(data)
    return json.loads(data, object_pairs_hook=_unique, parse_constant=_constant)


def _read(directory_fd, name):
    # Final-component no-follow plus directory-relative open prevents path swaps.
    try:
        fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=directory_fd)
    except FileNotFoundError:
        return 'not_recorded', None
    except OSError:
        raise ValueError('Unsafe diagnostic artifact') from None
    try:
        info = os.fstat(fd)
        if not stat.S_ISREG(info.st_mode):
            raise ValueError('Unsafe diagnostic artifact')
        if info.st_size > MAX_BYTES:
            return 'over_budget', None
        chunks = []
        remaining = MAX_BYTES + 1
        while remaining:
            block = os.read(fd, min(65536, remaining))
            if not block:
                break
            chunks.append(block)
            remaining -= len(block)
        data = b''.join(chunks)
        if len(data) > MAX_BYTES:
            return 'over_budget', None
        return 'recorded', data
    finally:
        os.close(fd)


def _object(data):
    try:
        obj = _load(data)
        return ('recorded', obj) if isinstance(obj, dict) else ('invalid_schema', None)
    except (ValueError, UnicodeError, RecursionError):
        return 'invalid_json', None


def _dict(value):
    return value if isinstance(value, dict) else {}


def _items(value, limit=64):
    return value if isinstance(value, list) and len(value) <= limit else []


def _answer_text(obj):
    answer = _dict(_dict(obj.get('result')).get('answer'))
    direct = answer.get('directConclusion')
    details = answer.get('detailedAnalysis', [])
    if not isinstance(direct, str) or not isinstance(details, list) or len(details) > 128:
        return None
    if not all(isinstance(value, str) for value in details):
        return None
    return '\n'.join([direct, *details])


def public_fact_predicates(obj):
    result = _dict(obj.get('result'))
    direct = _dict(result.get('answer')).get('directConclusion')
    assessment = _dict(_dict(obj.get('reliability')).get('assessment'))
    sources = _items(result.get('sources'), 256)
    return {
        'verified_state_count_present': bool(isinstance(direct, str) and re.search(r'\b50\b|\bfifty\b', direct, re.I)),
        'reliability_assessment_passed': assessment.get('passed') is True,
        'official_source_success_recorded': any(isinstance(x, dict) and x.get('sourceCode') == 'census_state_codes'
            and type(x.get('statusCode')) is int and x['statusCode'] == 200 for x in sources),
    }


def model_predicates(obj):
    result = _dict(obj.get('result'))
    text = _answer_text(obj)
    provider = result.get('modelProvider')
    decisions = _items(result.get('targetDecisions'))
    order = _items(_dict(obj.get('providerConfiguration')).get('targets'))
    positions = []
    valid_order = bool(order) and all(isinstance(x, str) for x in order)
    if valid_order:
        for decision in decisions:
            target = decision.get('target') if isinstance(decision, dict) else None
            if not isinstance(target, str) or target not in order:
                valid_order = False
                break
            positions.append(order.index(target))
    return {
        'refraction_explanation_present': None if text is None else bool(re.search('refract', text, re.I)),
        'reflection_explanation_present': None if text is None else bool(re.search('reflect', text, re.I)),
        'provider_permitted_by_unchanged_acceptance': isinstance(provider, str) and provider in ACCEPTED_MODEL_PROVIDERS,
        'used_provider_decision_recorded': any(isinstance(x, dict) and x.get('outcome') == 'used' for x in decisions),
        'saved_provider_order_preserved': bool(valid_order and positions == sorted(positions)),
    }


def _provider_evidence(obj):
    result = _dict(obj.get('result'))
    provider = result.get('modelProvider')
    known = provider if isinstance(provider, str) and provider in PROVIDERS else 'unrecognized'
    decisions = _items(result.get('targetDecisions'))
    rows = []
    for target in PROVIDERS:
        matching = [x for x in decisions if isinstance(x, dict) and x.get('target') == target]
        if not matching:
            continue
        rows.append({'target': target,
                     'outcomes': [code for code in OUTCOMES if any(x.get('outcome') == code for x in matching)],
                     'reasonCodes': [code for code in REASONS if any(x.get('reasonCode') == code for x in matching)]})
    return {'selectedProvider': known, 'recordedDecisions': rows}


def _http_projection(data):
    # jq -n emits multiline objects. Preserve all existing privacy constraints
    # while decoding that bounded, whitespace-separated JSON object stream.
    if len(data) > MAX_BYTES:
        return {'state': 'over_budget', 'observations': []}
    observations = []
    try:
        _check_depth(data)
        text = data.decode('utf-8')
        decoder = json.JSONDecoder(object_pairs_hook=_unique, parse_constant=_constant)
        position, count = 0, 0
        while True:
            while position < len(text) and text[position] in ' \t\r\n':
                position += 1
            if position == len(text):
                break
            if count >= 512:
                return {'state': 'over_budget', 'observations': []}
            obj, position = decoder.raw_decode(text, position)
            count += 1
            if not isinstance(obj, dict) or (position < len(text) and text[position] not in ' \t\r\n'):
                raise ValueError('invalid_record')
            name = obj.get('name')
            if isinstance(name, str) and name in HTTP_STAGES:
                status = obj.get('httpStatus')
                observations.append({'stage': HTTP_STAGES[name],
                    'httpStatus': status if isinstance(status, str) and status in HTTP_CODES else 'unrecognized',
                    'transportSucceeded': type(obj.get('curlExit')) is int and obj['curlExit'] == 0})
    except (ValueError, UnicodeError, RecursionError):
        return {'state': 'invalid_json', 'observations': []}
    return {'state': 'recorded', 'observations': observations}


def project(source: Path):
    try:
        directory_fd = os.open(source, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    except OSError:
        raise ValueError('Unsafe diagnostic directory') from None
    stages = []
    predicates = {}
    provider_evidence = None
    try:
        for stage, filename in ARTIFACTS:
            state, data = _read(directory_fd, filename)
            obj = None
            if state == 'recorded':
                state, obj = _object(data)
            if state == 'not_recorded':
                continue
            stages.append({'stage': stage, 'artifactState': state})
            if obj is None:
                continue
            if stage == 'public_fact':
                predicates[stage] = public_fact_predicates(obj)
            elif stage == 'model_routing':
                predicates[stage] = model_predicates(obj)
                provider_evidence = _provider_evidence(obj)
            elif stage == 'flowhive_planner_reconciliation':
                predicates[stage] = planner_reconciliation_predicates(obj)
        http_state, http_data = _read(directory_fd, 'uat-http-diagnostics.ndjson')
        http = _http_projection(http_data) if http_state == 'recorded' else {'state': http_state, 'observations': []}
    finally:
        os.close(directory_fd)
    if not stages and http['state'] == 'not_recorded':
        return None
    result = {'schema': 1, 'kind': 'diagnostic_only', 'artifactObservations': stages,
              'recordedPredicates': predicates, 'httpEvidence': http,
              'deadlineVerdict': 'not_recorded', 'acceptanceVerdict': 'not_inferred',
              'artifactPresenceIsNotAcceptance': True, 'rawContentPublished': False}
    if provider_evidence is not None:
        result['providerEvidence'] = provider_evidence
    return result
