#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-dashboard-calendar-test.yml"
EXPECTED_RELEASE="e6eac94ae774311beb70c46c60213eda32aeda24"

fail() {
  echo "DASHBOARD_CALENDAR_DEPLOYMENT_GUARD=FAIL: $*" >&2
  exit 1
}

[[ -f "$WORKFLOW" ]] || fail "Missing dashboard calendar deployment workflow."

require_text() {
  local value="$1"
  grep -Fq -- "$value" "$WORKFLOW" || fail "Missing workflow contract: $value"
}

require_text "name: ProjectPulse Deploy Dashboard Calendar Test"
require_text "default: $EXPECTED_RELEASE"
require_text "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require_text "DEPLOY-DASHBOARD-CALENDAR-TO-TEST"
require_text "refs/heads/main"
require_text "Only the verified dashboard calendar release may be deployed."
require_text "dashboard-personal-calendar-host"
require_text "resourceIds: [currentUserId]"
require_text "Current week approvals"
require_text "dashboardApprovalScope = 'current-week'"
require_text "new MutationObserver(scheduleApprovalSynchronization)"
require_text "Page Context Guide must be collapsed by default."
require_text "dashboard-personal-calendar-board"
require_text "dashboard-personal-calendar-days"
require_text "Capture current web image"
require_text "Deploy dashboard calendar web image only"
require_text "AZURE_WEB_APP"
require_text "apiDeployment\": \"unchanged"
require_text "migrations\": \"unchanged"
require_text "Roll back web image on failure"
require_text "dashboard-calendar-test-deployment-"

if grep -Fq 'AZURE_API_APP' "$WORKFLOW"; then
  fail "The dashboard calendar rollout must not reference or deploy the API app."
fi

for forbidden in \
  'PTP_DB_' \
  'PROJECTPULSE_TEST_DATABASE_URL' \
  'run-pr55-test-migration-job.sh' \
  'Apply and verify migrations' \
  'Deploy API'
do
  if grep -Fq "$forbidden" "$WORKFLOW"; then
    fail "The web-only rollout contains forbidden database/API behavior: $forbidden"
  fi
done

update_count="$(grep -Fc 'az containerapp update' "$WORKFLOW")"
[[ "$update_count" == '2' ]] || fail "Expected exactly one web deployment and one web rollback update; found $update_count."

web_target_count="$(grep -Fc -- "-n '\${{ vars.AZURE_WEB_APP }}'" "$WORKFLOW")"
[[ "$web_target_count" == '5' ]] || fail "Every capture, deployment, validation, and rollback operation must target the web app; found $web_target_count expected references."

if ! grep -Fq "git -C control merge-base --is-ancestor" "$WORKFLOW"; then
  fail "The release must be an ancestor of the merged deployment-control commit."
fi

if ! grep -Fq "@sha256:" "$WORKFLOW"; then
  fail "The workflow must capture and deploy immutable ACR digest references."
fi

echo "DASHBOARD_CALENDAR_DEPLOYMENT_GUARD=PASS"
