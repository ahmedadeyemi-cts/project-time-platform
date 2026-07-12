#!/usr/bin/env bash
set -Eeuo pipefail

PROVIDER_NAMESPACE="Microsoft.Quota"
BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2a4b-register-microsoft-quota-provider-$STAMP.log"

mkdir -p "$LOG_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

{
    section "AZ-05C2A4B - Register Microsoft.Quota Resource Provider"

    echo "TIME=$(date -u -Is)"
    echo "Subscription: $(az account show --query name --output tsv)"
    echo "Subscription ID: $(az account show --query id --output tsv)"
    echo "Provider: $PROVIDER_NAMESPACE"
    echo
    echo "This operation registers an Azure resource provider."
    echo "It does not create a VM or another billable Azure resource."

    section "Checking current provider state"

    CURRENT_STATE="$(
        az provider show \
            --namespace "$PROVIDER_NAMESPACE" \
            --query registrationState \
            --output tsv
    )"

    echo "CURRENT_PROVIDER_STATE=$CURRENT_STATE"

    if [ "$CURRENT_STATE" != "Registered" ]; then
        section "Submitting provider registration"

        az provider register \
            --namespace "$PROVIDER_NAMESPACE" \
            --only-show-errors \
            --output none
    else
        echo "Provider is already registered; no registration request was required."
    fi

    section "Waiting for Registered state"

    FINAL_STATE="$CURRENT_STATE"

    for attempt in $(seq 1 90); do
        FINAL_STATE="$(
            az provider show \
                --namespace "$PROVIDER_NAMESPACE" \
                --query registrationState \
                --output tsv
        )"

        echo "ATTEMPT=$attempt PROVIDER_STATE=$FINAL_STATE"

        if [ "$FINAL_STATE" = "Registered" ]; then
            break
        fi

        sleep 10
    done

    if [ "$FINAL_STATE" != "Registered" ]; then
        echo "ERROR: $PROVIDER_NAMESPACE did not reach Registered state within 15 minutes."
        exit 1
    fi

    section "Validating provider metadata"

    az provider show \
        --namespace "$PROVIDER_NAMESPACE" \
        --query '{Namespace:namespace,RegistrationState:registrationState,ResourceTypes:resourceTypes[].resourceType}' \
        --output json

    echo
    echo "MICROSOFT_QUOTA_PROVIDER_STATE=$FINAL_STATE"

    section "AZ-05C2A4B completed successfully"

    echo "No VM or other billable Azure resource was created."
    echo
    echo "************************************************************"
    echo "MICROSOFT QUOTA RESOURCE PROVIDER READY"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Registration log: $LOG"
