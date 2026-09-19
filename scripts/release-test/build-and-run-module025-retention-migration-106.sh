#!/usr/bin/env bash
# Add the retained-document prerequisite to the existing protected main lane.
# Reuse the owned private-network job, TLS database binding and cleanup checks.
set -Eeuo pipefail
ROOT="${PROJECTPULSE_RELEASE_ROOT:-$(pwd -P)}"
RELEASE="${RELIABILITY_RELEASE_COMMIT:-}"
ACR="${AZURE_ACR_NAME:-}"
[[ "${GITHUB_EVENT_NAME:-}" == workflow_dispatch && "${GITHUB_REF:-}" == refs/heads/main ]]
[[ "${TARGET_RELEASE_BRANCH:-}" == main ]]
case "${ACCEPTANCE_SCOPE:-}" in
  sow_role|sow_exports) ;;
  *) echo "ERROR: Module 025 retention migrations require sow_role or sow_exports acceptance." >&2; exit 1 ;;
esac
[[ "$RELEASE" =~ ^[a-f0-9]{40}$ && "$(git -C "$ROOT" rev-parse HEAD)" == "$RELEASE" ]]
[[ "$ACR" =~ ^[a-zA-Z0-9]+$ && "${GITHUB_RUN_ID:-}" =~ ^[0-9]+$ && "${GITHUB_RUN_ATTEMPT:-}" =~ ^[0-9]+$ ]]
API="$(az containerapp show -g "${AZURE_RESOURCE_GROUP:?}" -n "${AZURE_API_APP:?}" -o json --only-show-errors)"
jq -e '.tags.environment == "test"' <<<"$API" >/dev/null
unset API
CONTEXT="$(mktemp -d "${RUNNER_TEMP:-/tmp}/module025-retention-106-XXXXXX")"
trap 'rm -rf -- "$CONTEXT"' EXIT
install -m 0444 "$ROOT/database/migrations/106_module025_sow_sell_register.sql" "$CONTEXT/migration-106.sql"
install -m 0444 "$ROOT/database/migrations/110_module025_ungenerated_draft_delete.sql" "$CONTEXT/migration-110.sql"
install -m 0444 "$ROOT/database/migrations/111_connectwise_sell_provider.sql" "$CONTEXT/migration-111.sql"
printf '%s\n' "$RELEASE" > "$CONTEXT/release-commit"
(cd "$CONTEXT" && sha256sum migration-106.sql migration-110.sql migration-111.sql release-commit > SHA256SUMS)
cat > "$CONTEXT/entrypoint.sh" <<'ENTRYPOINT'
#!/usr/bin/env bash
set -Eeuo pipefail
cd /opt/projectpulse/release
[[ "${MAIN_RELEASE_MIGRATION_MODE:-}" == apply ]]
[[ "$(cat release-commit)" == "${MAIN_RELEASE_EXPECTED_RELEASE_COMMIT:?}" ]]
sha256sum --check --status SHA256SUMS
psql -X -v ON_ERROR_STOP=1 --file migration-106.sql
psql -X -v ON_ERROR_STOP=1 --file migration-110.sql
psql -X -v ON_ERROR_STOP=1 --file migration-111.sql
verified="$(psql -X -At -v ON_ERROR_STOP=1 <<'SQL'
SELECT (
  EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='106_module025_sow_sell_register')
  AND NOT EXISTS (
    SELECT 1 FROM unnest(ARRAY['module025_sow_gsd_generation_snapshots','module025_sow_gsd_versions',
      'module025_sow_gsd_artifact_issuance','module025_sow_sell_submissions','module025_sow_sell_dispatch',
      'module025_sow_sell_links','module025_sow_sell_receipts']) AS t(name)
    WHERE to_regclass('public.' || t.name) IS NULL)
  AND EXISTS(SELECT 1 FROM pg_trigger WHERE tgname='module025_generation_snapshot' AND NOT tgisinternal)
  AND EXISTS(SELECT 1 FROM pg_trigger WHERE tgname='module025_sell_receipt_validation' AND NOT tgisinternal)
  AND EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='110_module025_ungenerated_draft_delete')
  AND EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='111_connectwise_sell_provider')
  AND EXISTS(SELECT 1 FROM crm_integration_providers WHERE provider_key='connectwise_sell' AND provider_name='ConnectWise SELL' AND auth_model='api_key')
  AND NOT EXISTS(SELECT 1 FROM crm_integration_providers WHERE provider_key='zendesk_sell' AND is_enabled)
  AND pg_get_functiondef('module025_reject_evidence_mutation()'::regprocedure) LIKE '%projectpulse.module025_allow_draft_delete%'
)::text;
SQL
)"
[[ "$verified" == true ]]
echo 'MODULE025_RETENTION_MIGRATION_106=APPLIED_AND_VERIFIED'
echo 'MIGRATION_110_MODULE025_DRAFT_DELETE=APPLIED_AND_VERIFIED'
echo 'MIGRATION_111_CONNECTWISE_SELL=APPLIED_AND_VERIFIED'
ENTRYPOINT
cat > "$CONTEXT/Dockerfile" <<'DOCKERFILE'
FROM postgres:16-alpine
RUN apk add --no-cache bash coreutils ca-certificates
WORKDIR /opt/projectpulse/release
COPY migration-106.sql migration-110.sql migration-111.sql release-commit SHA256SUMS entrypoint.sh ./
RUN chmod 0444 migration-106.sql migration-110.sql migration-111.sql release-commit SHA256SUMS && chmod 0555 entrypoint.sh
ENTRYPOINT ["/opt/projectpulse/release/entrypoint.sh"]
DOCKERFILE
TAG="module025-retention-migrator:${RELEASE:0:12}-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"
az acr build --registry "$ACR" --image "$TAG" --file "$CONTEXT/Dockerfile" --timeout 1800 "$CONTEXT"
DIGEST=''
for attempt in {1..12}; do
  DIGEST="$(az acr repository show -n "$ACR" --image "$TAG" --query digest -o tsv --only-show-errors 2>/dev/null || true)"
  [[ "$DIGEST" =~ ^sha256:[a-f0-9]{64}$ ]] && break
  (( attempt < 12 )) && sleep 5
done
[[ "$DIGEST" =~ ^sha256:[a-f0-9]{64}$ ]]
export MAIN_RELEASE_EXPECTED_RELEASE_COMMIT="$RELEASE"
export MAIN_RELEASE_CONTROL_SHA="${RELIABILITY_CONTROL_SHA:?}"
export MAIN_RELEASE_MIGRATION_SCOPE=module025-retention-106-test
export MAIN_RELEASE_MIGRATION_IMAGE="$ACR.azurecr.io/module025-retention-migrator@$DIGEST"
export MAIN_RELEASE_MIGRATION_JOB_NAME="m025r-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"
export MAIN_RELEASE_MIGRATION_MODE=apply
bash "$ROOT/scripts/release-test/run-migration-job.sh"
mkdir -p "${EVIDENCE_DIR:?}"
jq -n --arg source "$RELEASE" --arg image "$MAIN_RELEASE_MIGRATION_IMAGE" \
  '{status:"applied_and_verified",migrations:["106_module025_sow_sell_register","110_module025_ungenerated_draft_delete","111_connectwise_sell_provider"],sourceCommit:$source,image:$image,productionMutation:false}' \
  > "$EVIDENCE_DIR/module025-retention-migration.json"
