#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="3471962b9c3b6255e62a185a2e1e9daa5ded9bff"
RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
MODE="${PROJECTPULSE_PR479_MIGRATION_MODE:-verify}"
ROLLBACK_CONFIRMATION="${PROJECTPULSE_PR479_ROLLBACK_CONFIRMATION:-}"
REQUIRED_ROLLBACK_CONFIRMATION="ROLLBACK-MIGRATIONS-068-070-3471962B"
NEW_068="${PROJECTPULSE_PR479_NEW_068:-false}"
NEW_069="${PROJECTPULSE_PR479_NEW_069:-false}"
NEW_070="${PROJECTPULSE_PR479_NEW_070:-false}"

MIGRATION_068="068_module006_standalone_pipeline_management.sql"
ROLLBACK_068="068_module006_standalone_pipeline_management_rollback.sql"
ID_068="068_module006_standalone_pipeline_management"
MIGRATION_069="069_module006_customer_pipeline_expansion.sql"
ROLLBACK_069="069_module006_customer_pipeline_expansion_rollback.sql"
ID_069="069_module006_customer_pipeline_expansion"
MIGRATION_070="070_module_033_project_forge.sql"
ROLLBACK_070="070_module_033_project_forge_rollback.sql"
ID_070="070_module_033_project_forge"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

query_scalar() {
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="$1"
}

is_registered() {
  query_scalar "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='$1');"
}

apply_if_missing() {
  local migration_id="$1"
  local migration_file="$2"
  local migration_number="$3"
  if [[ "$(is_registered "$migration_id")" == t ]]; then
    echo "PROJECTPULSE_MIGRATION_${migration_number}=ALREADY_REGISTERED"
    return 0
  fi
  echo "PROJECTPULSE_MIGRATION_${migration_number}=APPLYING"
  psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
    --file="$MIGRATION_ROOT/$migration_file"
  [[ "$(is_registered "$migration_id")" == t ]] ||
    fail "Migration $migration_number did not register after apply."
  echo "PROJECTPULSE_MIGRATION_${migration_number}=APPLIED"
}

rollback_if_new() {
  local was_new="$1"
  local migration_id="$2"
  local rollback_file="$3"
  local migration_number="$4"
  [[ "$was_new" == true ]] || {
    echo "PROJECTPULSE_MIGRATION_${migration_number}_ROLLBACK=SKIPPED_PREEXISTING"
    return 0
  }
  if [[ "$(is_registered "$migration_id")" == f ]]; then
    echo "PROJECTPULSE_MIGRATION_${migration_number}_ROLLBACK=NOT_REQUIRED"
    return 0
  fi
  psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
    --file="$MIGRATION_ROOT/$rollback_file"
  [[ "$(is_registered "$migration_id")" == f ]] ||
    fail "Migration $migration_number remains registered after rollback."
  echo "PROJECTPULSE_MIGRATION_${migration_number}_ROLLBACK=COMPLETE"
}

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
[[ "$MODE" == apply || "$MODE" == verify || "$MODE" == rollback ]] ||
  fail "PROJECTPULSE_PR479_MIGRATION_MODE must be apply, verify, or rollback."
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
for file in \
  "$MIGRATION_068" "$ROLLBACK_068" \
  "$MIGRATION_069" "$ROLLBACK_069" \
  "$MIGRATION_070" "$ROLLBACK_070"; do
  [[ -f "$MIGRATION_ROOT/$file" ]] || fail "Migration source is missing: $file"
done
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(wc -l < "$CHECKSUM_MANIFEST" | tr -d ' ')" == "6" ]] ||
  fail "Migration checksum manifest must contain exactly six SQL files."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."
echo "PROJECTPULSE_MIGRATIONS_068_070_CHECKSUM=VERIFIED"

for table in \
  schema_migrations app_users projects project_tasks project_assignments \
  app_roles app_permissions app_role_permissions app_feature_catalog \
  enterprise_notification_policies enterprise_notification_events \
  ai_capability_routes; do
  present="$(query_scalar "SELECT to_regclass('public.$table') IS NOT NULL;")"
  [[ "$present" == t ]] || fail "Required Test database table is unavailable: $table"
done

PRE_068="$(is_registered "$ID_068")"
PRE_069="$(is_registered "$ID_069")"
PRE_070="$(is_registered "$ID_070")"
[[ "$PRE_068" == t ]] && echo "PROJECTPULSE_MIGRATION_068_PREEXISTING=YES" || echo "PROJECTPULSE_MIGRATION_068_PREEXISTING=NO"
[[ "$PRE_069" == t ]] && echo "PROJECTPULSE_MIGRATION_069_PREEXISTING=YES" || echo "PROJECTPULSE_MIGRATION_069_PREEXISTING=NO"
[[ "$PRE_070" == t ]] && echo "PROJECTPULSE_MIGRATION_070_PREEXISTING=YES" || echo "PROJECTPULSE_MIGRATION_070_PREEXISTING=NO"
if [[ "$PRE_069" == t && "$PRE_068" == f ]]; then
  fail "Migration 069 is registered while its required Migration 068 is missing."
fi

read -r USERS_BEFORE PROJECTS_BEFORE TASKS_BEFORE ASSIGNMENTS_BEFORE <<<"$(
  query_scalar "
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM project_tasks),
      (SELECT COUNT(*) FROM project_assignments);" | tr '|' ' '
)"

if [[ "$MODE" == rollback ]]; then
  [[ "$ROLLBACK_CONFIRMATION" == "$REQUIRED_ROLLBACK_CONFIRMATION" ]] ||
    fail "The exact guarded rollback confirmation is required."
  for state in "$NEW_068" "$NEW_069" "$NEW_070"; do
    [[ "$state" == true || "$state" == false ]] ||
      fail "Rollback prior-state flags must be true or false."
  done

  if [[ "$NEW_068" == true ]]; then
    psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
DO $projectpulse479_module006_rollback_guard$
DECLARE
    relation_name TEXT;
    row_exists BOOLEAN;
BEGIN
    FOREACH relation_name IN ARRAY ARRAY[
        'module006_pipeline_records',
        'module006_pipeline_updates',
        'module006_pipeline_tasks',
        'module006_pipeline_task_events'
    ]
    LOOP
        IF to_regclass('public.' || relation_name) IS NOT NULL THEN
            EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I LIMIT 1)', relation_name)
            INTO row_exists;
            IF row_exists THEN
                RAISE EXCEPTION 'PR479 automatic rollback blocked: operational data exists in %.', relation_name;
            END IF;
        END IF;
    END LOOP;
END;
$projectpulse479_module006_rollback_guard$;
SQL
  elif [[ "$NEW_069" == true ]]; then
    if [[ "$(query_scalar "SELECT EXISTS (SELECT 1 FROM module006_pipeline_records WHERE lower(btrim(customer)) NOT IN ('toyota','hyundai'));")" == t ]]; then
      fail "Migration 069 rollback is blocked because additional-customer Module 006 data exists."
    fi
  fi

  if [[ "$NEW_070" == true ]]; then
    psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
DO $projectpulse479_forge_rollback_guard$
DECLARE
    relation_name TEXT;
    row_exists BOOLEAN;
BEGIN
    FOREACH relation_name IN ARRAY ARRAY[
        'project_forge_plans',
        'project_forge_plan_tasks',
        'project_forge_plan_assignments',
        'project_forge_task_dependencies',
        'project_forge_task_details',
        'project_forge_audit_events'
    ]
    LOOP
        IF to_regclass('public.' || relation_name) IS NOT NULL THEN
            EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I LIMIT 1)', relation_name)
            INTO row_exists;
            IF row_exists THEN
                RAISE EXCEPTION 'PR479 automatic rollback blocked: operational or audit data exists in %.', relation_name;
            END IF;
        END IF;
    END LOOP;
END;
$projectpulse479_forge_rollback_guard$;
SQL
  fi

  rollback_if_new "$NEW_070" "$ID_070" "$ROLLBACK_070" 070
  rollback_if_new "$NEW_069" "$ID_069" "$ROLLBACK_069" 069
  rollback_if_new "$NEW_068" "$ID_068" "$ROLLBACK_068" 068
  echo "PROJECTPULSE_MIGRATIONS_068_070_ROLLBACK=COMPLETE"
  exit 0
fi

case "$MODE" in
  apply)
    apply_if_missing "$ID_068" "$MIGRATION_068" 068
    apply_if_missing "$ID_069" "$MIGRATION_069" 069
    apply_if_missing "$ID_070" "$MIGRATION_070" 070
    ;;
  verify)
    [[ "$PRE_068" == t ]] || fail "Migration 068 is not registered."
    [[ "$PRE_069" == t ]] || fail "Migration 069 is not registered."
    [[ "$PRE_070" == t ]] || fail "Migration 070 is not registered."
    ;;
esac

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
DO $projectpulse479_release_verify$
DECLARE
    missing_count INTEGER;
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id='068_module006_standalone_pipeline_management'
    ) OR NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id='069_module006_customer_pipeline_expansion'
    ) OR NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id='070_module_033_project_forge'
    ) THEN
        RAISE EXCEPTION 'Required release migrations 068, 069, and 070 are not all registered.';
    END IF;

    SELECT COUNT(*) INTO missing_count
    FROM unnest(ARRAY[
        'module006_pipeline_records',
        'module006_pipeline_updates',
        'module006_pipeline_tasks',
        'module006_pipeline_task_events',
        'project_forge_plans',
        'project_forge_plan_tasks',
        'project_forge_plan_assignments',
        'project_forge_task_dependencies',
        'project_forge_task_details',
        'project_forge_audit_events'
    ]) AS required(relation_name)
    WHERE to_regclass('public.' || relation_name) IS NULL;
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Release schema is missing % required relation(s).', missing_count;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname='ck_module006_pipeline_records_customer_name'
    ) OR NOT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE schemaname='public'
          AND indexname='ix_module006_pipeline_records_customer_name'
    ) THEN
        RAISE EXCEPTION 'Migration 069 customer-pipeline invariants are missing.';
    END IF;

    SELECT COUNT(*) INTO missing_count
    FROM unnest(ARRAY[
        'trg_project_forge_plans_revision',
        'trg_project_forge_plan_tasks_validate',
        'trg_project_forge_plan_assignments_validate',
        'trg_project_forge_dependencies_validate',
        'trg_project_forge_task_details_validate',
        'trg_project_forge_audit_events_immutable'
    ]) AS required(trigger_name)
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname=required.trigger_name
          AND NOT tgisinternal
    );
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Migration 070 is missing % required integrity trigger(s).', missing_count;
    END IF;

    SELECT COUNT(*) INTO missing_count
    FROM unnest(ARRAY[
        'VIEW_PROJECT_FORGE_033',
        'MANAGE_PROJECT_FORGE_033',
        'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033',
        'USE_PROJECT_FORGE_AI_033'
    ]) AS required(permission_code)
    WHERE NOT EXISTS (
        SELECT 1 FROM app_permissions
        WHERE app_permissions.permission_code=required.permission_code
    );
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Migration 070 is missing % Project Forge permission(s).', missing_count;
    END IF;

    SELECT COUNT(*) INTO missing_count
    FROM unnest(ARRAY[
        'PROJECT_FORGE_REVIEW_ASSIGNED',
        'PROJECT_FORGE_TASK_ASSIGNED',
        'PROJECT_FORGE_TASK_UPDATED',
        'PROJECT_FORGE_PLAN_UPDATED'
    ]) AS required(policy_code)
    WHERE NOT EXISTS (
        SELECT 1 FROM enterprise_notification_policies
        WHERE enterprise_notification_policies.policy_code=required.policy_code
          AND source_module='033'
    );
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Migration 070 is missing % Module 065 Project Forge policy row(s).', missing_count;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM ai_capability_routes
        WHERE feature_code='project_forge_plan_estimate'
    ) THEN
        RAISE EXCEPTION 'Module 064 Project Forge AI route is missing.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM app_feature_catalog
        WHERE feature_code='PROJECT_FORGE'
          AND module_code='033'
          AND is_active=TRUE
    ) THEN
        RAISE EXCEPTION 'Module 033 feature registration is missing.';
    END IF;
END
$projectpulse479_release_verify$;
SQL

if [[ "$PRE_070" == f ]]; then
  FORGE_ROWS="$(query_scalar "SELECT (SELECT COUNT(*) FROM project_forge_plans) + (SELECT COUNT(*) FROM project_forge_plan_tasks);")"
  [[ "$FORGE_ROWS" == 0 ]] || fail "Migration 070 unexpectedly seeded Project Forge plans or tasks."
  echo "PROJECTPULSE_MIGRATION_070_SAMPLE_DATA=NONE"
fi

read -r USERS_AFTER PROJECTS_AFTER TASKS_AFTER ASSIGNMENTS_AFTER <<<"$(
  query_scalar "
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM project_tasks),
      (SELECT COUNT(*) FROM project_assignments);" | tr '|' ' '
)"

[[ "$USERS_AFTER" == "$USERS_BEFORE" ]] || fail "Release migrations changed app_users row count."
[[ "$PROJECTS_AFTER" == "$PROJECTS_BEFORE" ]] || fail "Release migrations changed projects row count."
[[ "$TASKS_AFTER" == "$TASKS_BEFORE" ]] || fail "Release migrations changed project_tasks row count."
[[ "$ASSIGNMENTS_AFTER" == "$ASSIGNMENTS_BEFORE" ]] || fail "Release migrations changed project_assignments row count."

echo "PROJECTPULSE_MIGRATIONS_068_070_OPERATIONAL_COUNTS=UNCHANGED"
echo "PROJECTPULSE_MIGRATIONS_068_070_INVARIANTS=VERIFIED"
if [[ "$MODE" == apply ]]; then
  echo "PROJECTPULSE_MIGRATIONS_068_070_RESULT=APPLIED_OR_ALREADY_PRESENT"
else
  echo "PROJECTPULSE_MIGRATIONS_068_070_RESULT=VERIFY_ONLY_PASS"
fi
