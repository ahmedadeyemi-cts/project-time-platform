#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATION="$ROOT/database/migrations/091_module_management_owner_storage_repair.sql"
ROLLBACK="$ROOT/database/rollback/091_module_management_owner_storage_repair_rollback.sql"
BACKEND="$ROOT/src/backend/ProjectTime.Api/Modules/ModuleCatalogOwnershipModule.cs"
ACCOUNT_CENTER="$ROOT/src/frontend/project-time-web/src/AccountCenterPortal.jsx"
DEPLOYMENT="$ROOT/.github/workflows/projectpulse-deploy-test.yml"
RUNNER="$ROOT/scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh"

for file in "$MIGRATION" "$ROLLBACK" "$BACKEND" "$ACCOUNT_CENTER" "$DEPLOYMENT" "$RUNNER"; do
  test -f "$file" || { echo "Missing owner-runtime artifact: $file" >&2; exit 1; }
done

grep -Fq "lower('Ahmed.Adeyemi@ussignal.local')" "$MIGRATION"
grep -Fq 'ADD COLUMN IF NOT EXISTS owner_user_id' "$MIGRATION"
grep -Fq 'MODULE_OWNER_DEFAULT_ASSIGNED' "$MIGRATION"
grep -Fq 'ownershipDoesNotGrantAccess' "$MIGRATION"
grep -Fq 'owner_user_id IS DISTINCT FROM owner_id' "$MIGRATION"
grep -Fq 'module.owner_user_id IS NOT DISTINCT FROM evidence.assigned_owner_user_id' "$ROLLBACK"
grep -Fq '091_module_management_owner_storage_repair.sql' "$BACKEND"
grep -Fq "LIKE '%@ussignal.local'" "$BACKEND"
grep -Fq 'document.documentElement.dataset.pulseLayout = normalized' "$ACCOUNT_CENTER"
grep -Fq 'detail: { experience: normalized, workspaceLayout: normalized }' "$ACCOUNT_CENTER"
grep -Fq 'database/migrations/089_module_catalog_role_administration_reconciliation.sql' "$DEPLOYMENT"
grep -Fq 'database/migrations/091_module_management_owner_storage_repair.sql' "$DEPLOYMENT"
grep -Fq 'MIGRATION_091=APPLIED_AND_VERIFIED' "$DEPLOYMENT"
grep -Fq "Module ownership catalog" "$DEPLOYMENT"
grep -Fq 'projectpulse-migration"] == "086-088-089-091"' "$RUNNER"

echo 'MODULE_MANAGEMENT_OWNER_STORAGE_MIGRATION_091=PASS'
