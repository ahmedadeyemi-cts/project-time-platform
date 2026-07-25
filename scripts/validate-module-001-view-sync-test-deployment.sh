#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-001-view-sync-test.yml"
EXPECTED="75f2f7be9f3f3d8f49b64d5413e3b20dea21fb02"

fail() { echo "MODULES_003_004_ROLLING_YEARS_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Modules 003 004 Rolling Years Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-MODULES-003-004-ROLLING-YEARS-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'previousYears: 3' \
  'futureYears: 6' \
  'totalYears: 10' \
  'getRollingYearOptions(currentYear)' \
  'const [selectedYear, setSelectedYear] = useState(currentYear);' \
  "import { getRollingYearOptions } from './rolling-year-window.js';" \
  'const holidayYearOptions = getRollingYearOptions().map(String);' \
  'reference2026=2023-2032' \
  'reference2030=2027-2036' \
  'validate:modules003004-rolling-years' \
  'Deploy Modules 003 and 004 rolling-years web image only' \
  'yearWindow":{"previous":3,"current":1,"future":6,"total":10}' \
  'apiDeployment":"unchanged' \
  'migration":"unchanged' \
  'database":"unchanged' \
  'moduleStates":"unchanged' \
  'Roll back web image on failure'
do require "$value"; done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 2 ]] ||
  fail "Expected one web deployment and one web rollback."
[[ "$(grep -Fc 'scripts/build-pr55-acr-image.sh' "$WORKFLOW")" == 1 ]] ||
  fail "Expected exactly one immutable web image build."
grep -Fq 'git -C control merge-base --is-ancestor' "$WORKFLOW" || fail "Release ancestry guard is missing."
grep -Fq '@$DIGEST' "$WORKFLOW" || fail "Immutable web digest construction is missing."
grep -Fq 'steps.before.outputs.old_web_image' "$WORKFLOW" || fail "Web rollback image capture is missing."
grep -Fq "! grep -Fq '2026 + index'" "$WORKFLOW" || fail "Module 003 fixed-year rejection is missing."
grep -Fq "! grep -Fq 'currentYear >= 2026'" "$WORKFLOW" || fail "Module 003 year clamp rejection is missing."
grep -Fq "! grep -Fq 'Array.from({ length: 11 }, (_, index) => String(currentYear + index))'" "$WORKFLOW" || fail "Module 004 legacy year-window rejection is missing."

for forbidden in \
  'AZURE_API_APP' \
  'PROJECTPULSE_TEST_DATABASE_URL' \
  'database/migrations' \
  'MODULE001_MIGRATION_IMAGE' \
  'MODULE_AVAILABILITY_MIGRATION_IMAGE' \
  'run-module-001-test-migration-job.sh' \
  'run-module-availability-test-migration-job.sh' \
  'Apply and verify migration' \
  'environment: production' \
  'AZURE_PRODUCTION' \
  'DEPLOY-PRODUCTION'
do
  grep -Fq "$forbidden" "$WORKFLOW" && fail "Forbidden rollout behavior: $forbidden"
done

bash -n "$0"
echo 'MODULES_003_004_ROLLING_YEARS_DEPLOYMENT_GUARD=PASS'
