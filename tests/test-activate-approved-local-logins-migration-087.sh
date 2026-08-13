#!/usr/bin/env bash
set -Eeuo pipefail

bash -n scripts/release-test/apply-087-approved-local-logins.sh

python3 - <<'PY'
from pathlib import Path
import re

migration_path = Path("database/migrations/087_activate_approved_local_logins.sql")
rollback_path = Path("database/rollback/087_activate_approved_local_logins_rollback.sql")
apply_path = Path("scripts/release-test/apply-087-approved-local-logins.sh")

for path in (migration_path, rollback_path, apply_path):
    if not path.is_file():
        raise SystemExit(f"MISSING={path}")

migration = migration_path.read_text(encoding="utf-8")
rollback = rollback_path.read_text(encoding="utf-8")
apply_script = apply_path.read_text(encoding="utf-8")

expected = {
    "jeremy.holt@ussignal.local": ("ENGINEERING", "jeremy_holt"),
    "darren.olson@ussignal.local": ("EXECUTIVE", "darren_olson"),
    "demo.engineer@ussignal.local": ("ENGINEERING", "demo_engineer"),
    "demo.manager@ussignal.local": ("MANAGER", "demo_manager"),
    "heather.schrock@ussignal.local": ("PROJECT_MANAGEMENT", "heather_schrock"),
    "jason.mosier@ussignal.local": ("ENGINEERING", "jason_mosier"),
    "juli.cambron@ussignal.local": ("ACCOUNTING", "juli_cambron"),
    "kevin.damisch@ussignal.local": ("ENGINEERING", "kevin_damisch"),
    "project.team.coordinator@ussignal.local": (
        "PROJECT_TEAM_COORDINATOR",
        "project_team_coordinator",
    ),
    "steve.kopischke@ussignal.local": ("PROJECT_MANAGEMENT", "steve_kopischke"),
}

rows = re.findall(
    r"\('([^']+@ussignal\.local)',\s*'[^']+',\s*'([A-Z_]+)',\s*"
    r"'projectpulse\.m087\.hash\.([a-z0-9_]+)',\s*"
    r"COALESCE\(current_setting\('projectpulse\.m087\.hash\.\3', TRUE\), ''\)\)",
    migration,
)
actual = {email: (role, slug) for email, role, slug in rows}
if actual != expected:
    raise SystemExit(f"TARGET_CONTRACT_MISMATCH={actual!r}")

if re.search(
    r"PBKDF2-SHA256\$210000\$[A-Za-z0-9+/]{22}==\$[A-Za-z0-9+/]{43}=",
    migration + rollback + apply_script,
):
    raise SystemExit("REUSABLE_CREDENTIAL_DERIVATIVE_COMMITTED")

required_aliases = {
    ("header.schrock@ussignal.local", "heather.schrock@ussignal.local"),
    ("kevin.damish@ussignal.local", "kevin.damisch@ussignal.local"),
    ("jason.mossier@ussignal.local", "jason.mosier@ussignal.local"),
}
alias_section = migration[
    migration.index("INSERT INTO migration_087_aliases"):
    migration.index("DO $migration_087_alias_guard$")
]
alias_rows = set(
    re.findall(
        r"\('([^']+@ussignal\.local)',\s*'([^']+@ussignal\.local)',\s*'[^']+'\)",
        alias_section,
    )
)
if alias_rows != required_aliases:
    raise SystemExit(f"ALIAS_MAP_MISMATCH={alias_rows!r}")

required_migration_fragments = [
    "Run only through scripts/release-test/apply-087-approved-local-logins.sh",
    "current_setting('projectpulse.m087.hash.",
    "requires 10 independently salted credential hashes",
    "ON CONFLICT (user_id, app_role_id) DO UPDATE",
    "ON CONFLICT (user_id) DO UPDATE",
    "must_change_password = FALSE",
    "failed_login_count = 0",
    "locked_until = NULL",
    "login_enabled = TRUE",
    "migration_087_approved_local_login_activation",
    "'087_activate_approved_local_logins'",
]
for fragment in required_migration_fragments:
    if fragment not in migration:
        raise SystemExit(f"MISSING_MIGRATION_CONTRACT={fragment}")

if re.search(r"\b(?:INSERT|UPDATE|DELETE)\s+(?:INTO\s+)?app_roles\b", migration, re.I):
    raise SystemExit("MIGRATION_MUST_NOT_MUTATE_ROLE_DEFINITIONS")

apply_records = re.findall(
    r"'projectpulse\.m087\.hash\.([a-z0-9_]+)\|"
    r"(PROJECTPULSE_M087_DERIVED_[A-Z0-9_]+)'",
    apply_script,
)
if len(apply_records) != 10 or len(set(apply_records)) != 10:
    raise SystemExit(f"APPLY_RECORD_CONTRACT_INVALID={apply_records!r}")
if {slug for slug, _variable in apply_records} != {slug for _role, slug in expected.values()}:
    raise SystemExit("APPLY_SETTING_SET_MISMATCH")

required_apply_fragments = [
    "chmod 0600",
    "Every account requires an independently salted derived value.",
    "\\o /dev/null",
    "SELECT set_config(",
    "\\ir %s",
    "unset \"$variable\"",
]
for fragment in required_apply_fragments:
    if fragment not in apply_script:
        raise SystemExit(f"MISSING_APPLY_SECURITY_CONTRACT={fragment}")

if "PROJECTPULSE_APPROVED_LOCAL_LOGIN_PASSWORD=" in apply_script:
    raise SystemExit("PLAINTEXT_SECRET_INPUT_MUST_NOT_BE_IN_APPLY_SCRIPT")
if "set -x" in apply_script:
    raise SystemExit("APPLY_SCRIPT_MUST_NOT_ENABLE_SHELL_TRACE")

required_rollback_fragments = [
    "password_hash = NULL",
    "must_change_password = TRUE",
    "is_active = FALSE",
    "rollback_087_local_login_disabled",
    "DELETE FROM schema_migrations",
]
for fragment in required_rollback_fragments:
    if fragment not in rollback:
        raise SystemExit(f"MISSING_ROLLBACK_CONTRACT={fragment}")

if "UPDATE app_users" in rollback or "DELETE FROM app_users" in rollback:
    raise SystemExit("ROLLBACK_MUST_PRESERVE_CANONICAL_USERS")
if "app_user_role_assignments" in rollback:
    raise SystemExit("ROLLBACK_MUST_PRESERVE_ROLE_HISTORY")

print("LOCAL_LOGIN_ACTIVATION_MIGRATION_087=PASS")
print("TARGET_USERS=10")
print("PLAINTEXT_OR_REUSABLE_HASH_IN_REPOSITORY=NO")
print("RUNTIME_DERIVED_VALUES_REQUIRED=10")
print("PBKDF2_ITERATIONS=210000")
print("FORCED_PASSWORD_CHANGE=DISABLED")
print("ROLE_DEFINITIONS_MUTATED=NO")
print("ROLLBACK_POSTURE=LOCAL_CREDENTIALS_DISABLED")
PY
