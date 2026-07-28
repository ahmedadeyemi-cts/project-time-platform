#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-module001-ptc-timer-dom-module026-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-module001-ptc-timer-dom-module026-test-deployment.yml"
RUNNER="$ROOT/scripts/run-module001-ptc-timer-dom-module026-test-deployment.sh"
SELF="$ROOT/scripts/validate-module001-ptc-timer-dom-module026-test-deployment.sh"
EXPECTED_RELEASE="816deeda98d3d875cdbdea1a42b5662815334eb9"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_file() { [[ -f "$1" ]] || fail "Missing required file: ${1#$ROOT/}"; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }
line_of() { grep -Fn -- "$2" "$1" | head -1 | cut -d: -f1; }

for file in "$DEPLOY" "$VALIDATE" "$RUNNER" "$SELF"; do
  require_file "$file"
done

require "$DEPLOY" 'name: ProjectPulse Deploy Module 001 PTC Timer DOM and Module 026 Test'
require "$DEPLOY" 'workflow_dispatch:'
require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'DEPLOY-MODULE001-PTC-TIMER-DOM-MODULE026-TO-TEST'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'group: projectpulse-deploy-module001-ptc-timer-dom-module026-test'
require "$DEPLOY" 'cancel-in-progress: false'
require "$DEPLOY" "[[ \"\$WORKFLOW_SOURCE_REF\" == 'refs/heads/main' ]]"
require "$DEPLOY" 'release_commit must be a complete 40-character SHA.'
require "$DEPLOY" 'Only the verified Module 001 PTC timer DOM and Module 026 source release may deploy.'
require "$DEPLOY" 'git -C control merge-base --is-ancestor "$TARGET_COMMIT" HEAD'
require "$DEPLOY" 'UseModule001ResultExecutionCompatibility'
require "$DEPLOY" 'Module001TimerTargetsAsync(context)'
require "$DEPLOY" 'Module001ActiveTimerAsync(context)'
require "$DEPLOY" 'Module001TimerHistoryAsync(context)'
require "$DEPLOY" 'await result.ExecuteAsync(context);'
require "$DEPLOY" 'module001-time-steward-v2-2026-07-28'
require "$DEPLOY" '"/api/runtime/timesheet/steward/v2/users"'
require "$DEPLOY" '"/api/runtime/timesheet/steward/v2/users/{targetUserId:guid}/workspace"'
require "$DEPLOY" '"/api/runtime/timesheet/steward/v2/entries/{timeEntryId:guid}/move"'
require "$DEPLOY" 'canAssignExistingProjectTaskDuringMove = true'
require "$DEPLOY" 'canMoveToNonProjectTime = true'
require "$DEPLOY" 'crossActivityTypeMove = true'
require "$DEPLOY" 'submissionOnBehalf = false'
require "$DEPLOY" "requiredCollections: ['users']"
require "$DEPLOY" 'Move time across all supported activity types'
require "$DEPLOY" 'Timer history'
require "$DEPLOY" 'sign-out, and session expiration'
require "$DEPLOY" 'data-projectpulse-react-owned-slot="true"'
require "$DEPLOY" 'runtimeDomInsertion=0'
require "$DEPLOY" 'PROJECTPULSE_REACT_OWNED_MORE_MENU'
require "$DEPLOY" 'runtimeChildReplacement=0'
require "$DEPLOY" "reactDomOwnership: 'attributes-only-v1'"
require "$DEPLOY" "moreMenu: 'react-owned-v1'"
require "$DEPLOY" "! grep -Fq 'link.replaceChildren'"
require "$DEPLOY" "! grep -Fq 'document.createElement' \"\$TIMER_PORTAL\""
require "$DEPLOY" 'Edit connection'
require "$DEPLOY" 'Add CRM platform'
require "$DEPLOY" 'zendesk_sell'
require "$DEPLOY" '/api/platform-operations/overview'
require "$DEPLOY" 'ProjectTime.Api.RouteResultExecutionTests.csproj'
require "$DEPLOY" 'ProjectTime.Api.AuthorizationTests.csproj'
require "$DEPLOY" 'test-projectpulse-api-startup.sh'
require "$DEPLOY" 'npm run validate:module001-enhancement'
require "$DEPLOY" 'npm run validate:module001-ptc-time-steward'
require "$DEPLOY" 'npm run validate:module001-ptc-timer-dom'
require "$DEPLOY" 'npm run validate:module026'
require "$DEPLOY" 'npm run validate:modules021026'
require "$DEPLOY" 'validate-group-1-navigation-work-consolidation.mjs'
require "$DEPLOY" 'validate-group-2a-provider-neutral-platform-operations.mjs'
require "$DEPLOY" 'npm run build'
require "$DEPLOY" 'azure/login@v2'
require "$DEPLOY" 'run-module001-ptc-timer-dom-module026-test-deployment.sh'
require "$DEPLOY" 'control/evidence/module001-ptc-timer-dom-module026-test-deployment.json'
reject "$DEPLOY" '^[[:space:]]*push:'
reject "$DEPLOY" '^[[:space:]]*schedule:'
reject "$DEPLOY" 'environment:[[:space:]]*production'
reject "$DEPLOY" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$DEPLOY" 'PROJECTPULSE_TEST_DATABASE_URL'
reject "$DEPLOY" '\bpsql\b'
reject "$DEPLOY" 'database/migrations/[0-9]'
reject "$DEPLOY" 'az[[:space:]]+role[[:space:]]+assignment'

SOURCE_CHECK_LINE="$(line_of "$DEPLOY" 'MODULE001_PTC_TIMER_DOM_MODULE026_SOURCE_VALIDATION=PASS')"
AZURE_LOGIN_LINE="$(line_of "$DEPLOY" 'azure/login@v2')"
[[ -n "$SOURCE_CHECK_LINE" && -n "$AZURE_LOGIN_LINE" && "$SOURCE_CHECK_LINE" -lt "$AZURE_LOGIN_LINE" ]] ||
  fail 'All source validation must complete before Azure login.'

require "$RUNNER" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$RUNNER" 'CURRENT_API_IMAGE="$(resolve_digest "$RAW_API_IMAGE")"'
require "$RUNNER" 'CURRENT_WEB_IMAGE="$(resolve_digest "$RAW_WEB_IMAGE")"'
require "$RUNNER" '[[ "${API_MODE,,}" == single && "${WEB_MODE,,}" == single ]]'
require "$RUNNER" 'restore_web'
require "$RUNNER" 'restore_api'
require "$RUNNER" 'Web rollback skipped because another image is active'
require "$RUNNER" 'API rollback skipped because another image is active'
require "$RUNNER" 'API_REPOSITORY='
require "$RUNNER" 'WEB_REPOSITORY='
require "$RUNNER" 'module001-ptc-timer-dom-$TARGET_COMMIT'
require "$RUNNER" "probe_api ptc_v2_users GET '/api/runtime/timesheet/steward/v2/users?weekStart=2026-07-26' '401,403'"
require "$RUNNER" "probe_api ptc_v2_workspace GET '/api/runtime/timesheet/steward/v2/users/00000000-0000-0000-0000-000000000000/workspace?weekStart=2026-07-26' '401,403'"
require "$RUNNER" "probe_api ptc_v2_move POST '/api/runtime/timesheet/steward/v2/entries/00000000-0000-0000-0000-000000000000/move' '401,403'"
require "$RUNNER" "probe_api timer_targets GET '/api/timesheet/timers/targets?weekStart=2026-07-26' '401,403'"
require "$RUNNER" "probe_api timer_active GET '/api/timesheet/timers/active' '401,403'"
require "$RUNNER" "probe_api timer_history GET '/api/timesheet/timers/history?weekStart=2026-07-26' '401,403'"
require "$RUNNER" "probe_api rbac_bootstrap GET '/api/rbac/v1/bootstrap' '401,403'"
require "$RUNNER" "probe_api module026_providers GET '/api/integrations/026/providers' '401,403'"
require "$RUNNER" "probe_api platform_overview GET '/api/platform-operations/overview' '401,403'"
require "$RUNNER" 'MODULE001_PTC_TIMER_DOM_API_PROTECTED_BOUNDARY=PASS'
require "$RUNNER" '/api/runtime/timesheet/steward/v2/users'
require "$RUNNER" '/api/runtime/timesheet/steward/v2/entries/'
require "$RUNNER" 'Engineering, Engineering Lead, Project Management, and Project Management Lead'
require "$RUNNER" 'Move time across all supported activity types'
require "$RUNNER" 'Requests / Service Requests'
require "$RUNNER" 'Project Tasks'
require "$RUNNER" 'Non-Project Time'
require "$RUNNER" '/api/timesheet/timers/targets'
require "$RUNNER" '/api/timesheet/timers/active'
require "$RUNNER" '/api/timesheet/timers/history'
require "$RUNNER" 'Timer history'
require "$RUNNER" 'sign-out, and session expiration'
require "$RUNNER" 'data-projectpulse-react-owned-slot'
require "$RUNNER" 'react-owned-v1'
require "$RUNNER" 'attributes-only-v1'
require "$RUNNER" 'Search by page name'
require "$RUNNER" 'Edit connection'
require "$RUNNER" 'Add CRM platform'
require "$RUNNER" '/api/platform-operations/overview'
require "$RUNNER" "! grep -Fq 'ptc-runtime-task-catalog-host'"
require "$RUNNER" "! grep -Fq 'module001-toolbar-host'"
require "$RUNNER" 'MODULE001_PTC_TIMER_DOM_API_VALIDATION=PASS'
require "$RUNNER" 'MODULE001_PTC_TIMER_DOM_WEB_VALIDATION=PASS'
require "$RUNNER" 'module001-ptc-timer-dom-module026-test-deployment.json'
require "$RUNNER" '"databaseMutation": false'
require "$RUNNER" '"module001ResultExecution": "explicit-iresult-v1"'
require "$RUNNER" '"crossTaskAssignmentDuringMove": "authorized-and-audited"'
require "$RUNNER" '"submissionOnBehalf": false'
require "$RUNNER" '"timerAuthority": "server"'
require "$RUNNER" '"runtimeNavigationMutation": "attributes-only"'
require "$RUNNER" '"module026Pr207": "editable-built-in-and-custom-connectors-included"'
require "$RUNNER" '"group2A": "modules-013-016-068-preserved"'
require "$RUNNER" '"timeOrTimerMutationPerformedByDeployment": false'
require "$RUNNER" '"credentialValuesChanged": false'
require "$RUNNER" '"externalProviderCallsPerformedByDeployment": false'
require "$RUNNER" 'MODULE001_PTC_TIMER_DOM_MODULE026_TEST_DEPLOYMENT=COMPLETE'
reject "$RUNNER" 'PROJECTPULSE_TEST_DATABASE_URL'
reject "$RUNNER" '\bpsql\b'
reject "$RUNNER" 'database/migrations/[0-9]'
reject "$RUNNER" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$RUNNER" 'environment:[[:space:]]*production'
reject "$RUNNER" 'api\.getbase\.com|graph\.microsoft\.com|sendMail|smtp\.office365\.com'

require "$VALIDATE" 'name: Validate Module 001 PTC Timer DOM and Module 026 Test Deployment'
require "$VALIDATE" 'release/module001-ptc-timer-dom-module026-test-*'
require "$VALIDATE" 'Enforce exact four-file deployment-control scope'
require "$VALIDATE" 'scripts/validate-module001-ptc-timer-dom-module026-test-deployment.sh'
require "$VALIDATE" 'bash -n scripts/run-module001-ptc-timer-dom-module026-test-deployment.sh'
require "$VALIDATE" 'ProjectTime.Api.RouteResultExecutionTests.csproj'
require "$VALIDATE" 'ProjectTime.Api.AuthorizationTests.csproj'
require "$VALIDATE" 'test-projectpulse-api-startup.sh'
require "$VALIDATE" 'validate-module-001-timesheet-timer-mobile.mjs'
require "$VALIDATE" 'validate-module-001-ptc-time-steward.mjs'
require "$VALIDATE" 'validate-module-001-ptc-timer-dom-ownership.mjs'
require "$VALIDATE" 'validate-group-1-navigation-work-consolidation.mjs'
require "$VALIDATE" 'validate-module-026-crm-erp-integrations.mjs'
require "$VALIDATE" 'validate-group-2a-provider-neutral-platform-operations.mjs'
require "$VALIDATE" 'npm run build'
reject "$VALIDATE" 'azure/login'
reject "$VALIDATE" 'az[[:space:]]+containerapp[[:space:]]+(update|job)'
reject "$VALIDATE" 'environment:[[:space:]]*production'
reject "$VALIDATE" 'PROJECTPULSE_TEST_DATABASE_URL'
reject "$VALIDATE" '\bpsql\b'

bash -n "$RUNNER"
bash -n "$SELF"
echo 'MODULE001_PTC_TIMER_DOM_MODULE026_TEST_DEPLOYMENT_GUARD=PASS'
