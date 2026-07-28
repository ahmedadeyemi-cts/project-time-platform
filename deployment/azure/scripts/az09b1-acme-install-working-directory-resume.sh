#!/usr/bin/env bash
set -Eeuo pipefail

REPOSITORY="ahmedadeyemi-cts/project-time-platform"
BRANCH="azure-migration/project-health-dashboard-foundation"
ORIGINAL_SCRIPT_PATH="deployment/azure/scripts/az09b-configure-west-custom-domain-tls.sh"

WORK_DIR="$(mktemp -d /tmp/phd-az09b1-XXXXXX)"
ORIGINAL_SCRIPT="$WORK_DIR/az09b-original.sh"
FIXED_SCRIPT="$WORK_DIR/az09b-working-directory-fixed.sh"
trap 'rm -rf "$WORK_DIR"' EXIT

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

fail() {
    echo "ERROR: $*" >&2
    exit 1
}

section "AZ-09B1 - acme.sh Install Working-Directory Resume"
echo "TIME=$(date -u -Is)"
echo "CLOUDFLARE_DNS_RECORD_REUSE=true"
echo "CERTIFICATE_ISSUED_BEFORE_RESUME=false"
echo "KEY_VAULT_CERTIFICATE_CHANGED_BEFORE_RESUME=false"
echo "APPLICATION_GATEWAY_HTTPS_CHANGED_BEFORE_RESUME=false"
echo "ORACLE_VM_REQUIRED=false"
echo "ACR_IMAGE_REBUILD=false"
echo "CONTAINER_APP_REDEPLOY=false"
echo "DATABASE_CHANGE=false"
echo "ORIGINAL_FAILURE=ACME_INSTALL_SOURCE_WORKING_DIRECTORY"

command -v gh >/dev/null 2>&1 || fail "GitHub CLI is required."
command -v python3 >/dev/null 2>&1 || fail "python3 is required."

gh api \
  -H "Accept: application/vnd.github.raw+json" \
  "repos/${REPOSITORY}/contents/${ORIGINAL_SCRIPT_PATH}?ref=${BRANCH}" \
  > "$ORIGINAL_SCRIPT"

[ -s "$ORIGINAL_SCRIPT" ] || fail "Original AZ-09B script download failed."

python3 - "$ORIGINAL_SCRIPT" "$FIXED_SCRIPT" <<'PY'
import sys
from pathlib import Path

source = Path(sys.argv[1]).read_text()
old = '''        "$ACME_SOURCE/acme.sh" --install --home "$ACME_HOME" --accountemail "$ACME_EMAIL" --nocron >/dev/null'''
new = '''        (
            cd "$ACME_SOURCE"
            ./acme.sh --install --home "$ACME_HOME" --accountemail "$ACME_EMAIL" --nocron >/dev/null
        )'''

count = source.count(old)
if count != 1:
    raise SystemExit(f"ERROR: Expected one acme.sh install invocation, found {count}.")

Path(sys.argv[2]).write_text(source.replace(old, new, 1))
PY

chmod 700 "$FIXED_SCRIPT"
bash -n "$FIXED_SCRIPT" || fail "Corrected AZ-09B syntax validation failed."

echo "ACME_INSTALL_WORKING_DIRECTORY_FIX=applied"
echo "FIXED_SCRIPT_SYNTAX=passed"
echo "NEXT_ACTION=REUSE_DNS_AND_COMPLETE_CERTIFICATE_KEYVAULT_AND_HTTPS"

PHD_CONFIGURE_WEST_CUSTOM_DOMAIN_TLS=YES bash "$FIXED_SCRIPT"
