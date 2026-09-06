#!/usr/bin/env bash
# Image entrypoint. The immutable image contains only approved 103/104 SQL and checksums.
set -Eeuo pipefail
ROOT=/opt/projectpulse/release
fail() { echo "ERROR: $*" >&2; exit 1; }
[[ "${MAIN_RELEASE_EXPECTED_RELEASE_COMMIT:-}" =~ ^[0-9a-f]{40}$ ]] || fail 'An exact release identity is required.'
[[ "$(cat "$ROOT/release-commit")" == "$MAIN_RELEASE_EXPECTED_RELEASE_COMMIT" ]] || fail 'Migration image release mismatch.'
[[ -n "${PROJECTPULSE_TEST_DATABASE_NAME:-}" && "${PGDATABASE:-}" == "$PROJECTPULSE_TEST_DATABASE_NAME" ]] || fail 'Test database identity mismatch.'
[[ "${MAIN_RELEASE_MIGRATION_MODE:-}" == apply || "${MAIN_RELEASE_MIGRATION_MODE:-}" == verify ]] || fail 'Only apply or verify is supported.'
cd "$ROOT"
sha256sum --check --strict SHA256SUMS >/dev/null || fail 'The immutable migration payload was changed.'
export PGOPTIONS='-c statement_timeout=120000 -c lock_timeout=15000'
if [[ "$MAIN_RELEASE_MIGRATION_MODE" == apply ]]; then
  psql -X -v ON_ERROR_STOP=1 <<'SQL'
SELECT pg_advisory_lock(660103104);
\i database/migrations/103_module_066_flowhive_enterprise_psa_revamp.sql
\i database/migrations/104_flowhive_bounded_ai_execution.sql
SELECT pg_advisory_unlock(660103104);
SQL
fi
verified="$(psql -X -At -v ON_ERROR_STOP=1 <<'SQL'
SELECT (
  (SELECT count(*) FROM schema_migrations WHERE migration_id IN (
    '103_module_066_flowhive_enterprise_psa_revamp','104_flowhive_bounded_ai_execution')) = 2
  AND to_regclass('public.project_flowhive_raid_events') IS NOT NULL
  AND to_regclass('public.project_flowhive_meetings') IS NOT NULL
  AND to_regclass('public.project_flowhive_meeting_events') IS NOT NULL
  AND to_regclass('public.project_flowhive_task_reminder_preferences') IS NOT NULL
  AND to_regclass('public.project_flowhive_task_reminder_events') IS NOT NULL
  AND (SELECT count(*) FROM pg_trigger WHERE NOT tgisinternal AND tgenabled IN ('O','A') AND tgname IN (
    'trg_project_flowhive_raid_audit_103','trg_project_flowhive_raid_events_immutable_103',
    'trg_project_flowhive_meeting_events_immutable_103','trg_project_flowhive_task_reminder_events_immutable_103',
    'trg_flowhive_104_execution_fence')) = 5
  AND to_regprocedure('public.projectpulse104_fence_planner_execution()') IS NOT NULL
  AND to_regclass('public.ix_flowhive_104_deadline') IS NOT NULL
  AND (SELECT count(*) FROM information_schema.columns WHERE table_schema='public'
    AND table_name='project_flowhive_ai_planner_runs' AND column_name IN (
      'execution_contract','deadline_at','input_fingerprint','source_selection_fingerprint','source_version_fingerprint',
      'expected_working_row_version','attempt_count','next_attempt_at','phase_started_at',
      'retry_document_processing','saved_working_row_version','saved_working_revision')) = 12
  AND EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public'
    AND table_name='project_flowhive_ai_planner_runs' AND column_name='saved_working_row_version' AND data_type='uuid')
  AND EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public'
    AND table_name='project_flowhive_ai_planner_runs' AND column_name='saved_working_revision' AND data_type='integer')
  AND NOT EXISTS(SELECT 1 FROM project_flowhive_ai_planner_runs
    WHERE status IN ('queued','processing','generating') AND execution_contract='')
)::text;
SQL
)"
[[ "$verified" == true ]] || fail 'FlowHive PSA migrations are not fully applied and enforced.'
echo 'FLOWHIVE_PSA_MIGRATIONS_103_104=APPLIED_AND_VERIFIED'
echo 'PRODUCTION_MUTATION=NONE'
