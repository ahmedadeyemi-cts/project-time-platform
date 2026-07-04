#!/usr/bin/env bash
set -euo pipefail

# Project Health Dashboard remaining sections one-time script
# Usage:
#   ./deployment/rocky-linux/project-pulse-remaining-sections-one-time.sh

APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
GIT_KEY="$HOME/.ssh/github_project_time_platform"
LOG_FILE="/tmp/project-pulse-remaining-sections.log"

log() {
  echo
  echo "============================================================"
  echo "==> $*"
  echo "============================================================"
}

run_if_exists() {
  local script_path="$1"
  if [ -f "$script_path" ]; then
    chmod +x "$script_path"
    echo "==> Running $script_path"
    "$script_path"
  else
    echo "==> Skipping missing script: $script_path"
  fi
}

main() {
  exec > >(tee "$LOG_FILE") 2>&1

  log "Starting Project Health Dashboard remaining sections implementation"

  if [ ! -d "$REPO_DIR/.git" ]; then
    echo "ERROR: $REPO_DIR is not a Git repository."
    exit 1
  fi

  cd "$REPO_DIR"

  log "Stopping frontend service before rebuild"
  sudo systemctl stop projecttime-frontend-public.service 2>/dev/null || true

  log "Pulling latest repo"
  if [ -f "$GIT_KEY" ]; then
    GIT_SSH_COMMAND="ssh -i $GIT_KEY -o IdentitiesOnly=yes" git pull
  else
    git pull
  fi

  log "Applying foundation migrations and current stability repairs"
  run_if_exists deployment/rocky-linux/apply-migration-011.sh
  run_if_exists deployment/rocky-linux/repair-all-time-entry-visibility.sh
  run_if_exists deployment/rocky-linux/repair-timesheet-week-500-order-by.sh
  run_if_exists deployment/rocky-linux/repair-row-state-labels.sh

  log "Applying remaining PSA module API and UI patches"
  run_if_exists deployment/rocky-linux/apply-remaining-psa-module-api-patch.sh
  run_if_exists deployment/rocky-linux/apply-remaining-psa-module-ui-patch.sh

  log "Publishing API"
  chmod +x deployment/rocky-linux/install-api-systemd-service.sh
  ./deployment/rocky-linux/install-api-systemd-service.sh

  log "Building frontend"
  chmod +x deployment/rocky-linux/build-frontend.sh
  ./deployment/rocky-linux/build-frontend.sh

  log "Restarting restricted public frontend"
  sudo systemctl restart projecttime-frontend-public.service

  log "Validation checks"
  echo "API version:"
  curl -s http://127.0.0.1:5080/api/version | jq . || curl -s http://127.0.0.1:5080/api/version || true

  echo
  echo "Schema table count:"
  curl -s http://127.0.0.1:5080/api/schema/tables | jq '.count' || true

  echo
  echo "Timesheet entries/day statuses:"
  curl -s "http://127.0.0.1:5080/api/timesheets/week?weekStart=2026-06-21" | jq '.entries, .dayStatuses' || true

  echo
  echo "Remaining module endpoints:"
  curl -s http://127.0.0.1:5080/api/project-intake/summary | jq '.count, .templates | length' || true
  curl -s http://127.0.0.1:5080/api/project-management/summary | jq '.milestoneCount, .riskCount' || true
  curl -s "http://127.0.0.1:5080/api/resource-scheduling/capacity?weekStart=2026-06-21" | jq '.count' || true
  curl -s http://127.0.0.1:5080/api/expenses/summary | jq '.count' || true
  curl -s http://127.0.0.1:5080/api/invoicing/summary | jq '.count' || true
  curl -s http://127.0.0.1:5080/api/reporting/executive-dashboard | jq '.count' || true

  echo
  echo "Frontend status:"
  curl -s -I http://127.0.0.1:5173/ || true

  echo
  echo "Service status:"
  systemctl --no-pager --full status projecttime-api.service || true
  systemctl --no-pager --full status projecttime-frontend-public.service || true

  log "Remaining sections implementation complete"
  echo "Log saved to: $LOG_FILE"
  echo "Test URL: http://167.234.223.32:5173/"
}

main "$@"
