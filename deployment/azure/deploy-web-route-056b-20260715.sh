#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077
set +x

SUBSCRIPTION="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"

APP_RG="rg-project-health-dashboard-test-app-westus3"
WEB_APP="ca-phd-test-web-westus3"

ACR_NAME="acrphdtest7825cc"
ACR_SERVER="acrphdtest7825cc.azurecr.io"
IMAGE_REPOSITORY="project-health-dashboard-web"

GITHUB_REPOSITORY="ahmedadeyemi-cts/project-time-platform"
BRANCH="source/invoice-billing-center-preview-20260714"
EXPECTED_HEAD="e4973fdf64a509b691568cb88dee871844943653"

REPO="$HOME/project-time-platform-module-042"
PUBLIC_URL="https://phd-west-test.onenecklab.com"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
TAG="route-056b-e4973fd-${STAMP,,}"
REVISION_SUFFIX="r056b-e4973fd-$(date -u +%m%d%H%M%S)"

STATE_DIR="$HOME/projectpulse-deployment-state"
STATE_FILE="$STATE_DIR/web-056b-$STAMP.txt"

DEPLOY_STARTED=0
OLD_IMAGE=""
OLD_REVISION=""
NEW_IMAGE=""
NEW_REVISION=""

fail() {
    echo
    echo "============================================================"
    echo "WEB_056B_DEPLOYMENT=FAILED"
    echo "ERROR=$*"
    echo "SOURCE_COMMIT=$EXPECTED_HEAD"
    echo "API_CHANGED=NO"
    echo "DATABASE_CHANGED=NO"
    echo "============================================================"
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 ||
        fail "Required command not found: $1"
}

wait_for_image() {
    local expected_image="$1"
    local expected_suffix="${2:-}"
    local attempt
    local image
    local ready_revision
    local running_status

    for attempt in $(seq 1 60); do
        image="$(
            az containerapp show \
                --resource-group "$APP_RG" \
                --name "$WEB_APP" \
                --query 'properties.template.containers[0].image' \
                -o tsv 2>/dev/null || true
        )"

        ready_revision="$(
            az containerapp show \
                --resource-group "$APP_RG" \
                --name "$WEB_APP" \
                --query 'properties.latestReadyRevisionName' \
                -o tsv 2>/dev/null || true
        )"

        running_status="$(
            az containerapp show \
                --resource-group "$APP_RG" \
                --name "$WEB_APP" \
                --query 'properties.runningStatus' \
                -o tsv 2>/dev/null || true
        )"

        echo "WAIT_ATTEMPT=$attempt"
        echo "CURRENT_IMAGE=$image"
        echo "LATEST_READY_REVISION=$ready_revision"
        echo "RUNNING_STATUS=$running_status"

        if [ "$image" = "$expected_image" ] &&
           [ "$running_status" = "Running" ]; then

            if [ -z "$expected_suffix" ] ||
               [[ "$ready_revision" == *"--$expected_suffix" ]]; then
                NEW_REVISION="$ready_revision"
                return 0
            fi
        fi

        sleep 10
    done

    return 1
}

rollback() {
    local rollback_suffix
    local rollback_revision

    if [ "$DEPLOY_STARTED" != "1" ] || [ -z "$OLD_IMAGE" ]; then
        return 0
    fi

    rollback_suffix="rollback-056b-$(date -u +%m%d%H%M%S)"

    echo
    echo "============================================================"
    echo "ROLLING BACK WEB CONTAINER APP"
    echo "============================================================"
    echo "ROLLBACK_IMAGE=$OLD_IMAGE"
    echo "ROLLBACK_SUFFIX=$rollback_suffix"

    az containerapp update \
        --resource-group "$APP_RG" \
        --name "$WEB_APP" \
        --image "$OLD_IMAGE" \
        --revision-suffix "$rollback_suffix" \
        --output none || {
            echo "ERROR: Rollback update command failed."
            return 1
        }

    if wait_for_image "$OLD_IMAGE" "$rollback_suffix"; then
        rollback_revision="$NEW_REVISION"
        echo "ROLLBACK_REVISION=$rollback_revision"
        echo "ROLLBACK_STATUS=COMPLETE"
        return 0
    fi

    echo "ROLLBACK_STATUS=FAILED_OR_TIMED_OUT"
    return 1
}

require_command az
require_command gh
require_command git
require_command curl
require_command grep
require_command sha256sum

echo "============================================================"
echo "PROJECTPULSE WEB 056B CONTAINER APP DEPLOYMENT"
echo "============================================================"
echo "TIME=$STAMP"
echo "EXPECTED_HEAD=$EXPECTED_HEAD"
echo "TARGET_WEB_APP=$WEB_APP"
echo "TARGET_URL=$PUBLIC_URL"
echo

az account set --subscription "$SUBSCRIPTION"

ACTIVE_SUBSCRIPTION="$(
    az account show --query id -o tsv
)"

[ "$ACTIVE_SUBSCRIPTION" = "$SUBSCRIPTION" ] ||
    fail "Unexpected Azure subscription: $ACTIVE_SUBSCRIPTION"

gh auth status >/dev/null 2>&1 ||
    fail "GitHub CLI is not authenticated. Run: gh auth login"

mkdir -p "$STATE_DIR"

if [ -e "$REPO" ]; then
    fail "Clone destination already exists: $REPO"
fi

echo "============================================================"
echo "CLONE CANONICAL SOURCE"
echo "============================================================"

gh repo clone "$GITHUB_REPOSITORY" "$REPO" -- \
    --branch "$BRANCH" \
    --single-branch

cd "$REPO"

ACTUAL_BRANCH="$(git branch --show-current)"
ACTUAL_HEAD="$(git rev-parse HEAD)"

echo "ACTUAL_BRANCH=$ACTUAL_BRANCH"
echo "ACTUAL_HEAD=$ACTUAL_HEAD"

[ "$ACTUAL_BRANCH" = "$BRANCH" ] ||
    fail "Expected branch $BRANCH but found $ACTUAL_BRANCH"

[ "$ACTUAL_HEAD" = "$EXPECTED_HEAD" ] ||
    fail "Remote branch is at $ACTUAL_HEAD, expected $EXPECTED_HEAD"

[ -z "$(git status --porcelain)" ] ||
    fail "Fresh clone is unexpectedly dirty"

grep -q \
    '056B_DASHBOARD_CARD_ROUTE_GUARD_START' \
    src/frontend/project-time-web/index.html ||
    fail "056B source marker is missing"

grep -q \
    '056B_DASHBOARD_CARD_ROUTE_GUARD_END' \
    src/frontend/project-time-web/index.html ||
    fail "056B ending marker is missing"

[ -f deployment/containers/web/Dockerfile ] ||
    fail "Web Dockerfile is missing"

echo
git log -3 --oneline --decorate

echo
echo "============================================================"
echo "CAPTURE CURRENT DEPLOYMENT FOR ROLLBACK"
echo "============================================================"

OLD_IMAGE="$(
    az containerapp show \
        --resource-group "$APP_RG" \
        --name "$WEB_APP" \
        --query 'properties.template.containers[0].image' \
        -o tsv
)"

OLD_REVISION="$(
    az containerapp show \
        --resource-group "$APP_RG" \
        --name "$WEB_APP" \
        --query 'properties.latestReadyRevisionName' \
        -o tsv
)"

[ -n "$OLD_IMAGE" ] ||
    fail "Could not determine the current web image"

echo "OLD_IMAGE=$OLD_IMAGE"
echo "OLD_REVISION=$OLD_REVISION"

cat > "$STATE_FILE" <<EOF
deploymentTimeUtc=$STAMP
sourceBranch=$BRANCH
sourceCommit=$EXPECTED_HEAD
webApp=$WEB_APP
oldImage=$OLD_IMAGE
oldRevision=$OLD_REVISION
newTag=$TAG
EOF

echo "ROLLBACK_STATE_FILE=$STATE_FILE"

echo
echo "============================================================"
echo "BUILD WEB IMAGE IN ACR"
echo "============================================================"

az acr build \
    --registry "$ACR_NAME" \
    --image "$IMAGE_REPOSITORY:$TAG" \
    --file deployment/containers/web/Dockerfile \
    .

NEW_DIGEST="$(
    az acr repository show \
        --name "$ACR_NAME" \
        --image "$IMAGE_REPOSITORY:$TAG" \
        --query digest \
        -o tsv
)"

[ -n "$NEW_DIGEST" ] ||
    fail "ACR did not return a digest for $IMAGE_REPOSITORY:$TAG"

NEW_IMAGE="$ACR_SERVER/$IMAGE_REPOSITORY@$NEW_DIGEST"

echo "NEW_TAG=$ACR_SERVER/$IMAGE_REPOSITORY:$TAG"
echo "NEW_DIGEST=$NEW_DIGEST"
echo "NEW_IMAGE=$NEW_IMAGE"

cat >> "$STATE_FILE" <<EOF
newDigest=$NEW_DIGEST
newImage=$NEW_IMAGE
revisionSuffix=$REVISION_SUFFIX
EOF

echo
echo "============================================================"
echo "DEPLOY IMMUTABLE IMAGE DIGEST"
echo "============================================================"

DEPLOY_STARTED=1

az containerapp update \
    --resource-group "$APP_RG" \
    --name "$WEB_APP" \
    --image "$NEW_IMAGE" \
    --revision-suffix "$REVISION_SUFFIX" \
    --output none

if ! wait_for_image "$NEW_IMAGE" "$REVISION_SUFFIX"; then
    echo "ERROR: New Container App revision did not become ready."
    rollback || true
    fail "Container App readiness validation failed"
fi

echo
echo "NEW_REVISION=$NEW_REVISION"

echo
echo "============================================================"
echo "LIVE APPLICATION VALIDATION"
echo "============================================================"

LIVE_HTML="$(mktemp)"
trap 'rm -f "$LIVE_HTML"' EXIT

LIVE_VERIFIED=0

for attempt in $(seq 1 30); do
    rm -f "$LIVE_HTML"

    HTTP_CODE="$(
        curl -fsS \
            -H 'Cache-Control: no-cache' \
            -o "$LIVE_HTML" \
            -w '%{http_code}' \
            "$PUBLIC_URL/?deployment=$TAG" 2>/dev/null || true
    )"

    LIVE_BYTES="$(wc -c < "$LIVE_HTML" 2>/dev/null || echo 0)"

    LIVE_ASSET="$(
        grep -oE '/assets/index-[A-Za-z0-9_-]+\.js' \
            "$LIVE_HTML" 2>/dev/null |
            sort -u |
            head -1
    )"

    if grep -q \
        'projectpulse-056b-dashboard-card-route-guard' \
        "$LIVE_HTML" 2>/dev/null; then
        GUARD_PRESENT="YES"
    else
        GUARD_PRESENT="NO"
    fi

    echo "LIVE_ATTEMPT=$attempt"
    echo "LIVE_HTTP_CODE=$HTTP_CODE"
    echo "LIVE_HTML_BYTES=$LIVE_BYTES"
    echo "LIVE_ASSET=$LIVE_ASSET"
    echo "LIVE_056B_GUARD=$GUARD_PRESENT"

    if [ "$HTTP_CODE" = "200" ] &&
       [ "$GUARD_PRESENT" = "YES" ] &&
       [ -n "$LIVE_ASSET" ] &&
       [ "$LIVE_ASSET" != "/assets/index-Df70t1xj.js" ]; then
        LIVE_VERIFIED=1
        break
    fi

    sleep 10
done

if [ "$LIVE_VERIFIED" != "1" ]; then
    echo "ERROR: Live site did not expose the new 056B frontend."
    rollback || true
    fail "Live frontend verification failed and rollback was attempted"
fi

LIVE_HTML_SHA256="$(
    sha256sum "$LIVE_HTML" |
        awk '{print $1}'
)"

cat >> "$STATE_FILE" <<EOF
newRevision=$NEW_REVISION
liveAsset=$LIVE_ASSET
liveHtmlBytes=$LIVE_BYTES
liveHtmlSha256=$LIVE_HTML_SHA256
liveGuardPresent=$GUARD_PRESENT
deploymentStatus=complete
EOF

echo
echo "============================================================"
echo "WEB_056B_DEPLOYMENT=COMPLETE"
echo "============================================================"
echo "SOURCE_BRANCH=$BRANCH"
echo "SOURCE_COMMIT=$EXPECTED_HEAD"
echo "OLD_IMAGE=$OLD_IMAGE"
echo "OLD_REVISION=$OLD_REVISION"
echo "NEW_IMAGE=$NEW_IMAGE"
echo "NEW_REVISION=$NEW_REVISION"
echo "LIVE_ASSET=$LIVE_ASSET"
echo "LIVE_HTML_BYTES=$LIVE_BYTES"
echo "LIVE_056B_GUARD=YES"
echo "ROLLBACK_STATE_FILE=$STATE_FILE"
echo "API_CHANGED=NO"
echo "DATABASE_CHANGED=NO"
echo "============================================================"
