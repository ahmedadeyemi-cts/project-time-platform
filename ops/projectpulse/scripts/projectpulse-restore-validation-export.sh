#!/usr/bin/env bash
set -euo pipefail

STATE_DIR="/opt/project-time-platform/state"
STATE_FILE="$STATE_DIR/restore-validation-status.json"
BACKUP_ROOT="/opt/project-time-platform/backups"
RESULTS_DIR="/opt/project-time-platform/restore-validation/results"
RUNBOOK="/opt/project-time-platform/runbooks/projectpulse-dr-restore-runbook.md"
CONFIG_FILE="/opt/project-time-platform/config/restore-validation.env"

mkdir -p "$STATE_DIR" "$RESULTS_DIR"

python3 - <<'PY'
import json
import os
import socket
import subprocess
import tarfile
import tempfile
import time
import warnings
from datetime import datetime, timezone
from pathlib import Path

warnings.filterwarnings("ignore", category=RuntimeWarning, module="tarfile")

state_file = Path("/opt/project-time-platform/state/restore-validation-status.json")
backup_root = Path("/opt/project-time-platform/backups")
results_dir = Path("/opt/project-time-platform/restore-validation/results")
runbook = Path("/opt/project-time-platform/runbooks/projectpulse-dr-restore-runbook.md")
config_file = Path("/opt/project-time-platform/config/restore-validation.env")

def now_iso():
    return datetime.now(timezone.utc).isoformat()

def read_env_file(path):
    values = {}
    if not path.exists():
        return values

    for raw_line in path.read_text(errors="ignore").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue

        key, value = line.split("=", 1)
        key = key.strip()
        value = value.strip()

        if len(value) >= 2 and value[0] == value[-1] and value[0] in ("'", '"'):
            value = value[1:-1]

        values[key] = value

    return values

def run(cmd, timeout=30, cwd=None):
    try:
        completed = subprocess.run(
            cmd,
            text=True,
            capture_output=True,
            timeout=timeout,
            cwd=str(cwd) if cwd else None,
            check=False
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

def add_check(checks, key, name, category, status, detail, evidence=None):
    checks.append({
        "key": key,
        "name": name,
        "category": category,
        "status": status,
        "detail": detail,
        "evidence": evidence or ""
    })

def safe_members(tar):
    unsafe = []
    for member in tar.getmembers():
        name = member.name
        if name.startswith("/") or ".." in Path(name).parts:
            unsafe.append(name)
    return unsafe

def newest_bundle():
    if not backup_root.exists():
        return None
    bundles = sorted(
        backup_root.glob("*.tgz"),
        key=lambda p: p.stat().st_mtime if p.exists() else 0,
        reverse=True
    )
    return bundles[0] if bundles else None

def resolve_selected_bundle(selected_backup):
    selected_backup = (selected_backup or "").strip()

    if not selected_backup:
        return newest_bundle(), "latest", ""

    selected_name = Path(selected_backup).name

    if selected_name != selected_backup:
        return None, "invalid", "Selected backup must be a backup filename only, not a path."

    if not selected_name.endswith(".tgz"):
        return None, "invalid", "Selected backup must end with .tgz."

    candidate = backup_root / selected_name

    if not candidate.exists():
        return None, "missing", f"Selected backup was not found: {selected_name}"

    return candidate, "selected", selected_name

settings = read_env_file(config_file)
selected_backup = settings.get("PROJECTPULSE_RESTORE_VALIDATION_SELECTED_BACKUP", "").strip()

checks = []
bundle, selection_mode, selection_detail = resolve_selected_bundle(selected_backup)
generated_at = now_iso()
validation_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")

payload = {
    "generatedAt": generated_at,
    "validationId": validation_id,
    "host": {
        "hostname": socket.gethostname(),
        "fqdn": socket.getfqdn()
    },
    "overallStatus": "unknown",
    "restorePoint": {
        "mode": selection_mode,
        "selectedBackup": selected_backup,
        "resolvedBackup": bundle.name if bundle else "",
        "detail": selection_detail
    },
    "backup": None,
    "summary": {},
    "checks": [],
    "runbook": {
        "path": str(runbook),
        "exists": runbook.exists()
    }
}

if selection_mode == "selected":
    add_check(
        checks,
        "restore_point_selected",
        "Selected restore point",
        "Restore Point",
        "ready",
        "A specific restore point was selected for validation.",
        bundle.name
    )
elif selection_mode == "latest" and bundle:
    add_check(
        checks,
        "restore_point_selected",
        "Selected restore point",
        "Restore Point",
        "ready",
        "No specific restore point was selected. The latest backup is being validated.",
        bundle.name
    )
elif selection_mode in ("invalid", "missing"):
    add_check(
        checks,
        "restore_point_selected",
        "Selected restore point",
        "Restore Point",
        "action_required",
        selection_detail,
        selected_backup
    )

if not bundle:
    add_check(
        checks,
        "backup_bundle_present",
        "Backup bundle found",
        "Backup",
        "action_required",
        "No usable backup bundle was available for restore validation."
    )
else:
    bundle_age_seconds = int(time.time() - bundle.stat().st_mtime)
    checksum_file = Path(str(bundle) + ".sha256")

    payload["backup"] = {
        "path": str(bundle),
        "name": bundle.name,
        "sizeBytes": bundle.stat().st_size,
        "ageSeconds": bundle_age_seconds,
        "ageHours": round(bundle_age_seconds / 3600, 2),
        "checksumPath": str(checksum_file),
        "checksumExists": checksum_file.exists()
    }

    add_check(
        checks,
        "backup_bundle_present",
        "Backup bundle found",
        "Backup",
        "ready",
        "Backup bundle was found.",
        str(bundle)
    )

    if checksum_file.exists():
        checksum_result = run(["sha256sum", "-c", str(checksum_file)], timeout=30)
        add_check(
            checks,
            "backup_checksum_valid",
            "Backup checksum validation",
            "Integrity",
            "ready" if checksum_result["ok"] else "action_required",
            "Checksum validation passed." if checksum_result["ok"] else "Checksum validation failed.",
            checksum_result["stdout"] or checksum_result["stderr"]
        )
    else:
        add_check(
            checks,
            "backup_checksum_valid",
            "Backup checksum validation",
            "Integrity",
            "warning",
            "Checksum file was not found next to the backup bundle.",
            str(checksum_file)
        )

    with tempfile.TemporaryDirectory(prefix="projectpulse-restore-validation-") as tmpdir:
        tmp_path = Path(tmpdir)

        try:
            with tarfile.open(bundle, "r:gz") as tar:
                unsafe = safe_members(tar)

                if unsafe:
                    add_check(
                        checks,
                        "backup_bundle_safe_paths",
                        "Backup archive path safety",
                        "Integrity",
                        "action_required",
                        "Backup archive contains unsafe paths.",
                        ", ".join(unsafe[:10])
                    )
                else:
                    add_check(
                        checks,
                        "backup_bundle_safe_paths",
                        "Backup archive path safety",
                        "Integrity",
                        "ready",
                        "Backup archive paths are safe to extract."
                    )

                tar.extractall(tmp_path)

            add_check(
                checks,
                "backup_bundle_extractable",
                "Backup bundle can be extracted",
                "Integrity",
                "ready",
                "Backup bundle opened and extracted successfully.",
                str(tmp_path)
            )

            db_dumps = list(tmp_path.rglob("*-database.dump")) + list(tmp_path.rglob("*.dump"))
            config_archives = list(tmp_path.rglob("*-config.tgz"))
            app_archives = list(tmp_path.rglob("*-app-snapshot.tgz"))

            db_dump = db_dumps[0] if db_dumps else None
            config_archive = config_archives[0] if config_archives else None
            app_archive = app_archives[0] if app_archives else None

            if db_dump:
                add_check(
                    checks,
                    "database_dump_present",
                    "Database dump present",
                    "Database",
                    "ready",
                    "PostgreSQL database dump was found.",
                    str(db_dump)
                )

                pg_restore_available = run(["bash", "-lc", "command -v pg_restore >/dev/null 2>&1"])
                if pg_restore_available["ok"]:
                    pg_restore_result = run(["pg_restore", "-l", str(db_dump)], timeout=60)
                    add_check(
                        checks,
                        "database_dump_readable",
                        "Database dump readable",
                        "Database",
                        "ready" if pg_restore_result["ok"] else "action_required",
                        "pg_restore can inspect the database dump." if pg_restore_result["ok"] else "pg_restore could not inspect the database dump.",
                        "\n".join((pg_restore_result["stdout"] or pg_restore_result["stderr"]).splitlines()[:10])
                    )
                else:
                    add_check(
                        checks,
                        "database_dump_readable",
                        "Database dump readable",
                        "Database",
                        "warning",
                        "pg_restore is not available on this server, so the dump could not be inspected."
                    )
            else:
                add_check(
                    checks,
                    "database_dump_present",
                    "Database dump present",
                    "Database",
                    "action_required",
                    "No PostgreSQL database dump was found inside the backup bundle."
                )

            if config_archive:
                try:
                    with tarfile.open(config_archive, "r:gz") as config_tar:
                        config_members = config_tar.getnames()

                    add_check(
                        checks,
                        "config_archive_readable",
                        "Configuration archive readable",
                        "Configuration",
                        "ready",
                        f"Configuration archive opened successfully with {len(config_members)} item(s).",
                        str(config_archive)
                    )
                except Exception as exc:
                    add_check(
                        checks,
                        "config_archive_readable",
                        "Configuration archive readable",
                        "Configuration",
                        "action_required",
                        "Configuration archive could not be opened.",
                        str(exc)
                    )
            else:
                add_check(
                    checks,
                    "config_archive_readable",
                    "Configuration archive readable",
                    "Configuration",
                    "action_required",
                    "Configuration archive was not found inside the backup bundle."
                )

            if app_archive:
                try:
                    with tarfile.open(app_archive, "r:gz") as app_tar:
                        app_members = app_tar.getnames()

                    add_check(
                        checks,
                        "app_snapshot_readable",
                        "Application snapshot readable",
                        "Application",
                        "ready",
                        f"Application snapshot archive opened successfully with {len(app_members)} item(s).",
                        str(app_archive)
                    )
                except Exception as exc:
                    add_check(
                        checks,
                        "app_snapshot_readable",
                        "Application snapshot readable",
                        "Application",
                        "action_required",
                        "Application snapshot archive could not be opened.",
                        str(exc)
                    )
            else:
                add_check(
                    checks,
                    "app_snapshot_readable",
                    "Application snapshot readable",
                    "Application",
                    "action_required",
                    "Application snapshot archive was not found inside the backup bundle."
                )

        except Exception as exc:
            add_check(
                checks,
                "backup_bundle_extractable",
                "Backup bundle can be extracted",
                "Integrity",
                "action_required",
                "Backup bundle could not be opened or extracted.",
                str(exc)
            )

if runbook.exists():
    add_check(
        checks,
        "dr_runbook_present",
        "DR restore runbook present",
        "Runbook",
        "ready",
        "DR restore runbook exists.",
        str(runbook)
    )
else:
    add_check(
        checks,
        "dr_runbook_present",
        "DR restore runbook present",
        "Runbook",
        "warning",
        "DR restore runbook was not found.",
        str(runbook)
    )

action_required = len([c for c in checks if c["status"] == "action_required"])
warning = len([c for c in checks if c["status"] == "warning"])
ready = len([c for c in checks if c["status"] == "ready"])

overall = "ready"
if action_required:
    overall = "action_required"
elif warning:
    overall = "warning"

payload["overallStatus"] = overall
payload["summary"] = {
    "ready": ready,
    "warning": warning,
    "actionRequired": action_required,
    "total": len(checks)
}
payload["checks"] = checks

results_dir.mkdir(parents=True, exist_ok=True)
result_file = results_dir / f"{validation_id}.json"
result_file.write_text(json.dumps(payload, indent=2))
os.chmod(result_file, 0o644)

tmp = state_file.with_suffix(".json.tmp")
tmp.write_text(json.dumps(payload, indent=2))
tmp.replace(state_file)
os.chmod(state_file, 0o644)

print(json.dumps({
    "status": overall,
    "validationId": validation_id,
    "restorePoint": payload["restorePoint"],
    "stateFile": str(state_file),
    "ready": ready,
    "warning": warning,
    "actionRequired": action_required
}))
PY
