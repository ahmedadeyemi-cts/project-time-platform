#!/usr/bin/env bash
set -Eeuo pipefail

RESOURCE_GROUP="${1:-}"
APP_NAME="${2:-}"
EXPECTED_REVISION="${3:-}"
EXPECTED_IMAGE="${4:-}"
MAX_ATTEMPTS="${5:-60}"
SLEEP_SECONDS="${6:-10}"

fail() { echo "ERROR: $*" >&2; exit 1; }
[[ -n "$RESOURCE_GROUP" ]] || fail "resource group is required"
[[ -n "$APP_NAME" ]] || fail "Container App name is required"
[[ -n "$EXPECTED_REVISION" ]] || fail "expected revision name is required"
[[ -n "$EXPECTED_IMAGE" ]] || fail "expected immutable image is required"
[[ "$MAX_ATTEMPTS" =~ ^[1-9][0-9]*$ ]] || fail "max attempts must be a positive integer"
[[ "$SLEEP_SECONDS" =~ ^[1-9][0-9]*$ ]] || fail "sleep seconds must be a positive integer"
command -v jq >/dev/null || fail "jq is required"

MODULE001B_PROTECTED_TEST_RECONCILE=false
case "$EXPECTED_REVISION" in
  *--m1be-*|*--m1bd-*) MODULE001B_PROTECTED_TEST_RECONCILE=true ;;
esac

for attempt in $(seq 1 "$MAX_ATTEMPTS"); do
  LATEST_READY="$(az containerapp show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$APP_NAME" \
    --query properties.latestReadyRevisionName \
    --output tsv --only-show-errors 2>/dev/null || true)"
  REVISION_JSON="$(az containerapp revision show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$APP_NAME" \
    --revision "$EXPECTED_REVISION" \
    --query '{image:properties.template.containers[0].image,provisioningState:properties.provisioningState,healthState:properties.healthState,active:properties.active,trafficWeight:properties.trafficWeight}' \
    --output json --only-show-errors 2>/dev/null || true)"

  IMAGE="$(jq -r '.image // empty' <<<"${REVISION_JSON:-{}}" 2>/dev/null || true)"
  PROVISIONING_STATE="$(jq -r '.provisioningState // empty' <<<"${REVISION_JSON:-{}}" 2>/dev/null || true)"
  HEALTH_STATE="$(jq -r '.healthState // empty' <<<"${REVISION_JSON:-{}}" 2>/dev/null || true)"
  ACTIVE="$(jq -r '.active // empty' <<<"${REVISION_JSON:-{}}" 2>/dev/null || true)"
  TRAFFIC_WEIGHT="$(jq -r '.trafficWeight // empty' <<<"${REVISION_JSON:-{}}" 2>/dev/null || true)"

  printf 'REVISION_WAIT app=%s attempt=%s expected=%s latestReady=%s image=%s provisioning=%s health=%s active=%s traffic=%s\n' \
    "$APP_NAME" "$attempt" "$EXPECTED_REVISION" "${LATEST_READY:-none}" "${IMAGE:-none}" \
    "${PROVISIONING_STATE:-unknown}" "${HEALTH_STATE:-unknown}" "${ACTIVE:-unknown}" "${TRAFFIC_WEIGHT:-unknown}"

  if [[ "$LATEST_READY" == "$EXPECTED_REVISION" && "$IMAGE" == "$EXPECTED_IMAGE" ]]; then
    if [[ "$MODULE001B_PROTECTED_TEST_RECONCILE" != true ]]; then
      echo "CONTAINERAPP_CANDIDATE_READY app=$APP_NAME revision=$EXPECTED_REVISION image=$EXPECTED_IMAGE"
      exit 0
    fi

    APP_JSON="$(az containerapp show \
      --resource-group "$RESOURCE_GROUP" \
      --name "$APP_NAME" \
      --output json --only-show-errors)" \
      || fail "Could not inspect the Module 001B protected-Test Container App before revision reconciliation."
    ENVIRONMENT_TAG="$(jq -r '.tags.environment // empty' <<<"$APP_JSON")"
    REVISION_MODE="$(jq -r '.properties.configuration.activeRevisionsMode // empty' <<<"$APP_JSON")"
    LATEST_REVISION="$(jq -r '.properties.latestRevisionName // empty' <<<"$APP_JSON")"

    [[ "$ENVIRONMENT_TAG" == test ]] \
      || fail "Refusing Module 001B revision reconciliation because the Container App is not tagged Test."
    [[ "${REVISION_MODE,,}" == single ]] \
      || fail "Refusing Module 001B revision reconciliation because the Container App is not in Single revision mode."
    [[ "$LATEST_REVISION" == "$EXPECTED_REVISION" ]] \
      || fail "Refusing Module 001B revision reconciliation because a different revision is now latest: expected=$EXPECTED_REVISION latest=${LATEST_REVISION:-none}."

    if [[ "$ACTIVE" == true ]]; then
      REVISIONS_JSON="$(az containerapp revision list \
        --resource-group "$RESOURCE_GROUP" \
        --name "$APP_NAME" \
        --output json --only-show-errors)" \
        || fail "Could not inspect active revisions for Module 001B protected-Test reconciliation."
      mapfile -t ACTIVE_REVISIONS < <(jq -r '.[]? | select(.properties.active == true) | .name' <<<"$REVISIONS_JSON")

      if [[ ${#ACTIVE_REVISIONS[@]} -eq 1 && "${ACTIVE_REVISIONS[0]}" == "$EXPECTED_REVISION" ]]; then
        echo "CONTAINERAPP_CANDIDATE_READY app=$APP_NAME revision=$EXPECTED_REVISION image=$EXPECTED_IMAGE singleRevisionConverged=true"
        exit 0
      fi

      for stale_revision in "${ACTIVE_REVISIONS[@]}"; do
        [[ "$stale_revision" == "$EXPECTED_REVISION" ]] && continue
        echo "MODULE001B_STALE_REVISION_DEACTIVATE app=$APP_NAME expected=$EXPECTED_REVISION stale=$stale_revision" >&2
        az containerapp revision deactivate \
          --resource-group "$RESOURCE_GROUP" \
          --name "$APP_NAME" \
          --revision "$stale_revision" \
          --output none --only-show-errors \
          || fail "Failed to deactivate stale Module 001B protected-Test revision: $stale_revision"
      done
    fi
  fi

  case "${PROVISIONING_STATE,,}:${HEALTH_STATE,,}" in
    failed:*|canceled:*|*:failed|*:unhealthy)
      echo "Candidate revision entered a terminal failure state." >&2
      break
      ;;
  esac
  sleep "$SLEEP_SECONDS"
done

az containerapp show \
  --resource-group "$RESOURCE_GROUP" \
  --name "$APP_NAME" \
  --query '{latestRevisionName:properties.latestRevisionName,latestReadyRevisionName:properties.latestReadyRevisionName,configuredImage:properties.template.containers[0].image,provisioningState:properties.provisioningState}' \
  --output json --only-show-errors >&2 || true
az containerapp revision list \
  --resource-group "$RESOURCE_GROUP" \
  --name "$APP_NAME" \
  --query '[].{name:name,active:properties.active,trafficWeight:properties.trafficWeight,provisioningState:properties.provisioningState,healthState:properties.healthState,image:properties.template.containers[0].image}' \
  --output json --only-show-errors >&2 || true
fail "The expected Container Apps revision did not become the latest ready revision with the required immutable image: app=$APP_NAME revision=$EXPECTED_REVISION"
