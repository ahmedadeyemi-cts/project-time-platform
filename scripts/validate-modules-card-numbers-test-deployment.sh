#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-modules-card-numbers-test.yml"
EXPECTED_RELEASE="dffeae27cbebe4a66f5c35a52624423790eb9537"

fail() {
  echo "MODULES_CARD_NUMBERS_DEPLOYMENT_GUARD=FAIL: $*" >&2
  exit 1
}

[[ -f "$WORKFLOW" ]] || fail "Missing Modules card-number deployment workflow."

require_text() {
  local value="$1"
  grep -Fq -- "$value" "$WORKFLOW" || fail "Missing workflow contract: $value"
}

require_text "name: ProjectPulse Deploy Modules Card Numbers Test"
require_text "default: $EXPECTED_RELEASE"
require_text "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require_text "DEPLOY-MODULE-NUMBERS-TO-TEST"
require_text "refs/heads/main"
require_text "Only the verified Modules card-number commit may be deployed."
require_text "CANONICAL_MODULE_NUMBER_BY_ROUTE"
require_text "moduleNumberForRoute"
require_text "'rate-card-administration': '055B'"
require_text "'work-register': '055C'"
require_text "'create-work-register': '055D'"
require_text "'user-guide': '999'"
require_text "Search by module number, name, route, or category"
require_text "Module number unavailable"
require_text "Capture current web image"
require_text "Deploy Modules card-number web image only"
require_text "AZURE_WEB_APP"
require_text "apiDeployment\": \"unchanged"
require_text "migrations\": \"unchanged"
require_text "Roll back web image on failure"
require_text "modules-card-numbers-test-deployment-"

if grep -Fq 'AZURE_API_APP' "$WORKFLOW"; then
  fail "The Modules card-number rollout must not reference or deploy the API app."
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

echo "MODULES_CARD_NUMBERS_DEPLOYMENT_GUARD=PASS"
