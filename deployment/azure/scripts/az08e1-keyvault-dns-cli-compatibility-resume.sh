#!/usr/bin/env bash
set -Eeuo pipefail

# Both values must come from the operator's reviewed checkpoint, never a mutable branch.
REVIEWED_SCRIPT_COMMIT="${PHD_REVIEWED_SCRIPT_COMMIT:?set the reviewed 40-character source commit}"
REVIEWED_SCRIPT_SHA256="${PHD_REVIEWED_SCRIPT_SHA256:?set the reviewed canonical script SHA-256}"
[[ "$REVIEWED_SCRIPT_COMMIT" =~ ^[0-9a-f]{40}$ && "$REVIEWED_SCRIPT_SHA256" =~ ^[0-9a-f]{64}$ ]] || {
  echo 'Invalid reviewed script identity' >&2; exit 1;
}

REPOSITORY="ahmedadeyemi-cts/project-time-platform"
MIGRATION_BRANCH="azure-migration/project-health-dashboard-foundation"
CANONICAL_PATH="deployment/azure/scripts/az08e-repair-keyvault-private-dns-and-finish-west-deployment.sh"
WORK_DIR="$(mktemp -d /tmp/phd-az08e1-XXXXXX)"
CANONICAL_SCRIPT="$WORK_DIR/az08e-canonical.sh"
FIXED_SCRIPT="$WORK_DIR/az08e-cli-compatible.sh"

trap 'rm -rf "$WORK_DIR"' EXIT

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

main() {
    section "AZ-08E1 - Key Vault DNS CLI Compatibility Resume"
    echo "TIME=$(date -u -Is)"
    echo "ACR_IMAGE_REBUILD=false"
    echo "REUSE_EXISTING_ACR_IMAGES=true"
    echo "API_APP_RECREATION=false"
    echo "ORIGINAL_FAILURE=DNS_ZONE_GROUP_DELETE_UNSUPPORTED_YES_FLAG"

    command -v gh >/dev/null 2>&1 || {
        echo "ERROR: GitHub CLI is required."
        return 1
    }

    gh auth status >/dev/null 2>&1 || {
        echo "ERROR: GitHub CLI is not authenticated."
        return 1
    }

    gh api \
        -H "Accept: application/vnd.github.raw+json" \
        "repos/${REPOSITORY}/contents/${CANONICAL_PATH}?ref=${REVIEWED_SCRIPT_COMMIT}" \
        > "$CANONICAL_SCRIPT"
    printf '%s  %s\n' "$REVIEWED_SCRIPT_SHA256" "$CANONICAL_SCRIPT" | sha256sum --check --status || { echo 'Canonical script digest mismatch' >&2; exit 1; }


    python3 - "$CANONICAL_SCRIPT" "$FIXED_SCRIPT" <<'PY'
from pathlib import Path
import sys

source = Path(sys.argv[1])
target = Path(sys.argv[2])
text = source.read_text(encoding="utf-8")

old = '''        az network private-endpoint dns-zone-group delete \\
            -g "$RG_NETWORK" \\
            --endpoint-name "$KEY_VAULT_PRIVATE_ENDPOINT" \\
            -n "$KEY_VAULT_ZONE_GROUP" \\
            --yes \\
            --only-show-errors
'''
new = '''        az network private-endpoint dns-zone-group delete \\
            -g "$RG_NETWORK" \\
            --endpoint-name "$KEY_VAULT_PRIVATE_ENDPOINT" \\
            -n "$KEY_VAULT_ZONE_GROUP" \\
            --only-show-errors
'''

if text.count(old) != 1:
    raise SystemExit("ERROR: Expected unsupported dns-zone-group delete --yes block was not found exactly once.")

text = text.replace(old, new, 1)
target.write_text(text, encoding="utf-8")
PY

    chmod +x "$FIXED_SCRIPT"
    bash -n "$FIXED_SCRIPT"

    echo "DNS_ZONE_GROUP_DELETE_YES_FLAG_REMOVED=yes"
    echo "FIXED_SCRIPT_SYNTAX=passed"
    echo "NEXT_ACTION=REPAIR_KEYVAULT_DNS_AND_FINISH_WEST_DEPLOYMENT"

    PHD_REPAIR_KEYVAULT_DNS_AND_FINISH=YES bash "$FIXED_SCRIPT"
}

main "$@"
