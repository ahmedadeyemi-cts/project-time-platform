#!/usr/bin/env bash
set -Eeuo pipefail

# Run this script in Azure Cloud Shell.
# It creates a short-lived user delegation SAS for the existing
# database-exports container. It does not enable shared-key access and does
# not write the SAS token to GitHub or to a persistent file.

STORAGE_ACCOUNT="stphdtest7825cc"
STORAGE_CONTAINER="database-exports"
VALIDITY_MINUTES="${VALIDITY_MINUTES:-60}"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

section "AZ-05C1C - Generate short-lived user delegation SAS"

echo "Storage account: $STORAGE_ACCOUNT"
echo "Container: $STORAGE_CONTAINER"
echo "Validity: ${VALIDITY_MINUTES} minutes"
echo "Authentication: current Azure Cloud Shell Microsoft Entra session"
echo

echo "This token grants only create, list, read, and write permissions."
echo "It is HTTPS-only and expires automatically."

START_TIME="$(date -u -d '-5 minutes' '+%Y-%m-%dT%H:%MZ')"
EXPIRY_TIME="$(date -u -d "+${VALIDITY_MINUTES} minutes" '+%Y-%m-%dT%H:%MZ')"

SAS_TOKEN="$(
    az storage container generate-sas \
        --account-name "$STORAGE_ACCOUNT" \
        --name "$STORAGE_CONTAINER" \
        --permissions clrw \
        --start "$START_TIME" \
        --expiry "$EXPIRY_TIME" \
        --https-only \
        --auth-mode login \
        --as-user \
        --output tsv
)"

if [ -z "$SAS_TOKEN" ] || [[ "$SAS_TOKEN" != *"sig="* ]]; then
    echo "ERROR: Azure CLI did not return a valid user delegation SAS."
    exit 1
fi

section "Copy the SAS token"

echo "Copy only the single token line between the markers."
echo "Do not paste the token into chat, email, GitHub, or a ticket."
echo

echo "-----BEGIN USER DELEGATION SAS-----"
printf '%s\n' "$SAS_TOKEN"
echo "-----END USER DELEGATION SAS-----"
echo

echo "Expires at UTC: $EXPIRY_TIME"
echo "The Azure CLI output does not include the leading '?' character."
echo

echo "************************************************************"
echo "USER DELEGATION SAS READY"
echo "************************************************************"

unset SAS_TOKEN
