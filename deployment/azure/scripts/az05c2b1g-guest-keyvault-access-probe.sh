#!/usr/bin/env bash
set -Eeuo pipefail

KEY_VAULT="kv-phd-t-eus-7825cc"
KEY_VAULT_SECRET="postgres-admin-password"
KEY_VAULT_HOST="${KEY_VAULT}.vault.azure.net"
PROBE_DIR="/var/lib/project-health-dashboard/az05c2b1g"
RESPONSE_FILE="$PROBE_DIR/keyvault-secret-response.json"

mkdir -p "$PROBE_DIR"
chmod 700 "$PROBE_DIR"
rm -f "$RESPONSE_FILE"

printf 'PROBE_TIME=%s\n' "$(date -u -Is)"
printf 'KEYVAULT_HOST=%s\n' "$KEY_VAULT_HOST"

printf 'KEYVAULT_DNS_BEGIN\n'
getent ahostsv4 "$KEY_VAULT_HOST" || true
printf 'KEYVAULT_DNS_END\n'

TOKEN_JSON="$(
    curl -fsS \
        --max-time 15 \
        -H Metadata:true \
        'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fvault.azure.net'
)"

ACCESS_TOKEN="$(jq -r '.access_token // empty' <<< "$TOKEN_JSON")"
unset TOKEN_JSON

if [ -z "$ACCESS_TOKEN" ]; then
    echo 'MANAGED_IDENTITY_KEYVAULT_TOKEN=failed'
    exit 1
fi

echo 'MANAGED_IDENTITY_KEYVAULT_TOKEN=success'

set +e
HTTP_CODE="$(
    curl -sS \
        --max-time 30 \
        --output "$RESPONSE_FILE" \
        --write-out '%{http_code}' \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        "https://${KEY_VAULT_HOST}/secrets/${KEY_VAULT_SECRET}?api-version=7.4"
)"
CURL_RC=$?
set -e

unset ACCESS_TOKEN

printf 'KEYVAULT_REST_CURL_EXIT_CODE=%s\n' "$CURL_RC"
printf 'KEYVAULT_REST_HTTP_STATUS=%s\n' "${HTTP_CODE:-000}"

if [ "$CURL_RC" -eq 0 ] && [ "$HTTP_CODE" = "200" ]; then
    if jq -e '.value | type == "string" and length > 0' "$RESPONSE_FILE" >/dev/null; then
        echo 'KEYVAULT_SECRET_VALUE_PRESENT=yes'
    else
        echo 'KEYVAULT_SECRET_VALUE_PRESENT=no'
        exit 1
    fi

    SECRET_ID="$(jq -r '.id // empty' "$RESPONSE_FILE")"
    SECRET_ENABLED="$(jq -r '.attributes.enabled // empty' "$RESPONSE_FILE")"
    SECRET_VERSION="${SECRET_ID##*/}"

    echo "KEYVAULT_SECRET_NAME=$KEY_VAULT_SECRET"
    echo "KEYVAULT_SECRET_VERSION_PRESENT=$([ -n "$SECRET_VERSION" ] && echo yes || echo no)"
    echo "KEYVAULT_SECRET_ENABLED=${SECRET_ENABLED:-unknown}"
    echo 'KEYVAULT_ACCESS_PROBE_RESULT=SECRET_RETRIEVAL_SUCCEEDED'
else
    ERROR_CODE="$(jq -r '.error.code // empty' "$RESPONSE_FILE" 2>/dev/null || true)"
    echo "KEYVAULT_ERROR_CODE=${ERROR_CODE:-none}"

    case "${HTTP_CODE:-000}" in
        401|403)
            echo 'KEYVAULT_ACCESS_PROBE_RESULT=RBAC_OR_AUTHORIZATION_DENIED'
            ;;
        000)
            echo 'KEYVAULT_ACCESS_PROBE_RESULT=NETWORK_OR_DNS_FAILURE'
            ;;
        *)
            echo 'KEYVAULT_ACCESS_PROBE_RESULT=REVIEW_HTTP_RESPONSE'
            ;;
    esac

    exit 1
fi

printf 'ACTIVE_RESTORE_PROCESSES_BEGIN\n'
ps -eo pid=,etimes=,args= \
    | grep -E '[a]zcopy copy|[p]g_restore|[p]sql|phd-restore-postgresql13-seed' \
    || true
printf 'ACTIVE_RESTORE_PROCESSES_END\n'

rm -f "$RESPONSE_FILE"
echo 'READ_ONLY_KEYVAULT_ACCESS_PROBE_COMPLETE'
