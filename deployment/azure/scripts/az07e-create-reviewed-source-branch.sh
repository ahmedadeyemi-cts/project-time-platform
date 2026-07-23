#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_HEAD="5a221da29cdfc1134e5d603175b311ff97658b67"
SOURCE_BRANCH="${PHD_SOURCE_BRANCH:-source/work-register-billing-lifecycle-20260712}"
BASE_DIR="${HOME}/project-health-dashboard-source-checkpoint"
LOG_DIR="${BASE_DIR}/logs"
CONFIG_DIR="${BASE_DIR}/config"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="${LOG_DIR}/az07e-create-reviewed-source-branch-${STAMP}.log"
STATE_FILE="${CONFIG_DIR}/az07e-reviewed-source-branch.env"
BUILD_ROOT=""

PYC_PATH="deployment/rocky-linux/__pycache__/serve-frontend-local.cpython-39.pyc"
BACKEND_PATH="src/backend/ProjectTime.Api/Program.cs"
FRONTEND_PATH="src/frontend/project-time-web/src/WorkRegisterCenter.jsx"
CSS_PATH="src/frontend/project-time-web/src/work-register-center.css"
SQL_BILLING_PATH="deployment/rocky-linux/projectpulse-055d5a-billing-identifiers-create-edit-ui.sql"
SQL_LIFECYCLE_PATH="deployment/rocky-linux/projectpulse-055d6b5b-project-lifecycle-sidecar.sql"

EXPECTED_CHANGED_PATHS=(
    "$PYC_PATH"
    "$BACKEND_PATH"
    "$FRONTEND_PATH"
    "$CSS_PATH"
    "$SQL_BILLING_PATH"
    "$SQL_LIFECYCLE_PATH"
)

EXPECTED_STAGED_PATHS=(
    ".gitignore"
    "$PYC_PATH"
    "$BACKEND_PATH"
    "$FRONTEND_PATH"
    "$CSS_PATH"
    "$SQL_BILLING_PATH"
    "$SQL_LIFECYCLE_PATH"
)

EXPECTED_HASHES=(
    "$PYC_PATH|38477f5b3197010810e7073858ecb8adc100060ae705a1021a0aa514d4a46d9c"
    "$BACKEND_PATH|cf49794d73420df09ecad36ea9fafc374392a403f018cbacfc235c0f610783e7"
    "$FRONTEND_PATH|3a6fd5ac178e84268713d360dd8b9f4b6ec575792ae07d532ee8ee109e7342b0"
    "$CSS_PATH|3fe4cb822ed66122213324161c2faaf65e322c494fa6417f21c1a18f5931714f"
    "$SQL_BILLING_PATH|09618f8a12b22604c673a4156df8cdfbdf70ec93c9036a15b08d142d7b725082"
    "$SQL_LIFECYCLE_PATH|2bb8dc66c3944d9e2629840f9a1b1d742a18722994fa6255cf68fea833826b3a"
)

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

cleanup() {
    if [ -n "$BUILD_ROOT" ] && [ -d "$BUILD_ROOT" ]; then
        rm -rf -- "$BUILD_ROOT"
    fi
}
trap cleanup EXIT

normalize_sorted() {
    sed '/^$/d' | LC_ALL=C sort
}

compare_lists() {
    local actual_file="$1"
    shift
    local expected_file
    expected_file="$(mktemp)"
    printf '%s\n' "$@" | normalize_sorted > "$expected_file"
    if ! diff -u "$expected_file" "$actual_file"; then
        rm -f "$expected_file"
        return 1
    fi
    rm -f "$expected_file"
}

{
    section "AZ-07E - Create Reviewed Source Branch, Validate, Commit, and Push"
    echo "TIME=$(date -u -Is)"
    echo "EXPECTED_HEAD=$EXPECTED_HEAD"
    echo "SOURCE_BRANCH=$SOURCE_BRANCH"
    echo "SOURCE_BRANCH_WRITE_ACTION=true"
    echo "SOURCE_COMMIT_WRITE_ACTION=true"
    echo "SOURCE_PUSH_WRITE_ACTION=true"
    echo "MAIN_BRANCH_DIRECT_COMMIT=false"
    echo "AZURE_RESOURCE_CREATION=false"
    echo "APPLICATION_IMAGE_BUILD_STARTED=false"

    [ "${PHD_CREATE_REVIEWED_SOURCE_COMMIT:-}" = "YES" ] \
        || fail "Set PHD_CREATE_REVIEWED_SOURCE_COMMIT=YES to authorize branch creation and commit."

    [ "${PHD_PUSH_REVIEWED_SOURCE_BRANCH:-}" = "YES" ] \
        || fail "Set PHD_PUSH_REVIEWED_SOURCE_BRANCH=YES to authorize pushing the reviewed branch."

    ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" \
        || fail "Current directory is not inside a Git repository."
    cd "$ROOT"

    CURRENT_BRANCH="$(git branch --show-current)"
    CURRENT_HEAD="$(git rev-parse HEAD)"
    UPSTREAM="$(git rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' 2>/dev/null || true)"

    echo "SOURCE_REPOSITORY_ROOT=$ROOT"
    echo "CURRENT_BRANCH=${CURRENT_BRANCH:-detached}"
    echo "CURRENT_HEAD=$CURRENT_HEAD"
    echo "CURRENT_UPSTREAM=${UPSTREAM:-not-configured}"

    [ "$CURRENT_BRANCH" = "main" ] \
        || fail "Expected current branch main; found ${CURRENT_BRANCH:-detached}."
    [ "$CURRENT_HEAD" = "$EXPECTED_HEAD" ] \
        || fail "Source HEAD changed since review."
    [ "$UPSTREAM" = "origin/main" ] \
        || fail "Expected upstream origin/main; found ${UPSTREAM:-not-configured}."

    AHEAD_COUNT="$(git rev-list --count origin/main..HEAD)"
    BEHIND_COUNT="$(git rev-list --count HEAD..origin/main)"
    STAGED_BEFORE="$(git diff --cached --name-only | sed '/^$/d' | wc -l | tr -d ' ')"

    echo "SOURCE_AHEAD_OF_ORIGIN_MAIN=$AHEAD_COUNT"
    echo "SOURCE_BEHIND_ORIGIN_MAIN=$BEHIND_COUNT"
    echo "SOURCE_STAGED_COUNT_BEFORE=$STAGED_BEFORE"

    [ "$AHEAD_COUNT" = "0" ] || fail "Source main is ahead of origin/main."
    [ "$BEHIND_COUNT" = "0" ] || fail "Source main is behind origin/main."
    [ "$STAGED_BEFORE" = "0" ] || fail "Staged changes already exist."

    command -v git >/dev/null 2>&1 || fail "git is required."
    command -v sha256sum >/dev/null 2>&1 || fail "sha256sum is required."
    command -v rsync >/dev/null 2>&1 || fail "rsync is required."
    command -v dotnet >/dev/null 2>&1 || fail "dotnet is required."
    command -v npm >/dev/null 2>&1 || fail "npm is required."

    GIT_USER_NAME="$(git config user.name || true)"
    GIT_USER_EMAIL="$(git config user.email || true)"
    [ -n "$GIT_USER_NAME" ] || fail "git user.name is not configured."
    [ -n "$GIT_USER_EMAIL" ] || fail "git user.email is not configured."

    echo "GIT_USER_NAME_CONFIGURED=yes"
    echo "GIT_USER_EMAIL_CONFIGURED=yes"
    echo "DOTNET_VERSION=$(dotnet --version)"
    echo "NODE_VERSION=$(node --version 2>/dev/null || echo unavailable)"
    echo "NPM_VERSION=$(npm --version)"

    section "Validating reviewed source inventory"

    ACTUAL_CHANGED_FILE="$(mktemp)"
    {
        git diff --name-only
        git ls-files --others --exclude-standard
    } | normalize_sorted > "$ACTUAL_CHANGED_FILE"

    echo "ACTUAL_CHANGED_PATH_COUNT=$(wc -l < "$ACTUAL_CHANGED_FILE" | tr -d ' ')"
    cat "$ACTUAL_CHANGED_FILE" | sed 's/^/ACTUAL_CHANGED_PATH=/'

    compare_lists "$ACTUAL_CHANGED_FILE" "${EXPECTED_CHANGED_PATHS[@]}" \
        || fail "Changed path inventory no longer matches the reviewed six-file set."
    rm -f "$ACTUAL_CHANGED_FILE"

    for entry in "${EXPECTED_HASHES[@]}"; do
        path="${entry%%|*}"
        expected_hash="${entry##*|}"
        [ -f "$path" ] || fail "Reviewed file is missing: $path"
        actual_hash="$(sha256sum -- "$path" | awk '{print $1}')"
        echo "REVIEWED_HASH[$path]=$actual_hash"
        [ "$actual_hash" = "$expected_hash" ] \
            || fail "Reviewed file hash changed: $path"
    done

    [ ! -e "refs/heads/$SOURCE_BRANCH" ] || true
    if git show-ref --verify --quiet "refs/heads/$SOURCE_BRANCH"; then
        fail "Local source branch already exists: $SOURCE_BRANCH"
    fi

    REMOTE_BRANCH_RC=0
    git ls-remote --exit-code --heads origin "$SOURCE_BRANCH" >/tmp/az07e-remote-branch.txt 2>/tmp/az07e-remote-branch.err \
        || REMOTE_BRANCH_RC=$?
    if [ "$REMOTE_BRANCH_RC" -eq 0 ]; then
        fail "Remote source branch already exists: $SOURCE_BRANCH"
    elif [ "$REMOTE_BRANCH_RC" -ne 2 ]; then
        cat /tmp/az07e-remote-branch.err >&2 || true
        fail "Unable to verify whether the remote source branch exists."
    fi
    rm -f /tmp/az07e-remote-branch.txt /tmp/az07e-remote-branch.err

    section "Creating dedicated source branch"
    git switch -c "$SOURCE_BRANCH"
    echo "SOURCE_BRANCH_CREATED=yes"
    echo "ACTIVE_BRANCH=$(git branch --show-current)"

    section "Removing generated Python bytecode from version control"

    git restore --source=HEAD --worktree -- "$PYC_PATH"

    if ! grep -Fxq '# Python bytecode' .gitignore; then
        printf '\n# Python bytecode\n' >> .gitignore
    fi
    if ! grep -Fxq '__pycache__/' .gitignore; then
        printf '__pycache__/\n' >> .gitignore
    fi
    if ! grep -Fxq '*.py[cod]' .gitignore; then
        printf '*.py[cod]\n' >> .gitignore
    fi

    git rm --cached -- "$PYC_PATH"

    echo "PYC_RESTORED_TO_HEAD=yes"
    echo "PYC_REMOVED_FROM_VERSION_CONTROL=yes"
    echo "PYTHON_BYTECODE_IGNORE_RULES_PRESENT=yes"
    echo "PYC_LOCAL_FILE_PRESENT=$([ -f "$PYC_PATH" ] && echo yes || echo no)"
    echo "PYC_NOW_IGNORED=$([ -n "$(git check-ignore "$PYC_PATH" 2>/dev/null || true)" ] && echo yes || echo no)"

    section "Staging reviewed source files"

    git add -- \
        .gitignore \
        "$BACKEND_PATH" \
        "$FRONTEND_PATH" \
        "$CSS_PATH" \
        "$SQL_BILLING_PATH" \
        "$SQL_LIFECYCLE_PATH"

    STAGED_FILE="$(mktemp)"
    git diff --cached --name-only | normalize_sorted > "$STAGED_FILE"

    echo "STAGED_PATH_COUNT=$(wc -l < "$STAGED_FILE" | tr -d ' ')"
    cat "$STAGED_FILE" | sed 's/^/STAGED_PATH=/'

    compare_lists "$STAGED_FILE" "${EXPECTED_STAGED_PATHS[@]}" \
        || fail "Staged path inventory is not the expected reviewed set."
    rm -f "$STAGED_FILE"

    git diff --cached --check
    echo "STAGED_DIFF_CHECK=passed"

    section "Validating in isolated temporary build workspace"

    BUILD_ROOT="$(mktemp -d /tmp/phd-az07e-build-XXXXXX)"
    BUILD_SOURCE="$BUILD_ROOT/source"
    mkdir -p "$BUILD_SOURCE"

    rsync -a \
        --exclude='.git/' \
        --exclude='node_modules/' \
        --exclude='bin/' \
        --exclude='obj/' \
        --exclude='dist/' \
        --exclude='__pycache__/' \
        --exclude='*.pyc' \
        "$ROOT/" "$BUILD_SOURCE/"

    echo "ISOLATED_BUILD_ROOT=$BUILD_ROOT"

    dotnet restore "$BUILD_SOURCE/src/backend/ProjectTime.Api/ProjectTime.Api.csproj"
    dotnet build \
        "$BUILD_SOURCE/src/backend/ProjectTime.Api/ProjectTime.Api.csproj" \
        --configuration Release \
        --no-restore
    echo "BACKEND_RELEASE_BUILD=passed"

    (
        cd "$BUILD_SOURCE/src/frontend/project-time-web"
        npm ci --prefer-offline --no-audit --no-fund
        npm run build
    )
    echo "FRONTEND_PRODUCTION_BUILD=passed"

    section "Committing reviewed source work"

    git commit -m "feat: add work register billing and lifecycle workflows"
    COMMIT_SHA="$(git rev-parse HEAD)"

    echo "SOURCE_COMMIT_CREATED=yes"
    echo "SOURCE_COMMIT_SHA=$COMMIT_SHA"
    echo "SOURCE_COMMIT_BRANCH=$(git branch --show-current)"

    POST_COMMIT_STATUS_COUNT="$(git status --short --untracked-files=all | sed '/^$/d' | wc -l | tr -d ' ')"
    echo "POST_COMMIT_STATUS_ENTRY_COUNT=$POST_COMMIT_STATUS_COUNT"
    git status --short --untracked-files=all

    [ "$POST_COMMIT_STATUS_COUNT" = "0" ] \
        || fail "Working tree is not clean after the reviewed source commit."

    section "Pushing reviewed source branch"

    git push --set-upstream origin "$SOURCE_BRANCH"

    REMOTE_COMMIT_SHA="$(git ls-remote origin "refs/heads/$SOURCE_BRANCH" | awk '{print $1}')"
    echo "REMOTE_SOURCE_BRANCH_COMMIT=$REMOTE_COMMIT_SHA"
    [ "$REMOTE_COMMIT_SHA" = "$COMMIT_SHA" ] \
        || fail "Remote branch commit does not match the local reviewed commit."

    cat > "$STATE_FILE" <<EOF
SOURCE_REPOSITORY_ROOT=$ROOT
SOURCE_BRANCH=$SOURCE_BRANCH
SOURCE_COMMIT_SHA=$COMMIT_SHA
REMOTE_SOURCE_BRANCH_COMMIT=$REMOTE_COMMIT_SHA
BACKEND_RELEASE_BUILD=passed
FRONTEND_PRODUCTION_BUILD=passed
PYC_REMOVED_FROM_VERSION_CONTROL=yes
PYTHON_BYTECODE_IGNORE_RULES_PRESENT=yes
SOURCE_PUSHED_AT=$(date -u -Is)
EOF
    chmod 600 "$STATE_FILE"

    echo "REVIEWED_SOURCE_BRANCH_RESULT=COMMITTED_AND_PUSHED"
    echo "SOURCE_STATE_FILE=$STATE_FILE"
    echo "MAIN_BRANCH_MODIFIED=false"
    echo "AZURE_APPLICATION_IMAGE_BUILD_STARTED=false"

    echo
    echo "************************************************************"
    echo "REVIEWED SOURCE BRANCH COMMITTED AND PUSHED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Execution log: $LOG"
