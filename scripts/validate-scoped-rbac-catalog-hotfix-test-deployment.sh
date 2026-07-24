#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-scoped-rbac-catalog-hotfix-test.yml"
EXPECTED="49713d8afad8200ebe558f4e410e8351e7328759"

fail() { echo "SCOPED_RBAC_CATALOG_HOTFIX_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Scoped RBAC Catalog Hotfix Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-SCOPED-RBAC-CATALOG-HOTFIX-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'projectpulse-scoped-rbac-catalog-normalized' \
  'source.Actions' \
  'source.Scopes' \
  'Deploy catalog hotfix web image only' \
  'apiDeployment":"unchanged' \
  'migration040":"unchanged' \
  'Roll back web image on failure'
do require "$value"; done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 2 ]] || fail "Expected one web deploy and one web rollback."
grep -Fq 'AZURE_API_APP' "$WORKFLOW" && fail "Web-only rollout must not reference the API app."
grep -Fq 'PROJECTPULSE_TEST_DATABASE_URL' "$WORKFLOW" && fail "Web-only rollout must not connect to the database."
grep -Fq 'database/migrations' "$WORKFLOW" && fail "Web-only rollout must not run migrations."
grep -Fq 'environment: production' "$WORKFLOW" && fail "Production environment is forbidden."
grep -Fq 'git -C control merge-base --is-ancestor' "$WORKFLOW" || fail "Release ancestry guard is missing."
grep -Fq '@$DIGEST' "$WORKFLOW" || fail "Immutable web digest construction is missing."

echo 'SCOPED_RBAC_CATALOG_HOTFIX_DEPLOYMENT_GUARD=PASS'
