#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="a536e33b48c41bf1dd867d7319e88f98e8aa152c"
RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
MODE="${PROJECTPULSE_PR467_MIGRATION_MODE:-verify}"
ROLLBACK_CONFIRMATION="${PROJECTPULSE_PR467_ROLLBACK_CONFIRMATION:-}"
REQUIRED_ROLLBACK_CONFIRMATION="ROLLBACK-MIGRATION-067-A536E33B"

MIGRATION_FILE="067_uat_expense_lifecycle_work_identifiers.sql"
ROLLBACK_FILE="067_uat_expense_lifecycle_work_identifiers_rollback.sql"
MIGRATION_ID="067_uat_expense_lifecycle_work_identifiers"
PREREQUISITE_ID="066_immutable_project_numbers"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

query_scalar() {
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="$1"
}

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
[[ "$MODE" == apply || "$MODE" == verify || "$MODE" == rollback ]] ||
  fail "PROJECTPULSE_PR467_MIGRATION_MODE must be apply, verify, or rollback."
command -v psql >/dev/null || fail "psql is required."
command -v sha256sum >/dev/null || fail "sha256sum is required."

if [[ -d "$RELEASE_ROOT/.git" ]]; then
  ACTUAL_RELEASE_COMMIT="$(git -C "$RELEASE_ROOT" rev-parse HEAD)"
elif [[ -f "$RELEASE_ROOT/.projectpulse-release-commit" ]]; then
  ACTUAL_RELEASE_COMMIT="$(tr -d '\r\n' < "$RELEASE_ROOT/.projectpulse-release-commit")"
else
  fail "Release marker is missing."
fi
[[ "$ACTUAL_RELEASE_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] ||
  fail "Unexpected release commit: $ACTUAL_RELEASE_COMMIT"

MIGRATION_ROOT="$RELEASE_ROOT/database/migrations"
CHECKSUM_MANIFEST="$MIGRATION_ROOT/SHA256SUMS"
for file in "$MIGRATION_FILE" "$ROLLBACK_FILE"; do
  [[ -f "$MIGRATION_ROOT/$file" ]] || fail "Migration source is missing: $file"
done
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(wc -l < "$CHECKSUM_MANIFEST" | tr -d ' ')" == "2" ]] ||
  fail "Migration checksum manifest must contain exactly two SQL files."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."
echo "PROJECTPULSE_MIGRATION_067_CHECKSUM=VERIFIED"

for table in schema_migrations project_expense_uploads projects app_users; do
  present="$(query_scalar "SELECT to_regclass('public.$table') IS NOT NULL;")"
  [[ "$present" == t ]] || fail "Required Test database table is unavailable: $table"
done

prerequisite_registered="$(query_scalar "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='$PREREQUISITE_ID');")"
[[ "$prerequisite_registered" == t ]] ||
  fail "Required prerequisite migration is not registered: $PREREQUISITE_ID"
echo "PROJECTPULSE_MIGRATION_067_PREREQUISITE_066=VERIFIED"

registered_before="$(query_scalar "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='$MIGRATION_ID');")"
if [[ "$registered_before" == t ]]; then
  echo "PROJECTPULSE_MIGRATION_067_PREEXISTING=YES"
else
  echo "PROJECTPULSE_MIGRATION_067_PREEXISTING=NO"
fi

read -r USERS_BEFORE PROJECTS_BEFORE UPLOADS_BEFORE LINES_BEFORE <<<"$(
  query_scalar "
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM project_expense_uploads),
      (SELECT COUNT(*) FROM project_expense_lines);" | tr '|' ' '
)"

if [[ "$MODE" == rollback ]]; then
  [[ "$ROLLBACK_CONFIRMATION" == "$REQUIRED_ROLLBACK_CONFIRMATION" ]] ||
    fail "The exact guarded rollback confirmation is required."
  if [[ "$registered_before" == f ]]; then
    echo "PROJECTPULSE_MIGRATION_067_ROLLBACK=NOT_REQUIRED"
    exit 0
  fi

  psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
    --file="$MIGRATION_ROOT/$ROLLBACK_FILE"

  registered_after="$(query_scalar "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='$MIGRATION_ID');")"
  table_after="$(query_scalar "SELECT to_regclass('public.project_expense_upload_acceptances') IS NOT NULL;")"
  [[ "$registered_after" == f ]] || fail "Migration 067 remains registered after rollback."
  [[ "$table_after" == f ]] || fail "Migration 067 acceptance table remains after rollback."
  echo "PROJECTPULSE_MIGRATION_067_ROLLBACK=COMPLETE"
  exit 0
fi

case "$registered_before:$MODE" in
  t:apply)
    echo "PROJECTPULSE_MIGRATION_067=ALREADY_REGISTERED"
    ;;
  t:verify)
    echo "PROJECTPULSE_MIGRATION_067=REGISTERED"
    ;;
  f:apply)
    echo "PROJECTPULSE_MIGRATION_067=APPLYING"
    psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
      --file="$MIGRATION_ROOT/$MIGRATION_FILE"
    ;;
  f:verify)
    fail "Migration 067 is not registered; apply authorization is required."
    ;;
  *)
    fail "Unexpected migration state: $registered_before:$MODE"
    ;;
esac

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
DO $projectpulse067_release_verify$
DECLARE
    mismatch_count INTEGER;
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id='066_immutable_project_numbers'
    ) THEN
        RAISE EXCEPTION 'Prerequisite Migration 066 is not registered.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id='067_uat_expense_lifecycle_work_identifiers'
    ) THEN
        RAISE EXCEPTION 'Migration 067 is not registered.';
    END IF;

    IF to_regclass('public.project_expense_upload_acceptances') IS NULL THEN
        RAISE EXCEPTION 'Migration 067 acceptance table is missing.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE schemaname='public'
          AND indexname='ix_project_expense_upload_acceptances_project'
    ) THEN
        RAISE EXCEPTION 'Migration 067 project acceptance index is missing.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname='trg_project_expense_acceptance_validate_insert'
          AND NOT tgisinternal
    ) THEN
        RAISE EXCEPTION 'Migration 067 validation trigger is missing.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname='trg_project_expense_acceptance_immutable'
          AND NOT tgisinternal
    ) THEN
        RAISE EXCEPTION 'Migration 067 immutability trigger is missing.';
    END IF;

    IF to_regprocedure('public.projectpulse067_validate_expense_acceptance_insert()') IS NULL THEN
        RAISE EXCEPTION 'Migration 067 validation function is missing.';
    END IF;

    IF to_regprocedure('public.projectpulse067_block_expense_acceptance_mutation()') IS NULL THEN
        RAISE EXCEPTION 'Migration 067 immutability function is missing.';
    END IF;

    SELECT COUNT(*)
      INTO mismatch_count
      FROM project_expense_upload_acceptances acceptance
      LEFT JOIN project_expense_uploads upload
        ON upload.project_expense_upload_id = acceptance.project_expense_upload_id
     WHERE upload.project_expense_upload_id IS NULL
        OR upload.deleted_at IS NOT NULL
        OR upload.is_current IS NOT TRUE
        OR acceptance.project_id IS DISTINCT FROM upload.project_id
        OR acceptance.expense_owner_user_id IS DISTINCT FROM upload.expense_owner_user_id
        OR acceptance.accepted_version_number IS DISTINCT FROM upload.version_number;

    IF mismatch_count <> 0 THEN
        RAISE EXCEPTION 'Migration 067 invariant failed for % acceptance row(s).', mismatch_count;
    END IF;
END
$projectpulse067_release_verify$;
SQL

read -r USERS_AFTER PROJECTS_AFTER UPLOADS_AFTER LINES_AFTER <<<"$(
  query_scalar "
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM project_expense_uploads),
      (SELECT COUNT(*) FROM project_expense_lines);" | tr '|' ' '
)"

[[ "$USERS_AFTER" == "$USERS_BEFORE" ]] || fail "Migration 067 changed app_users row count."
[[ "$PROJECTS_AFTER" == "$PROJECTS_BEFORE" ]] || fail "Migration 067 changed projects row count."
[[ "$UPLOADS_AFTER" == "$UPLOADS_BEFORE" ]] || fail "Migration 067 changed project_expense_uploads row count."
[[ "$LINES_AFTER" == "$LINES_BEFORE" ]] || fail "Migration 067 changed project_expense_lines row count."

ACCEPTANCE_ROWS="$(query_scalar "SELECT COUNT(*) FROM project_expense_upload_acceptances;")"
echo "PROJECTPULSE_MIGRATION_067_ACCEPTANCE_ROWS=$ACCEPTANCE_ROWS"
echo "PROJECTPULSE_MIGRATION_067_OPERATIONAL_COUNTS=UNCHANGED"
echo "PROJECTPULSE_MIGRATION_067_INVARIANTS=VERIFIED"
if [[ "$MODE" == apply ]]; then
  echo "PROJECTPULSE_MIGRATION_067_RESULT=APPLIED_OR_ALREADY_PRESENT"
else
  echo "PROJECTPULSE_MIGRATION_067_RESULT=VERIFY_ONLY_PASS"
fi
