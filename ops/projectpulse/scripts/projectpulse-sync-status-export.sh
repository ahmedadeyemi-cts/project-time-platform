#!/usr/bin/env bash
set -euo pipefail

STATE_DIR="/opt/project-time-platform/state"
STATE_FILE="$STATE_DIR/replication-sync-status.json"
APP_ROOT="/opt/project-time-platform/app/project-time-platform"
BACKUP_ROOT="/opt/project-time-platform/backups"

mkdir -p "$STATE_DIR"

if [ -f /opt/project-time-platform/config/postgres.env ]; then
  set -a
  source /opt/project-time-platform/config/postgres.env
  set +a
fi

if [ -f /opt/project-time-platform/config/replication-sync.env ]; then
  set -a
  source /opt/project-time-platform/config/replication-sync.env
  set +a
fi

python3 - <<'PY'
import json
import os
import socket
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path

state_file = Path("/opt/project-time-platform/state/replication-sync-status.json")
app_root = Path("/opt/project-time-platform/app/project-time-platform")
backup_root = Path("/opt/project-time-platform/backups")
results_root = backup_root / "results"

def now_iso():
    return datetime.now(timezone.utc).isoformat()

def run(cmd, cwd=None, timeout=10, env_extra=None):
    env = os.environ.copy()
    if env_extra:
        env.update(env_extra)

    try:
        completed = subprocess.run(
            cmd,
            cwd=str(cwd) if cwd else None,
            text=True,
            capture_output=True,
            timeout=timeout,
            check=False,
            env=env
        )
        return {
            "ok": completed.returncode == 0,
            "exitCode": completed.returncode,
            "stdout": completed.stdout.strip(),
            "stderr": completed.stderr.strip()
        }
    except Exception as exc:
        return {
            "ok": False,
            "exitCode": None,
            "stdout": "",
            "stderr": str(exc)
        }

def parse_systemctl_show(output):
    props = {}
    for line in (output or "").splitlines():
        if "=" in line:
            key, value = line.split("=", 1)
            props[key] = value
    return props

def systemctl_show(name):
    result = run([
        "systemctl",
        "show",
        name,
        "--property=Id,Description,LoadState,ActiveState,SubState,UnitFileState",
        "--no-page"
    ], timeout=8)
    return parse_systemctl_show(result["stdout"])

def first_existing_service(candidates):
    for candidate in candidates:
        props = systemctl_show(candidate)
        if props.get("LoadState") and props.get("LoadState") != "not-found":
            return candidate
    return candidates[0]

def service_status(name):
    props = systemctl_show(name)

    load_state = props.get("LoadState") or "unknown"
    active_state = props.get("ActiveState") or "unknown"
    sub_state = props.get("SubState") or ""
    enabled_state = props.get("UnitFileState") or "unknown"

    if load_state == "not-found":
        status = "not_configured"
        detail = "Service is not installed or not configured on this node."
    elif active_state == "active":
        status = "ready"
        detail = "Service is active."
    else:
        status = "action_required"
        detail = f"Service is {active_state or 'unknown'}."

    return {
        "name": name,
        "description": props.get("Description") or name,
        "active": active_state == "active",
        "activeState": active_state,
        "subState": sub_state,
        "enabledState": enabled_state,
        "loadState": load_state,
        "status": status,
        "detail": detail
    }

api_service = first_existing_service(["projecttime-api.service", "projectpulse-api.service"])
frontend_service = first_existing_service(["projecttime-frontend-public.service", "projectpulse-frontend-public.service"])
postgres_service = first_existing_service(["postgresql.service", "postgresql-16.service", "postgresql-15.service", "postgresql-14.service"])

services = [
    service_status(api_service),
    service_status(frontend_service),
    service_status("nginx.service"),
    service_status(postgres_service),
]

git = {
    "available": app_root.exists(),
    "branch": None,
    "commit": None,
    "dirtyFiles": None,
    "status": "warning",
    "detail": "Repository not found."
}

if app_root.exists():
    git_safe = str(app_root)
    branch = run(["git", "-c", f"safe.directory={git_safe}", "rev-parse", "--abbrev-ref", "HEAD"], cwd=app_root)
    commit = run(["git", "-c", f"safe.directory={git_safe}", "rev-parse", "--short", "HEAD"], cwd=app_root)
    dirty = run(["git", "-c", f"safe.directory={git_safe}", "status", "--short"], cwd=app_root)
    dirty_count = len([line for line in dirty["stdout"].splitlines() if line.strip()])

    git = {
        "available": True,
        "branch": branch["stdout"] if branch["ok"] else "unknown",
        "commit": commit["stdout"] if commit["ok"] else "unknown",
        "dirtyFiles": dirty_count if dirty["ok"] else None,
        "status": "ready" if branch["ok"] and commit["ok"] else "warning",
        "detail": "Git state captured." if branch["ok"] and commit["ok"] else (branch["stderr"] or commit["stderr"] or "Git state could not be fully captured.")
    }

def psql(sql):
    database_url = os.environ.get("DATABASE_URL") or os.environ.get("POSTGRES_CONNECTION_STRING")

    db_name = (
        os.environ.get("PGDATABASE")
        or os.environ.get("PTP_DB_NAME")
        or os.environ.get("POSTGRES_DB")
        or os.environ.get("PROJECTPULSE_DB_NAME")
        or os.environ.get("PROJECTTIME_DB_NAME")
        or "postgres"
    )

    if database_url:
        return run(["psql", database_url, "-Atqc", sql], timeout=10)

    user = os.environ.get("PTP_DB_USER") or os.environ.get("PGUSER")
    password = os.environ.get("PTP_DB_PASSWORD") or os.environ.get("PGPASSWORD")
    host = os.environ.get("PTP_DB_HOST") or os.environ.get("PGHOST") or "localhost"
    port = os.environ.get("PTP_DB_PORT") or os.environ.get("PGPORT") or "5432"

    if user:
        env_extra = {}
        if password:
            env_extra["PGPASSWORD"] = password

        return run([
            "psql",
            "-h", host,
            "-p", port,
            "-U", user,
            "-d", db_name,
            "-Atqc", sql
        ], timeout=10, env_extra=env_extra)

    return run(["runuser", "-u", "postgres", "--", "psql", "-d", db_name, "-Atqc", sql], timeout=10)

db = {
    "status": "warning",
    "detail": "PostgreSQL replication status could not be queried.",
    "isInRecovery": None,
    "role": "unknown",
    "replicationConnections": None,
    "walLsn": None,
    "replayLagSeconds": None,
    "replicationPeers": []
}

if run(["bash", "-lc", "command -v psql >/dev/null 2>&1"])["ok"]:
    recovery = psql("select pg_is_in_recovery();")
    repl_count = psql("select count(*) from pg_stat_replication;")
    lsn = psql("select case when pg_is_in_recovery() then coalesce(pg_last_wal_replay_lsn()::text,'') else coalesce(pg_current_wal_lsn()::text,'') end;")
    lag = psql("select case when pg_is_in_recovery() and pg_last_xact_replay_timestamp() is not null then extract(epoch from now() - pg_last_xact_replay_timestamp())::bigint::text else '0' end;")
    peers = psql("select application_name || '|' || coalesce(client_addr::text,'') || '|' || state || '|' || sync_state from pg_stat_replication;")

    if recovery["ok"]:
        is_recovery = recovery["stdout"].strip().lower() in ("t", "true", "1")
        role = "standby" if is_recovery else "primary"

        peer_rows = []
        if peers["ok"] and peers["stdout"]:
            for row in peers["stdout"].splitlines():
                parts = row.split("|")
                while len(parts) < 4:
                    parts.append("")
                peer_rows.append({
                    "applicationName": parts[0],
                    "clientAddress": parts[1],
                    "state": parts[2],
                    "syncState": parts[3]
                })

        db = {
            "status": "ready",
            "detail": f"Database role detected as {role}.",
            "isInRecovery": is_recovery,
            "role": role,
            "replicationConnections": int(repl_count["stdout"] or "0") if repl_count["ok"] and (repl_count["stdout"] or "0").isdigit() else None,
            "walLsn": lsn["stdout"] if lsn["ok"] else None,
            "replayLagSeconds": int(lag["stdout"] or "0") if lag["ok"] and (lag["stdout"] or "0").isdigit() else None,
            "replicationPeers": peer_rows
        }
    else:
        db["detail"] = "PostgreSQL replication status could not be queried: " + (recovery["stderr"] or recovery["stdout"] or "unknown psql error")
else:
    db["detail"] = "psql command is not available on this server."

bundles = sorted(backup_root.glob("*.tgz"), key=lambda p: p.stat().st_mtime if p.exists() else 0, reverse=True) if backup_root.exists() else []

if bundles:
    bundle = bundles[0]
    age_seconds = int(time.time() - bundle.stat().st_mtime)
    stale_hours = int(os.environ.get("PROJECTPULSE_SYNC_STALE_BACKUP_HOURS", "24") or "24")
    latest_backup = {
        "path": str(bundle),
        "name": bundle.name,
        "sizeBytes": bundle.stat().st_size,
        "ageSeconds": age_seconds,
        "ageHours": round(age_seconds / 3600, 2),
        "staleThresholdHours": stale_hours,
        "status": "ready" if age_seconds <= stale_hours * 3600 else "warning",
        "detail": "Recent backup found." if age_seconds <= stale_hours * 3600 else "Latest backup is older than threshold."
    }
else:
    latest_backup = {
        "path": None,
        "name": None,
        "sizeBytes": None,
        "ageSeconds": None,
        "ageHours": None,
        "staleThresholdHours": int(os.environ.get("PROJECTPULSE_SYNC_STALE_BACKUP_HOURS", "24") or "24"),
        "status": "warning",
        "detail": "No backup bundle found yet."
    }

results = sorted(results_root.glob("*.result.json"), key=lambda p: p.stat().st_mtime if p.exists() else 0, reverse=True) if results_root.exists() else []
latest_result = None

if results:
    result_path = results[0]
    try:
        latest_result = json.loads(result_path.read_text())
        latest_result["_resultFile"] = str(result_path)
    except Exception as exc:
        latest_result = {
            "_resultFile": str(result_path),
            "status": "unreadable",
            "error": str(exc)
        }

config_files = [
    "/opt/project-time-platform/config/postgres.env",
    "/opt/project-time-platform/config/backup-sftp.env",
    "/opt/project-time-platform/config/backup-azure.env",
    "/opt/project-time-platform/config/backup-notifications.env",
    "/opt/project-time-platform/config/backup-schedule.env",
    "/opt/project-time-platform/config/replication-sync.env",
]

config_status = []
for item in config_files:
    path = Path(item)
    config_status.append({
        "path": item,
        "exists": path.exists(),
        "status": "ready" if path.exists() else "warning",
        "detail": "Config file exists." if path.exists() else "Config file is missing."
    })

peer_name = os.environ.get("PROJECTPULSE_SYNC_PEER_NAME", "").strip()
peer_host = os.environ.get("PROJECTPULSE_SYNC_PEER_HOST", "").strip()
peer_url = os.environ.get("PROJECTPULSE_SYNC_PEER_URL", "").strip()

peer = {
    "name": peer_name,
    "host": peer_host,
    "url": peer_url,
    "configured": bool(peer_host or peer_url),
    "status": "not_configured",
    "detail": "Peer server is not configured yet. This is expected until the redundant server is built."
}

if peer_host:
    ping = run(["bash", "-lc", f"timeout 5 bash -c '</dev/tcp/{peer_host}/22'"], timeout=7)
    peer["status"] = "ready" if ping["ok"] else "warning"
    peer["detail"] = "Peer host is reachable on TCP/22." if ping["ok"] else "Peer host is configured but not reachable on TCP/22."

checks = []

for svc in services:
    checks.append({
        "category": "Service",
        "name": svc["name"],
        "status": svc["status"],
        "detail": svc["detail"]
    })

checks.append({
    "category": "Database",
    "name": "PostgreSQL role and replication",
    "status": db["status"],
    "detail": db["detail"]
})

checks.append({
    "category": "Source",
    "name": "Git deployment state",
    "status": git["status"],
    "detail": git["detail"]
})

checks.append({
    "category": "Backup",
    "name": "Backup freshness",
    "status": latest_backup["status"],
    "detail": latest_backup["detail"]
})

checks.append({
    "category": "Peer",
    "name": "Redundant peer configuration",
    "status": peer["status"],
    "detail": peer["detail"]
})

for cfg in config_status:
    checks.append({
        "category": "Configuration",
        "name": Path(cfg["path"]).name,
        "status": cfg["status"],
        "detail": cfg["detail"]
    })

action_required = len([c for c in checks if c["status"] == "action_required"])
warning = len([c for c in checks if c["status"] == "warning"])
not_configured = len([c for c in checks if c["status"] == "not_configured"])
ready = len([c for c in checks if c["status"] == "ready"])

overall = "ready"
if action_required:
    overall = "action_required"
elif warning:
    overall = "warning"

payload = {
    "generatedAt": now_iso(),
    "host": {
        "hostname": socket.gethostname(),
        "fqdn": socket.getfqdn()
    },
    "overallStatus": overall,
    "summary": {
        "ready": ready,
        "warning": warning,
        "notConfigured": not_configured,
        "actionRequired": action_required,
        "total": len(checks)
    },
    "database": db,
    "services": services,
    "git": git,
    "backup": {
        "latestBundle": latest_backup,
        "latestResult": latest_result
    },
    "peer": peer,
    "configuration": config_status,
    "checks": checks
}

tmp = state_file.with_suffix(".json.tmp")
tmp.write_text(json.dumps(payload, indent=2))
tmp.replace(state_file)
os.chmod(state_file, 0o644)

print(json.dumps({
    "status": overall,
    "stateFile": str(state_file),
    "ready": ready,
    "warning": warning,
    "notConfigured": not_configured,
    "actionRequired": action_required
}))
PY
