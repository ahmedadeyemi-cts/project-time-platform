#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="${PROJECTPULSE_RELEASE_ROOT:-$(pwd -P)}"
ACR_NAME="${AZURE_ACR_NAME:-}"
RELEASE_COMMIT="${RELIABILITY_RELEASE_COMMIT:-}"
RUN_ID="${GITHUB_RUN_ID:-0}"
RUN_ATTEMPT="${GITHUB_RUN_ATTEMPT:-0}"
MIGRATION_FILE="$ROOT/database/migrations/096_project_planning_document_authority.sql"
IDENTITY_SAFE_MIGRATION_FILE="$ROOT/database/migrations/097_project_planning_identity_safe_admission.sql"
OWNER_STORAGE_MIGRATION_FILE="$ROOT/database/migrations/098_module_management_owner_storage_reconciliation.sql"
CUSTOMER_SOURCE_MIGRATION_FILE="$ROOT/database/migrations/098_customer_directory_source_authority.sql"
MODULE025_MIGRATION_FILE="$ROOT/database/migrations/099_module025_sow_gsd_workspace.sql"
MODULE001B_CATALOG_MIGRATION_FILE="$ROOT/database/migrations/100_module001b_catalog_ownership_reconciliation.sql"
MIGRATION_RUNNER="$ROOT/scripts/release-test/run-project-planning-document-authority-migration-job.sh"
EVIDENCE_ROOT="${EVIDENCE_DIR:-}"
CONTEXT=""

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

cleanup() {
  local status=$?
  trap - EXIT INT TERM
  if [[ -n "$CONTEXT" && -d "$CONTEXT" ]]; then
    chmod -R u+rwX "$CONTEXT" 2>/dev/null || true
    rm -rf "$CONTEXT"
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

[[ "$ACR_NAME" =~ ^[A-Za-z0-9]+$ ]] || fail "AZURE_ACR_NAME is missing or invalid."
[[ "$RELEASE_COMMIT" =~ ^[0-9a-f]{40}$ ]] || fail "RELIABILITY_RELEASE_COMMIT must be an exact commit."
[[ -s "$MIGRATION_FILE" ]] || fail "Migration 096 source is missing."
[[ -s "$IDENTITY_SAFE_MIGRATION_FILE" ]] || fail "Migration 097 source is missing."
[[ -s "$OWNER_STORAGE_MIGRATION_FILE" ]] || fail "Module owner storage migration 098 source is missing."
[[ -s "$CUSTOMER_SOURCE_MIGRATION_FILE" ]] || fail "Customer source authority migration 098 source is missing."
[[ -s "$MODULE025_MIGRATION_FILE" ]] || fail "Module 025 SOW/GSD migration 099 source is missing."
[[ -s "$MODULE001B_CATALOG_MIGRATION_FILE" ]] || fail "Module 001B catalog migration 100 source is missing."
[[ -s "$MIGRATION_RUNNER" ]] || fail "Migration 096 private-network runner is missing."
for command_name in az jq mktemp install chmod; do
  command -v "$command_name" >/dev/null 2>&1 || fail "$command_name is required."
done

CONTEXT="$(mktemp -d "${RUNNER_TEMP:-/tmp}/project-planning-096-${RUN_ID}-${RUN_ATTEMPT}-XXXXXX")"
chmod 0700 "$CONTEXT"
install -d -m 0700 "$CONTEXT/database/migrations"
install -m 0444 "$MIGRATION_FILE" "$CONTEXT/database/migrations/096_project_planning_document_authority.sql"
install -m 0444 "$IDENTITY_SAFE_MIGRATION_FILE" "$CONTEXT/database/migrations/097_project_planning_identity_safe_admission.sql"
install -m 0444 "$OWNER_STORAGE_MIGRATION_FILE" "$CONTEXT/database/migrations/098_module_management_owner_storage_reconciliation.sql"
install -m 0444 "$CUSTOMER_SOURCE_MIGRATION_FILE" "$CONTEXT/database/migrations/098_customer_directory_source_authority.sql"
install -m 0444 "$MODULE025_MIGRATION_FILE" "$CONTEXT/database/migrations/099_module025_sow_gsd_workspace.sql"
install -m 0444 "$MODULE001B_CATALOG_MIGRATION_FILE" "$CONTEXT/database/migrations/100_module001b_catalog_ownership_reconciliation.sql"
install -m 0444 "$ROOT/database/migrations/101_deepseek_v4_provider.sql" "$CONTEXT/database/migrations/101_deepseek_v4_provider.sql"
printf '%s\n' "$RELEASE_COMMIT" > "$CONTEXT/release-commit"
chmod 0444 "$CONTEXT/release-commit"

cat > "$CONTEXT/entrypoint.sh" <<'ENTRYPOINT'
#!/usr/bin/env bash
set -Eeuo pipefail
ROOT=/opt/projectpulse/release
EXPECTED="${MAIN_RELEASE_EXPECTED_RELEASE_COMMIT:-}"
ACTUAL="$(cat "$ROOT/.projectpulse-release-commit")"
[[ "$EXPECTED" =~ ^[0-9a-f]{40}$ && "$ACTUAL" == "$EXPECTED" ]] || {
  echo 'ERROR: Migration 096 image release identity mismatch.' >&2
  exit 1
}
MIGRATION="$ROOT/database/migrations/096_project_planning_document_authority.sql"
[[ -f "$MIGRATION" ]] || {
  echo 'ERROR: Migration 096 source is missing from the immutable image.' >&2
  exit 1
}
psql -X -v ON_ERROR_STOP=1 --file "$MIGRATION"
IDENTITY_SAFE_MIGRATION="$ROOT/database/migrations/097_project_planning_identity_safe_admission.sql"
[[ -f "$IDENTITY_SAFE_MIGRATION" ]] || { echo 'ERROR: Migration 097 source is missing from the immutable image.' >&2; exit 1; }
psql -X -v ON_ERROR_STOP=1 --file "$IDENTITY_SAFE_MIGRATION"
OWNER_STORAGE_MIGRATION="$ROOT/database/migrations/098_module_management_owner_storage_reconciliation.sql"
[[ -f "$OWNER_STORAGE_MIGRATION" ]] || { echo 'ERROR: Module owner storage migration 098 source is missing from the immutable image.' >&2; exit 1; }
psql -X -v ON_ERROR_STOP=1 --file "$OWNER_STORAGE_MIGRATION"
CUSTOMER_SOURCE_MIGRATION="$ROOT/database/migrations/098_customer_directory_source_authority.sql"
[[ -f "$CUSTOMER_SOURCE_MIGRATION" ]] || { echo 'ERROR: Customer source authority migration 098 source is missing from the immutable image.' >&2; exit 1; }
psql -X -v ON_ERROR_STOP=1 --file "$CUSTOMER_SOURCE_MIGRATION"
MODULE025_MIGRATION="$ROOT/database/migrations/099_module025_sow_gsd_workspace.sql"
[[ -f "$MODULE025_MIGRATION" ]] || { echo 'ERROR: Module 025 SOW/GSD migration 099 source is missing from the immutable image.' >&2; exit 1; }
psql -X -v ON_ERROR_STOP=1 --file "$MODULE025_MIGRATION"
MODULE001B_CATALOG_MIGRATION="$ROOT/database/migrations/100_module001b_catalog_ownership_reconciliation.sql"
[[ -f "$MODULE001B_CATALOG_MIGRATION" ]] || { echo 'ERROR: Module 001B catalog migration 100 source is missing from the immutable image.' >&2; exit 1; }
psql -X -v ON_ERROR_STOP=1 --file "$MODULE001B_CATALOG_MIGRATION"
psql -X -v ON_ERROR_STOP=1 --file "$ROOT/database/migrations/101_deepseek_v4_provider.sql"
[[ "$(psql -X -At -v ON_ERROR_STOP=1 -c "SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='101_deepseek_v4_provider')")" == t ]] || exit 1
echo 'MIGRATION_101_DEEPSEEK_V4=APPLIED_AND_VERIFIED'
verification="$(psql -X -At -v ON_ERROR_STOP=1 <<'SQL'
SELECT
  EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='096_project_planning_document_authority')::text || '|' ||
  (to_regclass('public.project_planning_document_authority') IS NOT NULL)::text || '|' ||
  (to_regclass('public.current_project_planning_document_authority') IS NOT NULL)::text || '|' ||
  (to_regprocedure('public.projectpulse_reconcile_project_planning_document_authority(uuid,uuid,uuid,text,text,uuid,text,text,text,text,text,text,jsonb)') IS NOT NULL)::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='project_planning_document_authority' AND column_name='source_sha256')::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='project_planning_document_authority' AND column_name='document_version_id')::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='project_planning_document_authority' AND column_name='is_current')::text;
SQL
)"
[[ "$verification" == 'true|true|true|true|true|true|true' ]] || {
  echo "ERROR: Migration 096 verification failed: $verification" >&2
  exit 1
}
identity_safe_verification="$(psql -X -At -v ON_ERROR_STOP=1 <<'SQL'
SELECT
  EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='097_project_planning_identity_safe_admission')::text || '|' ||
  (NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgrelid='public.project_intake_documents'::regclass AND tgname IN ('trg_module001_057_queue_project_ai_document_insert','trg_module001_057_queue_project_ai_document_update') AND NOT tgisinternal))::text || '|' ||
  (to_regprocedure('public.module001_057_queue_project_ai_document()') IS NULL)::text || '|' ||
  (NOT EXISTS (SELECT 1 FROM pulse_ai_document_processing_jobs WHERE requested_purpose='project_ai_generation_grounding' AND actual_user_id IS NULL AND effective_user_id IS NULL AND job_status IN ('queued','scanning','extracting','awaiting_ocr','embedding','indexing','retry_wait','cancel_requested')))::text;
SQL
)"
[[ "$identity_safe_verification" == 'true|true|true|true' ]] || {
  echo "ERROR: Migration 097 verification failed: $identity_safe_verification" >&2
  exit 1
}
owner_storage_verification="$(psql -X -At -v ON_ERROR_STOP=1 <<'SQL'
SELECT
  EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='098_module_management_owner_storage_reconciliation')::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='scoped_role_policy_modules' AND column_name='owner_user_id')::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='scoped_role_policy_modules' AND column_name='owner_revision_number' AND is_nullable='NO')::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='scoped_role_policy_modules' AND column_name='owner_updated_at')::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='scoped_role_policy_modules' AND column_name='owner_updated_by_user_id')::text || '|' ||
  EXISTS(SELECT 1 FROM pg_constraint WHERE conname='fk_scoped_role_policy_modules_owner_user' AND conrelid='public.scoped_role_policy_modules'::regclass)::text || '|' ||
  EXISTS(SELECT 1 FROM pg_constraint WHERE conname='fk_scoped_role_policy_modules_owner_updated_by' AND conrelid='public.scoped_role_policy_modules'::regclass)::text;
SQL
)"
[[ "$owner_storage_verification" == 'true|true|true|true|true|true|true' ]] || {
  echo "ERROR: Module owner storage migration 098 verification failed: $owner_storage_verification" >&2
  exit 1
}
customer_source_verification="$(psql -X -At -v ON_ERROR_STOP=1 <<'SQL'
SELECT
  EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='098_customer_directory_source_authority')::text || '|' ||
  (to_regclass('public.customer_directory_source_authority') IS NOT NULL)::text || '|' ||
  (to_regclass('public.customer_directory_source_authority_history') IS NOT NULL)::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='customer_directory_source_authority' AND column_name='source_mode')::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='customer_directory_source_authority' AND column_name='provider_key')::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='customer_directory_source_links' AND column_name='source_system' AND character_maximum_length >= 120)::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='customer_directory_sync_runs' AND column_name='source_system' AND character_maximum_length >= 120)::text;
SQL
)"
[[ "$customer_source_verification" == 'true|true|true|true|true|true|true' ]] || {
  echo "ERROR: Customer source authority migration 098 verification failed: $customer_source_verification" >&2
  exit 1
}
module025_verification="$(psql -X -At -v ON_ERROR_STOP=1 <<'SQL'
SELECT
  (to_regclass('public.module025_sow_gsd_engagements') IS NOT NULL)::text || '|' ||
  (to_regclass('public.module025_sow_gsd_phases') IS NOT NULL)::text || '|' ||
  (to_regclass('public.module025_sow_gsd_events') IS NOT NULL)::text || '|' ||
  (to_regprocedure('public.module025_protect_sow_gsd_identity()') IS NOT NULL)::text || '|' ||
  EXISTS(SELECT 1 FROM pg_trigger WHERE tgrelid='public.module025_sow_gsd_engagements'::regclass AND tgname='trg_module025_protect_sow_gsd_identity' AND NOT tgisinternal)::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='module025_sow_gsd_engagements' AND column_name='commercial_model')::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='module025_sow_gsd_phases' AND column_name='suggested_hours')::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='module025_sow_gsd_phases' AND column_name='final_hours')::text;
SQL
)"
[[ "$module025_verification" == 'true|true|true|true|true|true|true|true' ]] || {
  echo "ERROR: Module 025 SOW/GSD migration 099 verification failed: $module025_verification" >&2
  exit 1
}
module001b_catalog_verification="$(psql -X -At -v ON_ERROR_STOP=1 <<'SQL'
SELECT
  EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='100_module001b_catalog_ownership_reconciliation')::text || '|' ||
  EXISTS(
    SELECT 1
    FROM scoped_role_policy_modules
    WHERE module_code='001B'
      AND module_name='Time Reallocation & Corrections'
      AND route_scope='time-reallocation'
      AND current_state='Installed'
      AND is_active=TRUE
      AND owner_revision_number IS NOT NULL
  )::text || '|' ||
  (to_regclass('public.module_catalog_reconciliation_100_module001b_evidence') IS NOT NULL)::text;
SQL
)"
[[ "$module001b_catalog_verification" == 'true|true|true' ]] || {
  echo "ERROR: Module 001B catalog migration 100 verification failed: $module001b_catalog_verification" >&2
  exit 1
}
echo 'MIGRATION_096=APPLIED_AND_VERIFIED'
echo 'MIGRATION_097=APPLIED_AND_VERIFIED'
echo 'MIGRATION_098_OWNER_STORAGE=APPLIED_AND_VERIFIED'
echo 'MIGRATION_098_CUSTOMER_SOURCE=APPLIED_AND_VERIFIED'
echo 'MIGRATION_099_MODULE025_SOW_GSD=APPLIED_AND_VERIFIED'
echo 'MIGRATION_100_MODULE001B_CATALOG=APPLIED_AND_VERIFIED'
ENTRYPOINT
chmod 0555 "$CONTEXT/entrypoint.sh"

DOCKERFILE="$CONTEXT/Dockerfile"
cat > "$DOCKERFILE" <<'DOCKERFILE_CONTENT'
FROM postgres:16-alpine
RUN apk add --no-cache bash coreutils ca-certificates
WORKDIR /opt/projectpulse/release
COPY release-commit .projectpulse-release-commit
COPY database/ database/
COPY entrypoint.sh /usr/local/bin/project-planning-document-authority-migrate
RUN chmod 0555 /usr/local/bin/project-planning-document-authority-migrate \
    && chmod 0444 .projectpulse-release-commit database/migrations/*.sql
ENTRYPOINT ["/usr/local/bin/project-planning-document-authority-migrate"]
DOCKERFILE_CONTENT
[[ -f "$DOCKERFILE" ]] || fail "Migration 096 Dockerfile was not created in its ACR build context."

SHORT_RELEASE="${RELEASE_COMMIT:0:12}"
REPOSITORY="project-health-dashboard-document-authority-migrator"
TAG="rel-${SHORT_RELEASE}-${RUN_ID}-${RUN_ATTEMPT}"
IMAGE="$REPOSITORY:$TAG"
BUILD_SUCCEEDED=0
for attempt in 1 2; do
  if az acr build \
      --registry "$ACR_NAME" \
      --image "$IMAGE" \
      --file "$DOCKERFILE" \
      --timeout 1800 \
      "$CONTEXT"; then
    BUILD_SUCCEEDED=1
    break
  fi
  (( attempt < 2 )) && sleep $((attempt * 15))
done
(( BUILD_SUCCEEDED == 1 )) || fail "Migration 096 immutable image build failed."

DIGEST=""
for attempt in $(seq 1 12); do
  DIGEST="$(az acr repository show \
    --name "$ACR_NAME" \
    --image "$IMAGE" \
    --query digest \
    -o tsv \
    --only-show-errors 2>/dev/null || true)"
  if [[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]]; then
    break
  fi
  (( attempt < 12 )) && sleep 5
done
[[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] || fail "Migration 096 immutable digest could not be resolved."

export RELIABILITY_MIGRATION_IMAGE="$ACR_NAME.azurecr.io/$REPOSITORY@$DIGEST"
export RELIABILITY_MIGRATION_JOB_NAME="pp096-${RUN_ID}-${RUN_ATTEMPT}"
export RELIABILITY_MIGRATION_SCOPE="project-planning-document-authority-test"
bash "$MIGRATION_RUNNER"
echo 'MIGRATION_096=APPLIED_AND_VERIFIED'
echo 'MIGRATION_097=APPLIED_AND_VERIFIED'
echo 'MIGRATION_098_OWNER_STORAGE=APPLIED_AND_VERIFIED'
echo 'MIGRATION_098_CUSTOMER_SOURCE=APPLIED_AND_VERIFIED'
echo 'MIGRATION_099_MODULE025_SOW_GSD=APPLIED_AND_VERIFIED'
echo 'MIGRATION_100_MODULE001B_CATALOG=APPLIED_AND_VERIFIED'

if [[ -n "$EVIDENCE_ROOT" ]]; then
  install -d -m 0700 "$EVIDENCE_ROOT"
  jq -n \
    --arg releaseCommit "$RELEASE_COMMIT" \
    --arg image "$RELIABILITY_MIGRATION_IMAGE" \
    '{status:"applied_and_verified",migration:"096_project_planning_document_authority",releaseCommit:$releaseCommit,image:$image,environment:"protected-test",privateNetworkJob:true,productionMutation:false}' \
    > "$EVIDENCE_ROOT/migration-096.json"
fi

if [[ -n "$EVIDENCE_ROOT" ]]; then
  jq -n \
    --arg releaseCommit "$RELEASE_COMMIT" \
    --arg image "$RELIABILITY_MIGRATION_IMAGE" \
    '{status:"applied_and_verified",migration:"097_project_planning_identity_safe_admission",releaseCommit:$releaseCommit,image:$image,environment:"protected-test",privateNetworkJob:true,productionMutation:false}' \
    > "$EVIDENCE_ROOT/migration-097.json"
fi

if [[ -n "$EVIDENCE_ROOT" ]]; then
  jq -n \
    --arg releaseCommit "$RELEASE_COMMIT" \
    --arg image "$RELIABILITY_MIGRATION_IMAGE" \
    '{status:"applied_and_verified",migration:"098_module_management_owner_storage_reconciliation",releaseCommit:$releaseCommit,image:$image,environment:"protected-test",privateNetworkJob:true,productionMutation:false}' \
    > "$EVIDENCE_ROOT/migration-098.json"
fi

if [[ -n "$EVIDENCE_ROOT" ]]; then
  jq -n \
    --arg releaseCommit "$RELEASE_COMMIT" \
    --arg image "$RELIABILITY_MIGRATION_IMAGE" \
    '{status:"applied_and_verified",migration:"098_customer_directory_source_authority",releaseCommit:$releaseCommit,image:$image,environment:"protected-test",privateNetworkJob:true,productionMutation:false}' \
    > "$EVIDENCE_ROOT/migration-098-customer-source.json"
fi

if [[ -n "$EVIDENCE_ROOT" ]]; then
  jq -n \
    --arg releaseCommit "$RELEASE_COMMIT" \
    --arg image "$RELIABILITY_MIGRATION_IMAGE" \
    '{status:"applied_and_verified",migration:"099_module025_sow_gsd_workspace",releaseCommit:$releaseCommit,image:$image,environment:"protected-test",privateNetworkJob:true,productionMutation:false}' \
    > "$EVIDENCE_ROOT/migration-099.json"
fi

if [[ -n "$EVIDENCE_ROOT" ]]; then
  jq -n \
    --arg releaseCommit "$RELEASE_COMMIT" \
    --arg image "$RELIABILITY_MIGRATION_IMAGE" \
    '{status:"applied_and_verified",migration:"100_module001b_catalog_ownership_reconciliation",releaseCommit:$releaseCommit,image:$image,environment:"protected-test",privateNetworkJob:true,productionMutation:false}' \
    > "$EVIDENCE_ROOT/migration-100.json"
fi
