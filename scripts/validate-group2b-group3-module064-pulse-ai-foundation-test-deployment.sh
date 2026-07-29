#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXPECTED_RELEASE="7db5a59fd5bd0850b6ea496a6d4e0d8ca0e02a0d"
WORKFLOW_SLUG="group2b-group3-module064-foundation"
SCRIPT_SLUG="group2b-group3-module064-pulse-ai-foundation"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-$WORKFLOW_SLUG-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-$WORKFLOW_SLUG-test-deployment.yml"
RUNNER="$ROOT/scripts/run-$SCRIPT_SLUG-test-deployment.sh"
SELF="$ROOT/scripts/validate-$SCRIPT_SLUG-test-deployment.sh"
fail() { echo "ERROR: $*" >&2; exit 1; }
for file in "$DEPLOY" "$VALIDATE" "$RUNNER" "$SELF"; do [[ -f "$file" ]] || fail "Missing $file"; done
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in $1: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden contract in $1: $2"; }
require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'DEPLOY-GROUP2B-GROUP3-MODULE064-PULSEAI-FOUNDATION-TO-TEST'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'SOURCE_BASELINE_COMMIT: 185a0030dbc96813c8cd46498668ca289805a4d7'
require "$DEPLOY" "run-$SCRIPT_SLUG-test-deployment.sh"
require "$DEPLOY" 'This cumulative release is API/web only and must not carry a database migration.'
require "$RUNNER" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$RUNNER" 'probe_json group2b'
require "$RUNNER" 'probe_json group3'
require "$RUNNER" 'probe_json module064'
require "$RUNNER" 'pulseAiDeepIntelligenceIncluded": false'
require "$RUNNER" 'vectorIndexCreated": false'
require "$RUNNER" 'externalProviderRoutingActivated": false'
require "$RUNNER" 'databaseMigration": false'
require "$VALIDATE" 'Enforce exact four-file deployment-control scope'
reject "$DEPLOY" '^[[:space:]]*push:'
reject "$DEPLOY" '^[[:space:]]*schedule:'
reject "$DEPLOY" 'environment:[[:space:]]*production'
reject "$DEPLOY" '\bpsql\b|az[[:space:]]+role[[:space:]]+assignment|graph\.microsoft\.com|/sendMail'
reject "$RUNNER" '\bpsql\b|database/migrations/[0-9]|az[[:space:]]+role[[:space:]]+assignment|graph\.microsoft\.com|/sendMail|api\.openai\.com|api\.anthropic\.com'
bash -n "$RUNNER"
echo 'GROUP2B_GROUP3_MODULE064_PULSE_AI_FOUNDATION_TEST_DEPLOYMENT_GUARD=PASS'
