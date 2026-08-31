#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="${PROJECTPULSE_RELEASE_ROOT:-$(pwd -P)}"
ACR_NAME="${AZURE_ACR_NAME:-}"
RELEASE_COMMIT="${RELIABILITY_RELEASE_COMMIT:-}"
RUN_ID="${GITHUB_RUN_ID:-0}"
RUN_ATTEMPT="${GITHUB_RUN_ATTEMPT:-0}"
EVIDENCE_ROOT="${EVIDENCE_DIR:-}"
MIGRATION_098_OWNER="$ROOT/database/migrations/098_module_management_owner_storage_reconciliation.sql"
MIGRATION_098_CUSTOMER="$ROOT/database/migrations/098_customer_directory_source_authority.sql"
MIGRATION_099="$ROOT/database/migrations/099_module025_sow_gsd_workspace.sql"
PRIVATE_NETWORK_LAUNCHER="$ROOT/scripts/release-test/run-project-planning-collaboration-migration-job.sh"
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
[[ -s "$MIGRATION_098_OWNER" ]] || fail "Migration 098 Module Management owner source is missing."
[[ -s "$MIGRATION_098_CUSTOMER" ]] || fail "Migration 098 customer-source authority source is missing."
[[ -s "$MIGRATION_099" ]] || fail "Migration 099 Module 025 SOW/GSD source is missing."
[[ -s "$PRIVATE_NETWORK_LAUNCHER" ]] || fail "Protected-Test private-network migration launcher is missing."
for command_name in az jq mktemp install chmod; do
  command -v "$command_name" >/dev/null 2>&1 || fail "$command_name is required."
done

CONTEXT="$(mktemp -d "${RUNNER_TEMP:-/tmp}/release-migrations-098-099-${RUN_ID}-${RUN_ATTEMPT}-XXXXXX")"
chmod 0700 "$CONTEXT"
install -d -m 0700 "$CONTEXT/database/migrations"
install -m 0444 "$MIGRATION_098_OWNER" "$CONTEXT/database/migrations/098_module_management_owner_storage_reconciliation.sql"
install -m 0444 "$MIGRATION_098_CUSTOMER" "$CONTEXT/database/migrations/098_customer_directory_source_authority.sql"
install -m 0444 "$MIGRATION_099" "$CONTEXT/database/migrations/099_module025_sow_gsd_workspace.sql"
printf '%s\n' "$RELEASE_COMMIT" > "$CONTEXT/release-commit"
chmod 0444 "$CONTEXT/release-commit"

cat > "$CONTEXT/entrypoint.sh" <<'ENTRYPOINT'
#!/usr/bin/env bash
set -Eeuo pipefail
ROOT=/opt/projectpulse/release
EXPECTED="${MAIN_RELEASE_EXPECTED_RELEASE_COMMIT:-}"
ACTUAL="$(cat "$ROOT/.projectpulse-release-commit")"
[[ "$EXPECTED" =~ ^[0-9a-f]{40}$ && "$ACTUAL" == "$EXPECTED" ]] || {
  echo 'ERROR: Protected-Test migration 098/099 image release identity mismatch.' >&2
  exit 1
}

for migration in \
  "$ROOT/database/migrations/098_module_management_owner_storage_reconciliation.sql" \
  "$ROOT/database/migrations/098_customer_directory_source_authority.sql" \
  "$ROOT/database/migrations/099_module025_sow_gsd_workspace.sql"; do
  [[ -f "$migration" ]] || {
    echo "ERROR: Required release migration is missing from the immutable image: $migration" >&2
    exit 1
  }
  psql -X -v ON_ERROR_STOP=1 --file "$migration"
done

verification="$(psql -X -At -v ON_ERROR_STOP=1 <<'SQL'
SELECT
  EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='098_module_management_owner_storage_reconciliation')::text || '|' ||
  EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='098_customer_directory_source_authority')::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='scoped_role_policy_modules' AND column_name='owner_user_id')::text || '|' ||
  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='scoped_role_policy_modules' AND column_name='owner_revision_number' AND is_nullable='NO')::text || '|' ||
  (to_regclass('public.customer_directory_source_authority') IS NOT NULL)::text || '|' ||
  (to_regclass('public.customer_directory_source_authority_history') IS NOT NULL)::text || '|' ||
  EXISTS(SELECT 1 FROM customer_directory_source_authority WHERE customer_source_authority_id=1 AND source_mode IN ('sell','crm','manual'))::text || '|' ||
  (to_regclass('public.module025_sow_gsd_engagements') IS NOT NULL)::text || '|' ||
  (to_regclass('public.module025_sow_gsd_phases') IS NOT NULL)::text || '|' ||
  (to_regclass('public.module025_sow_gsd_events') IS NOT NULL)::text || '|' ||
  EXISTS(
    SELECT 1 FROM pg_trigger
    WHERE tgrelid='public.module025_sow_gsd_engagements'::regclass
      AND tgname='trg_module025_protect_sow_gsd_identity'
      AND NOT tgisinternal
  )::text;
SQL
)"
[[ "$verification" == 'true|true|true|true|true|true|true|true|true|true|true' ]] || {
  echo "ERROR: Protected-Test migration 098/099 verification failed: $verification" >&2
  exit 1
}

echo 'MIGRATION_098_MODULE_MANAGEMENT_OWNER=APPLIED_AND_VERIFIED'
echo 'MIGRATION_098_CUSTOMER_DIRECTORY_SOURCE_AUTHORITY=APPLIED_AND_VERIFIED'
echo 'MIGRATION_099_MODULE025_SOW_GSD_WORKSPACE=APPLIED_AND_VERIFIED'
ENTRYPOINT
chmod 0555 "$CONTEXT/entrypoint.sh"

cat > "$CONTEXT/Dockerfile" <<'DOCKERFILE'
FROM postgres:16-alpine
RUN apk add --no-cache bash coreutils ca-certificates
WORKDIR /opt/projectpulse/release
COPY release-commit .projectpulse-release-commit
COPY database/ database/
COPY entrypoint.sh /usr/local/bin/release-migrations-098-099
RUN chmod 0555 /usr/local/bin/release-migrations-098-099 \
    && chmod 0444 .projectpulse-release-commit database/migrations/*.sql
ENTRYPOINT ["/usr/local/bin/release-migrations-098-099"]
DOCKERFILE

SHORT_RELEASE="${RELEASE_COMMIT:0:12}"
REPOSITORY="project-health-dashboard-release-migrator"
TAG="rel-${SHORT_RELEASE}-${RUN_ID}-${RUN_ATTEMPT}"
IMAGE="$REPOSITORY:$TAG"
BUILD_SUCCEEDED=0
for attempt in 1 2; do
  if az acr build \
      --registry "$ACR_NAME" \
      --image "$IMAGE" \
      --file "$CONTEXT/Dockerfile" \
      --timeout 1800 \
      "$CONTEXT"; then
    BUILD_SUCCEEDED=1
    break
  fi
  (( attempt < 2 )) && sleep $((attempt * 15))
done
(( BUILD_SUCCEEDED == 1 )) || fail "Protected-Test migration 098/099 immutable image build failed."

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
[[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] || fail "Protected-Test migration 098/099 immutable digest could not be resolved."

export RELIABILITY_MIGRATION_IMAGE="$ACR_NAME.azurecr.io/$REPOSITORY@$DIGEST"
export RELIABILITY_MIGRATION_JOB_NAME="pp099-${RUN_ID}-${RUN_ATTEMPT}"
export RELIABILITY_MIGRATION_SCOPE="release-migrations-098-099-test"
bash "$PRIVATE_NETWORK_LAUNCHER"

echo 'MIGRATION_098_MODULE_MANAGEMENT_OWNER=APPLIED_AND_VERIFIED'
echo 'MIGRATION_098_CUSTOMER_DIRECTORY_SOURCE_AUTHORITY=APPLIED_AND_VERIFIED'
echo 'MIGRATION_099_MODULE025_SOW_GSD_WORKSPACE=APPLIED_AND_VERIFIED'

if [[ -n "$EVIDENCE_ROOT" ]]; then
  install -d -m 0700 "$EVIDENCE_ROOT"
  jq -n \
    --arg releaseCommit "$RELEASE_COMMIT" \
    --arg image "$RELIABILITY_MIGRATION_IMAGE" \
    '{status:"applied_and_verified",migrations:["098_module_management_owner_storage_reconciliation","098_customer_directory_source_authority","099_module025_sow_gsd_workspace"],releaseCommit:$releaseCommit,image:$image,environment:"protected-test",privateNetworkJob:true,productionMutation:false}' \
    > "$EVIDENCE_ROOT/migrations-098-099.json"
fi
