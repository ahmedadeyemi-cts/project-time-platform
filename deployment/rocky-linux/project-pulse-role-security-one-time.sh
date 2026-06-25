#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
GIT_KEY="$HOME/.ssh/github_project_time_platform"
LOG_FILE="/tmp/project-pulse-role-security.log"

exec > >(tee "$LOG_FILE") 2>&1

cd "$REPO_DIR"

if [ -f "$GIT_KEY" ]; then
  GIT_SSH_COMMAND="ssh -i $GIT_KEY -o IdentitiesOnly=yes" git pull
else
  git pull
fi

sudo systemctl stop projecttime-frontend-public.service 2>/dev/null || true

chmod +x deployment/rocky-linux/apply-migration-013.sh
./deployment/rocky-linux/apply-migration-013.sh

chmod +x deployment/rocky-linux/apply-role-security-api-patch.sh
./deployment/rocky-linux/apply-role-security-api-patch.sh

chmod +x deployment/rocky-linux/apply-role-aware-ui-patch.sh
./deployment/rocky-linux/apply-role-aware-ui-patch.sh

chmod +x deployment/rocky-linux/install-api-systemd-service.sh
./deployment/rocky-linux/install-api-systemd-service.sh

chmod +x deployment/rocky-linux/build-frontend.sh
./deployment/rocky-linux/build-frontend.sh

sudo systemctl restart projecttime-frontend-public.service

curl -s http://127.0.0.1:5080/api/version | jq . || true
curl -s http://127.0.0.1:5080/api/security/me | jq . || true
curl -s http://127.0.0.1:5080/api/security/role-matrix | jq '.count, .roles[].roleCode' || true
curl -s -I http://127.0.0.1:5173/ || true

echo "Log saved to $LOG_FILE"
