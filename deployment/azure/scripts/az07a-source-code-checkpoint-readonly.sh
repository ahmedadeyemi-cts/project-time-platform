#!/usr/bin/env bash
set -Eeuo pipefail

REQUESTED_APP="${APP:-/opt/project-time-platform/app/project-time-platform-022}"
EXPECTED_BASE_HEAD="5a221da29cdfc1134e5d603175b311ff97658b67"
BASE_DIR="$HOME/project-health-dashboard-source-checkpoint"
LOG_DIR="$BASE_DIR/logs"
CONFIG_DIR="$BASE_DIR/config"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az07a-source-code-checkpoint-$STAMP.log"
STATUS_FILE="$LOG_DIR/az07a-git-status-$STAMP.txt"
HASH_FILE="$LOG_DIR/az07a-changed-file-hashes-$STAMP.txt"
STATE_FILE="$CONFIG_DIR/az07a-source-code-checkpoint.env"

mkdir -p "$LOG_DIR" "$CONFIG_DIR"

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

resolve_repository_root() {
    local candidate="$1"

    [ -n "$candidate" ] || return 1
    [ -e "$candidate" ] || return 1

    git -C "$candidate" rev-parse --is-inside-work-tree >/dev/null 2>&1 || return 1
    git -C "$candidate" rev-parse --show-toplevel
}

{
    section "AZ-07A - Read-Only Source Code Checkpoint"
    echo "TIME=$(date -u -Is)"
    echo "REQUESTED_APPLICATION_PATH=$REQUESTED_APP"
    echo "CURRENT_DIRECTORY=$(pwd -P)"
    echo "READ_ONLY_SOURCE_INSPECTION=true"
    echo "PATCH_CONTENT_COLLECTED=false"
    echo "SOURCE_FILES_MODIFIED=false"
    echo "GIT_STAGE_COMMIT_CHECKOUT_FETCH_PERFORMED=false"
    echo "APPLICATION_BUILD_STARTED=false"
    echo "AZURE_IMAGE_BUILD_STARTED=false"

    APP=""
    REPOSITORY_DETECTION_SOURCE=""

    if APP="$(resolve_repository_root "$REQUESTED_APP" 2>/dev/null)"; then
        REPOSITORY_DETECTION_SOURCE="requested-path"
    elif APP="$(resolve_repository_root "$(pwd -P)" 2>/dev/null)"; then
        REPOSITORY_DETECTION_SOURCE="current-directory"
    else
        fail "No Git worktree was found at the requested path or current directory."
    fi

    echo "APPLICATION_PATH=$APP"
    echo "REPOSITORY_DETECTION_SOURCE=$REPOSITORY_DETECTION_SOURCE"

    cd "$APP"

    ROOT="$(git rev-parse --show-toplevel)"
    GIT_DIR="$(git rev-parse --git-dir)"
    HEAD_SHA="$(git rev-parse HEAD)"
    BRANCH_NAME="$(git branch --show-current)"
    UPSTREAM="$(git rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' 2>/dev/null || true)"

    echo "SOURCE_REPOSITORY_ROOT=$ROOT"
    echo "SOURCE_GIT_DIR=$GIT_DIR"
    echo "SOURCE_BRANCH=${BRANCH_NAME:-detached}"
    echo "SOURCE_HEAD=$HEAD_SHA"
    echo "EXPECTED_BASE_HEAD=$EXPECTED_BASE_HEAD"
    echo "SOURCE_BASE_MATCH=$([ "$HEAD_SHA" = "$EXPECTED_BASE_HEAD" ] && echo yes || echo no)"
    echo "SOURCE_UPSTREAM=${UPSTREAM:-not-configured}"

    if [ -n "$UPSTREAM" ]; then
        AHEAD_COUNT="$(git rev-list --count "$UPSTREAM..HEAD" 2>/dev/null || echo unknown)"
        BEHIND_COUNT="$(git rev-list --count "HEAD..$UPSTREAM" 2>/dev/null || echo unknown)"
    else
        AHEAD_COUNT="unknown"
        BEHIND_COUNT="unknown"
    fi

    echo "SOURCE_AHEAD_OF_UPSTREAM=$AHEAD_COUNT"
    echo "SOURCE_BEHIND_UPSTREAM=$BEHIND_COUNT"

    section "Working-tree status"
    git status --short --untracked-files=all | tee "$STATUS_FILE"

    TRACKED_MODIFIED_COUNT="$(git diff --name-only | sed '/^$/d' | wc -l)"
    STAGED_COUNT="$(git diff --cached --name-only | sed '/^$/d' | wc -l)"
    UNTRACKED_COUNT="$(git ls-files --others --exclude-standard | sed '/^$/d' | wc -l)"
    STATUS_COUNT="$(sed '/^$/d' "$STATUS_FILE" | wc -l)"

    echo "SOURCE_TRACKED_MODIFIED_COUNT=$TRACKED_MODIFIED_COUNT"
    echo "SOURCE_STAGED_COUNT=$STAGED_COUNT"
    echo "SOURCE_UNTRACKED_COUNT=$UNTRACKED_COUNT"
    echo "SOURCE_STATUS_ENTRY_COUNT=$STATUS_COUNT"

    section "Changed-file names and hashes"
    : > "$HASH_FILE"

    {
        git diff --name-only -z
        git diff --cached --name-only -z
        git ls-files --others --exclude-standard -z
    } | sort -zu | while IFS= read -r -d '' path; do
        [ -n "$path" ] || continue
        if [ -f "$path" ]; then
            digest="$(sha256sum -- "$path" | awk '{print $1}')"
            printf '%s  %s\n' "$digest" "$path" | tee -a "$HASH_FILE"
        elif [ -e "$path" ]; then
            printf 'NON_REGULAR_FILE  %s\n' "$path" | tee -a "$HASH_FILE"
        else
            printf 'DELETED_OR_MISSING  %s\n' "$path" | tee -a "$HASH_FILE"
        fi
    done

    echo "PATCH_OR_DIFF_CONTENT_PRINTED=false"
    echo "CHANGED_FILE_HASH_FILE=$HASH_FILE"

    section "Build-input inventory"
    for path in \
        src/backend/ProjectTime.Api/ProjectTime.Api.csproj \
        src/frontend/project-time-web/package.json \
        src/frontend/project-time-web/package-lock.json; do
        echo "BUILD_INPUT[$path]=$([ -f "$path" ] && echo present || echo missing)"
    done

    mapfile -t DOCKERFILES < <(find . -type f \( -name Dockerfile -o -name 'Dockerfile.*' \) -not -path './.git/*' -print | sort)
    echo "DOCKERFILE_COUNT=${#DOCKERFILES[@]}"
    for path in "${DOCKERFILES[@]}"; do
        echo "DOCKERFILE=$path"
    done

    if [ "$STATUS_COUNT" -eq 0 ]; then
        SOURCE_CLEAN=true
        IMAGE_BUILD_ALLOWED=true
        NEXT_ACTION="PREPARE_REPRODUCIBLE_CONTAINER_BUILD"
        DECISION="CLEAN"
    else
        SOURCE_CLEAN=false
        IMAGE_BUILD_ALLOWED=false
        NEXT_ACTION="REVIEW_SANITIZE_COMMIT_AND_PUSH_SOURCE_CHANGES"
        DECISION="BLOCKED_DIRTY"
    fi

    cat > "$STATE_FILE" <<EOF
SOURCE_REPOSITORY_ROOT=$ROOT
SOURCE_GIT_DIR=$GIT_DIR
SOURCE_BRANCH=${BRANCH_NAME:-detached}
SOURCE_HEAD=$HEAD_SHA
SOURCE_UPSTREAM=${UPSTREAM:-not-configured}
SOURCE_AHEAD_OF_UPSTREAM=$AHEAD_COUNT
SOURCE_BEHIND_UPSTREAM=$BEHIND_COUNT
SOURCE_TRACKED_MODIFIED_COUNT=$TRACKED_MODIFIED_COUNT
SOURCE_STAGED_COUNT=$STAGED_COUNT
SOURCE_UNTRACKED_COUNT=$UNTRACKED_COUNT
SOURCE_STATUS_ENTRY_COUNT=$STATUS_COUNT
SOURCE_WORKTREE_CLEAN=$SOURCE_CLEAN
SOURCE_IMAGE_BUILD_ALLOWED=$IMAGE_BUILD_ALLOWED
SOURCE_CHECKPOINT_DECISION=$DECISION
SOURCE_CHECKPOINT_NEXT_ACTION=$NEXT_ACTION
SOURCE_CHECKPOINT_AT=$(date -u -Is)
SOURCE_STATUS_FILE=$STATUS_FILE
SOURCE_CHANGED_FILE_HASH_FILE=$HASH_FILE
EOF
    chmod 600 "$STATE_FILE"

    section "Decision"
    echo "SOURCE_WORKTREE_CLEAN=$SOURCE_CLEAN"
    echo "SOURCE_IMAGE_BUILD_ALLOWED=$IMAGE_BUILD_ALLOWED"
    echo "SOURCE_CHECKPOINT_DECISION=$DECISION"
    echo "NEXT_ACTION=$NEXT_ACTION"
    echo "SOURCE_CHECKPOINT_STATE_FILE=$STATE_FILE"

    if [ "$SOURCE_CLEAN" = true ]; then
        echo
        echo "************************************************************"
        echo "SOURCE CODE CHECKPOINT PASSED"
        echo "************************************************************"
    else
        echo
        echo "************************************************************"
        echo "SOURCE CODE CHECKPOINT BLOCKED BY UNCOMMITTED CHANGES"
        echo "************************************************************"
    fi

} 2>&1 | tee "$LOG"

echo
echo "Checkpoint log: $LOG"
echo "Status inventory: $STATUS_FILE"
echo "Changed-file hashes: $HASH_FILE"
