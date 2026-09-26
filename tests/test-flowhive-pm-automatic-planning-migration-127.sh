#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MIGRATION="$ROOT/database/migrations/127_flowhive_pm_automatic_planning_defaults.sql"
ROLLBACK="$ROOT/database/rollback/127_flowhive_pm_automatic_planning_defaults_rollback.sql"
grep -Fq "project_flowhive_auto_plan_user_defaults" "$MIGRATION"
grep -Fq "'pm_default'" "$MIGRATION"
grep -Fq "127_flowhive_pm_automatic_planning_defaults" "$MIGRATION"
grep -Fq "DROP TABLE IF EXISTS project_flowhive_auto_plan_user_defaults" "$ROLLBACK"
echo "FLOWHIVE_PM_AUTOMATIC_PLANNING_MIGRATION_127=PASSED"
