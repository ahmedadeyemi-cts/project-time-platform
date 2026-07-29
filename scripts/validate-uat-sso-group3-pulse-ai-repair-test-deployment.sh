#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXPECTED_RELEASE="b402f99130341995d692bb9f85fe195255c8ffb5"
SLUG="uat-sso-group3-pulse-ai-repair"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-uat-sso-group3-module011-repair-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-uat-sso-group3-module011-repair-test-deployment.yml"
RUNNER="$ROOT/scripts/run-$SLUG-test-deployment.sh"
SELF="$ROOT/scripts/validate-$SLUG-test-deployment.sh"

fail() { echo "ERROR: $*" >&2; exit 1; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in $1: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden contract in $1: $2"; }

for file in "$DEPLOY" "$VALIDATE" "$RUNNER" "$SELF"; do
  [[ -f "$file" ]] || fail "Missing $file"
done

require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'DEPLOY-UAT-SSO-GROUP3-PULSEAI-REPAIR-TO-TEST'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'SOURCE_BASELINE_COMMIT: 34f63946e80bd17ce9fa33c72ae2674f62081bae'
require "$DEPLOY" "run-$SLUG-test-deployment.sh"
require "$DEPLOY" 'This repair release is API/web only and must not carry a database migration.'

require "$RUNNER" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$RUNNER" 'probe_sso_state_origin_recovery'
require "$RUNNER" 'trusted_public_origin_unavailable'
require "$RUNNER" 'Project portfolio command center'
require "$RUNNER" 'Engineering assignments and project evidence'
require "$RUNNER" 'Customer, SELL, and delivery readiness'
require "$RUNNER" 'Projects, customers, SELL, and governed rates'
require "$RUNNER" '"ssoStateOriginRecoveryProbe": true'
require "$RUNNER" '"atomicStateConsumptionPreserved": true'
require "$RUNNER" '"nonceValidationPreserved": true'
require "$RUNNER" '"module011VisibleName": "Pulse AI"'
require "$RUNNER" '"pulseAiDeepIntelligenceIncluded": false'
require "$RUNNER" '"vectorIndexCreated": false'
require "$RUNNER" '"externalProviderRoutingActivated": false'
require "$RUNNER" '"databaseMigration": false'

require "$VALIDATE" 'Enforce exact four-file deployment-control scope'
require "$VALIDATE" 'Check out exact repair source release'
require "$VALIDATE" 'Build ProjectTime API from exact source release'
require "$VALIDATE" 'Build complete frontend production bundle from exact source release'

reject "$DEPLOY" '^[[:space:]]*push:'
reject "$DEPLOY" '^[[:space:]]*schedule:'
reject "$DEPLOY" 'environment:[[:space:]]*production'
reject "$DEPLOY" '\bpsql\b|az[[:space:]]+role[[:space:]]+assignment|graph\.microsoft\.com|/sendMail|api\.openai\.com|api\.anthropic\.com'
reject "$RUNNER" '\bpsql\b|database/migrations/[0-9]|az[[:space:]]+role[[:space:]]+assignment|graph\.microsoft\.com|/sendMail|api\.openai\.com|api\.anthropic\.com'

bash -n "$RUNNER"
echo 'UAT_SSO_GROUP3_PULSE_AI_REPAIR_TEST_DEPLOYMENT_GUARD=PASS'
