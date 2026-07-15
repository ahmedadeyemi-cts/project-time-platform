#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077
set +x

REPOSITORY="ahmedadeyemi-cts/project-time-platform"
SOURCE_BRANCH="source/invoice-billing-center-preview-20260714"
REQUIRED_FIX_COMMIT="3aa314ff2fbc9783d487dba2bb0ac631b1f02ea9"

OPS_BRANCH="ops/azure-web-route-056c-20260715"

SUBSCRIPTION="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
APP_RG="rg-project-health-dashboard-test-app-westus3"
WEB_APP="ca-phd-test-web-westus3"

ACR_NAME="acrphdtest7825cc"
ACR_SERVER="acrphdtest7825cc.azurecr.io"
IMAGE_REPOSITORY="project-health-dashboard-web"

PUBLIC_URL="https://phd-west-test.onenecklab.com"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
RUN_DIR="$HOME/projectpulse-056c-deploy-logs/$STAMP"
SOURCE_REPO="$RUN_DIR/source"
OPS_REPO="$RUN_DIR/ops"
BUILD_LOG="$RUN_DIR/acr-build.log"
EVENT_LOG="$RUN_DIR/events.log"
STATUS_FILE="$RUN_DIR/final.status"

mkdir -p "$RUN_DIR"

log() {
  echo "$*" | tee -a "$EVENT_LOG"
}

fail() {
  log ""
  log "============================================================"
  log "056C_WEB_DEPLOYMENT=FAILED"
  log "ERROR=$*"
  log "RUN_DIR=$RUN_DIR"
  log "API_CHANGED=NO"
  log "DATABASE_CHANGED=NO"
  log "============================================================"

  cat > "$STATUS_FILE" <<EOF
status=failed
error=$*
runDir=$RUN_DIR
apiChanged=no
databaseChanged=no
EOF

  exit 1
}

for COMMAND in gh git az curl grep awk sha256sum python3; do
  command -v "$COMMAND" >/dev/null 2>&1 ||
    fail "Missing command: $COMMAND"
done

gh auth status >/dev/null 2>&1 ||
  fail "GitHub CLI is not authenticated"

az account set --subscription "$SUBSCRIPTION"

log "============================================================"
log "056C DETACHED WEB DEPLOYMENT START"
log "============================================================"
log "RUN_DIR=$RUN_DIR"
log "PUBLIC_URL=$PUBLIC_URL"

REMOTE_SOURCE="$(
  git ls-remote \
    "https://github.com/$REPOSITORY.git" \
    "refs/heads/$SOURCE_BRANCH" |
  awk '{print $1}'
)"

[ -n "$REMOTE_SOURCE" ] ||
  fail "Could not resolve remote source branch"

log "REMOTE_SOURCE=$REMOTE_SOURCE"
log "REQUIRED_FIX_COMMIT=$REQUIRED_FIX_COMMIT"

gh repo clone "$REPOSITORY" "$SOURCE_REPO" -- \
  --branch "$SOURCE_BRANCH" \
  --single-branch >>"$EVENT_LOG" 2>&1

cd "$SOURCE_REPO"

SOURCE_COMMIT="$(git rev-parse HEAD)"

[ "$SOURCE_COMMIT" = "$REMOTE_SOURCE" ] ||
  fail "Clone source commit $SOURCE_COMMIT does not match remote $REMOTE_SOURCE"

git merge-base --is-ancestor "$REQUIRED_FIX_COMMIT" "$SOURCE_COMMIT" ||
  fail "Current source does not contain the 056C fix commit"

python3 scripts/validate-056c-dashboard-route-guard.py >>"$EVENT_LOG" 2>&1

grep -q 'data-projectpulse-guard-version="056C"' \
  src/frontend/project-time-web/index.html ||
  fail "056C guard marker missing from source"

if grep -q "if (!element.closest('.app-shell'))" \
  src/frontend/project-time-web/index.html; then
  fail "Obsolete app-shell dependency still exists in source"
fi

log "SOURCE_VALIDATION=PASSED"
log "SOURCE_COMMIT=$SOURCE_COMMIT"

log ""
log "============================================================"
log "PREPARE OPS BRANCH"
log "============================================================"

if git ls-remote \
  --exit-code \
  "https://github.com/$REPOSITORY.git" \
  "refs/heads/$OPS_BRANCH" >/dev/null 2>&1; then

  gh repo clone "$REPOSITORY" "$OPS_REPO" -- \
    --branch "$OPS_BRANCH" \
    --single-branch >>"$EVENT_LOG" 2>&1

  cd "$OPS_REPO"
else
  gh repo clone "$REPOSITORY" "$OPS_REPO" -- \
    --branch "$SOURCE_BRANCH" \
    --single-branch >>"$EVENT_LOG" 2>&1

  cd "$OPS_REPO"
  git switch -c "$OPS_BRANCH" >>"$EVENT_LOG" 2>&1
fi

git config --local user.name "Ahmed Adeyemi"
git config --local user.email \
  "ahmedadeyemi-cts@users.noreply.github.com"

mkdir -p deployment/azure

cat > deployment/azure/README-route-056c-deployment.md <<EOF
# 056C Azure web deployment

Current source branch:

\`$SOURCE_BRANCH\`

Current source commit:

\`$SOURCE_COMMIT\`

Required 056C fix commit present:

\`$REQUIRED_FIX_COMMIT\`

Target:

\`$WEB_APP\`

Public URL:

\`$PUBLIC_URL\`

This deployment updates only the web Container App image.
EOF

cp "$0" deployment/azure/deploy-web-route-056c-detached.sh
chmod 0755 deployment/azure/deploy-web-route-056c-detached.sh

git add deployment/azure/README-route-056c-deployment.md \
  deployment/azure/deploy-web-route-056c-detached.sh

if ! git diff --cached --quiet; then
  git diff --cached --check
  git commit -m "Add detached Azure web deployment workflow for 056C route guard" >>"$EVENT_LOG" 2>&1
  git push --set-upstream origin "$OPS_BRANCH" >>"$EVENT_LOG" 2>&1
fi

WORKFLOW_COMMIT="$(git rev-parse HEAD)"
log "WORKFLOW_COMMIT=$WORKFLOW_COMMIT"

log ""
log "============================================================"
log "CAPTURE CURRENT WEB DEPLOYMENT"
log "============================================================"

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

log "OLD_IMAGE=$OLD_IMAGE"
log "OLD_REVISION=$OLD_REVISION"

rollback() {
  log ""
  log "============================================================"
  log "ROLLBACK_STARTED"
  log "============================================================"

  az containerapp update \
    --resource-group "$APP_RG" \
    --name "$WEB_APP" \
    --image "$OLD_IMAGE" \
    --revision-suffix "rollback-056c-$(date -u +%m%d%H%M%S)" \
    --output none >>"$EVENT_LOG" 2>&1 || true

  log "ROLLBACK_IMAGE=$OLD_IMAGE"
}

TAG="route-056c-${SOURCE_COMMIT:0:7}-${STAMP,,}"
REVISION_SUFFIX="r056c-${SOURCE_COMMIT:0:7}-$(date -u +%m%d%H%M%S)"

log ""
log "============================================================"
log "BUILD WEB IMAGE IN ACR"
log "============================================================"
log "TAG=$TAG"
log "BUILD_LOG=$BUILD_LOG"

cd "$SOURCE_REPO"

az acr build \
  --registry "$ACR_NAME" \
  --image "$IMAGE_REPOSITORY:$TAG" \
  --file deployment/containers/web/Dockerfile \
  . >"$BUILD_LOG" 2>&1

NEW_DIGEST="$(
  az acr repository show \
    --name "$ACR_NAME" \
    --image "$IMAGE_REPOSITORY:$TAG" \
    --query digest \
    -o tsv
)"

[ -n "$NEW_DIGEST" ] ||
  fail "ACR build completed but no digest was returned"

NEW_IMAGE="$ACR_SERVER/$IMAGE_REPOSITORY@$NEW_DIGEST"

log "NEW_DIGEST=$NEW_DIGEST"
log "NEW_IMAGE=$NEW_IMAGE"

log ""
log "============================================================"
log "DEPLOY WEB IMAGE"
log "============================================================"
log "REVISION_SUFFIX=$REVISION_SUFFIX"

az containerapp update \
  --resource-group "$APP_RG" \
  --name "$WEB_APP" \
  --image "$NEW_IMAGE" \
  --revision-suffix "$REVISION_SUFFIX" \
  --output none >>"$EVENT_LOG" 2>&1

NEW_REVISION=""

for ATTEMPT in $(seq 1 60); do
  CURRENT_IMAGE="$(
    az containerapp show \
      --resource-group "$APP_RG" \
      --name "$WEB_APP" \
      --query 'properties.template.containers[0].image' \
      -o tsv 2>/dev/null || true
  )"

  READY_REVISION="$(
    az containerapp show \
      --resource-group "$APP_RG" \
      --name "$WEB_APP" \
      --query 'properties.latestReadyRevisionName' \
      -o tsv 2>/dev/null || true
  )"

  RUNNING_STATUS="$(
    az containerapp show \
      --resource-group "$APP_RG" \
      --name "$WEB_APP" \
      --query 'properties.runningStatus' \
      -o tsv 2>/dev/null || true
  )"

  log "WAIT_ATTEMPT=$ATTEMPT CURRENT_IMAGE=$CURRENT_IMAGE READY_REVISION=$READY_REVISION RUNNING_STATUS=$RUNNING_STATUS"

  if [ "$CURRENT_IMAGE" = "$NEW_IMAGE" ] &&
     [[ "$READY_REVISION" == *"--$REVISION_SUFFIX" ]] &&
     [ "$RUNNING_STATUS" = "Running" ]; then
    NEW_REVISION="$READY_REVISION"
    break
  fi

  sleep 10
done

[ -n "$NEW_REVISION" ] || {
  rollback
  fail "New revision did not become ready"
}

log "NEW_REVISION=$NEW_REVISION"

log ""
log "============================================================"
log "LIVE HTML VALIDATION"
log "============================================================"

LIVE_HTML="$(mktemp)"

LIVE_OK=0

for ATTEMPT in $(seq 1 30); do
  HTTP_CODE="$(
    curl -fsS \
      -H 'Cache-Control: no-cache' \
      -o "$LIVE_HTML" \
      -w '%{http_code}' \
      "$PUBLIC_URL/?route-guard-056c=$TAG" 2>/dev/null || true
  )"

  LIVE_ASSET="$(
    grep -oE '/assets/index-[A-Za-z0-9_-]+\.js' "$LIVE_HTML" 2>/dev/null |
    sort -u |
    head -1 || true
  )"

  LIVE_BYTES="$(wc -c < "$LIVE_HTML" 2>/dev/null || echo 0)"

  if grep -q 'data-projectpulse-guard-version="056C"' "$LIVE_HTML" 2>/dev/null &&
     grep -q "const GUARD_VERSION = '056C'" "$LIVE_HTML" 2>/dev/null &&
     grep -q '__projectPulse056BDashboardCardRouteGuardDiagnostics' "$LIVE_HTML" 2>/dev/null &&
     grep -q 'data-projectpulse-056b-visible-offender-count' "$LIVE_HTML" 2>/dev/null &&
     ! grep -q "if (!element.closest('.app-shell'))" "$LIVE_HTML" 2>/dev/null; then
    LIVE_056C_GUARD="YES"
  else
    LIVE_056C_GUARD="NO"
  fi

  log "LIVE_ATTEMPT=$ATTEMPT LIVE_HTTP_CODE=$HTTP_CODE LIVE_ASSET=$LIVE_ASSET LIVE_HTML_BYTES=$LIVE_BYTES LIVE_056C_GUARD=$LIVE_056C_GUARD"

  if [ "$HTTP_CODE" = "200" ] &&
     [ "$LIVE_056C_GUARD" = "YES" ]; then
    LIVE_OK=1
    break
  fi

  sleep 10
done

if [ "$LIVE_OK" != "1" ]; then
  rollback
  fail "Live 056C validation failed"
fi

LIVE_SHA="$(sha256sum "$LIVE_HTML" | awk '{print $1}')"
BUILD_LOG_SHA="$(sha256sum "$BUILD_LOG" | awk '{print $1}')"
EVENT_LOG_SHA="$(sha256sum "$EVENT_LOG" | awk '{print $1}')"

rm -f "$LIVE_HTML"

log ""
log "============================================================"
log "RECORD GIT EVIDENCE"
log "============================================================"

cd "$OPS_REPO"

RELEASE_DIR="deployment/azure/releases/$STAMP"
mkdir -p "$RELEASE_DIR"

cat > "$RELEASE_DIR/deployment-summary.txt" <<EOF
deployedAtUtc=$(date -u +%Y-%m-%dT%H:%M:%SZ)
sourceBranch=$SOURCE_BRANCH
sourceCommit=$SOURCE_COMMIT
requiredFixCommit=$REQUIRED_FIX_COMMIT
workflowCommit=$WORKFLOW_COMMIT
webContainerApp=$WEB_APP
oldImage=$OLD_IMAGE
oldRevision=$OLD_REVISION
newImage=$NEW_IMAGE
newDigest=$NEW_DIGEST
newRevision=$NEW_REVISION
revisionSuffix=$REVISION_SUFFIX
acrTag=$TAG
liveHttpCode=$HTTP_CODE
liveAsset=$LIVE_ASSET
liveHtmlBytes=$LIVE_BYTES
liveHtmlSha256=$LIVE_SHA
live056CGuard=YES
buildLogSha256=$BUILD_LOG_SHA
eventLogSha256=$EVENT_LOG_SHA
apiChanged=no
databaseChanged=no
EOF

cat > "$RELEASE_DIR/browser-validation.js" <<'EOF'
(() => {
  const result =
    window.__projectPulse056BDashboardCardRouteGuardDiagnostics?.();

  const known = [
    "Production Notification Center",
    "SOW Generator + Claude Research Review",
    "Sales-to-Delivery Intake Foundation",
    "CRM Integration Framework",
    "Signed SOW Handoff + Assignment Trigger"
  ];

  const headings = [
    ...document.querySelectorAll("h1,h2,h3,h4,h5,h6,[role='heading']")
  ];

  const visibleKnownDashboardHeadings = headings
    .filter((heading) =>
      known.some((title) =>
        heading.textContent.replace(/\s+/g, " ").trim().includes(title)
      )
    )
    .filter((heading) => {
      const card =
        heading.closest("[data-projectpulse-dashboard-only-card='true']") ||
        heading;

      const style = getComputedStyle(card);

      return !card.hidden &&
        style.display !== "none" &&
        style.visibility !== "hidden";
    })
    .map((heading) =>
      heading.textContent.replace(/\s+/g, " ").trim()
    );

  return {
    hash: location.hash,
    route: document.documentElement.getAttribute(
      "data-projectpulse-056b-route"
    ),
    guardVersion: document.documentElement.getAttribute(
      "data-projectpulse-056b-guard-version"
    ),
    markedCount: document.documentElement.getAttribute(
      "data-projectpulse-056b-marked-count"
    ),
    visibleOffenderCount: document.documentElement.getAttribute(
      "data-projectpulse-056b-visible-offender-count"
    ),
    runtimeResult: result,
    visibleKnownDashboardHeadings
  };
})()
EOF

git add "$RELEASE_DIR"

git diff --cached --check

git commit -m "Record successful 056C Azure web deployment" >>"$EVENT_LOG" 2>&1

EVIDENCE_COMMIT="$(git rev-parse HEAD)"

git push origin "HEAD:$OPS_BRANCH" >>"$EVENT_LOG" 2>&1

DEPLOYMENT_TAG="deploy/web-056c/$STAMP"

git tag -a "$DEPLOYMENT_TAG" \
  -m "Successful ProjectPulse 056C Azure web deployment"

git push origin "refs/tags/$DEPLOYMENT_TAG" >>"$EVENT_LOG" 2>&1

log ""
log "============================================================"
log "056C_WEB_DEPLOYMENT=COMPLETE"
log "============================================================"
log "SOURCE_COMMIT=$SOURCE_COMMIT"
log "OPS_BRANCH=$OPS_BRANCH"
log "WORKFLOW_COMMIT=$WORKFLOW_COMMIT"
log "EVIDENCE_COMMIT=$EVIDENCE_COMMIT"
log "DEPLOYMENT_TAG=$DEPLOYMENT_TAG"
log "OLD_IMAGE=$OLD_IMAGE"
log "OLD_REVISION=$OLD_REVISION"
log "NEW_IMAGE=$NEW_IMAGE"
log "NEW_REVISION=$NEW_REVISION"
log "LIVE_ASSET=$LIVE_ASSET"
log "LIVE_056C_GUARD=YES"
log "RUN_DIR=$RUN_DIR"
log "API_CHANGED=NO"
log "DATABASE_CHANGED=NO"
log "============================================================"

cat > "$STATUS_FILE" <<EOF
status=complete
sourceCommit=$SOURCE_COMMIT
evidenceCommit=$EVIDENCE_COMMIT
deploymentTag=$DEPLOYMENT_TAG
newImage=$NEW_IMAGE
newRevision=$NEW_REVISION
liveAsset=$LIVE_ASSET
live056CGuard=YES
runDir=$RUN_DIR
apiChanged=no
databaseChanged=no
EOF
