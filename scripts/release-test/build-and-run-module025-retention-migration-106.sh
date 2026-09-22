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
# LAYA_RELEASE_BEGIN runtime_role
LAYA_RUNTIME_ROLE="$(jq -er '[.properties.template.containers[].env[]? | select(.name == "PTP_DB_USER") | .value | select(type == "string" and length > 0)] | unique | if length == 1 then .[0] else error("One explicit API database role is required for Laya grants") end' <<<"$API")"
[[ "$LAYA_RUNTIME_ROLE" =~ ^[a-zA-Z_][a-zA-Z0-9_.@-]{0,62}$ ]] || { echo 'STOP: Unexpected API database role.' >&2; exit 1; }
# LAYA_RELEASE_END runtime_role
unset API
CONTEXT="$(mktemp -d "${RUNNER_TEMP:-/tmp}/module025-retention-106-XXXXXX")"
trap 'rm -rf -- "$CONTEXT"' EXIT
install -m 0444 "$ROOT/database/migrations/106_module025_sow_sell_register.sql" "$CONTEXT/migration-106.sql"
install -m 0444 "$ROOT/database/migrations/110_module025_ungenerated_draft_delete.sql" "$CONTEXT/migration-110.sql"
install -m 0444 "$ROOT/database/migrations/111_connectwise_sell_provider.sql" "$CONTEXT/migration-111.sql"
install -m 0444 "$ROOT/database/migrations/116_module025_governed_ownership_transfer.sql" "$CONTEXT/migration-116.sql"
install -m 0444 "$ROOT/database/migrations/117_module025_template_candidates.sql" "$CONTEXT/migration-117.sql"
install -m 0444 "$ROOT/database/migrations/118_module025_work_tracking.sql" "$CONTEXT/migration-118.sql"
install -m 0444 "$ROOT/database/migrations/119_module025_temporary_handoffs.sql" "$CONTEXT/migration-119.sql"
install -m 0444 "$ROOT/database/migrations/120_module025_handoff_notifications.sql" "$CONTEXT/migration-120.sql"
printf '%s\n' "$RELEASE" > "$CONTEXT/release-commit"
(cd "$CONTEXT" && sha256sum migration-106.sql migration-110.sql migration-111.sql migration-116.sql migration-117.sql migration-118.sql migration-119.sql migration-120.sql release-commit > SHA256SUMS)
# LAYA_RELEASE_BEGIN migration_files
install -m 0444 "$ROOT/deployment/laya/schema.sql" "$CONTEXT/laya-schema.sql"
install -m 0444 "$ROOT/deployment/laya/grants.sql" "$CONTEXT/laya-grants.sql"
install -m 0444 "$ROOT/deployment/laya/verify-database.sql" "$CONTEXT/laya-verify.sql"
printf '%s\n' "$LAYA_RUNTIME_ROLE" > "$CONTEXT/laya-runtime-role"
(cd "$CONTEXT" && sha256sum laya-schema.sql laya-grants.sql laya-verify.sql laya-runtime-role >> SHA256SUMS)
# LAYA_RELEASE_END migration_files
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
psql -X -v ON_ERROR_STOP=1 --file migration-116.sql
psql -X -v ON_ERROR_STOP=1 --file migration-117.sql
psql -X -v ON_ERROR_STOP=1 --file migration-118.sql
psql -X -v ON_ERROR_STOP=1 --file migration-119.sql
psql -X -v ON_ERROR_STOP=1 --file migration-120.sql
# LAYA_RELEASE_BEGIN migration_apply
LAYA_RUNTIME_ROLE="$(cat laya-runtime-role)"
[[ "$LAYA_RUNTIME_ROLE" =~ ^[a-zA-Z_][a-zA-Z0-9_.@-]{0,62}$ ]]
psql -X -v ON_ERROR_STOP=1 --file laya-schema.sql
psql -X -v ON_ERROR_STOP=1 -v "runtime_role=$LAYA_RUNTIME_ROLE" --file laya-grants.sql
psql -X -v ON_ERROR_STOP=1 -v "runtime_role=$LAYA_RUNTIME_ROLE" --file laya-verify.sql
echo 'LAYA_DATABASE_SCHEMA_AND_API_GRANTS=APPLIED_AND_VERIFIED'
# LAYA_RELEASE_END migration_apply
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
  -- SA workspace schema is verified before the unchanged job can report success.
  AND NOT EXISTS (
    SELECT 1 FROM unnest(ARRAY['116_module025_governed_ownership_transfer','117_module025_template_candidates',
      '118_module025_work_tracking','119_module025_temporary_handoffs','120_module025_handoff_notifications']) AS m(id)
    WHERE NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=m.id))
  AND pg_get_functiondef('module025_protect_sow_gsd_identity()'::regprocedure) LIKE '%transferTransactionId%'
  AND pg_get_functiondef('module025_reject_evidence_mutation()'::regprocedure) LIKE '%ownership_transferred%'
  AND NOT EXISTS (
    SELECT 1 FROM unnest(ARRAY['module025_template_candidates','module025_work_tracking_events',
      'module025_sow_gsd_handoffs']) AS t(name)
    WHERE to_regclass('public.' || t.name) IS NULL)
  AND NOT EXISTS (
    SELECT 1 FROM (VALUES
      ('module025_sow_gsd_engagements','trg_module025_protect_sow_gsd_identity'),
      ('module025_template_candidates','trg_module025_protect_template_candidate'),
      ('module025_work_tracking_events','trg_module025_protect_work_tracking'),
      ('module025_work_tracking_events','trg_module025_protect_work_tracking_truncate'),
      ('module025_work_tracking_events','trg_module025_validate_work_tracking_insert'),
      ('module025_sow_gsd_handoffs','module025_handoff_metadata_check'),
      ('module025_sow_gsd_handoffs','module025_handoff_no_change'),
      ('module025_sow_gsd_handoffs','module025_handoff_no_truncate')) AS required(table_name,trigger_name)
    WHERE NOT EXISTS(SELECT 1 FROM pg_trigger WHERE tgrelid=to_regclass('public.' || required.table_name)
      AND tgname=required.trigger_name AND NOT tgisinternal AND tgenabled IN ('O','A')))
  AND (SELECT count(*) FROM enterprise_notification_policies
    WHERE policy_code IN ('MODULE025_HANDOFF','MODULE025_COVERAGE_STARTED',
      'MODULE025_COVERAGE_RETURNED','MODULE025_HANDOFF_ACKNOWLEDGED')
      AND owner_module='065' AND source_module='025'
      AND recipient_strategy='module025_handoff' AND producer_contract='module025-handoff-v1')=4
)::text;
SQL
)"
[[ "$verified" == true ]]
echo 'MODULE025_RETENTION_MIGRATION_106=APPLIED_AND_VERIFIED'
echo 'MIGRATION_110_MODULE025_DRAFT_DELETE=APPLIED_AND_VERIFIED'
echo 'MIGRATION_111_CONNECTWISE_SELL=APPLIED_AND_VERIFIED'
echo 'MIGRATION_116_MODULE025_GOVERNED_OWNERSHIP_TRANSFER=APPLIED_AND_VERIFIED'
echo 'MIGRATION_117_MODULE025_TEMPLATE_CANDIDATES=APPLIED_AND_VERIFIED'
echo 'MIGRATION_118_MODULE025_WORK_TRACKING=APPLIED_AND_VERIFIED'
echo 'MIGRATION_119_MODULE025_TEMPORARY_HANDOFFS=APPLIED_AND_VERIFIED'
echo 'MIGRATION_120_MODULE025_HANDOFF_NOTIFICATIONS=APPLIED_AND_VERIFIED'
ENTRYPOINT
cat > "$CONTEXT/Dockerfile" <<'DOCKERFILE'
FROM postgres:16-alpine
RUN apk add --no-cache bash coreutils ca-certificates
WORKDIR /opt/projectpulse/release
COPY migration-106.sql migration-110.sql migration-111.sql migration-116.sql migration-117.sql migration-118.sql migration-119.sql migration-120.sql release-commit SHA256SUMS entrypoint.sh ./
RUN chmod 0444 migration-106.sql migration-110.sql migration-111.sql migration-116.sql migration-117.sql migration-118.sql migration-119.sql migration-120.sql release-commit SHA256SUMS && chmod 0555 entrypoint.sh
# LAYA_RELEASE_BEGIN migration_image
COPY laya-schema.sql laya-grants.sql laya-verify.sql laya-runtime-role ./
RUN chmod 0444 laya-schema.sql laya-grants.sql laya-verify.sql laya-runtime-role
# LAYA_RELEASE_END migration_image
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
  '{status:"applied_and_verified",migrations:["106_module025_sow_sell_register","110_module025_ungenerated_draft_delete","111_connectwise_sell_provider","116_module025_governed_ownership_transfer","117_module025_template_candidates","118_module025_work_tracking","119_module025_temporary_handoffs","120_module025_handoff_notifications"],sourceCommit:$source,image:$image,productionMutation:false}' \
  > "$EVIDENCE_DIR/module025-retention-migration.json"
# LAYA_RELEASE_BEGIN migration_evidence
jq -n --arg source "$RELEASE" --arg image "$MAIN_RELEASE_MIGRATION_IMAGE" \
  '{status:"applied_and_verified",schema:"celar_laya_decisions_v1",sourceCommit:$source,image:$image,apiRoleGrantsVerified:true,productionMutation:false,providerOrderChanged:false}' \
  > "$EVIDENCE_DIR/laya-database-migration.json"
# LAYA_RELEASE_END migration_evidence
