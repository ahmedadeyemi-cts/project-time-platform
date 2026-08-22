#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="${PROJECTPULSE_RELEASE_ROOT:-$(pwd -P)}"
ACR_NAME="${AZURE_ACR_NAME:-}"
RELEASE_COMMIT="${RELIABILITY_RELEASE_COMMIT:-${FLOWHIVE_RELEASE_COMMIT:-}}"
RUN_ID="${GITHUB_RUN_ID:-0}"
RUN_ATTEMPT="${GITHUB_RUN_ATTEMPT:-0}"
MIGRATION_FILE="$ROOT/database/migrations/094_flowhive_canonical_sow_authority.sql"
MIGRATION_RUNNER="$ROOT/scripts/release-test/run-flowhive-authority-migration-094-job.sh"
CONTEXT=""

fail() { echo "ERROR: $*" >&2; exit 1; }
cleanup() { local status=$?; trap - EXIT INT TERM; [[ -z "$CONTEXT" || ! -d "$CONTEXT" ]] || rm -rf "$CONTEXT"; exit "$status"; }
trap cleanup EXIT INT TERM

[[ "$ACR_NAME" =~ ^[A-Za-z0-9]+$ ]] || fail "AZURE_ACR_NAME is missing or invalid."
[[ "$RELEASE_COMMIT" =~ ^[0-9a-f]{40}$ ]] || fail "The exact protected-Test release commit is required."
[[ -s "$MIGRATION_FILE" ]] || fail "Migration 094 source is missing."
[[ -s "$MIGRATION_RUNNER" ]] || fail "Migration 094 runner is missing."
for command_name in az jq mktemp install chmod; do command -v "$command_name" >/dev/null || fail "$command_name is required."; done

CONTEXT="$(mktemp -d "${RUNNER_TEMP:-/tmp}/flowhive-094-${RUN_ID}-${RUN_ATTEMPT}-XXXXXX")"
chmod 0700 "$CONTEXT"
install -d -m 0700 "$CONTEXT/database/migrations"
install -m 0444 "$MIGRATION_FILE" "$CONTEXT/database/migrations/094_flowhive_canonical_sow_authority.sql"
printf '%s\n' "$RELEASE_COMMIT" > "$CONTEXT/release-commit"
chmod 0444 "$CONTEXT/release-commit"
cat > "$CONTEXT/entrypoint.sh" <<'ENTRYPOINT'
#!/usr/bin/env bash
set -Eeuo pipefail
ROOT=/opt/projectpulse/release
EXPECTED="${FLOWHIVE_EXPECTED_RELEASE_COMMIT:-}"
ACTUAL="$(cat "$ROOT/.projectpulse-release-commit")"
[[ "$EXPECTED" =~ ^[0-9a-f]{40}$ && "$ACTUAL" == "$EXPECTED" ]]
psql -X -v ON_ERROR_STOP=1 --file "$ROOT/database/migrations/094_flowhive_canonical_sow_authority.sql"
verification="$(psql -X -At -v ON_ERROR_STOP=1 <<'SQL'
SELECT
  EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='094_flowhive_canonical_sow_authority')::text || '|' ||
  (to_regclass('public.module094_flowhive_sow_authority_evidence') IS NOT NULL)::text || '|' ||
  (to_regprocedure('public.projectpulse094_reconcile_ready_work_register_sow(uuid)') IS NOT NULL)::text;
SQL
)"
[[ "$verification" == 'true|true|true' ]] || { echo "ERROR: Migration 094 verification failed: $verification" >&2; exit 1; }
echo 'MIGRATION_094=APPLIED_AND_VERIFIED'
ENTRYPOINT
chmod 0555 "$CONTEXT/entrypoint.sh"
cat > "$CONTEXT/Dockerfile" <<'DOCKERFILE'
FROM postgres:16-alpine
RUN apk add --no-cache bash coreutils ca-certificates
WORKDIR /opt/projectpulse/release
COPY release-commit .projectpulse-release-commit
COPY database/ database/
COPY entrypoint.sh /usr/local/bin/flowhive-authority-migrate
RUN chmod 0555 /usr/local/bin/flowhive-authority-migrate && chmod 0444 .projectpulse-release-commit database/migrations/*.sql
ENTRYPOINT ["/usr/local/bin/flowhive-authority-migrate"]
DOCKERFILE

SHORT_RELEASE="${RELEASE_COMMIT:0:12}"
REPOSITORY="project-health-dashboard-flowhive-authority-migrator"
TAG="rel-${SHORT_RELEASE}-${RUN_ID}-${RUN_ATTEMPT}"
IMAGE="$REPOSITORY:$TAG"
az acr build --registry "$ACR_NAME" --image "$IMAGE" --file Dockerfile --timeout 1800 "$CONTEXT"
DIGEST=""
for attempt in $(seq 1 12); do
  DIGEST="$(az acr repository show --name "$ACR_NAME" --image "$IMAGE" --query digest -o tsv --only-show-errors 2>/dev/null || true)"
  [[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] && break
  sleep 5
done
[[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] || fail "Migration 094 digest could not be resolved."
export FLOWHIVE_MIGRATION_IMAGE="$ACR_NAME.azurecr.io/$REPOSITORY@$DIGEST"
export FLOWHIVE_MIGRATION_JOB_NAME="pp094-${RUN_ID}-${RUN_ATTEMPT}"
export FLOWHIVE_MIGRATION_SCOPE="flowhive-authority-094-test"
export FLOWHIVE_RELEASE_COMMIT="$RELEASE_COMMIT"
export FLOWHIVE_CONTROL_SHA="${RELIABILITY_CONTROL_SHA:-$RELEASE_COMMIT}"
bash "$MIGRATION_RUNNER"
echo 'MIGRATION_094=APPLIED_AND_VERIFIED'
