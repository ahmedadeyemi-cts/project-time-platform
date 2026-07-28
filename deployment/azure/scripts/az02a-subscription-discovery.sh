#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="${LOCATION:-westus3}"
BASE_DIR="${HOME}/project-health-dashboard-azure"
LOG_DIR="${BASE_DIR}/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="${LOG_DIR}/az02a-subscription-discovery-${STAMP}.txt"

mkdir -p "$LOG_DIR"

PROVIDERS=(
  Microsoft.App
  Microsoft.ContainerRegistry
  Microsoft.DBforPostgreSQL
  Microsoft.Network
  Microsoft.Storage
  Microsoft.KeyVault
  Microsoft.Insights
  Microsoft.OperationalInsights
  Microsoft.Cdn
  Microsoft.ManagedIdentity
  Microsoft.EventGrid
  Microsoft.AlertsManagement
)

{
  echo "============================================================"
  echo "AZ-02A - Project Health Dashboard Subscription Discovery"
  echo "============================================================"
  echo "TIME=$(date -u -Is)"

  echo
  echo "Signed-in Azure account"
  az account show --query '{subscriptionName:name,subscriptionId:id,tenantId:tenantId,subscriptionState:state,signedInUser:user.name,userType:user.type}' --output json

  echo
  echo "Accessible subscriptions"
  az account list --query '[].{subscriptionName:name,subscriptionId:id,tenantId:tenantId,state:state,isDefault:isDefault}' --output table

  echo
  echo "Requested region"
  az account list-locations --query "[?name=='${LOCATION}'].{name:name,displayName:displayName,regionalDisplayName:regionalDisplayName,geographyGroup:metadata.geographyGroup,physicalLocation:metadata.physicalLocation}" --output table

  echo
  echo "Required resource-provider states"
  for provider in "${PROVIDERS[@]}"; do
    state="$(az provider show --namespace "$provider" --query registrationState --output tsv 2>/dev/null || echo Unavailable)"
    printf '%-40s %s\n' "$provider" "$state"
  done

  echo
  echo "Existing resources"
  az resource list --query '[].{name:name,type:type,resourceGroup:resourceGroup,location:location}' --output table

  echo
  echo "Existing resource groups"
  az group list --query '[].{name:name,location:location,provisioningState:properties.provisioningState}' --output table

  echo
  echo "Compute usage and quotas"
  az vm list-usage --location "$LOCATION" --query "[?currentValue > \`0\` || contains(name.localizedValue, 'Total Regional') || contains(name.localizedValue, 'Standard D')].{quota:name.localizedValue,current:currentValue,limit:limit}" --output table 2>/dev/null || echo "VM quota information unavailable."

  echo
  echo "No Azure resources were created or modified."
} 2>&1 | tee "$OUT"

echo "Discovery report: $OUT"
