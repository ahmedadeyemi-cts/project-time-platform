#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
python3 - "$ROOT/src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
text = path.read_text(encoding='utf-8')
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
print('ASSERTION_PASSED private_runtime_list_jobs_shape_matches_reader=true')
PY
