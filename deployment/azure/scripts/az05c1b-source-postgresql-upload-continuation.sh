#!/usr/bin/env bash
set -Eeuo pipefail

TENANT_ID="535941da-da72-4a8b-8378-983a54bec342"
BASE_DIR="$HOME/project-health-dashboard-migration"
EXPORT_ROOT="$BASE_DIR/exports"
EXPORT_DIR="${EXPORT_DIR:-}"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

if [ -z "$EXPORT_DIR" ]; then
    EXPORT_DIR="$(
        find "$EXPORT_ROOT" \
            -mindepth 1 \
            -maxdepth 1 \
            -type d \
            -name 'postgresql13-*' \
            -printf '%p\n' 2>/dev/null |
        sort |
        tail -n 1
    )"
fi

if [ -z "$EXPORT_DIR" ] || [ ! -d "$EXPORT_DIR" ]; then
    echo "ERROR: No PostgreSQL export directory was found."
    exit 1
fi

MANIFEST="$EXPORT_DIR/manifest.json"
CHECKSUMS="$EXPORT_DIR/SHA256SUMS"
AZCOPY_BIN="${AZCOPY_BIN:-$HOME/.local/bin/azcopy}"

if [ ! -f "$MANIFEST" ]; then
    echo "ERROR: Export manifest is missing: $MANIFEST"
    exit 1
fi

if [ ! -f "$CHECKSUMS" ]; then
    echo "ERROR: Checksum file is missing: $CHECKSUMS"
    exit 1
fi

if [ ! -x "$AZCOPY_BIN" ]; then
    if command -v azcopy >/dev/null 2>&1; then
        AZCOPY_BIN="$(command -v azcopy)"
    else
        echo "ERROR: AzCopy is not installed."
        exit 1
    fi
fi

readarray -t DESTINATION_VALUES < <(
    python3 - "$MANIFEST" <<'PY'
import json
import sys
from pathlib import Path

manifest = json.loads(Path(sys.argv[1]).read_text())
destination = manifest["azure_destination"]
account = destination["storage_account"]
container = destination["container"]
prefix = destination["prefix"]
print(account)
print(container)
print(prefix)
print(f"https://{account}.blob.core.windows.net/{container}/{prefix}")
PY
)

STORAGE_ACCOUNT="${DESTINATION_VALUES[0]}"
STORAGE_CONTAINER="${DESTINATION_VALUES[1]}"
REMOTE_PREFIX="${DESTINATION_VALUES[2]}"
DESTINATION="${DESTINATION_VALUES[3]}"

section "AZ-05C1B - Resume PostgreSQL Export Upload"

echo "Local export: $EXPORT_DIR"
echo "Azure destination: $DESTINATION"
echo "TIME=$(date -u -Is)"

section "Revalidating export package checksums"

(
    cd "$EXPORT_DIR"
    sha256sum --check SHA256SUMS
)

section "Authorizing AzCopy with device authentication"

export AZCOPY_AUTO_LOGIN_TYPE=DEVICE
export AZCOPY_TENANT_ID="$TENANT_ID"

cat <<EOF
A new device code will be displayed.
Open the displayed Microsoft sign-in page immediately on your laptop,
enter the new code, and sign in with the Azure migration account.
The prior device code has expired and must not be reused.
EOF

"$AZCOPY_BIN" list \
    "https://${STORAGE_ACCOUNT}.blob.core.windows.net/${STORAGE_CONTAINER}"

section "Uploading the existing verified export package"

"$AZCOPY_BIN" copy \
    "${EXPORT_DIR}/*" \
    "$DESTINATION" \
    --recursive=true \
    --overwrite=false \
    --check-length=true

section "Verifying uploaded Blob objects"

"$AZCOPY_BIN" list "$DESTINATION"

section "AZ-05C1B completed successfully"

echo "Local package: $EXPORT_DIR"
echo "Azure destination: $DESTINATION"
echo
echo "Do not delete the local package until restore validation is complete."
echo
echo "************************************************************"
echo "SOURCE POSTGRESQL EXPORT UPLOADED"
echo "************************************************************"
