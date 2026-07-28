#!/usr/bin/env bash
set -Eeuo pipefail

REPO="ahmedadeyemi-cts/project-time-platform"
SOURCE_PR="11"
EXPECTED_SOURCE_SHA="abf45bf824747767282f68fa5bd50909f9751eb0"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az11a-role-enforcement-user-switcher-source-inventory-$STAMP.log"
WORK_DIR="$(mktemp -d /tmp/phd-az11a-XXXXXX)"

PROGRAM_PATH="src/backend/ProjectTime.Api/Program.cs"
APP_PATH="src/frontend/project-time-web/src/App.jsx"

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
    section "AZ-11A - Role Enforcement and User Switcher Source Inventory"
    echo "TIME=$(date -u -Is)"
    echo "AZURE_RESOURCE_CHANGE=false"
    echo "DATABASE_CHANGE=false"
    echo "APPLICATION_DEPLOYMENT=false"
    echo "ORACLE_VM_REQUIRED=false"
    echo "SOURCE_BRANCH_MODIFICATION=false"

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
    TREE_FILE="$WORK_DIR/tree.json"
    CANDIDATE_LIST="$WORK_DIR/candidate-paths.txt"
    CANDIDATE_DIR="$WORK_DIR/candidates"
    mkdir -p "$CANDIDATE_DIR"

    fetch_raw_file "$PROGRAM_PATH" "$PROGRAM_FILE"
    fetch_raw_file "$APP_PATH" "$APP_FILE"

    PROGRAM_SHA256="$(sha256sum "$PROGRAM_FILE" | awk '{print $1}')"
    APP_SHA256="$(sha256sum "$APP_FILE" | awk '{print $1}')"

    echo "PROGRAM_CS_SHA256=$PROGRAM_SHA256"
    echo "APP_JSX_SHA256=$APP_SHA256"
    echo "PROGRAM_CS_LINES=$(wc -l < "$PROGRAM_FILE" | tr -d ' ')"
    echo "APP_JSX_LINES=$(wc -l < "$APP_FILE" | tr -d ' ')"

    section "Discovering role, permission, session, user, security, and audit files"

    gh api "repos/$REPO/git/trees/$SOURCE_SHA?recursive=1" > "$TREE_FILE"

    python3 - "$TREE_FILE" > "$CANDIDATE_LIST" <<'PY'
import json
import re
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
keywords = re.compile(r"(role|permission|user|session|auth|security|audit|impersonat|view[-_]?as)", re.I)
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

for path in sorted(paths)[:80]:
    print(path)
PY

    echo "CANDIDATE_FILE_COUNT=$(wc -l < "$CANDIDATE_LIST" | tr -d ' ')"
    sed -n '1,80p' "$CANDIDATE_LIST"

    candidate_number=0
    while IFS= read -r path; do
        [ -n "$path" ] || continue
        candidate_number=$((candidate_number + 1))
        destination="$CANDIDATE_DIR/$(printf '%03d' "$candidate_number")-$(basename "$path")"
        fetch_raw_file "$path" "$destination" || true
    done < "$CANDIDATE_LIST"

    section "Existing backend view-as and effective-user implementation"

    print_matches \
        "Backend view-as context" \
        "$PROGRAM_FILE" \
        'ApplyProjectPulseViewAsContextAsync|X-ProjectPulse-View-As-User|View-As|view-as|effective.?user|actor.?user|original.?user' \
        600

    print_matches \
        "Backend role and permission enforcement" \
        "$PROGRAM_FILE" \
        'role.?code|role.?name|permission|authorize|forbidden|Status403|administrator|admin.?only|require.?role|require.?permission' \
        750

    print_matches \
        "Backend user and audit persistence" \
        "$PROGRAM_FILE" \
        'app_users|user_roles|app_user_roles|role_permissions|audit|activity_log|security_event|session_user_id|effective_user_id|actor_user_id' \
        750

    section "Existing frontend user switcher and role-aware experience"

    print_matches \
        "Frontend global View-As implementation" \
        "$APP_FILE" \
        'GlobalViewAs|View as|view-as|projectPulseViewAsUser|X-ProjectPulse-View-As-User|view_as_read_only' \
        650

    print_matches \
        "Frontend role and permission awareness" \
        "$APP_FILE" \
        'role-admin|roles-permissions-matrix|roleCodes|permission|administrator|adminOnly|can[A-Z]|isAdmin|effective.?session' \
        750

    section "Candidate-file role and permission references"

    if find "$CANDIDATE_DIR" -type f -size +0c | grep -q .; then
        grep -R -n -E -i -C 3 \
            'view[-_ ]?as|role.?code|permission|administrator|admin.?only|effective.?user|actor.?user|audit' \
            "$CANDIDATE_DIR" 2>/dev/null \
            | head -n 900 \
            || true
    else
        echo "No additional candidate files were downloaded."
    fi

    section "Inventory conclusions"

    BACKEND_VIEW_AS_COUNT="$(grep -E -i -c 'ApplyProjectPulseViewAsContextAsync|X-ProjectPulse-View-As-User|view-as/users' "$PROGRAM_FILE" || true)"
    FRONTEND_VIEW_AS_COUNT="$(grep -E -i -c 'GlobalViewAs|X-ProjectPulse-View-As-User|projectPulseViewAsUser' "$APP_FILE" || true)"
    BACKEND_ROLE_COUNT="$(grep -E -i -c 'role.?code|permission|administrator|Status403|forbidden' "$PROGRAM_FILE" || true)"
    FRONTEND_ROLE_COUNT="$(grep -E -i -c 'roleCodes|roles-permissions-matrix|role-admin|permission|isAdmin' "$APP_FILE" || true)"

    echo "BACKEND_VIEW_AS_REFERENCE_COUNT=$BACKEND_VIEW_AS_COUNT"
    echo "FRONTEND_VIEW_AS_REFERENCE_COUNT=$FRONTEND_VIEW_AS_COUNT"
    echo "BACKEND_ROLE_REFERENCE_COUNT=$BACKEND_ROLE_COUNT"
    echo "FRONTEND_ROLE_REFERENCE_COUNT=$FRONTEND_ROLE_COUNT"
    echo "ROLE_ENFORCEMENT_USER_SWITCHER_INVENTORY_RESULT=READY"
    echo "NEXT_ACTION=DESIGN_AND_IMPLEMENT_SERVER_ENFORCED_RBAC_AND_AUDITED_USER_SWITCHING"

} 2>&1 | tee "$LOG"

echo
echo "Inventory log: $LOG"
