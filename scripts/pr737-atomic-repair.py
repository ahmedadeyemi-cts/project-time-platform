from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def write(path: str, content: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(content, encoding="utf-8")
    print(f"WROTE {path}")


def replace_once(path: str, old: str, new: str) -> None:
    target = ROOT / path
    text = target.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly one repair anchor, found {count}")
    target.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"PATCHED {path}")


def replace_all_required(path: str, old: str, new: str) -> None:
    target = ROOT / path
    text = target.read_text(encoding="utf-8")
    count = text.count(old)
    if count < 1:
        raise SystemExit(f"{path}: expected repair token {old!r}")
    target.write_text(text.replace(old, new), encoding="utf-8")
    print(f"PATCHED {path} replacements={count}")


migration = r'''-- ProjectPulse 097 — identity-safe private document admission.
--
-- Migration 057 installed an automatic project-document queue trigger that
-- populated requested_by_user_id but left actual_user_id and effective_user_id
-- NULL. The private worker correctly rejects such work as
-- authorization_identity_missing. Current FlowHive/Forge admission is performed
-- by authenticated project-scoped application code, while background admission
-- uses the explicitly configured document service principal. The legacy trigger
-- is therefore retired rather than weakening the worker authorization boundary.

BEGIN;

DO $projectpulse097_prerequisites$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '057_module_001_multi_timer_document_grounded_ai'
    ) THEN
        RAISE EXCEPTION 'Migration 097 requires migration 057 first.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '096_project_planning_document_authority'
    ) THEN
        RAISE EXCEPTION 'Migration 097 requires migration 096 first.';
    END IF;
END;
$projectpulse097_prerequisites$;

-- Remove the obsolete database-side queue authority. Do not recreate this
-- trigger in rollback: doing so would reintroduce identity-less private work.
DROP TRIGGER IF EXISTS trg_module001_057_queue_project_ai_document_insert
    ON project_intake_documents;
DROP TRIGGER IF EXISTS trg_module001_057_queue_project_ai_document_update
    ON project_intake_documents;
DROP FUNCTION IF EXISTS module001_057_queue_project_ai_document();

-- Active legacy jobs can block the authenticated recovery path through the
-- active-job uniqueness guard. Terminalize only those identity-less active rows.
-- Already-terminal historical rows remain untouched as audit evidence.
WITH retired AS (
    UPDATE pulse_ai_document_processing_jobs
       SET job_status = 'failed',
           completed_at = COALESCE(completed_at, NOW()),
           cancellation_requested = FALSE,
           lease_owner = '',
           lease_token = NULL,
           lease_heartbeat_at = NULL,
           lease_expires_at = NULL,
           diagnostic_code = 'legacy_identityless_queue_retired',
           diagnostic_message = 'Legacy Module 001 identity-less automatic queueing was retired; authenticated or service-principal admission is required.',
           updated_at = NOW()
     WHERE requested_purpose = 'project_ai_generation_grounding'
       AND actual_user_id IS NULL
       AND effective_user_id IS NULL
       AND job_status IN (
           'queued','scanning','extracting','awaiting_ocr','embedding',
           'indexing','retry_wait','cancel_requested'
       )
    RETURNING project_intake_document_id
)
UPDATE project_intake_documents AS document
   SET pulse_ai_processing_status = CASE
           WHEN document.pulse_ai_processing_status = 'ready' THEN 'ready'
           ELSE 'failed'
       END,
       pulse_ai_processing_error_code = CASE
           WHEN document.pulse_ai_processing_status = 'ready'
               THEN document.pulse_ai_processing_error_code
           ELSE 'legacy_identityless_queue_retired'
       END,
       pulse_ai_processing_updated_at = NOW()
 WHERE document.project_intake_document_id IN (
     SELECT project_intake_document_id FROM retired
 );

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '097_project_planning_identity_safe_admission',
    'Retire legacy identity-less project AI document queueing and require governed authenticated or service-principal admission',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
'''
write("database/migrations/097_project_planning_identity_safe_admission.sql", migration)

rollback = r'''-- ProjectPulse 097 fail-safe rollback.
--
-- The migration registration can be removed for release bookkeeping, but the
-- retired migration-057 queue trigger is intentionally not restored. Restoring
-- it would recreate private-document jobs without an authorization identity.

BEGIN;

DELETE FROM schema_migrations
WHERE migration_id = '097_project_planning_identity_safe_admission';

COMMIT;
'''
write("database/rollback/097_project_planning_identity_safe_admission_rollback.sql", rollback)

migration_test = r'''#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-identity-safe-097-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/097_project_planning_identity_safe_admission.sql"
ROLLBACK="/workspace/database/rollback/097_project_planning_identity_safe_admission_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT

psql_exec() {
  docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
    psql -X -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
}
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$actual" == "$expected" ]] || {
    echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label=$actual"
}

for required in \
  "$ROOT/database/migrations/097_project_planning_identity_safe_admission.sql" \
  "$ROOT/database/rollback/097_project_planning_identity_safe_admission_rollback.sql"; do
  [[ -s "$required" ]] || { echo "ASSERTION_FAILED missing=$required" >&2; exit 1; }
done

docker run -d --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" \
  -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" \
  postgres:16-alpine >/dev/null

ready=false
for _ in $(seq 1 90); do
  if docker exec -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
      psql -Atqc 'SELECT 1;' -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1; then
    ready=true
    break
  fi
  sleep 1
done
[[ "$ready" == true ]] || { docker logs "$CONTAINER" >&2 || true; exit 1; }

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE schema_migrations (
  migration_id text PRIMARY KEY,
  description text NOT NULL,
  applied_at timestamptz NOT NULL DEFAULT now()
);
INSERT INTO schema_migrations(migration_id, description) VALUES
('057_module_001_multi_timer_document_grounded_ai','test prerequisite'),
('096_project_planning_document_authority','test prerequisite');

CREATE TABLE project_intake_documents (
  project_intake_document_id uuid PRIMARY KEY,
  pulse_ai_processing_status text NOT NULL DEFAULT 'not_requested',
  pulse_ai_processing_error_code text NOT NULL DEFAULT '',
  pulse_ai_processing_updated_at timestamptz
);
CREATE TABLE pulse_ai_document_processing_jobs (
  pulse_ai_document_processing_job_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  project_intake_document_id uuid NOT NULL REFERENCES project_intake_documents(project_intake_document_id),
  requested_purpose text NOT NULL,
  actual_user_id uuid,
  effective_user_id uuid,
  job_status text NOT NULL,
  completed_at timestamptz,
  cancellation_requested boolean NOT NULL DEFAULT false,
  lease_owner text NOT NULL DEFAULT '',
  lease_token uuid,
  lease_heartbeat_at timestamptz,
  lease_expires_at timestamptz,
  diagnostic_code text NOT NULL DEFAULT '',
  diagnostic_message text NOT NULL DEFAULT '',
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE OR REPLACE FUNCTION module001_057_queue_project_ai_document()
RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END; $$;
CREATE TRIGGER trg_module001_057_queue_project_ai_document_insert
AFTER INSERT ON project_intake_documents
FOR EACH ROW EXECUTE FUNCTION module001_057_queue_project_ai_document();
CREATE TRIGGER trg_module001_057_queue_project_ai_document_update
AFTER UPDATE ON project_intake_documents
FOR EACH ROW EXECUTE FUNCTION module001_057_queue_project_ai_document();

INSERT INTO project_intake_documents(project_intake_document_id, pulse_ai_processing_status)
VALUES('97000000-0000-0000-0000-000000000001','queued');
INSERT INTO pulse_ai_document_processing_jobs(
  project_intake_document_id, requested_purpose, actual_user_id, effective_user_id,
  job_status, lease_owner, lease_token, lease_expires_at)
VALUES(
  '97000000-0000-0000-0000-000000000001', 'project_ai_generation_grounding',
  NULL, NULL, 'queued', 'legacy-worker', gen_random_uuid(), now()+interval '5 minutes');
SQL

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='097_project_planning_identity_safe_admission';")" migration_registered
assert_eq 0 "$(value "SELECT COUNT(*) FROM pg_trigger WHERE tgrelid='project_intake_documents'::regclass AND tgname IN ('trg_module001_057_queue_project_ai_document_insert','trg_module001_057_queue_project_ai_document_update') AND NOT tgisinternal;")" legacy_queue_triggers_retired
assert_eq 0 "$(value "SELECT COUNT(*) FROM pg_proc WHERE proname='module001_057_queue_project_ai_document';")" legacy_queue_function_retired
assert_eq failed "$(value "SELECT job_status FROM pulse_ai_document_processing_jobs WHERE project_intake_document_id='97000000-0000-0000-0000-000000000001';")" identityless_active_job_terminalized
assert_eq legacy_identityless_queue_retired "$(value "SELECT diagnostic_code FROM pulse_ai_document_processing_jobs WHERE project_intake_document_id='97000000-0000-0000-0000-000000000001';")" retirement_diagnostic_recorded
assert_eq failed "$(value "SELECT pulse_ai_processing_status FROM project_intake_documents WHERE project_intake_document_id='97000000-0000-0000-0000-000000000001';")" document_recoverable_failed_state

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='097_project_planning_identity_safe_admission';")" migration_reapply_idempotent

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='097_project_planning_identity_safe_admission';")" rollback_unregisters_migration
assert_eq 0 "$(value "SELECT COUNT(*) FROM pg_trigger WHERE tgrelid='project_intake_documents'::regclass AND tgname LIKE 'trg_module001_057_queue_project_ai_document%' AND NOT tgisinternal;")" rollback_does_not_restore_unsafe_trigger

echo 'PROJECT_PLANNING_IDENTITY_SAFE_ADMISSION_MIGRATION_097=PASS'
'''
write("tests/test-project-planning-identity-safe-admission-migration-097.sh", migration_test)

query_shape_test = r'''#!/usr/bin/env bash
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
'''
write("tests/test-pulse-ai-runtime-job-query-shape.sh", query_shape_test)

ci_workflow = r'''name: Project Planning Identity-safe Admission CI

on:
  pull_request:
    paths:
      - database/migrations/097_project_planning_identity_safe_admission.sql
      - database/rollback/097_project_planning_identity_safe_admission_rollback.sql
      - src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs
      - scripts/release-test/**
      - tests/test-project-planning-identity-safe-admission-migration-097.sh
      - tests/test-pulse-ai-runtime-job-query-shape.sh
      - .github/workflows/project-planning-identity-safe-admission-ci.yml
  push:
    branches:
      - fix/shared-project-document-planning-20260819
    paths:
      - database/migrations/097_project_planning_identity_safe_admission.sql
      - database/rollback/097_project_planning_identity_safe_admission_rollback.sql
      - src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs
      - scripts/release-test/**
      - tests/test-project-planning-identity-safe-admission-migration-097.sh
      - tests/test-pulse-ai-runtime-job-query-shape.sh
      - .github/workflows/project-planning-identity-safe-admission-ci.yml

permissions:
  contents: read

jobs:
  validate:
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09
      - name: Validate identity-safe migration
        run: bash tests/test-project-planning-identity-safe-admission-migration-097.sh
      - name: Validate runtime job query shape
        run: bash tests/test-pulse-ai-runtime-job-query-shape.sh
      - name: Validate release wiring
        shell: bash
        run: |
          set -Eeuo pipefail
          grep -Fq '097_project_planning_identity_safe_admission.sql' scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh
          grep -Fq 'MIGRATION_097=APPLIED_AND_VERIFIED' scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh
          grep -Fq 'MIGRATION_097=APPLIED_AND_VERIFIED' scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh
          ! grep -R -F '086-088-093-094-095-096"' scripts/release-test .github/workflows tests --include='*.sh' --include='*.yml' --include='*.yaml'
'''
write(".github/workflows/project-planning-identity-safe-admission-ci.yml", ci_workflow)

# Fix the aggregate jobs endpoint SELECT so it matches ReadJobsAsync/JobSelectSql.
replace_once(
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs",
    """                    j.maximum_attempts,\n                    j.cancellation_requested,\n                    j.correlation_id,\n""",
    """                    j.maximum_attempts,\n                    j.cancellation_requested,\n                    j.lease_owner,\n                    j.lease_token,\n                    j.lease_generation,\n                    j.lease_expires_at,\n                    j.correlation_id,\n""",
)

builder = "scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh"
replace_once(
    builder,
    'MIGRATION_FILE="$ROOT/database/migrations/096_project_planning_document_authority.sql"\nMIGRATION_RUNNER=',
    'MIGRATION_FILE="$ROOT/database/migrations/096_project_planning_document_authority.sql"\nIDENTITY_SAFE_MIGRATION_FILE="$ROOT/database/migrations/097_project_planning_identity_safe_admission.sql"\nMIGRATION_RUNNER=',
)
replace_once(
    builder,
    '[[ -s "$MIGRATION_FILE" ]] || fail "Migration 096 source is missing."\n[[ -s "$MIGRATION_RUNNER" ]]',
    '[[ -s "$MIGRATION_FILE" ]] || fail "Migration 096 source is missing."\n[[ -s "$IDENTITY_SAFE_MIGRATION_FILE" ]] || fail "Migration 097 source is missing."\n[[ -s "$MIGRATION_RUNNER" ]]',
)
replace_once(
    builder,
    'install -m 0444 "$MIGRATION_FILE" "$CONTEXT/database/migrations/096_project_planning_document_authority.sql"\n',
    'install -m 0444 "$MIGRATION_FILE" "$CONTEXT/database/migrations/096_project_planning_document_authority.sql"\ninstall -m 0444 "$IDENTITY_SAFE_MIGRATION_FILE" "$CONTEXT/database/migrations/097_project_planning_identity_safe_admission.sql"\n',
)
replace_once(
    builder,
    'psql -X -v ON_ERROR_STOP=1 --file "$MIGRATION"\nverification=',
    'psql -X -v ON_ERROR_STOP=1 --file "$MIGRATION"\nIDENTITY_SAFE_MIGRATION="$ROOT/database/migrations/097_project_planning_identity_safe_admission.sql"\n[[ -f "$IDENTITY_SAFE_MIGRATION" ]] || { echo \'ERROR: Migration 097 source is missing from the immutable image.\' >&2; exit 1; }\npsql -X -v ON_ERROR_STOP=1 --file "$IDENTITY_SAFE_MIGRATION"\nverification=',
)
replace_once(
    builder,
    """[[ \"$verification\" == 'true|true|true|true|true|true|true' ]] || {\n  echo \"ERROR: Migration 096 verification failed: $verification\" >&2\n  exit 1\n}\necho 'MIGRATION_096=APPLIED_AND_VERIFIED'\nENTRYPOINT\n""",
    """[[ \"$verification\" == 'true|true|true|true|true|true|true' ]] || {\n  echo \"ERROR: Migration 096 verification failed: $verification\" >&2\n  exit 1\n}\nidentity_safe_verification=\"$(psql -X -At -v ON_ERROR_STOP=1 <<'SQL'\nSELECT\n  EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='097_project_planning_identity_safe_admission')::text || '|' ||\n  (NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgrelid='public.project_intake_documents'::regclass AND tgname IN ('trg_module001_057_queue_project_ai_document_insert','trg_module001_057_queue_project_ai_document_update') AND NOT tgisinternal))::text || '|' ||\n  (to_regprocedure('public.module001_057_queue_project_ai_document()') IS NULL)::text || '|' ||\n  (NOT EXISTS (SELECT 1 FROM pulse_ai_document_processing_jobs WHERE requested_purpose='project_ai_generation_grounding' AND actual_user_id IS NULL AND effective_user_id IS NULL AND job_status IN ('queued','scanning','extracting','awaiting_ocr','embedding','indexing','retry_wait','cancel_requested')))::text;\nSQL\n)\"\n[[ \"$identity_safe_verification\" == 'true|true|true|true' ]] || {\n  echo \"ERROR: Migration 097 verification failed: $identity_safe_verification\" >&2\n  exit 1\n}\necho 'MIGRATION_096=APPLIED_AND_VERIFIED'\necho 'MIGRATION_097=APPLIED_AND_VERIFIED'\nENTRYPOINT\n""",
)
replace_once(
    builder,
    "echo 'MIGRATION_096=APPLIED_AND_VERIFIED'\n\nif [[ -n \"$EVIDENCE_ROOT\" ]]; then",
    "echo 'MIGRATION_096=APPLIED_AND_VERIFIED'\necho 'MIGRATION_097=APPLIED_AND_VERIFIED'\n\nif [[ -n \"$EVIDENCE_ROOT\" ]]; then",
)

# Keep the existing migration-096 evidence contract intact and add a separate 097 artifact.
with (ROOT / builder).open("a", encoding="utf-8") as handle:
    handle.write(r'''

if [[ -n "$EVIDENCE_ROOT" ]]; then
  jq -n \
    --arg releaseCommit "$RELEASE_COMMIT" \
    --arg image "$RELIABILITY_MIGRATION_IMAGE" \
    '{status:"applied_and_verified",migration:"097_project_planning_identity_safe_admission",releaseCommit:$releaseCommit,image:$image,environment:"protected-test",privateNetworkJob:true,productionMutation:false}' \
    > "$EVIDENCE_ROOT/migration-097.json"
fi
''')
print(f"PATCHED {builder} evidence_097=true")

runner = "scripts/release-test/run-project-planning-document-authority-migration-job.sh"
replace_all_required(runner, "096-project-planning-document-authority", "096-097-project-planning-document-authority")

systemwide = "scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh"
replace_once(
    systemwide,
    "echo 'MIGRATION_096=APPLIED_AND_VERIFIED'",
    "echo 'MIGRATION_096=APPLIED_AND_VERIFIED'\necho 'MIGRATION_097=APPLIED_AND_VERIFIED'",
)

# Any exact release ownership/control token that represented the complete migration
# bundle must include 097 now. Limit this to executable release/test control files.
roots = [ROOT / "scripts/release-test", ROOT / ".github/workflows", ROOT / "tests"]
old_bundle = "086-088-093-094-095-096"
new_bundle = "086-088-093-094-095-096-097"
for base in roots:
    if not base.exists():
        continue
    for path in base.rglob("*"):
        if not path.is_file() or path.suffix not in {".sh", ".yml", ".yaml", ".py"}:
            continue
        text = path.read_text(encoding="utf-8")
        if old_bundle in text:
            count = text.count(old_bundle)
            path.write_text(text.replace(old_bundle, new_bundle), encoding="utf-8")
            print(f"UPDATED_MIGRATION_BUNDLE {path.relative_to(ROOT)} replacements={count}")

print("PR737_ATOMIC_REPAIR_SOURCE_READY")
