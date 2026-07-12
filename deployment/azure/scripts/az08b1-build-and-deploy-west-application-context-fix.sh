#!/usr/bin/env bash
set -Eeuo pipefail

REPOSITORY="ahmedadeyemi-cts/project-time-platform"
MIGRATION_BRANCH="azure-migration/project-health-dashboard-foundation"
CANONICAL_PATH="deployment/azure/scripts/az08b-build-and-deploy-west-application.sh"
WORK_DIR="$(mktemp -d /tmp/phd-az08b1-XXXXXX)"
CANONICAL_SCRIPT="$WORK_DIR/az08b-canonical.sh"
FIXED_SCRIPT="$WORK_DIR/az08b-context-fixed.sh"

trap 'rm -rf "$WORK_DIR"' EXIT

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

section "AZ-08B1 - West Application ACR Build Context Fix"
echo "TIME=$(date -u -Is)"
echo "SOURCE_FILES_MODIFIED=false"
echo "AZURE_RESOURCES_CREATED_BY_FIX_WRAPPER=false"
echo "ORIGINAL_FAILURE_STAGE=BEFORE_ACR_BUILD_SUBMISSION"

command -v gh >/dev/null 2>&1 || {
    echo "ERROR: GitHub CLI is required."
    return 1 2>/dev/null || true
}

gh auth status >/dev/null 2>&1 || {
    echo "ERROR: GitHub CLI is not authenticated."
    return 1 2>/dev/null || true
}

gh api \
    -H "Accept: application/vnd.github.raw+json" \
    "repos/${REPOSITORY}/contents/${CANONICAL_PATH}?ref=${MIGRATION_BRANCH}" \
    > "$CANONICAL_SCRIPT"

python3 - "$CANONICAL_SCRIPT" "$FIXED_SCRIPT" <<'PY'
from pathlib import Path
import sys

source = Path(sys.argv[1])
target = Path(sys.argv[2])
text = source.read_text(encoding="utf-8")

anchor = '    section "Building API image in ACR"\n\n    az acr build'
replacement = (
    '    section "Building API image in ACR"\n\n'
    '    cd "$SOURCE_DIR"\n'
    '    echo "ACR_BUILD_CONTEXT=$SOURCE_DIR"\n\n'
    '    az acr build'
)

if text.count(anchor) != 1:
    raise SystemExit("ERROR: Expected API build anchor was not found exactly once.")

text = text.replace(anchor, replacement, 1)

context_line = '        "$SOURCE_DIR"'
if text.count(context_line) != 2:
    raise SystemExit("ERROR: Expected exactly two ACR source-context arguments.")

text = text.replace(context_line, '        .', 2)
target.write_text(text, encoding="utf-8")
PY

chmod +x "$FIXED_SCRIPT"
bash -n "$FIXED_SCRIPT"

echo "ACR_BUILD_CONTEXT_FIX=applied"
echo "FIXED_DEPLOYMENT_SCRIPT_SYNTAX=passed"
echo "NEXT_ACTION=BUILD_IMAGES_AND_DEPLOY_WEST_APPLICATION"

PHD_BUILD_AND_DEPLOY_WEST_APPLICATION=YES bash "$FIXED_SCRIPT"
