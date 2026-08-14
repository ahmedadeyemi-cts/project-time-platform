#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATION="$ROOT/database/migrations/088_systemwide_enterprise_reliability.sql"
ROLLBACK="$ROOT/database/rollback/088_systemwide_enterprise_reliability_rollback.sql"
[[ -f "$MIGRATION" && -f "$ROLLBACK" ]]
grep -Fq "088_systemwide_enterprise_reliability" "$MIGRATION"
grep -Fq "account_executive_user_id" "$MIGRATION"
grep -Fq "solution_architect_user_id" "$MIGRATION"
grep -Fq "idx_projectpulse_system_audit_events_correlation" "$MIGRATION"
grep -Fq "idx_auth_login_events_user_result" "$MIGRATION"
grep -Fq "DELETE FROM schema_migrations WHERE migration_id='088_systemwide_enterprise_reliability'" "$ROLLBACK"
! grep -Fq 'DROP COLUMN IF EXISTS account_executive_user_id' "$ROLLBACK"
! grep -Fq 'DROP COLUMN IF EXISTS solution_architect_user_id' "$ROLLBACK"
echo "MIGRATION_088_CONTRACT=PASS"
