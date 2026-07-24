#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-001-blank-page-hotfix-test.yml"
EXPECTED="821b346ce6fe2475c10717d1c2a46db234a63b32"

fail() { echo "MODULE001_BLANK_PAGE_HOTFIX_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Module 001 Blank Page Hotfix Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-MODULE-001-BLANK-PAGE-HOTFIX-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'assignedTasks: assignedOpenTasks' \
  'nonProjectCategories: categories' \
  "! grep -Fq 'assignedTasks.data'" \
  "! grep -Fq 'nonProjectCategories.data'" \
  'generate-module-001-integrated-app.mjs' \
  'validate-module-001-timesheet-timer-mobile.mjs' \
  'Deploy blank-page hotfix web image only' \
  'projectpulse:module001-state' \
  'projectpulse:module001-action' \
  'apiDeployment":"unchanged' \
  'migration041":"unchanged' \
  'database":"unchanged' \
  'Roll back web image on failure'
do require "$value"; done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 2 ]] ||
  fail "Expected one web deployment and one web rollback."
grep -Fq 'AZURE_API_APP' "$WORKFLOW" && fail "Web-only recovery must not reference the API app."
grep -Fq 'PROJECTPULSE_TEST_DATABASE_URL' "$WORKFLOW" && fail "Web-only recovery must not connect to the database."
grep -Fq 'database/migrations' "$WORKFLOW" && fail "Web-only recovery must not run migrations."
grep -Fq 'environment: production' "$WORKFLOW" && fail "Production environment is forbidden."
grep -Fq 'git -C control merge-base --is-ancestor' "$WORKFLOW" || fail "Release ancestry guard is missing."
grep -Fq '@$DIGEST' "$WORKFLOW" || fail "Immutable web digest construction is missing."
grep -Fq 'steps.before.outputs.old_web_image' "$WORKFLOW" || fail "Web rollback image capture is missing."
bash -n "$0"

echo 'MODULE001_BLANK_PAGE_HOTFIX_DEPLOYMENT_GUARD=PASS'
