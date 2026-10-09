#!/usr/bin/env bash
# Test-only release script. Execution MUST be mediated by reviewed workflow.
set -Eeuo pipefail
required() { [[ -n "${!1:-}" ]] || { echo "DENIED: required $1 missing" >&2; exit 1; }; }
for v in RELEASE_SHA EXPECTED_MAIN_SHA AZURE_SUBSCRIPTION_ID AZURE_RESOURCE_GROUP AZURE_API_APP AZURE_ACR_NAME; do required "$v"; done
[[ "$RELEASE_SHA" =~ ^[0-9a-f]{40}$ && "$RELEASE_SHA" == "$EXPECTED_MAIN_SHA" ]] || { echo 'DENIED: release is not exact main' >&2; exit 1; }
[[ "$AZURE_SUBSCRIPTION_ID" == cd32baeb-7b71-4bc0-8ea3-9f23a50903fe ]] || exit 1
[[ "$AZURE_RESOURCE_GROUP" == rg-project-health-dashboard-test-app-westus3 && "$AZURE_API_APP" == ca-phd-test-api-westus3 && "$AZURE_ACR_NAME" == acrphdtest7825cc ]] || exit 1
[[ "$(az account show --query id -o tsv --only-show-errors)" == "$AZURE_SUBSCRIPTION_ID" ]] || exit 1
[[ -f deployment/containers/api/Dockerfile ]] || exit 1
BEFORE="$(mktemp)"; AFTER="$(mktemp)"; CANDIDATE="$(mktemp)"
cleanup() { rm -f "$BEFORE" "$AFTER" "$CANDIDATE"; }
trap cleanup EXIT
az containerapp show -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" -o json --only-show-errors > "$BEFORE"
python3 scripts/resilience/verify-core-test-revision-snapshot.py < "$BEFORE"
OLD_IMAGE="$(jq -r '.properties.template.containers[0].image' "$BEFORE")"
OLD_REVISION="$(jq -r '.properties.latestReadyRevisionName' "$BEFORE")"
OLD_MODE="$(jq -r '.properties.configuration.activeRevisionsMode' "$BEFORE")"
[[ "$OLD_IMAGE" == "$AZURE_ACR_NAME.azurecr.io/"* ]] || exit 1
[[ "$OLD_MODE" == Single || "$OLD_MODE" == Multiple ]] || exit 1
ROLLOUT_STARTED=false
rollback() {
  if [[ "$ROLLOUT_STARTED" == true ]]; then
    echo 'CORE_TEST_ROLLBACK=STARTED' >&2
    az containerapp update -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" --image "$OLD_IMAGE" --set-env-vars "PROJECTPULSE_SOURCE_COMMIT=$OLD_SOURCE" --only-show-errors -o none || true
    az containerapp revision set-mode -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" --mode "$OLD_MODE" --only-show-errors -o none || true
    echo 'CORE_TEST_ROLLBACK=ATTEMPTED' >&2
  fi
}
OLD_SOURCE="$(jq -r '[.properties.template.containers[0].env[]? | select(.name=="PROJECTPULSE_SOURCE_COMMIT") | .value][0] // ""' "$BEFORE")"
trap 'rollback; cleanup' ERR
SHORT="${RELEASE_SHA:0:12}"
TAG="core-${SHORT}-${GITHUB_RUN_ID:-manual}-${GITHUB_RUN_ATTEMPT:-1}"
az acr build --registry "$AZURE_ACR_NAME" --image "project-health-dashboard-api:$TAG" --file deployment/containers/api/Dockerfile --timeout 3600 .
DIGEST="$(az acr repository show -n "$AZURE_ACR_NAME" --image "project-health-dashboard-api:$TAG" --query digest -o tsv --only-show-errors)"
[[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] || exit 1
NEW_IMAGE="$AZURE_ACR_NAME.azurecr.io/project-health-dashboard-api@$DIGEST"
if [[ "$OLD_MODE" == Single ]]; then
  az containerapp revision set-mode -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" --mode multiple --only-show-errors -o none
fi
ROLLOUT_STARTED=true
# Update in Multiple mode while old revision retains traffic. Explicitly reassert baseline.
az containerapp update -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" --image "$NEW_IMAGE" --set-env-vars "PROJECTPULSE_SOURCE_COMMIT=$RELEASE_SHA" --revision-suffix "core-${SHORT}-${GITHUB_RUN_ATTEMPT:-1}" --only-show-errors -o none
az containerapp ingress traffic set -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" --revision-weight "$OLD_REVISION=100" --only-show-errors -o none
az containerapp show -g "$AZURE_RESOURCE_GROUP" -n "$AZURE_API_APP" -o json --only-show-errors > "$AFTER"
NEW_REVISION="$(jq -r '.properties.latestRevisionName' "$AFTER")"
[[ "$NEW_REVISION" != "$OLD_REVISION" ]] || exit 1
# Revision readiness alone is insufficient: do not automatically promote without authenticated canary tests.
bash scripts/wait-containerapp-ready-revision.sh "$AZURE_RESOURCE_GROUP" "$AZURE_API_APP" "$NEW_REVISION" "$NEW_IMAGE" 60 10
jq -n --slurpfile a "$BEFORE" --slurpfile b "$AFTER" '{before:$a[0],candidate:$b[0]}' > "$CANDIDATE"
python3 scripts/resilience/verify-core-candidate-traffic.py < "$CANDIDATE"
echo "CORE_TEST_CANDIDATE_READY=$NEW_REVISION"
echo 'CORE_TEST_CANDIDATE_TRAFFIC=ZERO'
echo 'CORE_TEST_PROMOTION=NOT_AUTHORIZED'
echo 'CELAR_SOW_ACCEPTANCE=NOT_EXECUTED'
