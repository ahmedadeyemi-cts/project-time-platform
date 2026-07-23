#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-modules-page-test.yml"
EXPECTED_RELEASE="f743a07ba10785168f7160bf3508b247f39155d8"

fail() {
  echo "MODULES_PAGE_DEPLOYMENT_GUARD=FAIL: $*" >&2
  exit 1
}

[[ -f "$WORKFLOW" ]] || fail "Missing Modules page deployment workflow."

require_text() {
  local value="$1"
  grep -Fq -- "$value" "$WORKFLOW" || fail "Missing workflow contract: $value"
}

require_text "name: ProjectPulse Deploy Modules Page Test"
require_text "default: $EXPECTED_RELEASE"
require_text "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require_text "DEPLOY-MODULES-PAGE-TO-TEST"
require_text "refs/heads/main"
require_text "Only the verified Modules page commit may be deployed."
require_text "projectpulse-modules-navigation-link"
require_text "modules-directory-page"
require_text "main.app-shell.route-modules"
require_text "modules-directory-grid"
require_text "Capture current web image"
require_text "Deploy Modules page web image only"
require_text "AZURE_WEB_APP"
require_text "apiDeployment\": \"unchanged"
require_text "migrations\": \"unchanged"
require_text "Roll back web image on failure"
require_text "modules-page-test-deployment-"

if grep -Fq 'AZURE_API_APP' "$WORKFLOW"; then
  fail "The Modules rollout must not reference or deploy the API app."
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

echo "MODULES_PAGE_DEPLOYMENT_GUARD=PASS"
