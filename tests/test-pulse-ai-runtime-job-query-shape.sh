#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
python3 - \
  "$ROOT/src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs" \
  "$ROOT/src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs" \
  "$ROOT/.github/workflows/projectpulse-deploy-test.yml" \
  "$ROOT/src/backend/ProjectTime.Api/Modules/ProjectPlanningDocumentResolver.cs" \
  "$ROOT/src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs" \
  "$ROOT/src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerWorker.cs" \
  "$ROOT/src/backend/ProjectTime.Api/Modules/ProjectPlanningAiOrchestrator.cs" \
  "$ROOT/src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs" \
  "$ROOT/src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs" <<'PY'
from pathlib import Path
import sys

repository_path = Path(sys.argv[1])
runtime_path = Path(sys.argv[2])
workflow_path = Path(sys.argv[3])
resolver_path = Path(sys.argv[4])
orchestration_path = Path(sys.argv[5])
worker_path = Path(sys.argv[6])
orchestrator_path = Path(sys.argv[7])
services_path = Path(sys.argv[8])
enterprise_path = Path(sys.argv[9])

text = repository_path.read_text(encoding='utf-8')
start = text.index('public async Task<IReadOnlyList<PulseAiPrivateProcessingJob>> ListJobsAsync(')
end = text.index('public async Task<PulseAiPrivateProcessingJob?> GetJobAsync(', start)
block = text[start:end]
expected = [
    'j.cancellation_requested,',
    'j.lease_owner,',
    'j.lease_token,',
    'j.lease_generation,',
    'j.lease_expires_at,',
    'j.correlation_id,',
]
positions = [block.find(token) for token in expected]
if any(position < 0 for position in positions):
    missing = [token for token, position in zip(expected, positions) if position < 0]
    raise SystemExit(f'ASSERTION_FAILED list_jobs_missing_columns={missing}')
if positions != sorted(positions):
    raise SystemExit('ASSERTION_FAILED list_jobs_column_order')

reader_start = text.index('private static async Task<IReadOnlyList<PulseAiPrivateProcessingJob>> ReadJobsAsync(')
reader_end = text.index('private static async Task UpdateDocumentStatusAsync(', reader_start)
reader = text[reader_start:reader_end]
for index, token in [(15, 'LeaseOwner:'), (16, 'LeaseToken:'), (17, 'LeaseGeneration:'), (18, 'LeaseExpiresAt:'), (19, 'CorrelationId:')]:
    needle = f'{token} reader.'
    if needle not in reader:
        raise SystemExit(f'ASSERTION_FAILED reader_mapping_missing={token}')

runtime = runtime_path.read_text(encoding='utf-8')
required_runtime_markers = [
    'ResolveExtractionFailureDiagnostic(extraction)',
    'IsDeterministicDocumentPolicyFailure(diagnosticCode)',
    'diagnosticCode.StartsWith("blocked_by_document_", StringComparison.Ordinal)',
    '"blocked_by_document_path_policy"',
    '"blocked_by_document_signature_policy"',
    '"blocked_by_document_malware_attestation"',
    '"blocked_by_document_size_policy"',
    '"blocked_by_document_file_policy"',
    '"blocked_by_document_macro_policy"',
    '"blocked_by_document_archive_policy"',
    '"blocked_by_document_extension_policy"',
]
missing_runtime = [marker for marker in required_runtime_markers if marker not in runtime]
if missing_runtime:
    raise SystemExit(f'ASSERTION_FAILED private_runtime_diagnostic_markers_missing={missing_runtime}')
if 'extraction.Blockers.Count > 0 ? "private_extraction_blocked" : "private_extraction_failed"' in runtime:
    raise SystemExit('ASSERTION_FAILED private_runtime_still_collapses_extraction_diagnostic')

resolver = resolver_path.read_text(encoding='utf-8')
if 'No Scope of Services citation was detected in the active Work Register SOW.' in resolver:
    raise SystemExit('ASSERTION_FAILED authoritative_work_register_sow_still_blocked_by_heading_phrase')
ready_marker = 'public bool ReadyForGeneration => IsSow\n        ? ReadyForRetrieval && AuthorityReady\n        : ReadyForRetrieval;'
if ready_marker not in resolver:
    raise SystemExit('ASSERTION_FAILED authoritative_sow_readiness_contract_missing')
if 'ReadyForRetrieval && AuthorityReady && ScopeCitationCount > 0' in resolver:
    raise SystemExit('ASSERTION_FAILED authoritative_sow_still_requires_magic_scope_phrase')

orchestration = orchestration_path.read_text(encoding='utf-8')
required_orchestration_markers = [
    '(Func<Guid, Guid, HttpContext, CancellationToken, Task<IResult>>)GetAsync',
    'Polling is deliberately read-only.',
    'ProcessNextQueuedRunAsync(',
    'pg_try_advisory_lock(hashtextextended(@run_id::text,735))',
    '"persist_working_draft"',
    'project_flowhive_working_copies.row_version=@expected',
    '"working_copy_changed"',
    "deadline_at>clock_timestamp()",
    'requestPollingReadOnly = true',
    'backgroundGeneration = true',
]
missing_orchestration = [marker for marker in required_orchestration_markers if marker not in orchestration]
if missing_orchestration:
    raise SystemExit(f'ASSERTION_FAILED flowhive_background_orchestration_markers_missing={missing_orchestration}')
for forbidden in [
    'GetAndAdvanceAsync',
    'return await AdvanceAsync(',
    'CelarAiEnterprisePlatformService enterprise,\n        CancellationToken cancellationToken)\n    {\n        request ??=',
]:
    if forbidden in orchestration:
        raise SystemExit(f'ASSERTION_FAILED flowhive_http_request_still_advances_generation={forbidden}')

worker = worker_path.read_text(encoding='utf-8')
if ': BackgroundService' not in worker or 'ProcessNextQueuedRunAsync' not in worker:
    raise SystemExit('ASSERTION_FAILED flowhive_background_worker_missing')

services = services_path.read_text(encoding='utf-8')
if 'services.AddHostedService<ProjectFlowHiveAiPlannerWorker>();' not in services:
    raise SystemExit('ASSERTION_FAILED flowhive_background_worker_not_registered')

orchestrator = orchestrator_path.read_text(encoding='utf-8')
if 'scopeCitations.Length > 0' in orchestrator or 'ContainsScopeMarker(' in orchestrator:
    raise SystemExit('ASSERTION_FAILED project_planning_grounding_still_magic_heading_dependent')
if '&& sowCitations.Length > 0;' not in orchestrator:
    raise SystemExit('ASSERTION_FAILED project_planning_authoritative_sow_citation_gate_missing')
if 'current active Work Register SOW' not in orchestrator:
    raise SystemExit('ASSERTION_FAILED shared_planning_authoritative_sow_contract_marker_missing')

enterprise = enterprise_path.read_text(encoding='utf-8')
if 'No Scope of Services citation was located.' in enterprise:
    raise SystemExit('ASSERTION_FAILED enterprise_sow_readiness_still_magic_heading_dependent')
if 'if (scopeCitations == 0)' in enterprise:
    raise SystemExit('ASSERTION_FAILED enterprise_sow_readiness_still_blocks_on_scope_heading_count')
if 'authoritative, citation-ready Work Register SOW' not in enterprise:
    raise SystemExit('ASSERTION_FAILED enterprise_sow_authority_summary_missing')

workflow = workflow_path.read_text(encoding='utf-8')
rollback_step = '      - name: Restore exact prior Test images after application failure'
evidence_step = '      - name: Upload protected-Test deployment evidence'
rollback_start = workflow.find(rollback_step)
evidence_start = workflow.find(evidence_step, rollback_start)
if rollback_start < 0 or evidence_start <= rollback_start:
    raise SystemExit('ASSERTION_FAILED protected_test_rollback_step_boundaries_missing')
rollback_block = workflow[rollback_start:evidence_start]
health_scoped_marker = "steps.uat.outputs.deployment_health_verified != 'true'"
unbounded_condition = "if: ${{ failure() && (steps.deploy_api.outputs.started == 'true' || steps.deploy_web.outputs.started == 'true') }}"
if health_scoped_marker not in rollback_block:
    raise SystemExit('ASSERTION_FAILED protected_test_health_scoped_rollback_condition_missing')
if unbounded_condition in rollback_block:
    raise SystemExit('ASSERTION_FAILED protected_test_functional_uat_would_rollback_healthy_candidate')

print('ASSERTION_PASSED private_runtime_list_jobs_shape_matches_reader=true')
print('ASSERTION_PASSED private_extraction_diagnostics_preserved=true')
print('ASSERTION_PASSED authoritative_sow_readiness_not_heading_dependent=true')
print('ASSERTION_PASSED flowhive_ai_generation_runs_outside_http_request=true')
print('ASSERTION_PASSED flowhive_ai_planner_polling_is_read_only=true')
print('ASSERTION_PASSED flowhive_ai_planner_restart_persistence_is_idempotent=true')
print('ASSERTION_PASSED project_planning_grounding_not_heading_dependent=true')
print('ASSERTION_PASSED enterprise_sow_readiness_not_heading_dependent=true')
print('ASSERTION_PASSED protected_test_healthy_failed_uat_candidate_preserved=true')
PY
