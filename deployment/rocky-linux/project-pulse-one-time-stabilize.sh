#!/usr/bin/env bash
set -euo pipefail

# Project Pulse one-time stabilization script
# Usage:
#   ./deployment/rocky-linux/project-pulse-one-time-stabilize.sh 45.19.161.17
#
# This script intentionally resets the local working tree to origin/main, then
# reapplies the current migration/patch/repair sequence in a controlled order.
# It also installs a restricted public frontend service on port 5173.

ALLOWED_SOURCE_IP="${1:-45.19.161.17}"
PUBLIC_PORT="${2:-5173}"
APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"
FRONTEND_DIR="$REPO_DIR/src/frontend/project-time-web"
DIST_DIR="$FRONTEND_DIR/dist"
BACKUP_DIR="$APP_ROOT/backups/stabilize-$(date +%Y%m%d-%H%M%S)"
GIT_KEY="$HOME/.ssh/github_project_time_platform"

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

log "Starting Project Pulse one-time stabilization"
echo "Repository: $REPO_DIR"
echo "Allowed public source IP: $ALLOWED_SOURCE_IP"
echo "Public frontend port: $PUBLIC_PORT"

if [ ! -d "$REPO_DIR/.git" ]; then
  echo "ERROR: $REPO_DIR is not a Git repository."
  exit 1
fi

log "Stopping current services and frontend test servers"
sudo systemctl stop projecttime-frontend-public.service 2>/dev/null || true
sudo systemctl stop projecttime-api.service 2>/dev/null || true
pkill -f 'serve-frontend-local.py' 2>/dev/null || true
pkill -f 'serve-frontend-public-restricted.py' 2>/dev/null || true
pkill -f 'ProjectTime.Api.dll' 2>/dev/null || true

log "Backing up current local files before reset"
mkdir -p "$BACKUP_DIR"
cp -a "$API_FILE" "$BACKUP_DIR/Program.cs.before" 2>/dev/null || true
cp -a "$APP_FILE" "$BACKUP_DIR/App.jsx.before" 2>/dev/null || true
cp -a "$REPO_DIR/deployment/rocky-linux/serve-frontend-public-restricted.py" "$BACKUP_DIR/serve-frontend-public-restricted.py.before" 2>/dev/null || true
git -C "$REPO_DIR" status --short > "$BACKUP_DIR/git-status-before.txt" || true
echo "Backup saved to $BACKUP_DIR"

log "Resetting local repo to latest origin/main"
cd "$REPO_DIR"
if [ -f "$GIT_KEY" ]; then
  GIT_SSH_COMMAND="ssh -i $GIT_KEY -o IdentitiesOnly=yes" git fetch origin main
  git reset --hard origin/main
else
  echo "WARNING: $GIT_KEY not found. Trying git fetch without explicit key."
  git fetch origin main
  git reset --hard origin/main
fi

log "Making deployment scripts executable"
chmod +x deployment/rocky-linux/*.sh 2>/dev/null || true
chmod +x deployment/rocky-linux/*.py 2>/dev/null || true

log "Applying database migrations"
run_if_exists deployment/rocky-linux/apply-migration-006.sh
run_if_exists deployment/rocky-linux/apply-migration-007.sh
run_if_exists deployment/rocky-linux/apply-migration-008.sh
run_if_exists deployment/rocky-linux/apply-migration-009.sh
run_if_exists deployment/rocky-linux/apply-migration-010.sh

log "Applying application patches and repairs in controlled order"
run_if_exists deployment/rocky-linux/apply-project-pulse-branding-patch.sh
run_if_exists deployment/rocky-linux/apply-daily-submission-policy-patch.sh
run_if_exists deployment/rocky-linux/apply-daily-open-day-edit-fix.sh
run_if_exists deployment/rocky-linux/apply-open-days-frontend-hard-fix.sh
run_if_exists deployment/rocky-linux/apply-visible-unlock-for-submitted-days.sh
run_if_exists deployment/rocky-linux/apply-manager-approval-api-patch.sh
run_if_exists deployment/rocky-linux/apply-approval-inbox-bulk-api-patch.sh
run_if_exists deployment/rocky-linux/apply-engineer-save-lock-unlock-fix.sh
run_if_exists deployment/rocky-linux/apply-open-tasks-timesheet-patch.sh
run_if_exists deployment/rocky-linux/apply-approval-inbox-bulk-ui-patch.sh
run_if_exists deployment/rocky-linux/apply-current-user-open-tasks-fix.sh
run_if_exists deployment/rocky-linux/repair-project-task-save-submit-flow.sh
run_if_exists deployment/rocky-linux/repair-daily-submit-unlock-endpoints.sh
run_if_exists deployment/rocky-linux/repair-missing-insert-time-entries-helper.sh
run_if_exists deployment/rocky-linux/repair-open-tasks-duplicates-and-stale-dist.sh
run_if_exists deployment/rocky-linux/repair-duplicate-open-tasks-declarations.sh

log "Applying final source normalization guardrails"
python3 - <<'PY'
from pathlib import Path
import re

repo = Path('/opt/project-time-platform/app/project-time-platform')
api_file = repo / 'src/backend/ProjectTime.Api/Program.cs'
app_file = repo / 'src/frontend/project-time-web/src/App.jsx'
api = api_file.read_text()
app = app_file.read_text()

# Identity alignment for seeded demo/user assignments.
api = api.replace('const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.local";', 'const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";')
api = api.replace('const string DevelopmentUserEmail = "engineer@ussignal.local";', 'const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";')
api = api.replace('const string DevelopmentUserEmail = "developer@projectpulse.local";', 'const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";')
api = api.replace('manager@ussignal.local', 'ahmed.adeyemi@ussignal.com')
api = api.replace('dev.engineer@ussignal.local', 'ahmed.adeyemi@ussignal.com')

# Known compile guardrail.
api = api.replace(
    'var comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();',
    'object comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();')

# Version marker for this stabilization build.
api = re.sub(r'version = "0\.[0-9]+\.[0-9]+"', 'version = "0.4.8"', api)

# Frontend duplicate guardrail from repeated patch application.
lines = app.splitlines()
cleaned = []
previous = None
removed = 0
for line in lines:
    stripped = line.strip()
    duplicate_single_line_declarations = {
        'const assignedOpenTasks = openTasks.data?.tasks ?? [];',
    }
    if stripped in duplicate_single_line_declarations and stripped == previous:
        removed += 1
        continue
    cleaned.append(line)
    previous = stripped
app = '\n'.join(cleaned) + '\n'

api_file.write_text(api)
app_file.write_text(app)
print(f'Removed {removed} duplicate frontend declaration(s).')
PY

log "Installing restricted public frontend server script"
cat > deployment/rocky-linux/serve-frontend-public-restricted.py <<'PY'
#!/usr/bin/env python3
"""Restricted public frontend server for Project Pulse validation."""

from __future__ import annotations

import argparse
import importlib.util
import os
from http.server import ThreadingHTTPServer
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
LOCAL_SERVER_PATH = SCRIPT_DIR / "serve-frontend-local.py"

spec = importlib.util.spec_from_file_location("serve_frontend_local", LOCAL_SERVER_PATH)
if spec is None or spec.loader is None:
    raise SystemExit(f"Unable to load {LOCAL_SERVER_PATH}")

module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class RestrictedFrontendProxyHandler(module.FrontendProxyHandler):
    allowed_source_ips = {"127.0.0.1", "::1"}

    def handle_one_request(self) -> None:
        client_ip = self.client_address[0]
        if client_ip not in self.allowed_source_ips:
            try:
                self.send_response(403)
                self.send_header("Content-Type", "text/plain; charset=utf-8")
                self.send_header("Cache-Control", "no-store")
                self.end_headers()
                self.wfile.write(b"Forbidden: source IP is not allowed for this temporary validation server.\n")
            except Exception:
                pass
            print(f"Blocked request from {client_ip}")
            return
        super().handle_one_request()


def parse_allowed_ips(value: str) -> set[str]:
    configured_ips = {item.strip() for item in value.split(",") if item.strip()}
    return configured_ips | {"127.0.0.1", "::1"}


def main() -> None:
    parser = argparse.ArgumentParser(description="Serve Project Pulse frontend publicly with source-IP restriction.")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=5173)
    parser.add_argument("--allowed-source-ip", default=os.environ.get("PROJECT_PULSE_ALLOWED_SOURCE_IP", "45.19.161.17"))
    args = parser.parse_args()

    if not module.DIST_DIR.exists():
        raise SystemExit(f"Missing frontend build directory: {module.DIST_DIR}. Run build-frontend.sh first.")

    RestrictedFrontendProxyHandler.allowed_source_ips = parse_allowed_ips(args.allowed_source_ip)
    os.chdir(module.DIST_DIR)

    server = ThreadingHTTPServer((args.host, args.port), RestrictedFrontendProxyHandler)
    print(f"Serving frontend from {module.DIST_DIR}")
    print(f"Proxying /health and /api/* to {module.BACKEND_BASE_URL}")
    print(f"Public validation URL: http://<server-public-ip>:{args.port}/")
    print(f"Allowed source IPs: {', '.join(sorted(RestrictedFrontendProxyHandler.allowed_source_ips))}")
    print("Press CTRL+C to stop.")
    server.serve_forever()


if __name__ == "__main__":
    main()
PY
chmod +x deployment/rocky-linux/serve-frontend-public-restricted.py

log "Removing stale frontend build output"
rm -rf "$DIST_DIR"

log "Publishing API through systemd installer"
chmod +x deployment/rocky-linux/install-api-systemd-service.sh
./deployment/rocky-linux/install-api-systemd-service.sh

log "Building frontend"
chmod +x deployment/rocky-linux/build-frontend.sh
./deployment/rocky-linux/build-frontend.sh

log "Configuring local firewall for restricted public frontend port"
if command -v firewall-cmd >/dev/null 2>&1; then
  sudo firewall-cmd --permanent --add-rich-rule="rule family=\"ipv4\" source address=\"${ALLOWED_SOURCE_IP}/32\" port protocol=\"tcp\" port=\"${PUBLIC_PORT}\" accept" || true
  sudo firewall-cmd --reload || true
  sudo firewall-cmd --list-rich-rules || true
else
  echo "firewall-cmd not found. Skipping OS firewall configuration."
fi

log "Installing and starting restricted public frontend systemd service"
sudo tee /etc/systemd/system/projecttime-frontend-public.service >/dev/null <<EOF
[Unit]
Description=Project Pulse Restricted Public Frontend
After=network.target projecttime-api.service
Requires=projecttime-api.service

[Service]
Type=simple
User=opc
WorkingDirectory=$REPO_DIR
Environment=PROJECT_PULSE_ALLOWED_SOURCE_IP=$ALLOWED_SOURCE_IP
ExecStart=/usr/bin/python3 $REPO_DIR/deployment/rocky-linux/serve-frontend-public-restricted.py --host 0.0.0.0 --port $PUBLIC_PORT --allowed-source-ip $ALLOWED_SOURCE_IP
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable projecttime-frontend-public.service
sudo systemctl restart projecttime-frontend-public.service

log "Validation checks"
echo "API version:"
curl -s http://127.0.0.1:5080/api/version | jq . || curl -s http://127.0.0.1:5080/api/version || true

echo
echo "Open Tasks for 2026-06-21:"
curl -s "http://127.0.0.1:5080/api/assignments/open-tasks?weekStart=2026-06-21" | jq . || curl -s "http://127.0.0.1:5080/api/assignments/open-tasks?weekStart=2026-06-21" || true

echo
echo "Debug time entries for 2026-06-21:"
curl -s "http://127.0.0.1:5080/api/debug/time-entries?weekStart=2026-06-21" | jq . || curl -s "http://127.0.0.1:5080/api/debug/time-entries?weekStart=2026-06-21" || true

echo
echo "Frontend local status:"
curl -s -I "http://127.0.0.1:${PUBLIC_PORT}/" || true

echo
echo "Listening ports:"
ss -lntp | grep -E ':5080|:'"$PUBLIC_PORT" || true

echo
echo "Systemd services:"
systemctl --no-pager --full status projecttime-api.service || true
systemctl --no-pager --full status projecttime-frontend-public.service || true

log "Stabilization complete"
echo "Internal/local URL:  http://127.0.0.1:${PUBLIC_PORT}/"
echo "Public URL:          http://167.234.223.32:${PUBLIC_PORT}/"
echo
cat <<EOF
IMPORTANT: If the public URL still does not load, the remaining blocker is almost
certainly OCI networking. Add or confirm this OCI ingress rule on the VM's NSG
or subnet security list:

  Source CIDR:             ${ALLOWED_SOURCE_IP}/32
  IP Protocol:             TCP
  Destination Port Range:  ${PUBLIC_PORT}

The API remains private on 127.0.0.1:5080. Only the frontend proxy is exposed,
and the Python proxy also restricts access to ${ALLOWED_SOURCE_IP}, 127.0.0.1, and ::1.
EOF
