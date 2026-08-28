#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
python3 - \
  "$ROOT/src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs" \
  "$ROOT/src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs" \
  "$ROOT/.github/workflows/projectpulse-deploy-test.yml" <<'PY'
from pathlib import Path
import sys

repository_path = Path(sys.argv[1])
runtime_path = Path(sys.argv[2])
workflow_path = Path(sys.argv[3])

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

workflow = workflow_path.read_text(encoding='utf-8')
rollback_condition = "if: ${{ failure() && (steps.deploy_api.outputs.started == 'true' || steps.deploy_web.outputs.started == 'true') }}"
if rollback_condition not in workflow:
    raise SystemExit('ASSERTION_FAILED protected_test_failed_uat_rollback_condition_missing')
if "steps.uat.outputs.deployment_health_verified != 'true'" in workflow:
    raise SystemExit('ASSERTION_FAILED protected_test_rollback_still_suppressed_after_healthy_deployment')

print('ASSERTION_PASSED private_runtime_list_jobs_shape_matches_reader=true')
print('ASSERTION_PASSED private_extraction_diagnostics_preserved=true')
print('ASSERTION_PASSED protected_test_failed_uat_rolls_back=true')
PY
