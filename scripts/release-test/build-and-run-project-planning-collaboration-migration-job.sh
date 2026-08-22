#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="${PROJECTPULSE_RELEASE_ROOT:-$(pwd -P)}"
ACR_NAME="${AZURE_ACR_NAME:-}"
RELEASE_COMMIT="${RELIABILITY_RELEASE_COMMIT:-}"
RUN_ID="${GITHUB_RUN_ID:-0}"
RUN_ATTEMPT="${GITHUB_RUN_ATTEMPT:-0}"
MIGRATION_FILE="$ROOT/database/migrations/095_project_planning_collaboration_access.sql"
MIGRATION_RUNNER="$ROOT/scripts/release-test/run-project-planning-collaboration-migration-job.sh"
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
[[ -s "$MIGRATION_FILE" ]] || fail "Migration 095 source is missing."
[[ -s "$MIGRATION_RUNNER" ]] || fail "Migration 095 private-network runner is missing."

for command_name in az jq mktemp install chmod; do
  command -v "$command_name" >/dev/null 2>&1 || fail "$command_name is required."
done

CONTEXT="$(mktemp -d "${RUNNER_TEMP:-/tmp}/project-planning-095-${RUN_ID}-${RUN_ATTEMPT}-XXXXXX")"
chmod 0700 "$CONTEXT"
install -d -m 0700 "$CONTEXT/database/migrations"
install -m 0444 "$MIGRATION_FILE" "$CONTEXT/database/migrations/095_project_planning_collaboration_access.sql"
printf '%s\n' "$RELEASE_COMMIT" > "$CONTEXT/release-commit"
chmod 0444 "$CONTEXT/release-commit"

cat > "$CONTEXT/entrypoint.sh" <<'ENTRYPOINT'
#!/usr/bin/env bash
set -Eeuo pipefail
ROOT=/opt/projectpulse/release
EXPECTED="${MAIN_RELEASE_EXPECTED_RELEASE_COMMIT:-}"
ACTUAL="$(cat "$ROOT/.projectpulse-release-commit")"
[[ "$EXPECTED" =~ ^[0-9a-f]{40}$ && "$ACTUAL" == "$EXPECTED" ]] || {
  echo 'ERROR: Migration 095 image release identity mismatch.' >&2
  exit 1
}

MIGRATION="$ROOT/database/migrations/095_project_planning_collaboration_access.sql"
[[ -f "$MIGRATION" ]] || {
  echo 'ERROR: Migration 095 source is missing from the immutable image.' >&2
  exit 1
}
psql -X -v ON_ERROR_STOP=1 --file "$MIGRATION"

verification="$(psql -X -At -v ON_ERROR_STOP=1 <<'SQL'
SELECT
  EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='095_project_planning_collaboration_access')::text || '|' ||
  (to_regclass('public.project_planning_collaborators') IS NOT NULL)::text || '|' ||
  (to_regclass('public.project_planning_collaboration_audit_events') IS NOT NULL)::text || '|' ||
  EXISTS(
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='project_planning_collaborators'
      AND column_name='module_code'
  )::text || '|' ||
  EXISTS(
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='project_planning_collaborators'
      AND column_name='collaboration_level'
  )::text || '|' ||
  (to_regprocedure('public.projectpulse095_touch_planning_collaborator()') IS NOT NULL)::text || '|' ||
  (to_regprocedure('public.projectpulse095_audit_planning_collaborator()') IS NOT NULL)::text || '|' ||
  (to_regprocedure('public.projectpulse095_block_collaboration_audit_mutation()') IS NOT NULL)::text || '|' ||
  (SELECT COUNT(*) = 6 FROM app_permissions WHERE permission_code IN (
    'VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066',
    'REVIEW_FLOWHIVE_PLANNER_066',
    'EDIT_FLOWHIVE_PLANNER_066',
    'VIEW_ASSOCIATED_PROJECT_FORGE_033',
    'REVIEW_PROJECT_FORGE_PLAN_033',
    'EDIT_PROJECT_FORGE_REVIEW_PLAN_033'
  ))::text;
SQL
)"
[[ "$verification" == 'true|true|true|true|true|true|true|true|true' ]] || {
  echo "ERROR: Migration 095 verification failed: $verification" >&2
  exit 1
}

echo 'MIGRATION_095=APPLIED_AND_VERIFIED'
ENTRYPOINT
chmod 0555 "$CONTEXT/entrypoint.sh"

cat > "$CONTEXT/Dockerfile" <<'DOCKERFILE'
FROM postgres:16-alpine
RUN apk add --no-cache bash coreutils ca-certificates
WORKDIR /opt/projectpulse/release
COPY release-commit .projectpulse-release-commit
COPY database/ database/
COPY entrypoint.sh /usr/local/bin/project-planning-collaboration-migrate
RUN chmod 0555 /usr/local/bin/project-planning-collaboration-migrate \
    && chmod 0444 .projectpulse-release-commit database/migrations/*.sql
ENTRYPOINT ["/usr/local/bin/project-planning-collaboration-migrate"]
DOCKERFILE

SHORT_RELEASE="${RELEASE_COMMIT:0:12}"
REPOSITORY="project-health-dashboard-collaboration-migrator"
TAG="rel-${SHORT_RELEASE}-${RUN_ID}-${RUN_ATTEMPT}"
IMAGE="$REPOSITORY:$TAG"
BUILD_SUCCEEDED=0
for attempt in 1 2; do
  if az acr build \
      --registry "$ACR_NAME" \
      --image "$IMAGE" \
      --file Dockerfile \
      --timeout 1800 \
      "$CONTEXT"; then
    BUILD_SUCCEEDED=1
    break
  fi
  (( attempt < 2 )) && sleep $((attempt * 15))
done
(( BUILD_SUCCEEDED == 1 )) || fail "Migration 095 immutable image build failed."

DIGEST=""
for attempt in $(seq 1 12); do
  DIGEST="$(az acr repository show \
    --name "$ACR_NAME" \
    --image "$IMAGE" \
    --query digest \
    -o tsv \
    --only-show-errors 2>/dev/null || true)"
  if [[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]]; then break; fi
  (( attempt < 12 )) && sleep 5
done
[[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] || fail "Migration 095 immutable digest could not be resolved."

export RELIABILITY_MIGRATION_IMAGE="$ACR_NAME.azurecr.io/$REPOSITORY@$DIGEST"
export RELIABILITY_MIGRATION_JOB_NAME="pp095-${RUN_ID}-${RUN_ATTEMPT}"
export RELIABILITY_MIGRATION_SCOPE="project-planning-collaboration-test"

bash "$MIGRATION_RUNNER"
echo 'MIGRATION_095=APPLIED_AND_VERIFIED'

if [[ -n "$EVIDENCE_ROOT" ]]; then
  install -d -m 0700 "$EVIDENCE_ROOT"
  jq -n \
    --arg releaseCommit "$RELEASE_COMMIT" \
    --arg image "$RELIABILITY_MIGRATION_IMAGE" \
    '{
      status:"applied_and_verified",
      migration:"095_project_planning_collaboration_access",
      releaseCommit:$releaseCommit,
      image:$image,
      environment:"protected-test",
      privateNetworkJob:true,
      productionMutation:false
    }' > "$EVIDENCE_ROOT/migration-095.json"
fi
