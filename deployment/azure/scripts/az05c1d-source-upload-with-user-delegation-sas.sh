#!/usr/bin/env bash
set -Eeuo pipefail

# Run this script on the Oracle Linux source host after generating a
# short-lived user delegation SAS in Azure Cloud Shell.
# The SAS is entered through a silent prompt and is not stored in a file.

STORAGE_ACCOUNT="stphdtest7825cc"
STORAGE_CONTAINER="database-exports"
EXPORT_DIR="${EXPORT_DIR:-/home/opc/project-health-dashboard-migration/exports/postgresql13-20260712T023119Z}"
REMOTE_PREFIX="${REMOTE_PREFIX:-source-postgresql13/20260712T023119Z}"
AZCOPY_BIN="${AZCOPY_BIN:-$HOME/.local/bin/azcopy}"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

section "AZ-05C1D - Upload verified PostgreSQL export with SAS"

echo "Local export: $EXPORT_DIR"
echo "Remote prefix: $REMOTE_PREFIX"
echo "Storage account: $STORAGE_ACCOUNT"
echo "Container: $STORAGE_CONTAINER"

echo
if [ ! -d "$EXPORT_DIR" ]; then
    echo "ERROR: Export directory does not exist:"
    echo "$EXPORT_DIR"
    exit 1
fi

if [ ! -x "$AZCOPY_BIN" ]; then
    echo "ERROR: AzCopy executable was not found:"
    echo "$AZCOPY_BIN"
    exit 1
fi

if [ ! -f "$EXPORT_DIR/SHA256SUMS" ]; then
    echo "ERROR: SHA256SUMS is missing from the export package."
    exit 1
fi

section "Revalidating local export checksums"

(
    cd "$EXPORT_DIR"
    sha256sum --check SHA256SUMS
)

section "Reading the short-lived SAS token"

echo "Paste the token generated in Azure Cloud Shell."
echo "Input is hidden and will not be written to shell history."
read -r -s -p "User delegation SAS: " SAS_TOKEN
echo

SAS_TOKEN="${SAS_TOKEN#\?}"

if [ -z "$SAS_TOKEN" ] || [[ "$SAS_TOKEN" != *"sig="* ]]; then
    echo "ERROR: The supplied value does not look like a SAS token."
    unset SAS_TOKEN
    exit 1
fi

unset AZCOPY_AUTO_LOGIN_TYPE AZCOPY_TENANT_ID

DESTINATION_BASE="https://${STORAGE_ACCOUNT}.blob.core.windows.net/${STORAGE_CONTAINER}/${REMOTE_PREFIX}"
DESTINATION_WITH_SAS="${DESTINATION_BASE}?${SAS_TOKEN}"

section "Uploading migration package"

"$AZCOPY_BIN" copy \
    "${EXPORT_DIR}/*" \
    "$DESTINATION_WITH_SAS" \
    --recursive=true \
    --overwrite=false \
    --check-length=true \
    --from-to=LocalBlob

section "Verifying uploaded objects"

"$AZCOPY_BIN" list "$DESTINATION_WITH_SAS"

unset SAS_TOKEN DESTINATION_WITH_SAS

section "AZ-05C1D completed successfully"

echo "Local export: $EXPORT_DIR"
echo "Azure destination: $DESTINATION_BASE"
echo

echo "The SAS token was not saved by this script."
echo "It will expire at the time configured when it was generated."
echo "Do not delete the local export until Azure restore validation is complete."
echo

echo "************************************************************"
echo "SOURCE POSTGRESQL EXPORT UPLOADED"
echo "************************************************************"
