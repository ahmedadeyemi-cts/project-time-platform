#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-scoped-rbac-test.yml"
EXECUTOR="$ROOT/scripts/apply-scoped-rbac-test-migration.sh"
EXPECTED="19c7bee92e513b79ef83cc3b6ad3d2a781aa5b67"

fail() { echo "SCOPED_RBAC_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" && -f "$EXECUTOR" ]] || fail "Workflow or migration executor is missing."

require_workflow() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }
require_executor() { grep -Fq -- "$1" "$EXECUTOR" || fail "Executor missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Scoped RBAC Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-SCOPED-RBAC-TO-TEST' \
  "refs/heads/main" \
  'environment: test' \
  'Apply and verify migration 040 inside private network' \
  'Deploy API' \
  'Deploy web' \
  'Roll back API and web images on failure' \
  'scoped-rbac-test-deployment-' \
  'Authoritative, versioned administration' \
  'Strictly read-only representation'
do require_workflow "$value"; done

for value in \
  "EXPECTED_RELEASE_COMMIT=\"$EXPECTED\"" \
  '040_scoped_role_policy_versions.sql' \
  'Checksum mismatch' \
  'Legacy RBAC counts changed during migration 040.' \
  "module_code='003'" \
  "module_code='037'" \
  'Project Management received password-reset approval access.' \
  'Super Administrator policy-publish authority is missing.' \
  'PTC delegated approval authority is incomplete.'
do require_executor "$value"; done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 4 ]] || fail "Expected API/web deployment plus API/web rollback updates."
[[ "$(grep -Fc 'sha256:' "$WORKFLOW")" -ge 1 ]] || fail "Immutable image references are not enforced."
[[ "$(grep -Fc ':sha256:' "$EXECUTOR")" == 0 ]] || fail "Malformed checksum contracts detected."
[[ "$(grep -Ec '^[[:space:]]+\"040_scoped_role_policy_versions.*:[0-9a-f]{64}\"' "$EXECUTOR")" == 12 ]] || fail "Expected 12 checksum-pinned migration files."

for forbidden in \
  'environment: production' \
  'AZURE_PRODUCTION' \
  'DEPLOY-PRODUCTION' \
  'database/rollback/040_scoped_role_policy_versions_rollback.sql' \
  'git push' \
  'gh pr merge'
do
  grep -Fq "$forbidden" "$WORKFLOW" && fail "Forbidden rollout behavior: $forbidden"
done

if ! grep -Fq 'git -C control merge-base --is-ancestor' "$WORKFLOW"; then
  fail "Release ancestry guard is missing."
fi
if ! grep -Fq 'PROJECTPULSE_TEST_DATABASE_URL' "$EXECUTOR"; then
  fail "Executor is not restricted to the test database contract."
fi

echo 'SCOPED_RBAC_DEPLOYMENT_GUARD=PASS'
