#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(sys.argv[1]).resolve()
LOG = Path(sys.argv[2]).resolve()
REPORT = Path(sys.argv[3]).resolve()
ORIGINAL_HEAD = sys.argv[4]


def write_report(payload: dict[str, object]) -> None:
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")


def fail(message: str, **details: object) -> None:
    write_report({"status": "failed", "message": message, **details})
    raise SystemExit(message)


def git(*args: str) -> str:
    return subprocess.run(
        ["git", *args], cwd=ROOT, check=True, text=True, capture_output=True
    ).stdout.strip()


if git("rev-parse", "HEAD") != ORIGINAL_HEAD:
    write_report(
        {
            "status": "skipped",
            "reason": "target_branch_already_advanced",
            "head": git("rev-parse", "HEAD"),
        }
    )
    print(REPORT.read_text(encoding="utf-8"))
    raise SystemExit(0)

if not LOG.is_file():
    fail("The exact failed deployment log is unavailable.")
log_text = LOG.read_text(encoding="utf-8", errors="replace")
error = re.search(r"column\s+u\.is_active\s+does not exist", log_text, re.IGNORECASE)
if error is None:
    fail("The expected PostgreSQL failure was not found in the exact deployment log.")

context_start = max(0, log_text.rfind("\n", 0, max(0, error.start() - 24000)))
context_end = min(len(log_text), error.end() + 24000)
error_context = log_text[context_start:context_end]

function_names: list[str] = []
for pattern in (
    r"(?:PL/pgSQL|SQL) function\s+([A-Za-z_][A-Za-z0-9_.]*)\s*\(",
    r"function\s+([A-Za-z_][A-Za-z0-9_.]*)\s+line\s+\d+",
):
    for match in re.finditer(pattern, error_context, re.IGNORECASE):
        name = match.group(1).split(".")[-1]
        if name not in function_names:
            function_names.append(name)
if not function_names:
    fail("PostgreSQL did not identify the failing function.", errorContext=error_context)

migration_files = sorted((ROOT / "database" / "migrations").glob("*.sql"))
all_migrations = "\n".join(path.read_text(encoding="utf-8", errors="replace") for path in migration_files)

# Determine the canonical account-state column from the repository's app_users
# DDL. Do not add or infer a new database column.
column_types: dict[str, str] = {}
for block in re.finditer(
    r"CREATE\s+TABLE(?:\s+IF\s+NOT\s+EXISTS)?\s+(?:public\.)?app_users\s*\((.*?)\);",
    all_migrations,
    re.IGNORECASE | re.DOTALL,
):
    for line in block.group(1).splitlines():
        match = re.match(
            r"\s*([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z][A-Za-z0-9_]*(?:\([^)]*\))?)",
            line,
        )
        if match:
            column_types[match.group(1).lower()] = match.group(2).lower()
for match in re.finditer(
    r"ALTER\s+TABLE(?:\s+IF\s+EXISTS)?\s+(?:public\.)?app_users\s+ADD\s+COLUMN(?:\s+IF\s+NOT\s+EXISTS)?\s+([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z][A-Za-z0-9_]*(?:\([^)]*\))?)",
    all_migrations,
    re.IGNORECASE,
):
    column_types[match.group(1).lower()] = match.group(2).lower()

positive_boolean = ("is_enabled", "account_enabled", "enabled")
negative_boolean = ("is_disabled", "is_deleted", "disabled", "deleted")
timestamps = ("disabled_at", "deleted_at", "deactivated_at")
statuses = ("account_status", "user_status", "status")
replacement_column = ""
replacement_predicate = ""
replacement_kind = ""
for column in positive_boolean:
    if column in column_types:
        replacement_column = column
        replacement_predicate = f"COALESCE(u.{column}, TRUE)"
        replacement_kind = "positive_boolean"
        break
if not replacement_predicate:
    for column in negative_boolean:
        if column in column_types:
            replacement_column = column
            replacement_predicate = f"NOT COALESCE(u.{column}, FALSE)"
            replacement_kind = "negative_boolean"
            break
if not replacement_predicate:
    for column in timestamps:
        if column in column_types:
            replacement_column = column
            replacement_predicate = f"u.{column} IS NULL"
            replacement_kind = "nullable_timestamp"
            break
if not replacement_predicate:
    for column in statuses:
        if column in column_types:
            replacement_column = column
            replacement_predicate = (
                f"LOWER(COALESCE(NULLIF(BTRIM(u.{column}), ''), 'active')) "
                "NOT IN ('inactive','disabled','deleted','deactivated','locked')"
            )
            replacement_kind = "status_text"
            break
if not replacement_predicate:
    fail(
        "No canonical app_users account-state authority was found.",
        appUserColumns=column_types,
        functions=function_names,
        errorContext=error_context,
    )


def extract_function(source: str, name: str) -> str | None:
    start_match = re.search(
        rf"CREATE\s+OR\s+REPLACE\s+FUNCTION\s+(?:public\.)?{re.escape(name)}\s*\(",
        source,
        re.IGNORECASE,
    )
    if start_match is None:
        return None
    start = start_match.start()
    as_match = re.search(r"\bAS\s+(\$[A-Za-z0-9_]*\$)", source[start:], re.IGNORECASE)
    if as_match is None:
        return None
    tag = as_match.group(1)
    body_start = start + as_match.end()
    body_end = source.find(tag, body_start)
    if body_end < 0:
        return None
    statement_end = source.find(";", body_end + len(tag))
    if statement_end < 0:
        return None
    return source[start : statement_end + 1]

function_candidates: list[dict[str, str]] = []
for function_name in function_names:
    for source_file in migration_files:
        source = source_file.read_text(encoding="utf-8", errors="replace")
        statement = extract_function(source, function_name)
        if statement and re.search(r"\bu\.is_active\b", statement, re.IGNORECASE):
            function_candidates.append(
                {
                    "name": function_name,
                    "path": str(source_file.relative_to(ROOT)),
                    "statement": statement,
                }
            )

if len(function_candidates) != 1:
    fail(
        "The exact failing function could not be resolved uniquely.",
        functions=function_names,
        candidates=[{"name": item["name"], "path": item["path"]} for item in function_candidates],
        errorContext=error_context,
    )

candidate = function_candidates[0]
statement = candidate["statement"]
patterns = (
    r"COALESCE\(\s*u\.is_active\s*,\s*TRUE\s*\)\s*=\s*TRUE",
    r"COALESCE\(\s*u\.is_active\s*,\s*FALSE\s*\)\s*=\s*TRUE",
    r"u\.is_active\s*=\s*TRUE",
    r"\bu\.is_active\b",
)
corrected = statement
count = 0
for pattern in patterns:
    corrected, count = re.subn(pattern, replacement_predicate, corrected, count=1, flags=re.IGNORECASE)
    if count:
        break
if count != 1 or corrected == statement or re.search(r"\bu\.is_active\b", corrected, re.IGNORECASE):
    fail(
        "The invalid predicate was not replaced exactly once.",
        function=candidate["name"],
        sourcePath=candidate["path"],
        predicate=replacement_predicate,
    )

migration096 = ROOT / "database" / "migrations" / "096_project_planning_document_authority.sql"
source096 = migration096.read_text(encoding="utf-8")
marker_start = "-- BEGIN MIGRATION 096 TEST-SCHEMA IDENTITY COMPATIBILITY REPAIR"
marker_end = "-- END MIGRATION 096 TEST-SCHEMA IDENTITY COMPATIBILITY REPAIR"
if marker_start in source096 or marker_end in source096:
    fail("Migration 096 already contains a compatibility repair marker.")

# Execute the corrected CREATE OR REPLACE statement only when the legacy
# function exists. This updates an already-migrated Test database while keeping
# the isolated Migration 096 fixture independent of unrelated historical tables.
execute_tag = "$projectpulse096_corrected_function$"
do_tag = "$projectpulse096_identity_compatibility$"
repair_block = f"""
{marker_start}
-- Protected Test exposed a historical function compiled against the obsolete
-- app_users.is_active column. The canonical account-state authority in this
-- repository is app_users.{replacement_column}. Replace only the existing
-- function named by PostgreSQL; do not create an unrelated function in a fresh
-- database that has not installed that historical capability.
DO {do_tag}
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_proc
        WHERE proname = '{candidate['name']}'
    ) THEN
        EXECUTE {execute_tag}
{corrected}
{execute_tag};
    END IF;
END;
{do_tag};
{marker_end}
"""
insert_at = source096.find("\n", source096.find("BEGIN;")) + 1
if insert_at <= 0:
    fail("Migration 096 transaction boundary was not found.")
source096 = source096[:insert_at] + repair_block + source096[insert_at:]
migration096.write_text(source096, encoding="utf-8")

# Add a regression that verifies the corrective function body is embedded,
# schema-compatible, and that Migration 096 remains executable when the
# historical function is absent.
test_path = ROOT / "tests" / "test-project-planning-document-authority-migration-096.sh"
test_source = test_path.read_text(encoding="utf-8")
test_marker = "# Migration 096 historical app_users identity compatibility regression."
if test_marker not in test_source:
    insertion = "echo 'ASSERTION_PASSED target_postgres_database_ready=true'\n"
    if test_source.count(insertion) != 1:
        fail("Migration 096 test insertion point was not found exactly once.")
    static_block = f'''\n{test_marker}\ngrep -Fq -- '{marker_start}' "$ROOT/database/migrations/096_project_planning_document_authority.sql"\n! grep -Eq -- '\\bu\\.is_active\\b' "$ROOT/database/migrations/096_project_planning_document_authority.sql"\ngrep -Fq -- 'u.{replacement_column}' "$ROOT/database/migrations/096_project_planning_document_authority.sql"\necho 'ASSERTION_PASSED migration096_uses_canonical_app_user_state=true'\n\n'''
    test_source = test_source.replace(insertion, insertion + static_block, 1)
    test_path.write_text(test_source, encoding="utf-8")

changed = git("diff", "--name-only").splitlines()
expected = {
    "database/migrations/096_project_planning_document_authority.sql",
    "tests/test-project-planning-document-authority-migration-096.sh",
}
if set(changed) != expected:
    fail("Repair changed an unexpected source set.", changed=changed, expected=sorted(expected))
git("diff", "--check")

payload = {
    "status": "repaired",
    "error": "column u.is_active does not exist",
    "failingFunction": candidate["name"],
    "functionSource": candidate["path"],
    "replacementKind": replacement_kind,
    "replacementColumn": replacement_column,
    "replacementPredicate": replacement_predicate,
    "changedPaths": changed,
    "errorContext": error_context,
}
write_report(payload)
print(json.dumps(payload, indent=2, sort_keys=True))
