#!/usr/bin/env bash
set -euo pipefail

# 041J_ACTIVE_DEPLOY_STANDARD_START
# Project Health Dashboard / ProjectPulse active deployment script.
# This publishes backend changes to the actual systemd API path:
# /opt/project-time-platform/app/published/api
#
# Usage:
#   deployment/rocky-linux/projectpulse-deploy-active.sh
#
# Optional overrides:
#   APP_ROOT=/path/to/repo
#   PUBLIC_BASE_URL=https://projectpulse-test.onenecklab.com
#   LOCAL_API_BASE_URL=http://127.0.0.1:5080
#   ALLOW_DIRTY=1 deployment/rocky-linux/projectpulse-deploy-active.sh

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_ROOT="${APP_ROOT:-$(cd "$SCRIPT_DIR/../.." && pwd)}"

API_PROJECT="${API_PROJECT:-$APP_ROOT/src/backend/ProjectTime.Api/ProjectTime.Api.csproj}"
FRONTEND_ROOT="${FRONTEND_ROOT:-$APP_ROOT/src/frontend/project-time-web}"

API_PUBLISHED="${API_PUBLISHED:-/opt/project-time-platform/app/published/api}"
API_BACKUP_ROOT="${API_BACKUP_ROOT:-/opt/project-time-platform/app/published/api-backups}"
API_STAGE="${API_STAGE:-/tmp/projecttime-api-publish-active-$(date +%Y%m%d%H%M%S)}"
API_BACKUP="${API_BACKUP_ROOT}/api-$(date +%Y%m%d%H%M%S)"

API_SERVICE="${API_SERVICE:-projecttime-api.service}"
FRONTEND_SERVICE="${FRONTEND_SERVICE:-projecttime-frontend-public.service}"
NGINX_SERVICE="${NGINX_SERVICE:-nginx.service}"

PUBLIC_BASE_URL="${PUBLIC_BASE_URL:-https://projectpulse-test.onenecklab.com}"
LOCAL_API_BASE_URL="${LOCAL_API_BASE_URL:-http://127.0.0.1:5080}"

require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "ERROR: Required command not found: $1"
    exit 1
  }
}

require_command dotnet
require_command npm
require_command curl
require_command rsync
require_command sudo
require_command systemctl
require_command sha256sum

cd "$APP_ROOT"

echo "============================================================"
echo "ProjectPulse active deployment"
echo "============================================================"
echo "APP_ROOT=$APP_ROOT"
echo "API_PROJECT=$API_PROJECT"
echo "FRONTEND_ROOT=$FRONTEND_ROOT"
echo "API_PUBLISHED=$API_PUBLISHED"
echo "PUBLIC_BASE_URL=$PUBLIC_BASE_URL"
echo "LOCAL_API_BASE_URL=$LOCAL_API_BASE_URL"
echo "TIME=$(date -Is)"
echo

if [ ! -f "$API_PROJECT" ]; then
  echo "ERROR: API project not found: $API_PROJECT"
  exit 1
fi

if [ ! -f "$FRONTEND_ROOT/package.json" ]; then
  echo "ERROR: Frontend package.json not found: $FRONTEND_ROOT/package.json"
  exit 1
fi

if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "============================================================"
  echo "Git source revision"
  echo "============================================================"
  git log -1 --oneline
  git status --short
  echo

  if [ "${ALLOW_DIRTY:-0}" != "1" ] && [ -n "$(git status --short)" ]; then
    echo "ERROR: Worktree is dirty. Commit/stash changes or set ALLOW_DIRTY=1 for controlled validation."
    exit 1
  fi
fi

echo "============================================================"
echo "Active API service target"
echo "============================================================"
sudo systemctl cat "$API_SERVICE" | grep -E "WorkingDirectory=|ExecStart=" || true
echo

echo "============================================================"
echo "Build and publish API to staging"
echo "============================================================"
rm -rf "$API_STAGE"
mkdir -p "$API_STAGE"

dotnet publish "$API_PROJECT" -c Release -o "$API_STAGE"

if [ ! -f "$API_STAGE/ProjectTime.Api.dll" ]; then
  echo "ERROR: API publish did not produce ProjectTime.Api.dll."
  exit 1
fi

echo
echo "============================================================"
echo "Build frontend"
echo "============================================================"
npm --prefix "$FRONTEND_ROOT" run build

echo
echo "============================================================"
echo "Validate staged API includes known active routes"
echo "============================================================"
if strings -a -el "$API_STAGE/ProjectTime.Api.dll" | grep -F "/api/project-closeout/email/send" >/dev/null 2>&1 \
  || strings -a "$API_STAGE/ProjectTime.Api.dll" | grep -F "/api/project-closeout/email/send" >/dev/null 2>&1; then
  echo "Found /api/project-closeout/email/send in staged API DLL."
else
  echo "WARNING: /api/project-closeout/email/send was not visible in staged DLL strings."
fi

if strings -a -el "$API_STAGE/ProjectTime.Api.dll" | grep -F "/api/admin/user-admin/users/profile" >/dev/null 2>&1 \
  || strings -a "$API_STAGE/ProjectTime.Api.dll" | grep -F "/api/admin/user-admin/users/profile" >/dev/null 2>&1; then
  echo "Found /api/admin/user-admin/users/profile in staged API DLL."
else
  echo "WARNING: /api/admin/user-admin/users/profile was not visible in staged DLL strings."
fi

echo
echo "============================================================"
echo "Backup active API publish folder"
echo "============================================================"
if [ ! -d "$API_PUBLISHED" ]; then
  echo "ERROR: Active published API folder does not exist: $API_PUBLISHED"
  exit 1
fi

sudo mkdir -p "$API_BACKUP_ROOT"
sudo cp -a "$API_PUBLISHED" "$API_BACKUP"
echo "Backup created: $API_BACKUP"

echo
echo "============================================================"
echo "Deploy staged API to active published folder"
echo "============================================================"
sudo rsync -a --delete "$API_STAGE"/ "$API_PUBLISHED"/
sudo chown -R opc:opc "$API_PUBLISHED"

echo
echo "============================================================"
echo "Checksum validation"
echo "============================================================"
sha256sum "$API_STAGE/ProjectTime.Api.dll" "$API_PUBLISHED/ProjectTime.Api.dll"

echo
echo "============================================================"
echo "Restart services"
echo "============================================================"
restore_api_backup() {
  echo "Restoring API backup from $API_BACKUP"
  sudo rsync -a --delete "$API_BACKUP"/ "$API_PUBLISHED"/
  sudo chown -R opc:opc "$API_PUBLISHED"
  sudo systemctl restart "$API_SERVICE" || true
}

sudo systemctl restart "$API_SERVICE"
sleep 4

if ! systemctl is-active --quiet "$API_SERVICE"; then
  echo "ERROR: API service failed after deploy."
  restore_api_backup
  systemctl status "$API_SERVICE" --no-pager -l || true
  exit 1
fi

sudo systemctl restart "$FRONTEND_SERVICE"
sudo systemctl reload "$NGINX_SERVICE" || sudo systemctl restart "$NGINX_SERVICE"
sleep 3

systemctl is-active --quiet "$FRONTEND_SERVICE" || {
  systemctl status "$FRONTEND_SERVICE" --no-pager -l
  exit 1
}

systemctl is-active --quiet "$NGINX_SERVICE" || {
  systemctl status "$NGINX_SERVICE" --no-pager -l
  exit 1
}

echo "Services active."

echo
echo "============================================================"
echo "Smoke tests"
echo "============================================================"

health_tmp="$(mktemp)"
health_code="$(curl -ksS -o "$health_tmp" -w '%{http_code}' "$LOCAL_API_BASE_URL/health" || true)"
echo "GET $LOCAL_API_BASE_URL/health -> $health_code"
cat "$health_tmp"
echo
rm -f "$health_tmp"

if [ "$health_code" != "200" ]; then
  echo "ERROR: Local API health check failed."
  restore_api_backup
  exit 1
fi

closeout_tmp="$(mktemp)"
closeout_code="$(curl -ksS -X POST \
  -H 'Content-Type: application/json' \
  -o "$closeout_tmp" \
  -w '%{http_code}' \
  -d '{"projectCode":"ROUTE-DIAG","projectName":"Route Diagnostic","customerName":"Route Diagnostic Customer","projectStatus":"Closed","projectManagerName":"Demo Manager","projectManagerEmail":"ahmed.adeyemi+03@ussignal.com","recipients":[],"subject":"","body":"","triggeredBy":"deployment smoke"}' \
  "$PUBLIC_BASE_URL/api/project-closeout/email/send" || true)"
echo "POST $PUBLIC_BASE_URL/api/project-closeout/email/send without session -> $closeout_code"
cat "$closeout_tmp"
echo
rm -f "$closeout_tmp"

if [ "$closeout_code" != "401" ]; then
  echo "ERROR: Expected protected closeout email route to return 401 without session."
  exit 1
fi

profile_tmp="$(mktemp)"
profile_code="$(curl -ksS -X POST \
  -H 'Content-Type: application/json' \
  -o "$profile_tmp" \
  -w '%{http_code}' \
  -d '{"userId":"00000000-0000-0000-0000-000000000000","email":"deployment-smoke@example.com","displayName":"Deployment Smoke","jobTitle":"Smoke","departmentName":"Project Management Office","teamName":"Project Management","officeLocation":"","managerEmail":"","loginEnabled":true,"isActive":true}' \
  "$PUBLIC_BASE_URL/api/admin/user-admin/users/profile" || true)"
echo "POST $PUBLIC_BASE_URL/api/admin/user-admin/users/profile without session -> $profile_code"
cat "$profile_tmp"
echo
rm -f "$profile_tmp"

if [ "$profile_code" != "401" ]; then
  echo "ERROR: Expected protected user profile route to return 401 without session."
  exit 1
fi

echo
echo "============================================================"
echo "Active deployment completed successfully"
echo "============================================================"
echo "Backend published to: $API_PUBLISHED"
echo "API backup created at: $API_BACKUP"
echo "Frontend built from: $FRONTEND_ROOT"
echo
echo "Browser validation recommended after backend route changes:"
echo "- Hard refresh $PUBLIC_BASE_URL"
echo "- Test the changed workflow using DevTools Network"
# 041J_ACTIVE_DEPLOY_STANDARD_END
