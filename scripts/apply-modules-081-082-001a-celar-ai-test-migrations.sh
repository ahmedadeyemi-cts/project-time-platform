#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="1892c6d0187edc367a57b8cee2e868417dd9a01a"
RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
EXPECTED_DATABASE_NAME="${PROJECTPULSE_TEST_DATABASE_NAME:-}"
MODE="${MAIN_RELEASE_MIGRATION_MODE:-verify}"
MIGRATION_ROOT="$RELEASE_ROOT/database/migrations"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ "$EXPECTED_DATABASE_NAME" =~ ^[A-Za-z_][A-Za-z0-9_]{0,62}$ ]] ||
  fail "PROJECTPULSE_TEST_DATABASE_NAME must be an exact PostgreSQL identifier."
[[ "$MODE" == apply || "$MODE" == verify ]] || fail "MAIN_RELEASE_MIGRATION_MODE must be apply or verify."
command -v psql >/dev/null || fail "psql is required."
command -v sha256sum >/dev/null || fail "sha256sum is required."

PSQL_TARGET=()
if [[ -n "$DATABASE_URL" ]]; then
  PSQL_TARGET=("$DATABASE_URL")
else
  [[ -n "${PGHOST:-}" ]] || fail "PGHOST is not configured."
  [[ "${PGPORT:-}" =~ ^[0-9]{1,5}$ ]] || fail "PGPORT is not valid."
  [[ "${PGDATABASE:-}" == "$EXPECTED_DATABASE_NAME" ]] || fail "PGDATABASE does not match the protected Test database name."
  [[ -n "${PGUSER:-}" ]] || fail "PGUSER is not configured."
  [[ -n "${PGPASSWORD:-}" ]] || fail "PGPASSWORD is not configured."
fi

if [[ -d "$RELEASE_ROOT/.git" ]]; then
  ACTUAL_RELEASE_COMMIT="$(git -C "$RELEASE_ROOT" rev-parse HEAD)"
elif [[ -f "$RELEASE_ROOT/.projectpulse-release-commit" ]]; then
  ACTUAL_RELEASE_COMMIT="$(tr -d '\r\n' < "$RELEASE_ROOT/.projectpulse-release-commit")"
else
  fail "Release marker is missing."
fi
[[ "$ACTUAL_RELEASE_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] || fail "Unexpected release commit: $ACTUAL_RELEASE_COMMIT"

FILES=(
  075_pulse_product_rebrand.sql
  076_module_081_lab_equipment_tracker.sql
  077_module_082_enterprise_project_risk_register.sql
  078_module_001a_engineer_request_closeout.sql
)
HASHES=(
  524decbbf13c0aef05f16971b87797dbeec1b30d6fde552a18316bb1bdca0b5d
  0fd7addecfb43c8d7341c6882e02affd3b6c6f4eb8eea49c4b7aa086270ae47d
  22725eb63bb57d82f23f431d6a7c007740b758040c16b294590947901072dada
  de2e814fb3a96fc45bb9e15e7074b1b88b1c9d13b4b0b2cc9191379bcdc97162
)

[[ -f "$MIGRATION_ROOT/SHA256SUMS" ]] || fail "Migration checksum manifest is missing."
mapfile -t ACTUAL_FILES < <(
  for path in "$MIGRATION_ROOT"/*.sql; do
    [[ -f "$path" ]] && basename "$path"
  done | LC_ALL=C sort
)
diff -u <(printf '%s\n' "${FILES[@]}" | LC_ALL=C sort) <(printf '%s\n' "${ACTUAL_FILES[@]}") ||
  fail "Migration image must contain exactly migrations 075, 076, 077, and 078."
[[ "$(wc -l < "$MIGRATION_ROOT/SHA256SUMS" | tr -d ' ')" == 4 ]] ||
  fail "SHA256SUMS must contain exactly four entries."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."

for index in "${!FILES[@]}"; do
  file="${FILES[$index]}"
  actual="$(sha256sum "$MIGRATION_ROOT/$file" | awk '{print $1}')"
  [[ "$actual" == "${HASHES[$index]}" ]] || fail "Unexpected source bytes for $file."
  [[ "$(grep -c '^BEGIN;$' "$MIGRATION_ROOT/$file")" == 1 ]] || fail "$file must contain one top-level BEGIN."
  [[ "$(grep -c '^COMMIT;$' "$MIGRATION_ROOT/$file")" == 1 ]] || fail "$file must contain one top-level COMMIT."
done
echo "MODULES_081_082_001A_MIGRATION_SOURCE_075_078=VERIFIED"

BODY_ROOT="$(mktemp -d)"
cleanup() {
  local status=$?
  rm -rf "$BODY_ROOT"
  unset DATABASE_URL PGPASSWORD
  exit "$status"
}
trap cleanup EXIT INT TERM

for file in "${FILES[@]}"; do
  sed -e '/^BEGIN;$/d' -e '/^COMMIT;$/d' "$MIGRATION_ROOT/$file" > "$BODY_ROOT/$file"
done

APPLY_BOOL=false
[[ "$MODE" == apply ]] && APPLY_BOOL=true

psql "${PSQL_TARGET[@]}" \
  --no-psqlrc \
  --set=ON_ERROR_STOP=1 \
  --set=release_apply="$APPLY_BOOL" \
  --set=expected_database_name="$EXPECTED_DATABASE_NAME" \
  --set=body075="$BODY_ROOT/${FILES[0]}" \
  --set=body076="$BODY_ROOT/${FILES[1]}" \
  --set=body077="$BODY_ROOT/${FILES[2]}" \
  --set=body078="$BODY_ROOT/${FILES[3]}" <<'SQL'
\set ON_ERROR_STOP on
BEGIN;
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '20min';
SELECT set_config('projectpulse.release.expected_database', :'expected_database_name', true) AS value \gset release_database_

DO $release_database_identity$
BEGIN
  IF current_database() <> current_setting('projectpulse.release.expected_database') THEN
    RAISE EXCEPTION 'Connected database does not match the protected Test database identity.';
  END IF;
  IF to_regclass('public.projects') IS NULL OR to_regclass('public.schema_migrations') IS NULL THEN
    RAISE EXCEPTION 'The protected Test database sentinel tables are unavailable.';
  END IF;
END
$release_database_identity$;
\echo DATABASE_IDENTITY=TEST_SENTINEL_VERIFIED

SELECT pg_advisory_xact_lock(75076078);

DO $release_prerequisites$
DECLARE
  required_id text;
BEGIN
  FOREACH required_id IN ARRAY ARRAY[
    '074_module_066_project_flowhive_production'
  ] LOOP
    IF (SELECT COUNT(*) FROM schema_migrations WHERE migration_id=required_id) <> 1 THEN
      RAISE EXCEPTION 'Required prerequisite migration is missing or duplicated: %', required_id;
    END IF;
  END LOOP;
  IF EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id IN ('076_module_001a_engineer_request_closeout')
  ) THEN
    RAISE EXCEPTION 'A superseded migration 076 identity is present; refusing the reconciled 076-078 release.';
  END IF;
END
$release_prerequisites$;

SELECT set_config('projectpulse.release.apply', :'release_apply', true) AS value \gset release_apply_setting_

DO $release_legacy_risk_schema$
DECLARE
  migration_077_present boolean;
  table_present boolean;
  legacy_id_present boolean;
  enterprise_id_present boolean;
  legacy_column_count integer;
  enterprise_column_count integer;
BEGIN
  SELECT EXISTS(
    SELECT 1 FROM schema_migrations
    WHERE migration_id='077_module_082_enterprise_project_risk_register'
  ) INTO migration_077_present;
  table_present := to_regclass('public.project_risks') IS NOT NULL;

  SELECT
    COUNT(*) FILTER (WHERE column_name='project_risk_id') > 0,
    COUNT(*) FILTER (WHERE column_name='risk_id') > 0,
    COUNT(*) FILTER (WHERE
      (column_name='project_risk_id' AND data_type='uuid' AND is_nullable='NO')
      OR (column_name='project_id' AND data_type='uuid' AND is_nullable='NO')
      OR (column_name='risk_title' AND data_type='character varying' AND is_nullable='NO')
      OR (column_name='risk_description' AND data_type='text' AND is_nullable='YES')
      OR (column_name='probability' AND data_type='character varying' AND is_nullable='NO')
      OR (column_name='impact' AND data_type='character varying' AND is_nullable='NO')
      OR (column_name='risk_status' AND data_type='character varying' AND is_nullable='NO')
      OR (column_name='mitigation_plan' AND data_type='text' AND is_nullable='YES')
      OR (column_name='owner_user_id' AND data_type='uuid' AND is_nullable='YES')
      OR (column_name='created_at' AND data_type='timestamp with time zone' AND is_nullable='NO')
      OR (column_name='updated_at' AND data_type='timestamp with time zone' AND is_nullable='NO')
    ),
    COUNT(*) FILTER (WHERE
      (column_name='risk_id' AND data_type='uuid' AND is_nullable='NO')
      OR (column_name='risk_number' AND data_type='integer' AND is_nullable='NO')
      OR (column_name='project_id' AND data_type='uuid' AND is_nullable='NO')
      OR (column_name='project_code_snapshot' AND data_type='character varying' AND is_nullable='NO')
      OR (column_name='risk_owner_user_id' AND data_type='uuid' AND is_nullable='NO')
      OR (column_name='next_review_date' AND data_type='date' AND is_nullable='NO')
      OR (column_name='inherent_exposure' AND data_type='smallint' AND is_nullable='YES')
      OR (column_name='revision_number' AND data_type='integer' AND is_nullable='NO')
    )
  INTO legacy_id_present,enterprise_id_present,legacy_column_count,enterprise_column_count
  FROM information_schema.columns
  WHERE table_schema='public' AND table_name='project_risks';

  IF to_regclass('public.project_risks_legacy_011') IS NOT NULL THEN
    RAISE EXCEPTION 'A stale Module 011 risk reconciliation table is present.';
  END IF;

  IF migration_077_present THEN
    IF NOT table_present OR enterprise_column_count <> 8 OR legacy_id_present THEN
      RAISE EXCEPTION 'Migration 077 is recorded but the enterprise project risk schema is not exact.';
    END IF;
    RETURN;
  END IF;

  IF NOT table_present THEN
    RETURN;
  END IF;

  IF legacy_id_present AND NOT enterprise_id_present AND legacy_column_count = 11 THEN
    IF current_setting('projectpulse.release.apply') <> 'true' THEN
      RAISE EXCEPTION 'The legacy Module 011 project risk table requires apply mode reconciliation.';
    END IF;
    EXECUTE 'ALTER TABLE public.project_risks RENAME TO project_risks_legacy_011';
    RETURN;
  END IF;

  IF enterprise_column_count = 8 AND NOT legacy_id_present THEN
    RETURN;
  END IF;

  RAISE EXCEPTION 'The existing project risk table is neither the exact Module 011 legacy shape nor the Module 082 enterprise shape.';
END
$release_legacy_risk_schema$;

SELECT to_regclass('public.project_risks_legacy_011') IS NOT NULL AS upgrade \gset legacy_risk_

SELECT
  COUNT(*) = 0 AS absent,
  COUNT(*) = 1 AND COUNT(*) FILTER (WHERE migration_id='075_pulse_product_rebrand') = 1 AS prefix075,
  COUNT(*) = 2
    AND COUNT(*) FILTER (WHERE migration_id IN (
      '075_pulse_product_rebrand',
      '076_module_081_lab_equipment_tracker'
    )) = 2 AS prefix076,
  COUNT(*) = 3
    AND COUNT(*) FILTER (WHERE migration_id IN (
      '075_pulse_product_rebrand',
      '076_module_081_lab_equipment_tracker',
      '077_module_082_enterprise_project_risk_register'
    )) = 3 AS prefix077,
  COUNT(*) = 4 AND COUNT(DISTINCT migration_id) = 4 AS complete,
  NOT (
    COUNT(*) = 0
    OR (COUNT(*) = 1 AND COUNT(*) FILTER (WHERE migration_id='075_pulse_product_rebrand') = 1)
    OR (COUNT(*) = 2 AND COUNT(*) FILTER (WHERE migration_id IN (
      '075_pulse_product_rebrand',
      '076_module_081_lab_equipment_tracker'
    )) = 2)
    OR (COUNT(*) = 3 AND COUNT(*) FILTER (WHERE migration_id IN (
      '075_pulse_product_rebrand',
      '076_module_081_lab_equipment_tracker',
      '077_module_082_enterprise_project_risk_register'
    )) = 3)
    OR (COUNT(*) = 4 AND COUNT(DISTINCT migration_id) = 4)
  ) AS inconsistent
FROM schema_migrations
WHERE migration_id IN (
  '075_pulse_product_rebrand',
  '076_module_081_lab_equipment_tracker',
  '077_module_082_enterprise_project_risk_register',
  '078_module_001a_engineer_request_closeout'
)
\gset release_target_

\if :release_target_inconsistent
  \echo ERROR: Refusing partial, duplicate, or out-of-order 075-078 migration state.
  \quit 3
\endif
\if :release_apply
  \if :release_target_complete
    \echo MODULES_081_082_001A_LEDGER=COMPLETE_RECONCILING
  \else
    \echo MODULES_081_082_001A_LEDGER=SAFE_PREFIX_APPLYING
  \endif
\else
  \if :release_target_complete
    \echo MODULES_081_082_001A_LEDGER=COMPLETE_VERIFYING
  \else
    \echo ERROR: Migrations 075-078 are incomplete in verify mode.
    \quit 3
  \endif
\endif

CREATE TEMP TABLE release_business_counts AS
SELECT
  (SELECT COUNT(*) FROM app_users) AS app_users,
  (SELECT COUNT(*) FROM projects) AS projects,
  (SELECT COUNT(*) FROM project_assignments) AS project_assignments,
  (SELECT COUNT(*) FROM project_tasks) AS project_tasks,
  (SELECT COUNT(*) FROM time_entries) AS time_entries;

SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='075_pulse_product_rebrand') AS present \gset m075_
\if :m075_present
  \echo MIGRATION_075=ALREADY_PRESENT_VERIFYING
\else
  \if :release_apply
    \echo MIGRATION_075=APPLYING
    \i :body075
  \else
    \quit 3
  \endif
\endif

SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='076_module_081_lab_equipment_tracker') AS present \gset m076_
\if :m076_present
  \echo MIGRATION_076=ALREADY_PRESENT_VERIFYING
\else
  \if :release_apply
    \echo MIGRATION_076=APPLYING
    \i :body076
  \else
    \quit 3
  \endif
\endif

SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='077_module_082_enterprise_project_risk_register') AS present \gset m077_
\if :m077_present
  \echo MIGRATION_077=ALREADY_PRESENT_VERIFYING
\else
  \if :release_apply
    \echo MIGRATION_077=APPLYING
    \i :body077
    \if :legacy_risk_upgrade
      \echo MIGRATION_077_LEGACY_RISK_DATA=CONVERGING
      WITH source AS (
        SELECT
          legacy.*,
          project.project_code,
          project.project_name,
          COALESCE(client.client_name,'') AS customer_name,
          COALESCE(
            (SELECT candidate.user_id FROM app_users candidate
             WHERE candidate.user_id=legacy.owner_user_id AND candidate.is_active=TRUE),
            (SELECT candidate.user_id FROM app_users candidate
             WHERE candidate.user_id=project.project_manager_user_id AND candidate.is_active=TRUE),
            (SELECT candidate.user_id FROM app_users candidate
             WHERE candidate.is_active=TRUE ORDER BY candidate.created_at,candidate.user_id LIMIT 1)
          ) AS actor_user_id,
          CASE lower(btrim(legacy.probability))
            WHEN 'low' THEN 1 WHEN 'medium' THEN 3 WHEN 'high' THEN 5 WHEN 'critical' THEN 5 ELSE 3
          END AS probability_value,
          CASE lower(btrim(legacy.impact))
            WHEN 'low' THEN 1 WHEN 'medium' THEN 3 WHEN 'high' THEN 5 WHEN 'critical' THEN 5 ELSE 3
          END AS impact_value,
          CASE lower(btrim(legacy.risk_status))
            WHEN 'proposed' THEN 'proposed'
            WHEN 'monitoring' THEN 'monitoring'
            WHEN 'accepted' THEN 'accepted'
            WHEN 'realized' THEN 'realized'
            WHEN 'closed' THEN 'closed'
            WHEN 'retired' THEN 'retired'
            ELSE 'open'
          END AS enterprise_status
        FROM project_risks_legacy_011 legacy
        JOIN projects project ON project.project_id=legacy.project_id
        LEFT JOIN clients client ON client.client_id=project.client_id
      )
      INSERT INTO project_risks(
        risk_id,risk_number,project_id,project_code_snapshot,project_name_snapshot,customer_name_snapshot,
        risk_title,cause_statement,uncertain_event_statement,impact_statement,description,risk_type,category,
        date_identified,identified_by_user_id,risk_owner_user_id,probability_score,
        schedule_impact_score,cost_impact_score,scope_impact_score,quality_impact_score,
        customer_impact_score,security_impact_score,compliance_impact_score,resource_impact_score,
        operational_impact_score,response_strategy,response_plan,mitigation_actions,next_review_date,
        review_cadence,risk_status,realized_at,evidence_references,created_by_user_id,updated_by_user_id,
        closed_by_user_id,created_at,updated_at,closed_at,revision_number
      )
      SELECT
        source.project_risk_id,1,source.project_id,left(source.project_code,100),left(source.project_name,255),
        left(source.customer_name,255),
        CASE WHEN length(btrim(source.risk_title)) >= 3 THEN left(btrim(source.risk_title),240)
             ELSE 'Legacy risk ' || left(source.project_risk_id::text,8) END,
        COALESCE(NULLIF(btrim(source.risk_description),''),'Legacy risk migrated from Module 011.'),
        'Legacy risk condition retained during Module 082 convergence.',
        COALESCE(NULLIF(btrim(source.risk_description),''),'Legacy risk impact retained during Module 082 convergence.'),
        COALESCE(source.risk_description,''),'threat','Legacy / Foundation',
        COALESCE(source.created_at::date,CURRENT_DATE),source.actor_user_id,source.actor_user_id,source.probability_value,
        source.impact_value,source.impact_value,source.impact_value,source.impact_value,
        source.impact_value,source.impact_value,source.impact_value,source.impact_value,source.impact_value,
        CASE WHEN NULLIF(btrim(source.mitigation_plan),'') IS NULL THEN 'accept' ELSE 'mitigate' END,
        COALESCE(source.mitigation_plan,''),COALESCE(source.mitigation_plan,''),
        GREATEST(COALESCE(source.updated_at::date,CURRENT_DATE),CURRENT_DATE),'monthly',source.enterprise_status,
        CASE WHEN source.enterprise_status='realized' THEN COALESCE(source.updated_at,NOW()) ELSE NULL END,
        jsonb_build_array(jsonb_build_object(
          'source','migration_011','legacyProjectRiskId',source.project_risk_id::text
        )),
        source.actor_user_id,source.actor_user_id,
        CASE WHEN source.enterprise_status IN ('closed','retired') THEN source.actor_user_id ELSE NULL END,
        COALESCE(source.created_at,NOW()),COALESCE(source.updated_at,source.created_at,NOW()),
        CASE WHEN source.enterprise_status IN ('closed','retired') THEN COALESCE(source.updated_at,NOW()) ELSE NULL END,
        1
      FROM source
      WHERE source.actor_user_id IS NOT NULL;

      INSERT INTO project_risk_versions(
        risk_id,project_id,version_number,risk_snapshot,change_reason,created_by_user_id,created_at
      )
      SELECT
        risk.risk_id,risk.project_id,1,to_jsonb(risk),
        'Migrated from the Module 011 project risk foundation.',risk.updated_by_user_id,risk.updated_at
      FROM project_risks risk
      JOIN project_risks_legacy_011 legacy ON legacy.project_risk_id=risk.risk_id;

      INSERT INTO project_risk_audit_events(
        project_id,risk_id,event_code,actual_actor_user_id,effective_actor_user_id,new_state,event_metadata,occurred_at
      )
      SELECT
        risk.project_id,risk.risk_id,'LEGACY_RISK_MIGRATED_077',risk.updated_by_user_id,risk.updated_by_user_id,
        to_jsonb(risk),jsonb_build_object('source','migration_011','reconciliation','module_082'),risk.updated_at
      FROM project_risks risk
      JOIN project_risks_legacy_011 legacy ON legacy.project_risk_id=risk.risk_id;

      DO $release_legacy_risk_data$
      DECLARE legacy_count bigint; migrated_count bigint; version_count bigint; audit_count bigint;
      BEGIN
        SELECT COUNT(*) INTO legacy_count FROM project_risks_legacy_011;
        SELECT COUNT(*) INTO migrated_count
        FROM project_risks risk JOIN project_risks_legacy_011 legacy ON legacy.project_risk_id=risk.risk_id;
        SELECT COUNT(*) INTO version_count
        FROM project_risk_versions version JOIN project_risks_legacy_011 legacy ON legacy.project_risk_id=version.risk_id
        WHERE version.version_number=1;
        SELECT COUNT(*) INTO audit_count
        FROM project_risk_audit_events audit JOIN project_risks_legacy_011 legacy ON legacy.project_risk_id=audit.risk_id
        WHERE audit.event_code='LEGACY_RISK_MIGRATED_077';
        IF migrated_count <> legacy_count OR version_count <> legacy_count OR audit_count <> legacy_count THEN
          RAISE EXCEPTION 'Module 011 project risk data did not reconcile completely into Module 082.';
        END IF;
      END
      $release_legacy_risk_data$;

      DROP TABLE project_risks_legacy_011;
      \echo MIGRATION_077_LEGACY_RISK_DATA=CONVERGED
    \endif
  \else
    \quit 3
  \endif
\endif

SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='078_module_001a_engineer_request_closeout') AS present \gset m078_
\if :m078_present
  \echo MIGRATION_078=ALREADY_PRESENT_VERIFYING
\else
  \if :release_apply
    \echo MIGRATION_078=APPLYING
    \i :body078
  \else
    \quit 3
  \endif
\endif

DO $release_postconditions$
DECLARE
  required_table text;
  required_permission text;
  required_feature text;
BEGIN
  IF (SELECT COUNT(*) FROM schema_migrations WHERE migration_id='075_pulse_product_rebrand') <> 1
     OR (SELECT COUNT(*) FROM schema_migrations WHERE migration_id='076_module_081_lab_equipment_tracker') <> 1
     OR (SELECT COUNT(*) FROM schema_migrations WHERE migration_id='077_module_082_enterprise_project_risk_register') <> 1
     OR (SELECT COUNT(*) FROM schema_migrations WHERE migration_id='078_module_001a_engineer_request_closeout') <> 1 THEN
    RAISE EXCEPTION 'Migration 075-078 ledger evidence is incomplete.';
  END IF;

  IF to_regclass('public.project_risks_legacy_011') IS NOT NULL THEN
    RAISE EXCEPTION 'The Module 011 project risk reconciliation table was not retired.';
  END IF;

  IF (
    SELECT COUNT(*)
    FROM information_schema.columns
    WHERE table_schema='public' AND table_name='project_risks'
      AND column_name IN (
        'risk_id','risk_number','project_id','project_code_snapshot','risk_owner_user_id',
        'next_review_date','inherent_exposure','revision_number'
      )
  ) <> 8 THEN
    RAISE EXCEPTION 'The Module 082 enterprise project risk schema is incomplete.';
  END IF;

  IF (
    SELECT COUNT(*)
    FROM pg_constraint
    WHERE conrelid='public.billing_invoices'::regclass
      AND conname='ck_billing_invoices_number_format'
      AND contype='c'
      AND pg_get_constraintdef(oid) LIKE '%PHD|PULSE%'
  ) <> 1 THEN
    RAISE EXCEPTION 'Migration 075 invoice compatibility constraint is unavailable.';
  END IF;

  IF to_regprocedure('public.reserve_project_invoice_number(uuid)') IS NULL
     OR POSITION(
       '''PULSE-''' IN pg_get_functiondef(to_regprocedure('public.reserve_project_invoice_number(uuid)'))
     ) = 0
     OR POSITION(
       '''PHD-''' IN pg_get_functiondef(to_regprocedure('public.reserve_project_invoice_number(uuid)'))
     ) > 0 THEN
    RAISE EXCEPTION 'Migration 075 Pulse invoice generator is unavailable.';
  END IF;

  FOREACH required_table IN ARRAY ARRAY[
    'lab_equipment', 'lab_ip_allocations', 'lab_cable_connections', 'lab_rack_reservations',
    'lab_import_batches', 'lab_import_rows', 'lab_equipment_audit_events',
    'project_risk_counters', 'project_risks', 'project_risk_versions', 'project_risk_actions',
    'project_risk_action_history', 'project_risk_audit_events',
    'module001a_engineer_task_closeouts', 'module001a_engineer_task_closeout_events'
  ] LOOP
    IF to_regclass('public.' || required_table) IS NULL THEN
      RAISE EXCEPTION 'Required release table is missing: %', required_table;
    END IF;
  END LOOP;

  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='project_assignments' AND column_name='module001a_closeout_status'
  ) THEN
    RAISE EXCEPTION 'Module 001A billing-lock projection column is missing.';
  END IF;

  IF (SELECT COUNT(*) FROM pg_trigger WHERE tgname IN (
    'trg_lab_equipment_touch_076', 'trg_lab_ip_touch_076', 'trg_lab_connection_touch_076',
    'trg_lab_ip_validate_076', 'trg_lab_rack_validate_076', 'trg_lab_audit_immutable_076',
    'trg_project_risk_number_077', 'trg_project_risk_owner_077', 'trg_project_risk_touch_077',
    'trg_project_risk_action_validate_077', 'trg_project_risk_action_touch_077',
    'trg_project_risk_versions_immutable_077', 'trg_project_risk_action_history_immutable_077',
    'trg_project_risk_audit_immutable_077', 'trg_module001a_closeout_touch_078',
    'trg_module001a_events_immutable_078', 'trg_module001a_time_guard_078',
    'trg_module001a_project_final_078', 'trg_module001a_task_final_078'
  ) AND NOT tgisinternal AND tgenabled <> 'D') <> 19 THEN
    RAISE EXCEPTION 'One or more migration 076-078 database controls are unavailable.';
  END IF;

  FOREACH required_permission IN ARRAY ARRAY[
    'VIEW_LAB_EQUIPMENT_081', 'MANAGE_LAB_EQUIPMENT_081',
    'VIEW_PROJECT_RISKS_082', 'MANAGE_PROJECT_RISKS_082',
    'VIEW_ENGINEER_TASK_CLOSEOUT_001A', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'
  ] LOOP
    IF (SELECT COUNT(*) FROM app_permissions WHERE permission_code=required_permission) <> 1 THEN
      RAISE EXCEPTION 'Required release permission is missing or duplicated: %', required_permission;
    END IF;
  END LOOP;

  FOREACH required_feature IN ARRAY ARRAY[
    'LAB_EQUIPMENT_TRACKER_081', 'ENTERPRISE_PROJECT_RISK_REGISTER_082', 'ENGINEER_TASK_CLOSEOUT_001A'
  ] LOOP
    IF (SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code=required_feature AND is_active=TRUE) <> 1 THEN
      RAISE EXCEPTION 'Required release feature is missing, duplicated, or inactive: %', required_feature;
    END IF;
  END LOOP;

  IF EXISTS (
    SELECT 1 FROM release_business_counts before
    WHERE before.app_users <> (SELECT COUNT(*) FROM app_users)
       OR before.projects <> (SELECT COUNT(*) FROM projects)
       OR before.project_assignments <> (SELECT COUNT(*) FROM project_assignments)
       OR before.project_tasks <> (SELECT COUNT(*) FROM project_tasks)
       OR before.time_entries <> (SELECT COUNT(*) FROM time_entries)
  ) THEN
    RAISE EXCEPTION 'Core business row counts changed while applying migrations 075-078.';
  END IF;
END
$release_postconditions$;

COMMIT;
\echo MODULES_081_082_001A_MIGRATIONS_075_078=VERIFIED
SQL

echo "MODULES_081_082_001A_MIGRATION_MODE=$MODE"
