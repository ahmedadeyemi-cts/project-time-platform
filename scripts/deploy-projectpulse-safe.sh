#!/usr/bin/env bash
set -Eeuo pipefail

REPO_ROOT="${PROJECTPULSE_REPO_ROOT:-/opt/project-time-platform/app/project-time-platform}"
DB_NAME="${PROJECTPULSE_DB_NAME:-Project Health Dashboard}"
BACKUP_ROOT="${PROJECTPULSE_BACKUP_ROOT:-/opt/project-time-platform/backups/manual-deploy}"
PUBLIC_URL="${PROJECTPULSE_PUBLIC_URL:-https://projectpulse-test.onenecklab.com}"

cd "$REPO_ROOT"

STAMP="$(date +%Y%m%d-%H%M%S)"
BACKUP_DIR="$BACKUP_ROOT/$STAMP"

echo "== Project Health Dashboard safe deployment =="
echo "Repo: $REPO_ROOT"
echo "Backup dir: $BACKUP_DIR"
echo "Public URL: $PUBLIC_URL"

mkdir -p "$BACKUP_DIR"

echo "== Pre-deploy git status =="
git status --short | tee "$BACKUP_DIR/git-status-before.txt"

echo "== Database backup =="
if command -v pg_dump >/dev/null 2>&1; then
  sudo -u postgres pg_dump -Fc "$DB_NAME" > "$BACKUP_DIR/${DB_NAME}.dump"
  echo "Database backup created: $BACKUP_DIR/${DB_NAME}.dump"
else
  echo "pg_dump not found. Refusing to deploy without database backup."
  exit 1
fi

echo "== Backend build and publish =="
dotnet restore src/backend/ProjectTime.Api/ProjectTime.Api.csproj
dotnet build src/backend/ProjectTime.Api/ProjectTime.Api.csproj --configuration Release --no-restore

API_PUBLISH_TMP="/tmp/projectpulse-api-publish-$STAMP"
rm -rf "$API_PUBLISH_TMP"
dotnet publish src/backend/ProjectTime.Api/ProjectTime.Api.csproj --configuration Release --no-build --output "$API_PUBLISH_TMP"

mkdir -p "$BACKUP_DIR/published-api-before"
if [ -d /opt/project-time-platform/app/published/api ]; then
  cp -a /opt/project-time-platform/app/published/api/. "$BACKUP_DIR/published-api-before/"
fi

mkdir -p /opt/project-time-platform/app/published/api
rsync -a --delete "$API_PUBLISH_TMP"/ /opt/project-time-platform/app/published/api/

echo "Published API updated at /opt/project-time-platform/app/published/api"

echo "== Frontend build =="
pushd src/frontend/project-time-web >/dev/null
if [ -f package-lock.json ]; then
  npm ci
else
  npm install
fi
npm run build
popd >/dev/null

echo "== Restart services =="
sudo systemctl restart projecttime-api.service
sudo systemctl restart projecttime-frontend-public.service
sudo systemctl reload nginx.service || sudo systemctl restart nginx.service

echo "== Health checks =="
sleep 3
curl -fsS "$PUBLIC_URL/health" | tee "$BACKUP_DIR/health.json"
echo
curl -fsS "$PUBLIC_URL/api/version" | tee "$BACKUP_DIR/version.json"
echo

echo "== Service status =="
systemctl --no-pager --full status projecttime-api.service | tee "$BACKUP_DIR/projecttime-api.status.txt" || true
systemctl --no-pager --full status projecttime-frontend-public.service | tee "$BACKUP_DIR/projecttime-frontend-public.status.txt" || true
systemctl --no-pager --full status nginx.service | tee "$BACKUP_DIR/nginx.status.txt" || true

echo "Safe deployment completed."
