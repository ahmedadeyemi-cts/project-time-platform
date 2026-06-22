#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
API_DIR="$REPO_DIR/src/backend/ProjectTime.Api"
PUBLISH_DIR="$APP_ROOT/app/published/api"
SERVICE_SRC="$REPO_DIR/deployment/rocky-linux/projecttime-api.service"
SERVICE_DST="/etc/systemd/system/projecttime-api.service"
ENV_FILE="$APP_ROOT/config/postgres.env"

if [ ! -f "$ENV_FILE" ]; then
  echo "ERROR: Missing $ENV_FILE"
  exit 1
fi

if [ ! -f "$SERVICE_SRC" ]; then
  echo "ERROR: Missing $SERVICE_SRC"
  exit 1
fi

if [ ! -d "$API_DIR" ]; then
  echo "ERROR: Missing $API_DIR"
  exit 1
fi

echo "==> Stopping any manual API processes"
pkill -f 'ProjectTime.Api' || true
pkill -f 'dotnet run' || true

echo "==> Creating publish directory"
mkdir -p "$PUBLISH_DIR"

echo "==> Publishing API"
cd "$API_DIR"
dotnet restore
dotnet publish -c Release -o "$PUBLISH_DIR"

echo "==> Installing systemd service"
sudo cp "$SERVICE_SRC" "$SERVICE_DST"
sudo systemctl daemon-reload
sudo systemctl enable --now projecttime-api

echo "==> Service status"
sudo systemctl status projecttime-api --no-pager

echo "==> Local listener check"
ss -ltnp | grep 5080 || true

echo "==> API health checks"
curl -s http://127.0.0.1:5080/health && echo
curl -s http://127.0.0.1:5080/api/db-health && echo
curl -s http://127.0.0.1:5080/api/schema/tables && echo

echo "==> Project Time API systemd setup complete"
