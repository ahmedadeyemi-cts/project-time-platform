#!/usr/bin/env bash
set -Eeuo pipefail

STORAGE_ACCOUNT="stphdtest7825cc"
STORAGE_CONTAINER="database-exports"
SOURCE_PREFIX="source-postgresql13/20260712T023119Z"
BLOB_HOST="${STORAGE_ACCOUNT}.blob.core.windows.net"
PROBE_DIR="/var/lib/project-health-dashboard/az05c2b1b"

mkdir -p "$PROBE_DIR"
chmod 700 "$PROBE_DIR"

REST_RESPONSE="$PROBE_DIR/list-blobs-response.xml"
AZCOPY_OUTPUT="$PROBE_DIR/azcopy-list-output.txt"
AZCOPY_LOG_DIR="$PROBE_DIR/azcopy-logs"
AZCOPY_PLAN_DIR="$PROBE_DIR/azcopy-plans"

rm -rf "$AZCOPY_LOG_DIR" "$AZCOPY_PLAN_DIR"
mkdir -p "$AZCOPY_LOG_DIR" "$AZCOPY_PLAN_DIR"

printf 'PROBE_TIME=%s\n' "$(date -u -Is)"
printf 'BLOB_HOST=%s\n' "$BLOB_HOST"

printf 'BLOB_DNS_BEGIN\n'
getent ahostsv4 "$BLOB_HOST" || true
printf 'BLOB_DNS_END\n'

TOKEN_JSON="$(
    curl -fsS \
        --max-time 15 \
        -H Metadata:true \
        'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fstorage.azure.com%2F'
)"

ACCESS_TOKEN="$(jq -r '.access_token // empty' <<< "$TOKEN_JSON")"
unset TOKEN_JSON

if [ -z "$ACCESS_TOKEN" ]; then
    echo 'MANAGED_IDENTITY_STORAGE_TOKEN=failed'
    exit 1
fi

echo 'MANAGED_IDENTITY_STORAGE_TOKEN=success'

REQUEST_DATE="$(LC_ALL=C date -u '+%a, %d %b %Y %H:%M:%S GMT')"

HTTP_CODE="$(
    curl -sS \
        --max-time 30 \
        --output "$REST_RESPONSE" \
        --write-out '%{http_code}' \
        --get \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "x-ms-date: $REQUEST_DATE" \
        -H 'x-ms-version: 2023-11-03' \
        "https://${BLOB_HOST}/${STORAGE_CONTAINER}" \
        --data 'restype=container' \
        --data 'comp=list' \
        --data-urlencode "prefix=${SOURCE_PREFIX}"
)"

unset ACCESS_TOKEN

printf 'BLOB_REST_HTTP_STATUS=%s\n' "$HTTP_CODE"

if [ "$HTTP_CODE" = '200' ]; then
    BLOB_COUNT="$(grep -o '<Blob>' "$REST_RESPONSE" | wc -l | tr -d ' ')"
    printf 'BLOB_REST_LIST=success\n'
    printf 'BLOB_REST_COUNT=%s\n' "$BLOB_COUNT"
    printf 'BLOB_REST_NAMES_BEGIN\n'
    grep -o '<Name>[^<]*</Name>' "$REST_RESPONSE" \
        | sed -e 's#<Name>##' -e 's#</Name>##' \
        | head -n 25 || true
    printf 'BLOB_REST_NAMES_END\n'
else
    printf 'BLOB_REST_LIST=failed\n'
    printf 'BLOB_REST_ERROR_BEGIN\n'
    sed -n '1,80p' "$REST_RESPONSE" || true
    printf 'BLOB_REST_ERROR_END\n'
fi

export AZCOPY_AUTO_LOGIN_TYPE=MSI
export AZCOPY_LOG_LOCATION="$AZCOPY_LOG_DIR"
export AZCOPY_JOB_PLAN_LOCATION="$AZCOPY_PLAN_DIR"

set +e
timeout 45 azcopy list \
    "https://${BLOB_HOST}/${STORAGE_CONTAINER}/${SOURCE_PREFIX}" \
    --output-type text \
    > "$AZCOPY_OUTPUT" 2>&1
AZCOPY_LIST_RC=$?
set -e

printf 'AZCOPY_LIST_EXIT_CODE=%s\n' "$AZCOPY_LIST_RC"
printf 'AZCOPY_LIST_OUTPUT_BEGIN\n'
sed -n '1,100p' "$AZCOPY_OUTPUT" || true
printf 'AZCOPY_LIST_OUTPUT_END\n'

printf 'ACTIVE_ORIGINAL_RESTORE_PROCESSES_BEGIN\n'
ps -eo pid=,etimes=,args= \
    | grep -E '[a]zcopy copy|[p]g_restore|[p]sql|phd-restore-postgresql13-seed' \
    || true
printf 'ACTIVE_ORIGINAL_RESTORE_PROCESSES_END\n'

if [ "$HTTP_CODE" = '200' ] && [ "$AZCOPY_LIST_RC" = '0' ]; then
    echo 'BLOB_ACCESS_PROBE_RESULT=ACCESS_AND_LISTING_SUCCEEDED'
elif [ "$HTTP_CODE" = '403' ]; then
    echo 'BLOB_ACCESS_PROBE_RESULT=RBAC_OR_AUTHORIZATION_DENIED'
elif [ "$HTTP_CODE" = '000' ]; then
    echo 'BLOB_ACCESS_PROBE_RESULT=NETWORK_OR_DNS_FAILURE'
elif [ "$AZCOPY_LIST_RC" = '124' ]; then
    echo 'BLOB_ACCESS_PROBE_RESULT=AZCOPY_LIST_TIMED_OUT'
else
    echo 'BLOB_ACCESS_PROBE_RESULT=REVIEW_HTTP_AND_AZCOPY_OUTPUT'
fi

echo 'READ_ONLY_BLOB_ACCESS_PROBE_COMPLETE'
