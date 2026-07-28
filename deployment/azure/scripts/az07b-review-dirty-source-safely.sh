#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_HEAD="5a221da29cdfc1134e5d603175b311ff97658b67"
SOURCE_ROOT="${SOURCE_ROOT:-$(git rev-parse --show-toplevel 2>/dev/null || true)}"
BASE_DIR="$HOME/project-health-dashboard-source-checkpoint"
LOG_DIR="$BASE_DIR/logs"
CONFIG_DIR="$BASE_DIR/config"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az07b-review-dirty-source-$STAMP.log"
STATE_FILE="$CONFIG_DIR/az07b-dirty-source-review.env"

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

[ -n "$SOURCE_ROOT" ] || fail "Current directory is not inside a Git repository."
[ -d "$SOURCE_ROOT" ] || fail "Source repository path does not exist: $SOURCE_ROOT"
git -C "$SOURCE_ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1 || fail "Not a Git worktree: $SOURCE_ROOT"

{
    section "AZ-07B - Safe Dirty Source Review"
    echo "TIME=$(date -u -Is)"
    echo "SOURCE_REPOSITORY_ROOT=$SOURCE_ROOT"
    echo "READ_ONLY_SOURCE_REVIEW=true"
    echo "PATCH_CONTENT_PRINTED=false"
    echo "SECRET_VALUES_PRINTED=false"
    echo "SOURCE_FILES_MODIFIED=false"
    echo "GIT_STAGE_COMMIT_CHECKOUT_FETCH_PERFORMED=false"
    echo "APPLICATION_BUILD_STARTED=false"
    echo "AZURE_IMAGE_BUILD_STARTED=false"

    HEAD_SHA="$(git -C "$SOURCE_ROOT" rev-parse HEAD)"
    BRANCH_NAME="$(git -C "$SOURCE_ROOT" branch --show-current)"
    echo "SOURCE_HEAD=$HEAD_SHA"
    echo "SOURCE_BRANCH=${BRANCH_NAME:-detached}"
    echo "EXPECTED_HEAD=$EXPECTED_HEAD"
    echo "SOURCE_HEAD_MATCH=$([ "$HEAD_SHA" = "$EXPECTED_HEAD" ] && echo yes || echo no)"

    section "Changed-file inventory"
    mapfile -d '' -t CHANGED_PATHS < <(
        {
            git -C "$SOURCE_ROOT" diff --name-only -z
            git -C "$SOURCE_ROOT" diff --cached --name-only -z
            git -C "$SOURCE_ROOT" ls-files --others --exclude-standard -z
        } | sort -zu
    )

    echo "CHANGED_PATH_COUNT=${#CHANGED_PATHS[@]}"
    for path in "${CHANGED_PATHS[@]}"; do
        [ -n "$path" ] || continue
        full="$SOURCE_ROOT/$path"
        if git -C "$SOURCE_ROOT" ls-files --error-unmatch -- "$path" >/dev/null 2>&1; then
            tracked=true
        else
            tracked=false
        fi
        if [ -f "$full" ]; then
            size="$(stat -c '%s' -- "$full")"
            mime="$(file --brief --mime-type -- "$full" 2>/dev/null || echo unknown)"
            kind="$(file --brief -- "$full" 2>/dev/null || echo unknown)"
            digest="$(sha256sum -- "$full" | awk '{print $1}')"
        else
            size=0
            mime=missing
            kind=missing
            digest=missing
        fi
        echo "CHANGED_FILE=$path"
        echo "  TRACKED=$tracked"
        echo "  SIZE_BYTES=$size"
        echo "  MIME_TYPE=$mime"
        echo "  FILE_KIND=$kind"
        echo "  SHA256=$digest"
    done

    section "Git change summary"
    echo "WORKTREE_DIFF_STAT_BEGIN"
    git -C "$SOURCE_ROOT" diff --stat -- . || true
    echo "WORKTREE_DIFF_STAT_END"
    echo "WORKTREE_NUMSTAT_BEGIN"
    git -C "$SOURCE_ROOT" diff --numstat -- . || true
    echo "WORKTREE_NUMSTAT_END"
    echo "STAGED_DIFF_STAT_BEGIN"
    git -C "$SOURCE_ROOT" diff --cached --stat -- . || true
    echo "STAGED_DIFF_STAT_END"

    section "Whitespace and patch integrity checks"
    set +e
    git -C "$SOURCE_ROOT" diff --check -- .
    WORKTREE_DIFF_CHECK_RC=$?
    git -C "$SOURCE_ROOT" diff --cached --check -- .
    STAGED_DIFF_CHECK_RC=$?
    set -e
    echo "WORKTREE_DIFF_CHECK_RC=$WORKTREE_DIFF_CHECK_RC"
    echo "STAGED_DIFF_CHECK_RC=$STAGED_DIFF_CHECK_RC"

    section "Generated-artifact classification"
    PYC_PATH="deployment/rocky-linux/__pycache__/serve-frontend-local.cpython-39.pyc"
    echo "PYC_TRACKED=$([ -n "$(git -C "$SOURCE_ROOT" ls-files -- "$PYC_PATH")" ] && echo true || echo false)"
    set +e
    PYC_IGNORE_MATCH="$(git -C "$SOURCE_ROOT" check-ignore -v --no-index -- "$PYC_PATH" 2>/dev/null)"
    PYC_IGNORE_RC=$?
    set -e
    echo "PYC_IGNORE_RULE_FOUND=$([ "$PYC_IGNORE_RC" -eq 0 ] && echo true || echo false)"
    if [ "$PYC_IGNORE_RC" -eq 0 ]; then
        echo "PYC_IGNORE_RULE=$PYC_IGNORE_MATCH"
    else
        echo "PYC_IGNORE_RULE=none"
    fi
    echo "PYC_RECOMMENDATION=EXCLUDE_GENERATED_BINARY_FROM_SOURCE_COMMIT"

    section "Secret-pattern scan without value disclosure"
    python3 - "$SOURCE_ROOT" "${CHANGED_PATHS[@]}" <<'PY'
import re
import sys
from pathlib import Path

root = Path(sys.argv[1])
paths = sys.argv[2:]
patterns = {
    "PRIVATE_KEY_HEADER": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----"),
    "CREDENTIAL_ASSIGNMENT": re.compile(r"(?i)\b(password|passwd|pwd|secret|client[_-]?secret|api[_-]?key|access[_-]?key|token|connection[_-]?string)\b\s*[:=]\s*['\"]?[^\s'\";,]{6,}"),
    "URI_EMBEDDED_CREDENTIAL": re.compile(r"(?i)\b[a-z][a-z0-9+.-]*://[^\s/:]+:[^\s/@]+@"),
    "AZURE_SAS_SIGNATURE": re.compile(r"(?i)(?:\?|&|\b)sig=[A-Za-z0-9%+/=_-]{16,}"),
    "AWS_ACCESS_KEY": re.compile(r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"),
    "GITHUB_TOKEN": re.compile(r"\bgh[opurs]_[A-Za-z0-9_]{20,}\b"),
    "JWT_LIKE_TOKEN": re.compile(r"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b"),
}

text_file_count = 0
binary_or_unreadable_count = 0
total_findings = 0
for rel in paths:
    path = root / rel
    if not path.is_file():
        continue
    try:
        data = path.read_bytes()
    except OSError:
        binary_or_unreadable_count += 1
        continue
    if b"\x00" in data:
        binary_or_unreadable_count += 1
        print(f"SECRET_SCAN_FILE={rel};TYPE=binary;FINDINGS=not-scanned")
        continue
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError:
        binary_or_unreadable_count += 1
        print(f"SECRET_SCAN_FILE={rel};TYPE=non-utf8;FINDINGS=not-scanned")
        continue

    text_file_count += 1
    findings = []
    for line_no, line in enumerate(text.splitlines(), start=1):
        for name, pattern in patterns.items():
            if pattern.search(line):
                findings.append((line_no, name))
    total_findings += len(findings)
    if findings:
        summary = ",".join(f"{name}@L{line_no}" for line_no, name in findings)
        print(f"SECRET_SCAN_FILE={rel};TYPE=text;FINDING_COUNT={len(findings)};LOCATIONS={summary}")
    else:
        print(f"SECRET_SCAN_FILE={rel};TYPE=text;FINDING_COUNT=0")

print(f"SECRET_SCAN_TEXT_FILE_COUNT={text_file_count}")
print(f"SECRET_SCAN_BINARY_OR_UNREADABLE_COUNT={binary_or_unreadable_count}")
print(f"SECRET_SCAN_TOTAL_FINDINGS={total_findings}")
print("SECRET_SCAN_VALUES_DISCLOSED=false")
PY

    section "Build-container readiness"
    mapfile -t DOCKERFILES < <(find "$SOURCE_ROOT" -type f \( -name Dockerfile -o -name 'Dockerfile.*' \) -not -path "$SOURCE_ROOT/.git/*" -print | sort)
    echo "DOCKERFILE_COUNT=${#DOCKERFILES[@]}"
    echo "CONTAINER_IMAGE_BUILD_READY=false"
    echo "CONTAINER_IMAGE_BUILD_BLOCKERS=DIRTY_SOURCE,NO_DOCKERFILES"

    section "Decision"
    echo "SOURCE_REVIEW_RESULT=REVIEW_REQUIRED"
    echo "SOURCE_COMMIT_ALLOWED=false"
    echo "SOURCE_IMAGE_BUILD_ALLOWED=false"
    echo "NEXT_ACTION=REVIEW_SECRET_SCAN_AND_PREPARE_DEDICATED_SOURCE_COMMIT"

    cat > "$STATE_FILE" <<EOF
SOURCE_REPOSITORY_ROOT=$SOURCE_ROOT
SOURCE_HEAD=$HEAD_SHA
SOURCE_BRANCH=${BRANCH_NAME:-detached}
CHANGED_PATH_COUNT=${#CHANGED_PATHS[@]}
WORKTREE_DIFF_CHECK_RC=$WORKTREE_DIFF_CHECK_RC
STAGED_DIFF_CHECK_RC=$STAGED_DIFF_CHECK_RC
PYC_RECOMMENDATION=EXCLUDE_GENERATED_BINARY_FROM_SOURCE_COMMIT
DOCKERFILE_COUNT=${#DOCKERFILES[@]}
SOURCE_REVIEW_RESULT=REVIEW_REQUIRED
SOURCE_COMMIT_ALLOWED=false
SOURCE_IMAGE_BUILD_ALLOWED=false
SOURCE_REVIEW_AT=$(date -u -Is)
EOF
    chmod 600 "$STATE_FILE"

    echo "SOURCE_REVIEW_STATE_FILE=$STATE_FILE"
    echo
    echo "************************************************************"
    echo "SAFE DIRTY SOURCE REVIEW COMPLETE"
    echo "************************************************************"
} 2>&1 | tee "$LOG"

echo
echo "Review log: $LOG"
