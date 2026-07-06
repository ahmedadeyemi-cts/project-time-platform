#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="${APP_ROOT:-/opt/project-time-platform/app/project-time-platform-022}"
BACKUP_ROOT="${BACKUP_ROOT:-/opt/project-time-platform/app/projectpulse-restore-smoke}"
SERVICE_NAME="${SERVICE_NAME:-projecttime-api.service}"

echo "============================================================"
echo "ProjectPulse 050 Backup Restore Smoke"
echo "============================================================"
echo "APP_ROOT=$APP_ROOT"
echo "BACKUP_ROOT=$BACKUP_ROOT"
echo "SERVICE_NAME=$SERVICE_NAME"
echo "TIME=$(date -Is)"
echo

mkdir -p "$BACKUP_ROOT"

echo "This script is intentionally conservative."
echo "It verifies backup/restore readiness inputs and creates a restore-smoke run folder."
echo "It does not overwrite production data."
echo

RUN_DIR="$BACKUP_ROOT/run-$(date -u +%Y%m%d%H%M%S)"
mkdir -p "$RUN_DIR"

{
  echo "ProjectPulse 050 Backup Restore Smoke"
  echo "time=$(date -Is)"
  echo "app_root=$APP_ROOT"
  echo "service_name=$SERVICE_NAME"
  echo "git_commit=$(cd "$APP_ROOT" && git rev-parse HEAD 2>/dev/null || echo unknown)"
  echo "git_status=$(cd "$APP_ROOT" && git status --short 2>/dev/null | wc -l)"
  echo
  echo "Required manual production DR drill:"
  echo "1. Capture a fresh database backup."
  echo "2. Restore to a disposable database or VM."
  echo "3. Start API against the restored database."
  echo "4. Confirm /health returns 200."
  echo "5. Confirm profile_photo_data_url and profile_photo_updated_at exist on app_users."
  echo "6. Confirm time-entry, approvals, accounting export, and closeout read paths load."
  echo "7. Record the restore result and timestamp."
} > "$RUN_DIR/restore-smoke-manifest.txt"

echo "Restore smoke manifest written:"
cat "$RUN_DIR/restore-smoke-manifest.txt"
echo

echo "NOTE: This is a repeatable DR smoke harness, not a destructive restore."
echo "Next hardening after 050 should wire this to an actual disposable DB restore."
