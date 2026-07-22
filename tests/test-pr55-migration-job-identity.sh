#!/usr/bin/env bash
set -Eeuo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATION_JOB="$REPO_ROOT/scripts/run-pr55-test-migration-job.sh"
FIXTURE_ROOT="$(mktemp -d)"
BIN_DIR="$FIXTURE_ROOT/bin"

cleanup() {
  rm -rf "$FIXTURE_ROOT"
}
trap cleanup EXIT

mkdir -p "$BIN_DIR"

cat > "$BIN_DIR/az" <<'AZ'
#!/usr/bin/env bash
set -Eeuo pipefail

printf '%q ' "$@" >> "$AZURE_CALL_LOG"
printf '\n' >> "$AZURE_CALL_LOG"

if [[ "$1 $2" == "containerapp show" ]]; then
  query=""
  for ((index = 1; index <= $#; index++)); do
    if [[ "${!index}" == "--query" ]]; then
      next=$((index + 1))
      query="${!next}"
      break
    fi
  done

  case "$query" in
    properties.managedEnvironmentId)
      printf '%s\n' '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.App/managedEnvironments/cae-test'
      ;;
    *'.identity | [0]')
      printf '%s\n' "$TEST_REGISTRY_IDENTITY"
      ;;
    'identity.userAssignedIdentities | keys(@)')
      printf '%s\n' "$TEST_ASSIGNED_IDENTITIES"
      ;;
    *'.username | [0]'|*'.passwordSecretRef | [0]')
      ;;
    *)
      echo "Unexpected containerapp show query: $query" >&2
      exit 91
      ;;
  esac
elif [[ "$1 $2 $3" == "containerapp job show" ]]; then
  exit 1
elif [[ "$1 $2 $3" == "containerapp job create" ]]; then
  :
elif [[ "$1 $2 $3" == "containerapp job start" ]]; then
  printf '%s\n' 'pr55-mig-test-execution'
elif [[ "$1 $2 $3 $4" == "containerapp job execution list" ]]; then
  printf '%s\n' 'Succeeded'
elif [[ "$1 $2 $3" == "containerapp job delete" ]]; then
  :
elif [[ "$1 $2" == "acr login" ]]; then
  printf '%s\n' 'temporary-test-token'
else
  printf 'Unexpected az call:' >&2
  printf ' %q' "$@" >&2
  printf '\n' >&2
  exit 92
fi
AZ
chmod +x "$BIN_DIR/az"

cat > "$BIN_DIR/sleep" <<'SLEEP'
#!/usr/bin/env bash
exit 0
SLEEP
chmod +x "$BIN_DIR/sleep"

export PATH="$BIN_DIR:$PATH"
export AZURE_RESOURCE_GROUP='rg-test'
export AZURE_API_APP='ca-test-api'
export AZURE_ACR_NAME='acrtest'
export PR55_MIGRATION_IMAGE='acrtest.azurecr.io/project-health-dashboard-pr55-migrator@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
export PROJECTPULSE_TEST_DATABASE_URL='fixture-database-url-without-credentials'
export PR55_MIGRATION_JOB_NAME='pr55-mig-test'
export AZURE_CALL_LOG="$FIXTURE_ROOT/az-calls.log"

REGISTRY_IDENTITY='/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/RG-IDENTITIES/providers/Microsoft.ManagedIdentity/userAssignedIdentities/ACR-PULL'
ASSIGNED_IDENTITY='/SUBSCRIPTIONS/00000000-0000-0000-0000-000000000000/RESOURCEGROUPS/rg-identities/PROVIDERS/MICROSOFT.MANAGEDIDENTITY/USERASSIGNEDIDENTITIES/acr-pull'
export TEST_REGISTRY_IDENTITY="$REGISTRY_IDENTITY"
export TEST_ASSIGNED_IDENTITIES="$ASSIGNED_IDENTITY"

SUCCESS_OUTPUT="$FIXTURE_ROOT/success.out"
"$MIGRATION_JOB" >"$SUCCESS_OUTPUT" 2>&1

grep -Fq 'PR55_MIGRATION_JOB_REGISTRY_AUTH=REUSABLE_USER_ASSIGNED_IDENTITY' "$SUCCESS_OUTPUT"
grep -Fq -- "--mi-user-assigned $REGISTRY_IDENTITY" "$AZURE_CALL_LOG"
grep -Fq -- "--registry-identity $REGISTRY_IDENTITY" "$AZURE_CALL_LOG"
grep -Fq 'PR55_MIGRATION_JOB_STATUS=SUCCEEDED' "$SUCCESS_OUTPUT"
grep -Fq 'PR55_MIGRATION_JOB_CLEANUP=COMPLETE' "$SUCCESS_OUTPUT"

: > "$AZURE_CALL_LOG"
export TEST_ASSIGNED_IDENTITIES='/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-identities/providers/Microsoft.ManagedIdentity/userAssignedIdentities/different-identity'
UNASSIGNED_OUTPUT="$FIXTURE_ROOT/unassigned.out"
if "$MIGRATION_JOB" >"$UNASSIGNED_OUTPUT" 2>&1; then
  echo 'Expected an unassigned registry identity to be rejected.' >&2
  exit 1
fi

grep -Fq 'The test API ACR user-assigned identity is not assigned to the API app.' "$UNASSIGNED_OUTPUT"
if grep -Fq 'containerapp job create' "$AZURE_CALL_LOG"; then
  echo 'The migration job was created despite the unassigned identity guard.' >&2
  exit 1
fi

: > "$AZURE_CALL_LOG"
export TEST_REGISTRY_IDENTITY='system'
export TEST_ASSIGNED_IDENTITIES=''
FALLBACK_OUTPUT="$FIXTURE_ROOT/fallback.out"
"$MIGRATION_JOB" >"$FALLBACK_OUTPUT" 2>&1

grep -Fq 'PR55_MIGRATION_JOB_REGISTRY_AUTH=EPHEMERAL_AZURE_TOKEN' "$FALLBACK_OUTPUT"
grep -Fq -- '--registry-username 00000000-0000-0000-0000-000000000000' "$AZURE_CALL_LOG"
if grep -Fq -- '--mi-user-assigned' "$AZURE_CALL_LOG" ||
   grep -Fq -- '--registry-identity' "$AZURE_CALL_LOG"; then
  echo 'The credential fallback unexpectedly reused an application identity.' >&2
  exit 1
fi

echo 'PR55_MIGRATION_JOB_IDENTITY_TEST=PASS'
