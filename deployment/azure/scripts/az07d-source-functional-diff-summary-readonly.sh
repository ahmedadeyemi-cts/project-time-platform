#!/usr/bin/env bash
set -Eeuo pipefail

SOURCE_ROOT="${SOURCE_ROOT:-$(git rev-parse --show-toplevel 2>/dev/null || true)}"
BASE_DIR="${BASE_DIR:-$HOME/project-health-dashboard-source-checkpoint}"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az07d-source-functional-diff-summary-$STAMP.log"

mkdir -p "$LOG_DIR"

if [ -z "$SOURCE_ROOT" ]; then
    echo "ERROR: Current directory is not inside a Git repository."
    return 1 2>/dev/null || true
else
    SOURCE_ROOT="$SOURCE_ROOT" python3 - <<'PY' | tee "$LOG"
import json
import os
import re
import subprocess
from collections import Counter
from pathlib import Path

root = Path(os.environ["SOURCE_ROOT"]).resolve()
expected_head = "5a221da29cdfc1134e5d603175b311ff97658b67"
expected_text_paths = {
    "src/backend/ProjectTime.Api/Program.cs",
    "src/frontend/project-time-web/src/WorkRegisterCenter.jsx",
    "src/frontend/project-time-web/src/work-register-center.css",
    "deployment/rocky-linux/projectpulse-055d5a-billing-identifiers-create-edit-ui.sql",
    "deployment/rocky-linux/projectpulse-055d6b5b-project-lifecycle-sidecar.sql",
}
expected_generated_path = (
    "deployment/rocky-linux/__pycache__/"
    "serve-frontend-local.cpython-39.pyc"
)


def git(*args, text=True):
    return subprocess.run(
        ["git", "-C", str(root), *args],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=text,
        check=False,
    )


def section(title):
    print()
    print("=" * 60)
    print(title)
    print("=" * 60)


def added_lines(path):
    result = git("diff", "--unified=0", "--no-color", "--", path)
    additions = []
    current_line = None

    for raw in result.stdout.splitlines():
        if raw.startswith("@@"):
            match = re.search(r"\+(\d+)(?:,(\d+))?", raw)
            current_line = int(match.group(1)) if match else None
            continue

        if raw.startswith("+++") or raw.startswith("---"):
            continue

        if current_line is None:
            continue

        if raw.startswith("+"):
            additions.append((current_line, raw[1:]))
            current_line += 1
        elif raw.startswith("-"):
            continue
        else:
            current_line += 1

    return additions


def removed_lines(path):
    result = git("diff", "--unified=0", "--no-color", "--", path)
    removals = []
    current_line = None

    for raw in result.stdout.splitlines():
        if raw.startswith("@@"):
            match = re.search(r"-(\d+)(?:,(\d+))?", raw)
            current_line = int(match.group(1)) if match else None
            continue

        if raw.startswith("+++") or raw.startswith("---"):
            continue

        if current_line is None:
            continue

        if raw.startswith("-"):
            removals.append((current_line, raw[1:]))
            current_line += 1
        elif raw.startswith("+"):
            continue
        else:
            current_line += 1

    return removals


def unique_sorted(values):
    return sorted({value for value in values if value})


section("AZ-07D - Read-Only Functional Diff Summary")
head = git("rev-parse", "HEAD").stdout.strip()
branch = git("branch", "--show-current").stdout.strip() or "detached"

print(f"SOURCE_REPOSITORY_ROOT={root}")
print(f"SOURCE_HEAD={head}")
print(f"EXPECTED_HEAD={expected_head}")
print(f"SOURCE_HEAD_MATCH={'yes' if head == expected_head else 'no'}")
print(f"SOURCE_BRANCH={branch}")
print("READ_ONLY_SOURCE_REVIEW=true")
print("PATCH_CONTENT_PRINTED=false")
print("SECRET_VALUES_PRINTED=false")
print("SOURCE_FILES_MODIFIED=false")
print("GIT_WRITE_ACTION_PERFORMED=false")
print("APPLICATION_BUILD_STARTED=false")
print("AZURE_IMAGE_BUILD_STARTED=false")

tracked = unique_sorted(git("diff", "--name-only").stdout.splitlines())
untracked = unique_sorted(
    git("ls-files", "--others", "--exclude-standard").stdout.splitlines()
)
all_changed = sorted(set(tracked) | set(untracked))

print(f"TRACKED_CHANGED_FILE_COUNT={len(tracked)}")
print(f"UNTRACKED_FILE_COUNT={len(untracked)}")
print(f"TOTAL_CHANGED_PATH_COUNT={len(all_changed)}")

unexpected = sorted(
    set(all_changed) - expected_text_paths - {expected_generated_path}
)
missing_expected = sorted(expected_text_paths - set(all_changed))

print(f"UNEXPECTED_CHANGED_PATH_COUNT={len(unexpected)}")
for path in unexpected:
    print(f"UNEXPECTED_CHANGED_PATH={path}")

print(f"MISSING_EXPECTED_TEXT_PATH_COUNT={len(missing_expected)}")
for path in missing_expected:
    print(f"MISSING_EXPECTED_TEXT_PATH={path}")

section("Per-file functional change counts")
for path in all_changed:
    full_path = root / path
    tracked_state = git("ls-files", "--error-unmatch", "--", path).returncode == 0
    additions = added_lines(path) if tracked_state else []
    removals = removed_lines(path) if tracked_state else []

    print(f"CHANGED_FILE={path}")
    print(f"  TRACKED={str(tracked_state).lower()}")
    print(f"  ADDED_LINE_COUNT={len(additions)}")
    print(f"  REMOVED_LINE_COUNT={len(removals)}")
    print(f"  FILE_EXISTS={str(full_path.is_file()).lower()}")

section("Backend structural summary")
backend_path = "src/backend/ProjectTime.Api/Program.cs"
backend_additions = added_lines(backend_path)
backend_text = "\n".join(line for _, line in backend_additions)

route_pattern = re.compile(
    r"\bMap(Get|Post|Put|Delete|Patch)\s*\(\s*[\"']([^\"']+)[\"']"
)
route_entries = [
    (method.upper(), route)
    for method, route in route_pattern.findall(backend_text)
]

print(f"BACKEND_ADDED_ROUTE_COUNT={len(route_entries)}")
for method, route in sorted(route_entries):
    print(f"BACKEND_ADDED_ROUTE={method} {route}")

route_counter = Counter(route_entries)
duplicate_routes = sorted(
    entry for entry, count in route_counter.items() if count > 1
)
print(f"BACKEND_DUPLICATE_ADDED_ROUTE_COUNT={len(duplicate_routes)}")
for method, route in duplicate_routes:
    print(f"BACKEND_DUPLICATE_ADDED_ROUTE={method} {route}")

identifier_patterns = {
    "TYPE": re.compile(
        r"\b(?:class|record|struct|enum|interface)\s+([A-Za-z_][A-Za-z0-9_]*)"
    ),
    "ASYNC_METHOD": re.compile(
        r"\b(?:public|private|internal|protected)?\s*"
        r"(?:static\s+)?(?:async\s+)?[A-Za-z_][A-Za-z0-9_<>,?\[\]. ]*\s+"
        r"([A-Za-z_][A-Za-z0-9_]*)\s*\("
    ),
}

for label, pattern in identifier_patterns.items():
    names = unique_sorted(pattern.findall(backend_text))
    print(f"BACKEND_ADDED_{label}_COUNT={len(names)}")
    for name in names[:100]:
        print(f"BACKEND_ADDED_{label}={name}")
    if len(names) > 100:
        print(f"BACKEND_ADDED_{label}_TRUNCATED=true")

sql_reference_pattern = re.compile(
    r"\b(?:FROM|JOIN|INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+"
    r"(?:[\"']?)([A-Za-z_][A-Za-z0-9_.]*)(?:[\"']?)",
    re.IGNORECASE,
)
backend_sql_objects = unique_sorted(sql_reference_pattern.findall(backend_text))
print(f"BACKEND_ADDED_SQL_REFERENCE_COUNT={len(backend_sql_objects)}")
for name in backend_sql_objects[:100]:
    print(f"BACKEND_ADDED_SQL_REFERENCE={name}")
if len(backend_sql_objects) > 100:
    print("BACKEND_ADDED_SQL_REFERENCE_TRUNCATED=true")

section("Frontend structural summary")
frontend_path = "src/frontend/project-time-web/src/WorkRegisterCenter.jsx"
frontend_additions = added_lines(frontend_path)
frontend_text = "\n".join(line for _, line in frontend_additions)

api_path_pattern = re.compile(r"[\"'`](/api/[A-Za-z0-9_./{}?=&:-]+)[\"'`]")
frontend_api_paths = unique_sorted(api_path_pattern.findall(frontend_text))
print(f"FRONTEND_ADDED_API_PATH_COUNT={len(frontend_api_paths)}")
for path in frontend_api_paths:
    print(f"FRONTEND_ADDED_API_PATH={path}")

function_pattern = re.compile(
    r"\b(?:function\s+([A-Za-z_][A-Za-z0-9_]*)|"
    r"(?:const|let)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*"
    r"(?:async\s*)?\([^)]*\)\s*=>)"
)
frontend_functions = unique_sorted(
    first or second for first, second in function_pattern.findall(frontend_text)
)
print(f"FRONTEND_ADDED_FUNCTION_COUNT={len(frontend_functions)}")
for name in frontend_functions[:150]:
    print(f"FRONTEND_ADDED_FUNCTION={name}")
if len(frontend_functions) > 150:
    print("FRONTEND_ADDED_FUNCTION_TRUNCATED=true")

hook_pattern = re.compile(r"\b(use[A-Z][A-Za-z0-9_]*)\s*\(")
frontend_hooks = unique_sorted(hook_pattern.findall(frontend_text))
print(f"FRONTEND_ADDED_HOOK_REFERENCE_COUNT={len(frontend_hooks)}")
for name in frontend_hooks:
    print(f"FRONTEND_ADDED_HOOK_REFERENCE={name}")

section("CSS structural summary")
css_path = "src/frontend/project-time-web/src/work-register-center.css"
css_additions = added_lines(css_path)
css_text = "\n".join(line for _, line in css_additions)
selector_pattern = re.compile(r"^\s*([^@{}][^{}]*)\s*\{", re.MULTILINE)
selectors = unique_sorted(
    selector.strip() for selector in selector_pattern.findall(css_text)
)
print(f"CSS_ADDED_SELECTOR_COUNT={len(selectors)}")
for selector in selectors[:100]:
    print(f"CSS_ADDED_SELECTOR={selector}")
if len(selectors) > 100:
    print("CSS_ADDED_SELECTOR_TRUNCATED=true")

section("SQL migration structural summary")
sql_object_pattern = re.compile(
    r"\b(?:CREATE|ALTER|DROP)\s+"
    r"(?:OR\s+REPLACE\s+)?"
    r"(?:TABLE|VIEW|INDEX|FUNCTION|PROCEDURE|TRIGGER|TYPE|SEQUENCE)\s+"
    r"(?:IF\s+(?:NOT\s+)?EXISTS\s+)?"
    r"([A-Za-z_][A-Za-z0-9_.]*)",
    re.IGNORECASE,
)

for sql_path in sorted(path for path in untracked if path.endswith(".sql")):
    full_path = root / sql_path
    text = full_path.read_text(encoding="utf-8")
    objects = unique_sorted(sql_object_pattern.findall(text))
    statements = Counter(
        match.upper()
        for match in re.findall(
            r"\b(CREATE|ALTER|DROP|INSERT|UPDATE|DELETE|COMMENT|DO|BEGIN|COMMIT)\b",
            text,
            flags=re.IGNORECASE,
        )
    )

    print(f"SQL_FILE={sql_path}")
    print(f"  SQL_LINE_COUNT={len(text.splitlines())}")
    print(f"  SQL_OBJECT_COUNT={len(objects)}")
    for name in objects:
        print(f"  SQL_OBJECT={name}")
    print(f"  SQL_HAS_BEGIN={str(bool(re.search(r'\bBEGIN\b', text, re.I))).lower()}")
    print(f"  SQL_HAS_COMMIT={str(bool(re.search(r'\bCOMMIT\b', text, re.I))).lower()}")
    print(
        "  SQL_HAS_IF_NOT_EXISTS="
        f"{str(bool(re.search(r'\bIF\s+NOT\s+EXISTS\b', text, re.I))).lower()}"
    )
    for statement, count in sorted(statements.items()):
        print(f"  SQL_STATEMENT_COUNT[{statement}]={count}")

section("Generated artifact and ignore-rule status")
pyc_tracked = bool(git("ls-files", "--", expected_generated_path).stdout.strip())
ignore_result = git(
    "check-ignore", "-v", "--no-index", "--", expected_generated_path
)
print(f"PYC_TRACKED={str(pyc_tracked).lower()}")
print(f"PYC_IGNORE_RULE_FOUND={str(ignore_result.returncode == 0).lower()}")
print("PYC_RECOMMENDATION=RESTORE_GENERATED_BINARY_TO_HEAD_AND_ADD_IGNORE_RULE")

section("Available validation commands")
backend_project = root / "src/backend/ProjectTime.Api/ProjectTime.Api.csproj"
frontend_package = root / "src/frontend/project-time-web/package.json"

print(f"BACKEND_PROJECT_PRESENT={str(backend_project.is_file()).lower()}")
print(f"FRONTEND_PACKAGE_PRESENT={str(frontend_package.is_file()).lower()}")

if frontend_package.is_file():
    try:
        package = json.loads(frontend_package.read_text(encoding="utf-8"))
        scripts = sorted((package.get("scripts") or {}).keys())
    except Exception:
        scripts = []
    print(f"FRONTEND_PACKAGE_SCRIPT_COUNT={len(scripts)}")
    for name in scripts:
        print(f"FRONTEND_PACKAGE_SCRIPT={name}")

candidate_tests = []
for path in root.rglob("*.csproj"):
    if ".git" in path.parts:
        continue
    text = path.read_text(encoding="utf-8", errors="ignore")
    if "Microsoft.NET.Test.Sdk" in text or "<IsTestProject>true" in text:
        candidate_tests.append(str(path.relative_to(root)))

print(f"BACKEND_TEST_PROJECT_COUNT={len(candidate_tests)}")
for path in sorted(candidate_tests):
    print(f"BACKEND_TEST_PROJECT={path}")

section("Decision")
blocking_reasons = []
if head != expected_head:
    blocking_reasons.append("SOURCE_HEAD_CHANGED")
if unexpected:
    blocking_reasons.append("UNEXPECTED_CHANGED_PATHS")
if missing_expected:
    blocking_reasons.append("EXPECTED_TEXT_PATH_MISSING")
if duplicate_routes:
    blocking_reasons.append("DUPLICATE_ADDED_BACKEND_ROUTES")
if not pyc_tracked:
    blocking_reasons.append("EXPECTED_TRACKED_PYC_STATE_CHANGED")

if blocking_reasons:
    result = "FUNCTIONAL_REVIEW_REQUIRES_INVESTIGATION"
else:
    result = "FUNCTIONAL_SCOPE_SUMMARY_READY"

print(f"FUNCTIONAL_DIFF_REVIEW_RESULT={result}")
print(f"FUNCTIONAL_BLOCKING_REASON_COUNT={len(blocking_reasons)}")
for reason in blocking_reasons:
    print(f"FUNCTIONAL_BLOCKING_REASON={reason}")
print("SOURCE_COMMIT_ALLOWED=false")
print("SOURCE_IMAGE_BUILD_ALLOWED=false")
print(
    "NEXT_ACTION=PREPARE_SAFE_SOURCE_BRANCH_GENERATED_FILE_CLEANUP_AND_VALIDATION"
)

print()
print("*" * 60)
print("FUNCTIONAL DIFF SUMMARY COMPLETE")
print("*" * 60)
PY

    echo
    echo "Functional review log: $LOG"
fi
