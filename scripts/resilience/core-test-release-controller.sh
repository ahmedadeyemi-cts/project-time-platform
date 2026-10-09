#!/usr/bin/env bash
# Protected-Test-only, zero-traffic staging. Never promote candidate automatically.
set -Eeuo pipefail
required() { [[ -n "${!1:-}" ]] || { echo "DENIED: missing $1" >&2; exit 1; }; }
for v in RELEASE_SHA EXPECTED_MAIN_SHA AZURE_SUBSCRIPTION_ID AZURE_RESOURCE_GROUP AZURE_API_APP AZURE_ACR_NAME GITHUB_RUN_ID; do required "$v"; done
[[ "$RELEASE_SHA" =~ ^[a-f0-9]{40}$ && "$RELEASE_SHA" == "$EXPECTED_MAIN_SHA" ]] || { echo 'DENIED: SHA mismatch' >&2; exit 1; }
[[ "$AZURE_SUBSCRIPTION_ID" == cd32baeb-7b71-4bc0-8ea3-9f23a50903fe ]] || exit 1
[[ "$AZURE_RESOURCE_GROUP" == rg-project-health-dashboard-test-app-westus3 && "$AZURE_API_APP" == ca-phd-test-api-westus3 && "$AZURE_ACR_NAME" == acrphdtest7825cc ]] || exit 1
[[ "$(az account show --query id -o tsv --only-show-errors)" == "$AZURE_SUBSCRIPTION_ID" ]] || exit 1
[[ "$(git rev-parse HEAD)" == "$RELEASE_SHA" ]] || exit 1
for cmd in az jq python3 git; do command -v "$cmd" >/dev/null || exit 1; done
TMP="$(mktemp -d)"; BEFORE="$TMP/before.json"; AFTER="$TMP/after.json"; CANDIDATE="$TMP/candidate.json"
AZURE_MUTATED=false; FINISHED=false
cleanup() {
  local status=$?
  trap - EXIT ERR INT TERM
  if [[ "$AZURE_MUTATED" == true && "$FINISHED" != true ]]; then
    echo 'CORE_TEST_CANDIDATE_RECOVERY_REQUIRED=true' >&2
    # Restore traffic to known-good revision before attempting other recovery.
    az containerapp ingress traffic set -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" --revision-weight "$OLD_REVISION=100" --only-show-errors -o none || echo 'TRAFFIC_RESTORE_FAILED=true' >&2
    # Keep Multiple to preserve old ready revision; full config/image recovery requires reviewed supervisor.
    echo 'CORE_TEST_AUTOMATIC_ROLLBACK=INCOMPLETE' >&2
    status=1
  fi
  rm -rf "$TMP"
  exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
az containerapp show -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" -o json --only-show-errors > "$BEFORE"
python3 scripts/resilience/verify-core-test-revision-snapshot.py < "$BEFORE"
OLD_REVISION="$(jq -r '.properties.latestReadyRevisionName' "$BEFORE")"
OLD_MODE="$(jq -r '.properties.configuration.activeRevisionsMode' "$BEFORE")"
[[ "$OLD_MODE" == Single || "$OLD_MODE" == Multiple ]] || exit 1
# Stop immediately if the source revision changes during build.
TAG="core-${RELEASE_SHA:0:12}-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT:-1}"
az acr build --registry "$AZURE_ACR_NAME" --image "project-health-dashboard-api:$TAG" --file deployment/containers/api/Dockerfile --timeout 3600 .
DIGEST="$(az acr repository show -n "$AZURE_ACR_NAME" --image "project-health-dashboard-api:$TAG" --query digest -o tsv --only-show-errors)"
[[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] || exit 1
NEW_IMAGE="$AZURE_ACR_NAME.azurecr.io/project-health-dashboard-api@$DIGEST"
CURRENT_READY="$(az containerapp show -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" --query properties.latestReadyRevisionName -o tsv --only-show-errors)"
[[ "$CURRENT_READY" == "$OLD_REVISION" ]] || { echo 'DENIED: baseline changed during image build' >&2; exit 1; }
if [[ "$OLD_MODE" == Single ]]; then
  # Any mutation from this point must have an explicit recovery path.
  AZURE_MUTATED=true
  az containerapp revision set-mode -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" --mode multiple --only-show-errors -o none
fi
# After changing revision mode, confirm baseline is still receiving all traffic.
az containerapp show -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" -o json --only-show-errors > "$AFTER"
[[ "$(jq -r '.properties.latestReadyRevisionName' "$AFTER")" == "$OLD_REVISION" ]] || exit 1
[[ "$(jq -r '[.properties.configuration.ingress.traffic[]?.weight] | add // 0' "$AFTER")" == 100 ]] || exit 1
AZURE_MUTATED=true
az containerapp update -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" --image "$NEW_IMAGE" --set-env-vars "PROJECTPULSE_SOURCE_COMMIT=$RELEASE_SHA" --revision-suffix "core-${RELEASE_SHA:0:12}-${GITHUB_RUN_ATTEMPT:-1}" --only-show-errors -o none
az containerapp ingress traffic set -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" --revision-weight "$OLD_REVISION=100" --only-show-errors -o none
az containerapp show -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" -o json --only-show-errors > "$AFTER"
NEW_REVISION="$(jq -r '.properties.latestRevisionName' "$AFTER")"
[[ "$NEW_REVISION" != "$OLD_REVISION" ]] || exit 1
bash scripts/wait-containerapp-ready-revision.sh "$AZURE_RESOURCE_GROUP" "$AZURE_API_APP" "$NEW_REVISION" "$NEW_IMAGE" 60 10
jq -n --slurpfile a "$BEFORE" --slurpfile b "$AFTER" '{before:$a[0],candidate:$b[0]}' > "$CANDIDATE"
python3 scripts/resilience/verify-core-candidate-traffic.py < "$CANDIDATE"
# Never call this full acceptance until authenticated tests and rollback are reviewed.
FINISHED=true
echo "CORE_TEST_CANDIDATE_READY=$NEW_REVISION"
echo 'CORE_TEST_PROMOTION=NOT_AUTHORIZED'
echo 'CELAR_SOW_ACCEPTANCE=NOT_EXECUTED'
