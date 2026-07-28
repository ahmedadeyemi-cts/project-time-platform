#!/usr/bin/env bash
set -Eeuo pipefail

SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
LOCATION="westus3"

RG_NETWORK="rg-project-health-dashboard-test-network-westus3"
RG_DATA="rg-project-health-dashboard-test-data-westus3"
APP_GATEWAY="agw-phd-test-westus3"
INGRESS_PUBLIC_IP="pip-phd-test-ingress-westus3"
KEY_VAULT_NAME="kv-phd-t-w3-7825cc"
APPGW_IDENTITY="id-phd-test-appgw-westus3"

CUSTOM_DOMAIN="phd-west-test.onenecklab.com"
CLOUDFLARE_ZONE_NAME="onenecklab.com"
KEY_VAULT_CERTIFICATE_NAME="tls-phd-west-test-onenecklab-com"
APPGW_SSL_CERTIFICATE_NAME="ssl-phd-west-test"
HTTPS_FRONTEND_PORT="port-https-443"
HTTPS_LISTENER="listener-https-phd-west-test"
HTTP_CUSTOM_LISTENER="listener-http-phd-west-test"
HTTPS_RULE="rule-https-phd-west-test"
HTTP_REDIRECT_RULE="rule-http-redirect-phd-west-test"
REDIRECT_CONFIG="redirect-http-to-https-phd-west-test"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
ACME_HOME="$HOME/.acme.sh"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az09b-west-custom-domain-tls-$STAMP.log"
STATE_FILE="$CONFIG_DIR/az09b-west-custom-domain-tls.env"
WORK_DIR="$(mktemp -d /tmp/phd-az09b-XXXXXX)"

mkdir -p "$CONFIG_DIR" "$LOG_DIR"
chmod 700 "$WORK_DIR"
trap 'unset CF_Token CF_Zone_ID PFX_PASSWORD; rm -rf "$WORK_DIR"' EXIT

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

wait_for_gateway() {
    local attempt state
    for attempt in $(seq 1 80); do
        state="$(az network application-gateway show -g "$RG_NETWORK" -n "$APP_GATEWAY" --query provisioningState -o tsv 2>/dev/null || true)"
        echo "APP_GATEWAY_PROVISIONING_CHECK[$attempt]=${state:-not-found}"
        if [ "$state" = "Succeeded" ]; then
            return 0
        fi
        if [ "$state" = "Failed" ]; then
            return 1
        fi
        sleep 30
    done
    return 1
}

wait_for_dns() {
    local expected_ip="$1"
    local attempt resolved
    for attempt in $(seq 1 30); do
        resolved="$(python3 - "$CUSTOM_DOMAIN" <<'PY'
import socket
import sys
try:
    values = sorted({item[4][0] for item in socket.getaddrinfo(sys.argv[1], 80, type=socket.SOCK_STREAM)})
    print("\n".join(values))
except Exception:
    pass
PY
)"
        echo "PUBLIC_DNS_CHECK[$attempt]=${resolved:-unresolved}"
        if grep -Fxq "$expected_ip" <<< "$resolved"; then
            return 0
        fi
        sleep 10
    done
    return 1
}

cloudflare_success() {
    python3 - "$1" <<'PY'
import json
import sys
from pathlib import Path
obj = json.loads(Path(sys.argv[1]).read_text())
print("yes" if obj.get("success") is True else "no")
PY
}

cloudflare_result_id() {
    python3 - "$1" <<'PY'
import json
import sys
from pathlib import Path
obj = json.loads(Path(sys.argv[1]).read_text())
result = obj.get("result")
if isinstance(result, list):
    print((result[0] or {}).get("id", "") if result else "")
elif isinstance(result, dict):
    print(result.get("id", ""))
else:
    print("")
PY
}

{
    section "AZ-09B - West Custom Domain and TLS"
    echo "TIME=$(date -u -Is)"
    echo "CUSTOM_DOMAIN=$CUSTOM_DOMAIN"
    echo "CERTIFICATE_AUTHORITY=LetsEncrypt"
    echo "ACME_VALIDATION_METHOD=Cloudflare_DNS_01"
    echo "KEY_VAULT_CERTIFICATE_STORAGE=true"
    echo "APPLICATION_GATEWAY_HTTPS_LISTENER=true"
    echo "HTTP_TO_HTTPS_REDIRECT=true"
    echo "ORACLE_VM_REQUIRED=false"
    echo "ACR_IMAGE_REBUILD=false"
    echo "CONTAINER_APP_REDEPLOY=false"
    echo "DATABASE_CHANGE=false"
    echo "EAST_POSTGRESQL_REPLICA_CREATED=false"

    [ "${PHD_CONFIGURE_WEST_CUSTOM_DOMAIN_TLS:-}" = "YES" ] \
        || fail "Set PHD_CONFIGURE_WEST_CUSTOM_DOMAIN_TLS=YES to authorize DNS, certificate, Key Vault, and Application Gateway changes."

    command -v az >/dev/null 2>&1 || fail "Azure CLI is required."
    command -v curl >/dev/null 2>&1 || fail "curl is required."
    command -v git >/dev/null 2>&1 || fail "git is required."
    command -v openssl >/dev/null 2>&1 || fail "openssl is required."
    command -v python3 >/dev/null 2>&1 || fail "python3 is required."

    az account set --subscription "$SUBSCRIPTION_ID"
    CURRENT_SUBSCRIPTION="$(az account show --query id -o tsv)"
    [ "$CURRENT_SUBSCRIPTION" = "$SUBSCRIPTION_ID" ] || fail "Active subscription does not match."
    echo "CURRENT_SUBSCRIPTION_MATCH=yes"

    section "Collecting Cloudflare DNS authorization securely"

    if [ -z "${CF_Token:-}" ]; then
        read -r -s -p "Cloudflare API token for onenecklab.com: " CF_Token
        echo
    fi
    [ -n "$CF_Token" ] || fail "Cloudflare API token is empty."

    if [ -z "${CF_Zone_ID:-}" ]; then
        read -r -p "Cloudflare Zone ID for onenecklab.com: " CF_Zone_ID
    fi
    [ -n "$CF_Zone_ID" ] || fail "Cloudflare Zone ID is empty."

    ACME_EMAIL="${PHD_ACME_EMAIL:-}"
    if [ -z "$ACME_EMAIL" ]; then
        read -r -p "Email address for Let's Encrypt notices: " ACME_EMAIL
    fi
    [[ "$ACME_EMAIL" == *@*.* ]] || fail "A valid ACME email address is required."

    echo "CLOUDFLARE_TOKEN_CAPTURED=yes"
    echo "CLOUDFLARE_ZONE_ID_CAPTURED=yes"
    echo "ACME_EMAIL_CAPTURED=yes"

    section "Validating Azure public entry resources"

    APPGW_STATE="$(az network application-gateway show -g "$RG_NETWORK" -n "$APP_GATEWAY" --query provisioningState -o tsv)"
    APPGW_SKU="$(az network application-gateway show -g "$RG_NETWORK" -n "$APP_GATEWAY" --query sku.name -o tsv)"
    PUBLIC_IP="$(az network public-ip show -g "$RG_NETWORK" -n "$INGRESS_PUBLIC_IP" --query ipAddress -o tsv)"
    KEY_VAULT_ID="$(az keyvault show -g "$RG_DATA" -n "$KEY_VAULT_NAME" --query id -o tsv)"

    [ "$APPGW_STATE" = "Succeeded" ] || fail "Application Gateway state is $APPGW_STATE."
    [ "$APPGW_SKU" = "WAF_v2" ] || fail "Application Gateway SKU is $APPGW_SKU."
    [ -n "$PUBLIC_IP" ] || fail "Ingress public IP is empty."
    [ -n "$KEY_VAULT_ID" ] || fail "Key Vault ID is empty."

    echo "APPLICATION_GATEWAY_STATE=$APPGW_STATE"
    echo "APPLICATION_GATEWAY_SKU=$APPGW_SKU"
    echo "WEST_INGRESS_PUBLIC_IP=$PUBLIC_IP"

    section "Creating or updating Cloudflare DNS A record"

    ZONE_RESPONSE="$WORK_DIR/cloudflare-zone.json"
    curl -fsS \
        -H "Authorization: Bearer $CF_Token" \
        -H "Content-Type: application/json" \
        "https://api.cloudflare.com/client/v4/zones/$CF_Zone_ID" \
        > "$ZONE_RESPONSE"

    [ "$(cloudflare_success "$ZONE_RESPONSE")" = "yes" ] || fail "Cloudflare token or Zone ID validation failed."

    RETURNED_ZONE_NAME="$(python3 - "$ZONE_RESPONSE" <<'PY'
import json
import sys
from pathlib import Path
obj = json.loads(Path(sys.argv[1]).read_text())
print((obj.get("result") or {}).get("name", ""))
PY
)"
    [ "$RETURNED_ZONE_NAME" = "$CLOUDFLARE_ZONE_NAME" ] || fail "Cloudflare Zone ID belongs to $RETURNED_ZONE_NAME, expected $CLOUDFLARE_ZONE_NAME."

    RECORD_QUERY_RESPONSE="$WORK_DIR/cloudflare-record-query.json"
    curl -fsS \
        -H "Authorization: Bearer $CF_Token" \
        -H "Content-Type: application/json" \
        "https://api.cloudflare.com/client/v4/zones/$CF_Zone_ID/dns_records?type=A&name=$CUSTOM_DOMAIN" \
        > "$RECORD_QUERY_RESPONSE"

    [ "$(cloudflare_success "$RECORD_QUERY_RESPONSE")" = "yes" ] || fail "Cloudflare DNS record query failed."
    RECORD_ID="$(cloudflare_result_id "$RECORD_QUERY_RESPONSE")"

    DNS_PAYLOAD="$WORK_DIR/cloudflare-dns-payload.json"
    python3 - "$CUSTOM_DOMAIN" "$PUBLIC_IP" > "$DNS_PAYLOAD" <<'PY'
import json
import sys
print(json.dumps({
    "type": "A",
    "name": sys.argv[1],
    "content": sys.argv[2],
    "ttl": 120,
    "proxied": False,
    "comment": "Project Health Dashboard West Application Gateway"
}))
PY

    RECORD_WRITE_RESPONSE="$WORK_DIR/cloudflare-record-write.json"
    if [ -n "$RECORD_ID" ]; then
        curl -fsS -X PUT \
            -H "Authorization: Bearer $CF_Token" \
            -H "Content-Type: application/json" \
            --data-binary "@$DNS_PAYLOAD" \
            "https://api.cloudflare.com/client/v4/zones/$CF_Zone_ID/dns_records/$RECORD_ID" \
            > "$RECORD_WRITE_RESPONSE"
        DNS_RECORD_ACTION="updated"
    else
        curl -fsS -X POST \
            -H "Authorization: Bearer $CF_Token" \
            -H "Content-Type: application/json" \
            --data-binary "@$DNS_PAYLOAD" \
            "https://api.cloudflare.com/client/v4/zones/$CF_Zone_ID/dns_records" \
            > "$RECORD_WRITE_RESPONSE"
        DNS_RECORD_ACTION="created"
    fi

    [ "$(cloudflare_success "$RECORD_WRITE_RESPONSE")" = "yes" ] || fail "Cloudflare DNS record write failed."
    echo "CLOUDFLARE_DNS_RECORD_ACTION=$DNS_RECORD_ACTION"
    echo "CLOUDFLARE_DNS_RECORD=$CUSTOM_DOMAIN"
    echo "CLOUDFLARE_DNS_RECORD_IP=$PUBLIC_IP"
    echo "CLOUDFLARE_DNS_PROXIED=false"

    wait_for_dns "$PUBLIC_IP" || fail "Public DNS did not resolve $CUSTOM_DOMAIN to $PUBLIC_IP."
    echo "PUBLIC_DNS_RESOLUTION=passed"

    section "Issuing Let's Encrypt certificate with Cloudflare DNS-01"

    ACME_SOURCE="$WORK_DIR/acme.sh-source"
    if [ ! -x "$ACME_HOME/acme.sh" ]; then
        git clone --depth 1 https://github.com/acmesh-official/acme.sh.git "$ACME_SOURCE" >/dev/null 2>&1
        "$ACME_SOURCE/acme.sh" --install --home "$ACME_HOME" --accountemail "$ACME_EMAIL" --nocron >/dev/null
    fi

    chmod 700 "$ACME_HOME"
    [ -x "$ACME_HOME/acme.sh" ] || fail "acme.sh installation failed."

    export CF_Token CF_Zone_ID

    "$ACME_HOME/acme.sh" \
        --register-account \
        --server letsencrypt \
        --accountemail "$ACME_EMAIL" \
        >/dev/null 2>&1 || true

    "$ACME_HOME/acme.sh" \
        --issue \
        --server letsencrypt \
        --dns dns_cf \
        --dnssleep 30 \
        --keylength ec-256 \
        -d "$CUSTOM_DOMAIN"

    PRIVATE_KEY="$WORK_DIR/private-key.pem"
    FULL_CHAIN="$WORK_DIR/full-chain.pem"

    "$ACME_HOME/acme.sh" \
        --install-cert \
        --ecc \
        -d "$CUSTOM_DOMAIN" \
        --key-file "$PRIVATE_KEY" \
        --fullchain-file "$FULL_CHAIN" \
        >/dev/null

    [ -s "$PRIVATE_KEY" ] || fail "Issued certificate private key is missing."
    [ -s "$FULL_CHAIN" ] || fail "Issued certificate full chain is missing."
    chmod 600 "$PRIVATE_KEY" "$FULL_CHAIN"

    CERT_NOT_AFTER="$(openssl x509 -in "$FULL_CHAIN" -noout -enddate | cut -d= -f2-)"
    CERT_SUBJECT="$(openssl x509 -in "$FULL_CHAIN" -noout -subject | sed 's/^subject=//')"
    echo "TLS_CERTIFICATE_SUBJECT=$CERT_SUBJECT"
    echo "TLS_CERTIFICATE_NOT_AFTER=$CERT_NOT_AFTER"

    section "Importing TLS certificate into Azure Key Vault"

    PFX_FILE="$WORK_DIR/$KEY_VAULT_CERTIFICATE_NAME.pfx"
    PFX_PASSWORD="$(openssl rand -base64 36 | tr -d '\n')"

    openssl pkcs12 -export \
        -inkey "$PRIVATE_KEY" \
        -in "$FULL_CHAIN" \
        -name "$CUSTOM_DOMAIN" \
        -out "$PFX_FILE" \
        -passout pass:"$PFX_PASSWORD"
    chmod 600 "$PFX_FILE"

    az keyvault certificate import \
        --vault-name "$KEY_VAULT_NAME" \
        --name "$KEY_VAULT_CERTIFICATE_NAME" \
        --file "$PFX_FILE" \
        --password "$PFX_PASSWORD" \
        --only-show-errors \
        --output none

    CERT_SECRET_ID_VERSIONED="$(az keyvault secret show --vault-name "$KEY_VAULT_NAME" --name "$KEY_VAULT_CERTIFICATE_NAME" --query id -o tsv)"
    CERT_SECRET_ID_VERSIONLESS="$(python3 - "$CERT_SECRET_ID_VERSIONED" <<'PY'
import sys
parts = sys.argv[1].rstrip('/').split('/')
print('/'.join(parts[:-1]) + '/')
PY
)"

    [ -n "$CERT_SECRET_ID_VERSIONLESS" ] || fail "Versionless Key Vault certificate secret ID is empty."
    echo "KEY_VAULT_CERTIFICATE_NAME=$KEY_VAULT_CERTIFICATE_NAME"
    echo "KEY_VAULT_CERTIFICATE_IMPORTED=yes"

    section "Granting Application Gateway access to Key Vault"

    if ! az identity show -g "$RG_NETWORK" -n "$APPGW_IDENTITY" --output none >/dev/null 2>&1; then
        az identity create \
            -g "$RG_NETWORK" \
            -n "$APPGW_IDENTITY" \
            -l "$LOCATION" \
            --tags application="Project Health Dashboard" environment=test purpose=application-gateway-keyvault \
            --only-show-errors \
            --output none
        IDENTITY_ACTION="created"
    else
        IDENTITY_ACTION="existing"
    fi

    IDENTITY_ID="$(az identity show -g "$RG_NETWORK" -n "$APPGW_IDENTITY" --query id -o tsv)"
    IDENTITY_PRINCIPAL_ID="$(az identity show -g "$RG_NETWORK" -n "$APPGW_IDENTITY" --query principalId -o tsv)"
    [ -n "$IDENTITY_ID" ] || fail "Application Gateway identity ID is empty."
    [ -n "$IDENTITY_PRINCIPAL_ID" ] || fail "Application Gateway identity principal ID is empty."

    KV_ROLE_COUNT="$(az role assignment list \
        --assignee "$IDENTITY_PRINCIPAL_ID" \
        --scope "$KEY_VAULT_ID" \
        --role "Key Vault Secrets User" \
        --query 'length(@)' \
        -o tsv 2>/dev/null || echo 0)"

    if [ "$KV_ROLE_COUNT" = "0" ]; then
        az role assignment create \
            --assignee-object-id "$IDENTITY_PRINCIPAL_ID" \
            --assignee-principal-type ServicePrincipal \
            --role "Key Vault Secrets User" \
            --scope "$KEY_VAULT_ID" \
            --only-show-errors \
            --output none
        ROLE_ACTION="created"
    else
        ROLE_ACTION="existing"
    fi

    echo "APP_GATEWAY_IDENTITY_ACTION=$IDENTITY_ACTION"
    echo "APP_GATEWAY_KEY_VAULT_ROLE_ACTION=$ROLE_ACTION"

    az network application-gateway identity assign \
        -g "$RG_NETWORK" \
        --gateway-name "$APP_GATEWAY" \
        --identity "$IDENTITY_ID" \
        --no-wait \
        --only-show-errors \
        --output none

    wait_for_gateway || fail "Application Gateway identity assignment did not reach Succeeded."
    echo "Waiting 60 seconds for managed identity and role propagation."
    sleep 60

    section "Creating Application Gateway HTTPS configuration"

    if az network application-gateway ssl-cert show \
        -g "$RG_NETWORK" \
        --gateway-name "$APP_GATEWAY" \
        -n "$APPGW_SSL_CERTIFICATE_NAME" \
        --output none >/dev/null 2>&1; then
        az network application-gateway ssl-cert update \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$APPGW_SSL_CERTIFICATE_NAME" \
            --key-vault-secret-id "$CERT_SECRET_ID_VERSIONLESS" \
            --no-wait \
            --only-show-errors \
            --output none
        SSL_CERT_ACTION="updated"
    else
        az network application-gateway ssl-cert create \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$APPGW_SSL_CERTIFICATE_NAME" \
            --key-vault-secret-id "$CERT_SECRET_ID_VERSIONLESS" \
            --no-wait \
            --only-show-errors \
            --output none
        SSL_CERT_ACTION="created"
    fi

    wait_for_gateway || fail "Application Gateway SSL certificate reference did not reach Succeeded."
    echo "APPLICATION_GATEWAY_SSL_CERT_ACTION=$SSL_CERT_ACTION"

    if ! az network application-gateway frontend-port show \
        -g "$RG_NETWORK" \
        --gateway-name "$APP_GATEWAY" \
        -n "$HTTPS_FRONTEND_PORT" \
        --output none >/dev/null 2>&1; then
        az network application-gateway frontend-port create \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$HTTPS_FRONTEND_PORT" \
            --port 443 \
            --only-show-errors \
            --output none
    fi

    FRONTEND_IP_NAME="$(az network application-gateway frontend-ip list -g "$RG_NETWORK" --gateway-name "$APP_GATEWAY" --query '[0].name' -o tsv)"
    PORT80_NAME="$(az network application-gateway frontend-port list -g "$RG_NETWORK" --gateway-name "$APP_GATEWAY" --query '[?port==`80`].name | [0]' -o tsv)"
    BACKEND_POOL_NAME="$(az network application-gateway address-pool list -g "$RG_NETWORK" --gateway-name "$APP_GATEWAY" --query '[0].name' -o tsv)"
    HTTP_SETTINGS_NAME="$(az network application-gateway http-settings list -g "$RG_NETWORK" --gateway-name "$APP_GATEWAY" --query '[0].name' -o tsv)"

    [ -n "$FRONTEND_IP_NAME" ] || fail "Frontend IP configuration name is empty."
    [ -n "$PORT80_NAME" ] || fail "Frontend port 80 name is empty."
    [ -n "$BACKEND_POOL_NAME" ] || fail "Backend pool name is empty."
    [ -n "$HTTP_SETTINGS_NAME" ] || fail "Backend HTTP settings name is empty."

    if az network application-gateway http-listener show -g "$RG_NETWORK" --gateway-name "$APP_GATEWAY" -n "$HTTPS_LISTENER" --output none >/dev/null 2>&1; then
        az network application-gateway http-listener update \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$HTTPS_LISTENER" \
            --frontend-ip "$FRONTEND_IP_NAME" \
            --frontend-port "$HTTPS_FRONTEND_PORT" \
            --ssl-cert "$APPGW_SSL_CERTIFICATE_NAME" \
            --host-name "$CUSTOM_DOMAIN" \
            --only-show-errors \
            --output none
    else
        az network application-gateway http-listener create \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$HTTPS_LISTENER" \
            --frontend-ip "$FRONTEND_IP_NAME" \
            --frontend-port "$HTTPS_FRONTEND_PORT" \
            --ssl-cert "$APPGW_SSL_CERTIFICATE_NAME" \
            --host-name "$CUSTOM_DOMAIN" \
            --only-show-errors \
            --output none
    fi

    if az network application-gateway http-listener show -g "$RG_NETWORK" --gateway-name "$APP_GATEWAY" -n "$HTTP_CUSTOM_LISTENER" --output none >/dev/null 2>&1; then
        az network application-gateway http-listener update \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$HTTP_CUSTOM_LISTENER" \
            --frontend-ip "$FRONTEND_IP_NAME" \
            --frontend-port "$PORT80_NAME" \
            --host-name "$CUSTOM_DOMAIN" \
            --only-show-errors \
            --output none
    else
        az network application-gateway http-listener create \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$HTTP_CUSTOM_LISTENER" \
            --frontend-ip "$FRONTEND_IP_NAME" \
            --frontend-port "$PORT80_NAME" \
            --host-name "$CUSTOM_DOMAIN" \
            --only-show-errors \
            --output none
    fi

    if az network application-gateway rule show -g "$RG_NETWORK" --gateway-name "$APP_GATEWAY" -n "$HTTPS_RULE" --output none >/dev/null 2>&1; then
        az network application-gateway rule update \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$HTTPS_RULE" \
            --http-listener "$HTTPS_LISTENER" \
            --address-pool "$BACKEND_POOL_NAME" \
            --http-settings "$HTTP_SETTINGS_NAME" \
            --priority 20 \
            --rule-type Basic \
            --only-show-errors \
            --output none
    else
        az network application-gateway rule create \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$HTTPS_RULE" \
            --http-listener "$HTTPS_LISTENER" \
            --address-pool "$BACKEND_POOL_NAME" \
            --http-settings "$HTTP_SETTINGS_NAME" \
            --priority 20 \
            --rule-type Basic \
            --only-show-errors \
            --output none
    fi

    if az network application-gateway redirect-config show -g "$RG_NETWORK" --gateway-name "$APP_GATEWAY" -n "$REDIRECT_CONFIG" --output none >/dev/null 2>&1; then
        az network application-gateway redirect-config update \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$REDIRECT_CONFIG" \
            --type Permanent \
            --target-listener "$HTTPS_LISTENER" \
            --include-path true \
            --include-query-string true \
            --only-show-errors \
            --output none
    else
        az network application-gateway redirect-config create \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$REDIRECT_CONFIG" \
            --type Permanent \
            --target-listener "$HTTPS_LISTENER" \
            --include-path true \
            --include-query-string true \
            --only-show-errors \
            --output none
    fi

    if az network application-gateway rule show -g "$RG_NETWORK" --gateway-name "$APP_GATEWAY" -n "$HTTP_REDIRECT_RULE" --output none >/dev/null 2>&1; then
        az network application-gateway rule update \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$HTTP_REDIRECT_RULE" \
            --http-listener "$HTTP_CUSTOM_LISTENER" \
            --redirect-config "$REDIRECT_CONFIG" \
            --priority 10 \
            --rule-type Basic \
            --only-show-errors \
            --output none
    else
        az network application-gateway rule create \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$HTTP_REDIRECT_RULE" \
            --http-listener "$HTTP_CUSTOM_LISTENER" \
            --redirect-config "$REDIRECT_CONFIG" \
            --priority 10 \
            --rule-type Basic \
            --only-show-errors \
            --output none
    fi

    wait_for_gateway || fail "Application Gateway HTTPS configuration did not reach Succeeded."

    section "Validating HTTPS custom domain"

    HTTPS_STATUS=""
    for attempt in $(seq 1 30); do
        HTTPS_STATUS="$(curl -sS -o /dev/null -w '%{http_code}' --connect-timeout 10 --max-time 30 "https://$CUSTOM_DOMAIN/" 2>/dev/null || true)"
        echo "CUSTOM_DOMAIN_HTTPS_CHECK[$attempt]=${HTTPS_STATUS:-000}"
        if [ "$HTTPS_STATUS" = "200" ]; then
            break
        fi
        sleep 20
    done

    HEALTH_STATUS="$(curl -sS -o /dev/null -w '%{http_code}' --connect-timeout 10 --max-time 30 "https://$CUSTOM_DOMAIN/health" 2>/dev/null || true)"
    HTTP_REDIRECT_STATUS="$(curl -sS -o /dev/null -w '%{http_code}' --connect-timeout 10 --max-time 30 "http://$CUSTOM_DOMAIN/" 2>/dev/null || true)"

    [ "$HTTPS_STATUS" = "200" ] || fail "Custom-domain HTTPS root did not return HTTP 200."
    [ "$HEALTH_STATUS" = "200" ] || fail "Custom-domain HTTPS health endpoint did not return HTTP 200."
    [[ "$HTTP_REDIRECT_STATUS" =~ ^30[178]$ ]] || fail "HTTP endpoint did not return a permanent HTTPS redirect."

    CERTIFICATE_ISSUER="$(echo | openssl s_client -connect "$CUSTOM_DOMAIN:443" -servername "$CUSTOM_DOMAIN" 2>/dev/null | openssl x509 -noout -issuer | sed 's/^issuer=//')"

    echo "CUSTOM_DOMAIN_HTTPS_URL=https://$CUSTOM_DOMAIN"
    echo "CUSTOM_DOMAIN_HTTPS_STATUS=$HTTPS_STATUS"
    echo "CUSTOM_DOMAIN_HEALTH_STATUS=$HEALTH_STATUS"
    echo "CUSTOM_DOMAIN_HTTP_REDIRECT_STATUS=$HTTP_REDIRECT_STATUS"
    echo "CUSTOM_DOMAIN_CERTIFICATE_ISSUER=$CERTIFICATE_ISSUER"

    cat > "$STATE_FILE" <<EOF
CUSTOM_DOMAIN=$CUSTOM_DOMAIN
CUSTOM_DOMAIN_HTTPS_URL=https://$CUSTOM_DOMAIN
CUSTOM_DOMAIN_HTTPS_STATUS=$HTTPS_STATUS
CUSTOM_DOMAIN_HEALTH_STATUS=$HEALTH_STATUS
CUSTOM_DOMAIN_HTTP_REDIRECT_STATUS=$HTTP_REDIRECT_STATUS
CERTIFICATE_AUTHORITY=LetsEncrypt
KEY_VAULT_CERTIFICATE_NAME=$KEY_VAULT_CERTIFICATE_NAME
APPLICATION_GATEWAY=$APP_GATEWAY
APPLICATION_GATEWAY_SSL_CERTIFICATE=$APPGW_SSL_CERTIFICATE_NAME
APPLICATION_GATEWAY_HTTPS_LISTENER=$HTTPS_LISTENER
APPLICATION_GATEWAY_HTTP_REDIRECT_LISTENER=$HTTP_CUSTOM_LISTENER
APPLICATION_GATEWAY_IDENTITY=$APPGW_IDENTITY
CLOUDFLARE_DNS_PROXIED=false
CERTIFICATE_RENEWAL_AUTOMATION_PENDING=true
WEST_CUSTOM_DOMAIN_TLS_RESULT=READY
COMPLETED_AT=$(date -u -Is)
EOF
    chmod 600 "$STATE_FILE"

    echo "CERTIFICATE_RENEWAL_AUTOMATION_PENDING=true"
    echo "WEST_CUSTOM_DOMAIN_TLS_RESULT=READY"
    echo "WEST_CUSTOM_DOMAIN_TLS_STATE_FILE=$STATE_FILE"
    echo "ORACLE_VM_REQUIRED=false"
    echo "EAST_POSTGRESQL_REPLICA_CREATED=false"

    echo
    echo "************************************************************"
    echo "WEST CUSTOM DOMAIN AND TLS READY"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "TLS deployment log: $LOG"
