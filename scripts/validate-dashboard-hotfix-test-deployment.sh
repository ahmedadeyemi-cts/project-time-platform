#!/usr/bin/env bash
set -Eeuo pipefail

WORKFLOW=".github/workflows/projectpulse-deploy-dashboard-hotfix-test.yml"
EXPECTED_RELEASE="2344e0ed5c6b2bab4208a34d0f7e2c2a82ced4d9"
EXPECTED_CONFIRMATION="DEPLOY-DASHBOARD-HOTFIX-TO-TEST"

fail() {
  echo "dashboard-hotfix deployment guard: $*" >&2
  exit 1
}

[[ -f "$WORKFLOW" ]] || fail "missing workflow: $WORKFLOW"

required_text=(
  "name: ProjectPulse Deploy Dashboard Hotfix Test"
  "workflow_dispatch:"
  "default: $EXPECTED_RELEASE"
  "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
  "$EXPECTED_CONFIRMATION"
  "refs/heads/main"
  "environment: test"
  "cancel-in-progress: false"
  "build-pr55-acr-image.sh"
  "deployment/containers/web/Dockerfile"
  "AZURE_WEB_APP"
  "activeRevisionsMode"
  "single-revision mode"
  "CURRENT_WEB_IMAGE_IMMUTABLE"
  "Deploy web hotfix only"
  "Validate served hotfix assets and web image"
  "CSS_PATH="
  "dashfix-app.compact.css"
  "main.app-shell.route-dashboard>#role-welcome-dashboard.role-welcome-dashboard{display:grid!important"
  "ACTIVE_WEB_IMAGE"
  "apiDeployment\": \"unchanged"
  "migrations\": \"unchanged"
  "Roll back web image on failure"
  "previousWebImage"
)

for text in "${required_text[@]}"; do
  grep -Fq "$text" "$WORKFLOW" || fail "missing required contract: $text"
done

for forbidden in \
  'AZURE_API_APP' \
  'Deploy API' \
  'run-pr55-test-migration-job.sh' \
  'apply-pr55-test-migrations.sh' \
  'database/migrations/' \
  'PTP_DB_' \
  'psql'; do
  if grep -Fq "$forbidden" "$WORKFLOW"; then
    fail "web-only workflow contains forbidden API/database operation: $forbidden"
  fi
done

update_count="$(grep -Fc 'az containerapp update' "$WORKFLOW")"
[[ "$update_count" == '2' ]] || fail "expected exactly one web deployment and one web rollback update; found $update_count"

if grep -Fq 'cancel-in-progress: true' "$WORKFLOW"; then
  fail 'deployment concurrency must not cancel an in-progress rollout'
fi

if ! grep -Fq "grep -Fq '#role-welcome-dashboard'" "$WORKFLOW" \
  || ! grep -Fq "grep -Fq 'display: grid !important'" "$WORKFLOW"; then
  fail 'source checkout must verify the dashboard visibility contract before Azure login'
fi

if ! grep -Fq "JS_STATUS" "$WORKFLOW" \
  || ! grep -Fq "CSS_STATUS" "$WORKFLOW"; then
  fail 'served deployment validation must verify both JavaScript and CSS assets'
fi

if ! grep -Fq "failure() && steps.deploy_web.outputs.started == 'true'" "$WORKFLOW"; then
  fail 'web rollback must run after any post-deployment failure'
fi

echo 'Dashboard hotfix web-only deployment guard passed.'
