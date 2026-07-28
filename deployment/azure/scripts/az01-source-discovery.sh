#!/usr/bin/env bash
set -Eeuo pipefail

APP="${APP:-/opt/project-time-platform/app/project-time-platform-022}"
API_RUNTIME="${API_RUNTIME:-/opt/project-time-platform/app/published/api}"
FRONTEND_DIR="${FRONTEND_DIR:-$APP/src/frontend/project-time-web}"
DB_NAME="${DB_NAME:-ProjectPulse}"
DOMAIN="${DOMAIN:-projectpulse-test.onenecklab.com}"

SERVICES=(
  projecttime-api.service
  projecttime-frontend-public.service
  nginx.service
)

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT_DIR="/tmp/project-health-dashboard-azure-az01-${STAMP}"
REPORT="$OUT_DIR/source-discovery.txt"
BUNDLE="/tmp/project-health-dashboard-azure-az01-${STAMP}.tar.gz"

mkdir -p "$OUT_DIR"
cd "$APP"

if ! sudo -n true; then
  echo "ERROR: passwordless sudo is required."
  exit 1
fi

section() {
  echo
  echo "============================================================"
  echo "$1"
  echo "============================================================"
}

{
  section "AZ-01 - Project Health Dashboard Source Discovery"
  echo "UTC: $(date -u -Is)"
  echo "Hostname: $(hostname -f 2>/dev/null || hostname)"
  echo "Application path: $APP"
  echo "API runtime: $API_RUNTIME"
  echo "Database: $DB_NAME"
  echo "Current domain: $DOMAIN"

  section "Operating system and compute"
  hostnamectl 2>/dev/null || true
  cat /etc/os-release 2>/dev/null || true
  uname -a
  lscpu 2>/dev/null || true
  free -h
  uptime
  ps -eo pid,user,comm,%cpu,%mem,rss,vsz --sort=-rss | head -n 30

  section "Storage"
  lsblk -o NAME,TYPE,SIZE,FSTYPE,FSVER,LABEL,UUID,MOUNTPOINTS,MODEL 2>/dev/null || true
  df -hT
  findmnt -o TARGET,SOURCE,FSTYPE,OPTIONS 2>/dev/null || true
  du -sh "$APP" "$API_RUNTIME" "$FRONTEND_DIR/dist" 2>/dev/null || true
  sudo -n du -x -h --max-depth=3 /opt/project-time-platform 2>/dev/null | sort -h | tail -n 50 || true

  section "Networking"
  ip -brief address 2>/dev/null || true
  ip route show 2>/dev/null || true
  sudo -n ss -lntup 2>/dev/null || true
  getent ahostsv4 "$DOMAIN" 2>/dev/null || true

  section "Firewall and SELinux"
  sudo -n firewall-cmd --state 2>/dev/null || true
  sudo -n firewall-cmd --get-active-zones 2>/dev/null || true
  sudo -n firewall-cmd --list-all-zones 2>/dev/null || true
  getenforce 2>/dev/null || true
  sestatus 2>/dev/null || true

  section "Runtime versions"
  git --version 2>/dev/null || true
  dotnet --info 2>/dev/null || true
  node --version 2>/dev/null || true
  npm --version 2>/dev/null || true
  python3 --version 2>/dev/null || true
  nginx -v 2>&1 || true
  psql --version 2>/dev/null || true

  section "Systemd services"
  for service in "${SERVICES[@]}"; do
    echo "----- $service -----"
    sudo -n systemctl show "$service" \
      -p Id -p Description -p LoadState -p ActiveState -p SubState \
      -p UnitFileState -p User -p Group -p MainPID -p WorkingDirectory \
      -p ExecStart -p Restart -p EnvironmentFiles --no-pager 2>/dev/null || true

    pid="$(systemctl show "$service" -p MainPID --value 2>/dev/null || true)"
    if [[ "$pid" =~ ^[0-9]+$ ]] && [ "$pid" -gt 0 ]; then
      echo "Environment variable names only:"
      sudo -n sh -c "tr '\0' '\n' < /proc/$pid/environ" 2>/dev/null \
        | sed -E 's/=.*$//' \
        | grep -E '^[A-Za-z_][A-Za-z0-9_]*$' \
        | sort -u || true
    fi
  done

  section "Nginx and certificate inventory"
  sudo -n nginx -T 2>&1 \
    | grep -E '(^|[[:space:]])(listen|server_name|proxy_pass|root|alias|ssl_certificate|ssl_certificate_key|client_max_body_size)[[:space:]]' \
    | sed -E 's#(ssl_certificate_key[[:space:]]+).*#\1[PRIVATE KEY PATH REDACTED];#' || true
  if command -v certbot >/dev/null 2>&1; then
    sudo -n certbot certificates 2>/dev/null || true
  fi

  section "Repository checkpoint"
  echo "Root: $(git rev-parse --show-toplevel)"
  echo "Branch: $(git branch --show-current)"
  echo "HEAD: $(git rev-parse HEAD)"
  git log -1 --oneline --decorate
  git status --short
  git diff --stat
  git diff --cached --stat
  git ls-files --others --exclude-standard
  git status --porcelain=v1 > "$OUT_DIR/git-status.txt"
  git diff --binary > "$OUT_DIR/worktree.patch"
  git diff --cached --binary > "$OUT_DIR/index.patch"
  git log -20 --date=iso --pretty=format:'%h %ad %an %s' > "$OUT_DIR/recent-commits.txt"

  section "Configuration path inventory"
  echo "Contents are not collected."
  find "$APP" "$API_RUNTIME" /etc/nginx /etc/systemd/system \
    -maxdepth 6 -type f \
    \( -name 'appsettings*.json' -o -name '*.service' -o -name '*.conf' \
       -o -name '*.env' -o -name 'package.json' -o -name 'package-lock.json' \
       -o -name '*.csproj' -o -name '*.sql' \) \
    -printf '%m %u:%g %s %TY-%Tm-%TdT%TH:%TM:%TS %p\n' 2>/dev/null | sort || true

  section "PostgreSQL"
  sudo -n -u postgres psql --dbname="$DB_NAME" --set=ON_ERROR_STOP=1 --pset=pager=off <<'SQL'
SELECT version() AS postgres_version;
SELECT current_database() AS database_name,
       pg_size_pretty(pg_database_size(current_database())) AS database_size;
SELECT current_setting('data_directory') AS data_directory,
       current_setting('port') AS port,
       current_setting('listen_addresses') AS listen_addresses,
       current_setting('config_file') AS config_file,
       current_setting('hba_file') AS hba_file;
SELECT rolname, rolcanlogin, rolsuper, rolcreatedb, rolcreaterole,
       rolreplication, rolbypassrls
FROM pg_roles ORDER BY rolname;
SELECT extname, extversion FROM pg_extension ORDER BY extname;
SELECT schemaname, relname AS table_name, n_live_tup AS estimated_rows,
       pg_size_pretty(pg_total_relation_size(format('%I.%I', schemaname, relname)::regclass)) AS total_size
FROM pg_stat_user_tables
ORDER BY pg_total_relation_size(format('%I.%I', schemaname, relname)::regclass) DESC,
         schemaname, relname;
SQL

  section "Timers"
  systemctl list-timers --all --no-pager 2>/dev/null || true

  section "Health"
  health_code="$(curl --silent --output "$OUT_DIR/api-health.json" --write-out '%{http_code}' --max-time 10 http://127.0.0.1:5080/health || true)"
  echo "API health HTTP: ${health_code:-000}"
  cat "$OUT_DIR/api-health.json" 2>/dev/null || true

  public_code="$(curl --silent --location --output "$OUT_DIR/public-index.html" --write-out '%{http_code}' --max-time 20 "https://${DOMAIN}/?azureDiscovery=${STAMP}" || true)"
  echo "Public HTTP: ${public_code:-000}"

  section "AZ-01 complete"
  echo "No application files, database rows, services, DNS records, or Azure resources were modified."
  echo "Secret values, private keys, database passwords, and password hashes were not collected."
} 2>&1 | tee "$REPORT"

sha256sum "$OUT_DIR"/* > "$OUT_DIR/SHA256SUMS.txt"
tar -czf "$BUNDLE" -C "$OUT_DIR" .

echo "Report: $REPORT"
echo "Bundle: $BUNDLE"
