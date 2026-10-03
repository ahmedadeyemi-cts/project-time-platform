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


def fail(message: str, **details: object) -> None:
    payload = {"status": "failed", "message": message, **details}
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
    raise SystemExit(message)


def run(*args: str) -> str:
    result = subprocess.run(args, cwd=ROOT, check=True, text=True, capture_output=True)
    return result.stdout


if not LOG.is_file():
    fail("Deployment job log was not downloaded.")
log_text = LOG.read_text(encoding="utf-8", errors="replace")
error_match = re.search(r"column\s+u\.is_active\s+does not exist", log_text, re.IGNORECASE)
if error_match is None:
    fail("The expected Protected Test database error was not found in the deployment log.")

line_start = log_text.rfind("\n", 0, error_match.start())
context_start = max(0, log_text.rfind("\n", 0, max(0, line_start - 18000)))
context_end = min(len(log_text), error_match.end() + 18000)
error_context = log_text[context_start:context_end]

function_names = []
for match in re.finditer(
    r"(?:PL/pgSQL|SQL) function\s+([A-Za-z_][A-Za-z0-9_\.]*)\s*\(",
    error_context,
    re.IGNORECASE,
):
    value = match.group(1).split(".")[-1]
    if value not in function_names:
        function_names.append(value)

hint_match = re.search(
    r"Perhaps you meant to reference the column\s+\"([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\"",
    error_context,
    re.IGNORECASE,
)

source_occurrences: list[dict[str, object]] = []
for file_path in sorted(ROOT.rglob("*")):
    if not file_path.is_file() or ".git" in file_path.parts:
        continue
    if file_path.suffix.lower() not in {".sql", ".cs", ".sh", ".py"}:
        continue
    try:
        text = file_path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        continue
    for match in re.finditer(r"\bu\.is_active\b", text, re.IGNORECASE):
        line = text.count("\n", 0, match.start()) + 1
        source_occurrences.append(
            {
                "path": str(file_path.relative_to(ROOT)),
                "line": line,
                "text": text,
                "start": match.start(),
                "end": match.end(),
            }
        )

if not source_occurrences:
    fail(
        "No permanent source occurrence of u.is_active was found.",
        errorContext=error_context[-12000:],
        functions=function_names,
    )

# Prefer the function named by PostgreSQL, then the migration chain re-applied by
# the Protected Test controller. Never alter generated build output or arbitrary
# application source as part of a database repair.
def occurrence_score(item: dict[str, object]) -> tuple[int, int, str]:
    path = str(item["path"])
    text = str(item["text"])
    function_score = 0 if function_names and any(name in text for name in function_names) else 1
    migration_score = 0 if path.startswith("database/migrations/") else 1
    return function_score, migration_score, path

ranked = sorted(source_occurrences, key=occurrence_score)
best_score = occurrence_score(ranked[0])[:2]
candidates = [item for item in ranked if occurrence_score(item)[:2] == best_score]
unique_paths = sorted({str(item["path"]) for item in candidates})
if len(candidates) != 1:
    fail(
        "The invalid identity predicate was ambiguous; no source was modified.",
        errorContext=error_context[-12000:],
        functions=function_names,
        hint=(hint_match.groups() if hint_match else None),
        candidates=[{"path": item["path"], "line": item["line"]} for item in candidates],
        allOccurrences=[{"path": item["path"], "line": item["line"]} for item in source_occurrences],
    )

candidate = candidates[0]
source_path = ROOT / str(candidate["path"])
source = str(candidate["text"])

# Resolve the real app_users account-state authority from permanent schema
# migrations. The repair may use a PostgreSQL hint only when it identifies an
# existing boolean column in the same failing query.
all_migrations = "\n".join(
    path.read_text(encoding="utf-8", errors="replace")
    for path in sorted((ROOT / "database" / "migrations").glob("*.sql"))
)
app_user_columns: set[str] = set()
for block in re.finditer(
    r"CREATE\s+TABLE(?:\s+IF\s+NOT\s+EXISTS)?\s+(?:public\.)?app_users\s*\((.*?)\);",
    all_migrations,
    re.IGNORECASE | re.DOTALL,
):
    for line in block.group(1).splitlines():
        column = re.match(r"\s*([A-Za-z_][A-Za-z0-9_]*)\s+", line)
        if column:
            app_user_columns.add(column.group(1).lower())
for match in re.finditer(
    r"ALTER\s+TABLE(?:\s+IF\s+EXISTS)?\s+(?:public\.)?app_users\s+ADD\s+COLUMN(?:\s+IF\s+NOT\s+EXISTS)?\s+([A-Za-z_][A-Za-z0-9_]*)",
    all_migrations,
    re.IGNORECASE,
):
    app_user_columns.add(match.group(1).lower())

replacement_kind = ""
replacement_predicate = ""
replacement_column = ""
if hint_match and hint_match.group(2).lower() in app_user_columns and hint_match.group(1).lower() == "u":
    replacement_column = hint_match.group(2)
    replacement_predicate = f"COALESCE(u.{replacement_column}, TRUE)"
    replacement_kind = "postgres_hint_boolean"
else:
    boolean_candidates = (
        "is_enabled",
        "account_enabled",
        "enabled",
    )
    negative_boolean_candidates = ("is_disabled", "is_deleted")
    timestamp_candidates = ("disabled_at", "deleted_at", "deactivated_at")
    status_candidates = ("account_status", "user_status", "status")
    for column in boolean_candidates:
        if column in app_user_columns:
            replacement_column = column
            replacement_predicate = f"COALESCE(u.{column}, TRUE)"
            replacement_kind = "boolean_enabled"
            break
    if not replacement_predicate:
        for column in negative_boolean_candidates:
            if column in app_user_columns:
                replacement_column = column
                replacement_predicate = f"NOT COALESCE(u.{column}, FALSE)"
                replacement_kind = "boolean_disabled"
                break
    if not replacement_predicate:
        for column in timestamp_candidates:
            if column in app_user_columns:
                replacement_column = column
                replacement_predicate = f"u.{column} IS NULL"
                replacement_kind = "nullable_disabled_timestamp"
                break
    if not replacement_predicate:
        for column in status_candidates:
            if column in app_user_columns:
                replacement_column = column
                replacement_predicate = (
                    f"LOWER(COALESCE(NULLIF(BTRIM(u.{column}), ''), 'active')) "
                    "NOT IN ('inactive','disabled','deleted','deactivated','locked')"
                )
                replacement_kind = "status_text"
                break

if not replacement_predicate:
    fail(
        "The canonical app_users account-state column could not be proven from migrations.",
        errorContext=error_context[-12000:],
        functions=function_names,
        appUserColumns=sorted(app_user_columns),
        candidate={"path": candidate["path"], "line": candidate["line"]},
    )

patterns = [
    (r"COALESCE\(\s*u\.is_active\s*,\s*TRUE\s*\)\s*=\s*TRUE", replacement_predicate),
    (r"COALESCE\(\s*u\.is_active\s*,\s*FALSE\s*\)\s*=\s*TRUE", replacement_predicate),
    (r"u\.is_active\s*=\s*TRUE", replacement_predicate),
    (r"u\.is_active\s*=\s*FALSE", f"NOT ({replacement_predicate})"),
    (r"\bu\.is_active\b", replacement_predicate),
]
patched = source
replacement_count = 0
for pattern, value in patterns:
    patched, count = re.subn(pattern, value, patched, count=1, flags=re.IGNORECASE)
    if count:
        replacement_count = count
        break
if replacement_count != 1 or patched == source:
    fail(
        "The exact invalid predicate could not be replaced once.",
        candidate={"path": candidate["path"], "line": candidate["line"]},
        replacement=replacement_predicate,
    )
if re.search(r"\bu\.is_active\b", patched, re.IGNORECASE):
    fail(
        "The selected permanent source still contains u.is_active after repair.",
        candidate={"path": candidate["path"], "line": candidate["line"]},
    )
source_path.write_text(patched, encoding="utf-8")

# Extend the existing Migration 096 executable regression with the exact
# Test-compatible app_users column and predicate. This catches the schema drift
# that the clean-room migration test previously missed.
test_path = ROOT / "tests" / "test-project-planning-document-authority-migration-096.sh"
test_source = test_path.read_text(encoding="utf-8")
marker = "# Protected-Test app_users compatibility regression for Migration 096."
if marker not in test_source:
    insert_after = "echo 'ASSERTION_PASSED target_postgres_database_ready=true'\n"
    if test_source.count(insert_after) != 1:
        fail("Migration 096 regression insertion point was not found exactly once.")
    if replacement_kind in {"boolean_enabled", "postgres_hint_boolean"}:
        column_ddl = f"{replacement_column} boolean NOT NULL DEFAULT TRUE"
        inactive_value = "FALSE"
    elif replacement_kind == "boolean_disabled":
        column_ddl = f"{replacement_column} boolean NOT NULL DEFAULT FALSE"
        inactive_value = "TRUE"
    elif replacement_kind == "nullable_disabled_timestamp":
        column_ddl = f"{replacement_column} timestamptz NULL"
        inactive_value = "NOW()"
    else:
        column_ddl = f"{replacement_column} text NOT NULL DEFAULT 'active'"
        inactive_value = "'disabled'"
    block = f'''\n{marker}\npsql_exec <<'SQL'\nCREATE TABLE app_users (\n  user_id uuid PRIMARY KEY,\n  {column_ddl}\n);\nINSERT INTO app_users(user_id,{replacement_column}) VALUES\n  ('96000000-0000-0000-0000-000000000091', DEFAULT),\n  ('96000000-0000-0000-0000-000000000092', {inactive_value});\nSQL\nassert_eq 1 "$(value "SELECT COUNT(*) FROM app_users u WHERE {replacement_predicate};")" protected_test_active_user_predicate\n\n'''
    test_source = test_source.replace(insert_after, insert_after + block, 1)
    test_path.write_text(test_source, encoding="utf-8")

changed = run("git", "diff", "--name-only").splitlines()
allowed = {str(source_path.relative_to(ROOT)), str(test_path.relative_to(ROOT))}
if not changed or any(path not in allowed for path in changed):
    fail("Repair changed an unexpected path.", changed=changed, allowed=sorted(allowed))
run("git", "diff", "--check")

REPORT.parent.mkdir(parents=True, exist_ok=True)
REPORT.write_text(
    json.dumps(
        {
            "status": "repaired",
            "error": "column u.is_active does not exist",
            "errorContext": error_context[-12000:],
            "functions": function_names,
            "hint": (hint_match.groups() if hint_match else None),
            "sourcePath": str(source_path.relative_to(ROOT)),
            "sourceLine": candidate["line"],
            "appUserColumns": sorted(app_user_columns),
            "replacementKind": replacement_kind,
            "replacementColumn": replacement_column,
            "replacementPredicate": replacement_predicate,
            "changedPaths": changed,
        },
        indent=2,
        sort_keys=True,
    ),
    encoding="utf-8",
)
print(REPORT.read_text(encoding="utf-8"))
