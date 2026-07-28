#!/usr/bin/env bash
set -Eeuo pipefail

REPO="ahmedadeyemi-cts/project-time-platform"
SOURCE_PR="11"
EXPECTED_SOURCE_SHA="abf45bf824747767282f68fa5bd50909f9751eb0"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az12a-project-intake-resource-assignment-source-inventory-$STAMP.log"
WORK_DIR="$(mktemp -d /tmp/phd-az12a-XXXXXX)"

PROGRAM_PATH="src/backend/ProjectTime.Api/Program.cs"
APP_PATH="src/frontend/project-time-web/src/App.jsx"
WORK_REGISTER_PATH="src/frontend/project-time-web/src/WorkRegisterCenter.jsx"

mkdir -p "$LOG_DIR"
chmod 700 "$WORK_DIR"
trap 'rm -rf "$WORK_DIR"' EXIT

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

fail() {
    echo "ERROR: $*" >&2
    return 1
}

fetch_raw_file() {
    local path="$1"
    local destination="$2"

    gh api \
        -H "Accept: application/vnd.github.raw+json" \
        "repos/$REPO/contents/$path?ref=$SOURCE_SHA" \
        > "$destination"

    [ -s "$destination" ] || fail "Downloaded file is empty: $path"
}

print_matches() {
    local label="$1"
    local file="$2"
    local expression="$3"
    local limit="$4"

    echo "--- $label ---"
    grep -n -E -i -C 4 "$expression" "$file" 2>/dev/null \
        | head -n "$limit" \
        || true
}

{
    section "AZ-12A - Project Intake and Resource Assignment Source Inventory"
    echo "TIME=$(date -u -Is)"
    echo "AZURE_RESOURCE_CHANGE=false"
    echo "DATABASE_CHANGE=false"
    echo "APPLICATION_DEPLOYMENT=false"
    echo "ORACLE_VM_REQUIRED=false"
    echo "SOURCE_BRANCH_MODIFICATION=false"
    echo "ROLE_ENFORCEMENT_REIMPLEMENTATION=false"

    command -v gh >/dev/null 2>&1 || fail "GitHub CLI is required."
    command -v python3 >/dev/null 2>&1 || fail "python3 is required."
    command -v sha256sum >/dev/null 2>&1 || fail "sha256sum is required."

    section "Resolving PR #11 source revision"

    SOURCE_SHA="$(gh api "repos/$REPO/pulls/$SOURCE_PR" --jq '.head.sha')"
    SOURCE_BRANCH="$(gh api "repos/$REPO/pulls/$SOURCE_PR" --jq '.head.ref')"
    PR_STATE="$(gh api "repos/$REPO/pulls/$SOURCE_PR" --jq '.state')"

    echo "SOURCE_PR=$SOURCE_PR"
    echo "SOURCE_PR_STATE=$PR_STATE"
    echo "SOURCE_BRANCH=$SOURCE_BRANCH"
    echo "SOURCE_SHA=$SOURCE_SHA"
    echo "EXPECTED_SOURCE_SHA=$EXPECTED_SOURCE_SHA"

    [ "$PR_STATE" = "open" ] || fail "PR #11 is not open."
    [ "$SOURCE_SHA" = "$EXPECTED_SOURCE_SHA" ] \
        || fail "PR #11 head changed. Expected $EXPECTED_SOURCE_SHA but found $SOURCE_SHA. Review before continuing."

    section "Downloading canonical backend and frontend source"

    PROGRAM_FILE="$WORK_DIR/Program.cs"
    APP_FILE="$WORK_DIR/App.jsx"
    WORK_REGISTER_FILE="$WORK_DIR/WorkRegisterCenter.jsx"
    TREE_FILE="$WORK_DIR/tree.json"
    CANDIDATE_LIST="$WORK_DIR/candidate-paths.txt"
    CANDIDATE_DIR="$WORK_DIR/candidates"
    mkdir -p "$CANDIDATE_DIR"

    fetch_raw_file "$PROGRAM_PATH" "$PROGRAM_FILE"
    fetch_raw_file "$APP_PATH" "$APP_FILE"
    fetch_raw_file "$WORK_REGISTER_PATH" "$WORK_REGISTER_FILE"

    echo "PROGRAM_CS_SHA256=$(sha256sum "$PROGRAM_FILE" | awk '{print $1}')"
    echo "APP_JSX_SHA256=$(sha256sum "$APP_FILE" | awk '{print $1}')"
    echo "WORK_REGISTER_CENTER_SHA256=$(sha256sum "$WORK_REGISTER_FILE" | awk '{print $1}')"
    echo "PROGRAM_CS_LINES=$(wc -l < "$PROGRAM_FILE" | tr -d ' ')"
    echo "APP_JSX_LINES=$(wc -l < "$APP_FILE" | tr -d ' ')"
    echo "WORK_REGISTER_CENTER_LINES=$(wc -l < "$WORK_REGISTER_FILE" | tr -d ' ')"

    section "Discovering intake, assignment, allocation, scheduling, and workload files"

    gh api "repos/$REPO/git/trees/$SOURCE_SHA?recursive=1" > "$TREE_FILE"

    python3 - "$TREE_FILE" > "$CANDIDATE_LIST" <<'PY'
import json
import re
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
keywords = re.compile(r"(intake|resource|assign|allocation|staff|engineer|project.?manager|capacity|schedule|workload|handoff|customer|sold.?work|work.?register)", re.I)
allowed = re.compile(r"\.(cs|jsx|js|ts|tsx|sql|json)$", re.I)
paths = []
for item in obj.get("tree", []):
    path = item.get("path", "")
    if item.get("type") != "blob":
        continue
    if not allowed.search(path):
        continue
    if not (path.startswith("src/") or path.startswith("deployment/rocky-linux/")):
        continue
    if keywords.search(path):
        paths.append(path)

for path in sorted(paths)[:120]:
    print(path)
PY

    echo "CANDIDATE_FILE_COUNT=$(wc -l < "$CANDIDATE_LIST" | tr -d ' ')"
    sed -n '1,120p' "$CANDIDATE_LIST"

    candidate_number=0
    while IFS= read -r path; do
        [ -n "$path" ] || continue
        candidate_number=$((candidate_number + 1))
        destination="$CANDIDATE_DIR/$(printf '%03d' "$candidate_number")-$(basename "$path")"
        fetch_raw_file "$path" "$destination" || true
    done < "$CANDIDATE_LIST"

    section "Existing backend project intake implementation"

    print_matches \
        "Backend intake routes, state transitions, and persistence" \
        "$PROGRAM_FILE" \
        'project.?intake|intake.?package|intake.?status|handoff|sold.?work|customer.?intake|launch.?readiness|work.?register.?intake' \
        900

    section "Existing backend resource assignment implementation"

    print_matches \
        "Backend resource assignment, allocation, scheduling, and workload" \
        "$PROGRAM_FILE" \
        'resource.?assign|project.?assign|engineer.?assign|allocation|capacity|resource.?schedul|project.?workload|staffing|assigned.?user|assigned.?engineer|project.?manager' \
        1100

    section "Existing frontend intake and assignment experience"

    print_matches \
        "App-level intake and resource navigation" \
        "$APP_FILE" \
        'project.?intake|resource.?assign|resource.?schedul|project.?workload|allocation|staffing|handoff|work.?register' \
        900

    print_matches \
        "Work Register intake, project creation, and assignment behavior" \
        "$WORK_REGISTER_FILE" \
        'intake|create.?project|project.?manager|engineer|resource|assign|allocation|staff|schedule|workload|handoff' \
        1100

    section "Candidate-file intake and assignment references"

    if find "$CANDIDATE_DIR" -type f -size +0c | grep -q .; then
        grep -R -n -E -i -C 3 \
            'project.?intake|intake.?package|resource.?assign|project.?assign|engineer.?assign|allocation|capacity|resource.?schedul|project.?workload|staffing|handoff|sold.?work' \
            "$CANDIDATE_DIR" 2>/dev/null \
            | head -n 1400 \
            || true
    else
        echo "No additional candidate files were downloaded."
    fi

    section "Inventory conclusions"

    BACKEND_INTAKE_COUNT="$(grep -E -i -c 'project.?intake|intake.?package|handoff|sold.?work' "$PROGRAM_FILE" || true)"
    BACKEND_ASSIGNMENT_COUNT="$(grep -E -i -c 'resource.?assign|project.?assign|engineer.?assign|allocation|capacity|resource.?schedul|project.?workload|staffing' "$PROGRAM_FILE" || true)"
    FRONTEND_INTAKE_COUNT="$(grep -E -i -c 'project.?intake|intake.?package|handoff|sold.?work' "$APP_FILE" "$WORK_REGISTER_FILE" 2>/dev/null | awk -F: '{sum += $NF} END {print sum+0}')"
    FRONTEND_ASSIGNMENT_COUNT="$(grep -E -i -c 'resource.?assign|project.?assign|engineer.?assign|allocation|capacity|resource.?schedul|project.?workload|staffing' "$APP_FILE" "$WORK_REGISTER_FILE" 2>/dev/null | awk -F: '{sum += $NF} END {print sum+0}')"

    echo "BACKEND_PROJECT_INTAKE_REFERENCE_COUNT=$BACKEND_INTAKE_COUNT"
    echo "BACKEND_RESOURCE_ASSIGNMENT_REFERENCE_COUNT=$BACKEND_ASSIGNMENT_COUNT"
    echo "FRONTEND_PROJECT_INTAKE_REFERENCE_COUNT=$FRONTEND_INTAKE_COUNT"
    echo "FRONTEND_RESOURCE_ASSIGNMENT_REFERENCE_COUNT=$FRONTEND_ASSIGNMENT_COUNT"
    echo "PROJECT_INTAKE_RESOURCE_ASSIGNMENT_INVENTORY_RESULT=READY"
    echo "NEXT_ACTION=RECONCILE_EXISTING_WORKFLOWS_AND_IMPLEMENT_ONLY_CONFIRMED_GAPS"

} 2>&1 | tee "$LOG"

echo
echo "Inventory log: $LOG"
