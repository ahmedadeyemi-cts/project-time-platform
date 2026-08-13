#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MIGRATION="$ROOT/database/migrations/087_activate_approved_local_logins.sql"
PSQL_BIN="${PSQL_BIN:-psql}"

fail() {
    echo "ERROR: $*" >&2
    exit 1
}

[[ -f "$MIGRATION" ]] || fail "Migration 087 is missing."
command -v "$PSQL_BIN" >/dev/null 2>&1 || fail "$PSQL_BIN is required."

records=(
    'projectpulse.m087.hash.jeremy_holt|PROJECTPULSE_M087_DERIVED_JEREMY_HOLT'
    'projectpulse.m087.hash.darren_olson|PROJECTPULSE_M087_DERIVED_DARREN_OLSON'
    'projectpulse.m087.hash.demo_engineer|PROJECTPULSE_M087_DERIVED_DEMO_ENGINEER'
    'projectpulse.m087.hash.demo_manager|PROJECTPULSE_M087_DERIVED_DEMO_MANAGER'
    'projectpulse.m087.hash.heather_schrock|PROJECTPULSE_M087_DERIVED_HEATHER_SCHROCK'
    'projectpulse.m087.hash.jason_mosier|PROJECTPULSE_M087_DERIVED_JASON_MOSIER'
    'projectpulse.m087.hash.juli_cambron|PROJECTPULSE_M087_DERIVED_JULI_CAMBRON'
    'projectpulse.m087.hash.kevin_damisch|PROJECTPULSE_M087_DERIVED_KEVIN_DAMISCH'
    'projectpulse.m087.hash.project_team_coordinator|PROJECTPULSE_M087_DERIVED_PROJECT_TEAM_COORDINATOR'
    'projectpulse.m087.hash.steve_kopischke|PROJECTPULSE_M087_DERIVED_STEVE_KOPISCHKE'
)

declare -A seen=()
sql_file="$(mktemp)"
chmod 0600 "$sql_file"
trap 'rm -f "$sql_file"; for record in "${records[@]}"; do unset "${record#*|}"; done' EXIT

{
    printf '%s\n' '\set ON_ERROR_STOP on' '\set ECHO none' '\o /dev/null'
    for record in "${records[@]}"; do
        setting="${record%%|*}"
        variable="${record#*|}"
        value="${!variable:-}"
        [[ "$value" =~ ^PBKDF2-SHA256\$210000\$[A-Za-z0-9+/]{22}==\$[A-Za-z0-9+/]{43}=$ ]] \
            || fail "$variable must be supplied as a protected runtime-derived PBKDF2 value."
        [[ -z "${seen[$value]:-}" ]] || fail "Every account requires an independently salted derived value."
        seen[$value]=1
        printf "SELECT set_config('%s', '%s', FALSE);\n" "$setting" "$value"
        unset "$variable"
    done
    printf '%s\n' '\o'
    printf '\\ir %s\n' "$MIGRATION"
    printf '%s\n' '\echo MIGRATION_087_APPROVED_LOCAL_LOGINS=APPLIED'
} > "$sql_file"

"$PSQL_BIN" -X --set=ON_ERROR_STOP=1 "$@" < "$sql_file"
