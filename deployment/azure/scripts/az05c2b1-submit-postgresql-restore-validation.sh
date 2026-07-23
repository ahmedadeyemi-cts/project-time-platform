#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
RG_MIGRATION="rg-project-health-dashboard-test-migration-eastus"
VM_NAME="vm-phd-test-db-migrate-eus"
PREP_RUN_COMMAND="phd-prepare-rocky10"
RESTORE_RUN_COMMAND="phd-restore-postgresql13-seed"

STORAGE_ACCOUNT="stphdtest7825cc"
STORAGE_CONTAINER="database-exports"
SOURCE_PREFIX="source-postgresql13/20260712T023119Z"
DUMP_FILE="ProjectPulse-pg13-20260712T023119Z.dump"
EXPECTED_DUMP_BYTES="3341746"
RESULT_PREFIX_ROOT="restore-results"

KEY_VAULT="kv-phd-t-eus-7825cc"
KEY_VAULT_SECRET="postgres-admin-password"
POSTGRES_FQDN="pg-phd-test-w3-7825cc.postgres.database.azure.com"
POSTGRES_DATABASE="project_health_dashboard"
POSTGRES_ADMIN="phdpgadmin"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2b1-submit-postgresql-restore-validation-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/az05c2b1-restore-validation.env"
WORK_DIR="$(mktemp -d)"

mkdir -p "$CONFIG_DIR" "$LOG_DIR"
trap 'rm -rf "$WORK_DIR"' EXIT

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

{
    section "AZ-05C2B1 - Submit PostgreSQL Initial-Seed Restore and Validation"

    SUBSCRIPTION_ID="$(az account show --query id --output tsv)"
    RESULT_PREFIX="$RESULT_PREFIX_ROOT/$STAMP"

    echo "TIME=$(date -u -Is)"
    echo "Subscription ID: $SUBSCRIPTION_ID"
    echo "Resource group: $RG_MIGRATION"
    echo "VM: $VM_NAME"
    echo "Source prefix: $SOURCE_PREFIX"
    echo "Target PostgreSQL: $POSTGRES_FQDN/$POSTGRES_DATABASE"
    echo "Result prefix: $RESULT_PREFIX"
    echo
    echo "This operation restores the verified initial PostgreSQL seed into the private Azure target."
    echo "It will not print or persist the PostgreSQL password."

    section "Validating prepared restore runner"

    VM_STATE="$(az vm show -g "$RG_MIGRATION" -n "$VM_NAME" --query provisioningState -o tsv)"
    POWER_STATE="$(az vm get-instance-view -g "$RG_MIGRATION" -n "$VM_NAME" --query "instanceView.statuses[?starts_with(code, 'PowerState/')].displayStatus | [0]" -o tsv)"

    echo "VM_PROVISIONING_STATE=$VM_STATE"
    echo "VM_POWER_STATE=$POWER_STATE"

    [ "$VM_STATE" = "Succeeded" ] || fail "Restore runner provisioning state is not Succeeded."
    [ "$POWER_STATE" = "VM running" ] || fail "Restore runner is not running."

    PREP_STATE="$(az vm run-command show -g "$RG_MIGRATION" --vm-name "$VM_NAME" --run-command-name "$PREP_RUN_COMMAND" --instance-view --query instanceView.executionState -o tsv)"
    PREP_EXIT="$(az vm run-command show -g "$RG_MIGRATION" --vm-name "$VM_NAME" --run-command-name "$PREP_RUN_COMMAND" --instance-view --query instanceView.exitCode -o tsv)"
    PREP_OUTPUT="$(az vm run-command show -g "$RG_MIGRATION" --vm-name "$VM_NAME" --run-command-name "$PREP_RUN_COMMAND" --instance-view --query instanceView.output -o tsv)"

    echo "PREP_EXECUTION_STATE=$PREP_STATE"
    echo "PREP_EXIT_CODE=$PREP_EXIT"

    [ "$PREP_STATE" = "Succeeded" ] || fail "Rocky Linux preparation Run Command did not succeed."
    [ "$PREP_EXIT" = "0" ] || fail "Rocky Linux preparation Run Command returned a nonzero exit code."
    grep -q 'PRIVATE ROCKY 10 RESTORE RUNNER PREPARATION READY' <<< "$PREP_OUTPUT" \
        || fail "Preparation success marker was not found."

    section "Assigning temporary result-upload permission"

    VM_PRINCIPAL_ID="$(az vm identity show -g "$RG_MIGRATION" -n "$VM_NAME" --query principalId -o tsv)"
    STORAGE_ACCOUNT_ID="$(az resource list --name "$STORAGE_ACCOUNT" --resource-type Microsoft.Storage/storageAccounts --query '[0].id' -o tsv)"
    CONTAINER_SCOPE="$STORAGE_ACCOUNT_ID/blobServices/default/containers/$STORAGE_CONTAINER"

    [ -n "$VM_PRINCIPAL_ID" ] || fail "VM managed identity principal ID is empty."
    [ -n "$STORAGE_ACCOUNT_ID" ] || fail "Storage account resource ID is empty."

    EXISTING_CONTRIBUTOR_ID="$(az role assignment list --assignee "$VM_PRINCIPAL_ID" --role "Storage Blob Data Contributor" --scope "$CONTAINER_SCOPE" --query '[0].id' -o tsv 2>/dev/null || true)"
    TEMP_CONTRIBUTOR_CREATED="false"

    if [ -n "$EXISTING_CONTRIBUTOR_ID" ]; then
        TEMP_CONTRIBUTOR_ASSIGNMENT_ID="$EXISTING_CONTRIBUTOR_ID"
        echo "TEMP_RESULT_UPLOAD_ROLE=existing"
    else
        ROLE_ASSIGNMENT_NAME="$(python3 - "$VM_PRINCIPAL_ID" "$CONTAINER_SCOPE" <<'PY'
import sys
import uuid
print(uuid.uuid5(uuid.NAMESPACE_URL, sys.argv[1] + '|' + sys.argv[2] + '|phd-restore-results'))
PY
        )"

        TEMP_CONTRIBUTOR_ASSIGNMENT_ID="$(az role assignment create \
            --name "$ROLE_ASSIGNMENT_NAME" \
            --assignee-object-id "$VM_PRINCIPAL_ID" \
            --assignee-principal-type ServicePrincipal \
            --role "Storage Blob Data Contributor" \
            --scope "$CONTAINER_SCOPE" \
            --query id \
            --output tsv)"

        TEMP_CONTRIBUTOR_CREATED="true"
        echo "TEMP_RESULT_UPLOAD_ROLE=created"
    fi

    echo "TEMP_RESULT_UPLOAD_SCOPE=$CONTAINER_SCOPE"

    section "Building restore and validation guest command"

    GUEST_SCRIPT="$WORK_DIR/restore-and-validate.sh"

    cat > "$GUEST_SCRIPT" <<EOF
#!/usr/bin/env bash
set -Eeuo pipefail

STATE_DIR="/var/lib/project-health-dashboard/az05c2b1"
SOURCE_DIR="\$STATE_DIR/source"
RESULT_DIR="\$STATE_DIR/results"
RUN_LOG="/var/log/phd-az05c2b1-restore-validation.log"
RESULT_PREFIX="$RESULT_PREFIX"
STORAGE_ACCOUNT="$STORAGE_ACCOUNT"
STORAGE_CONTAINER="$STORAGE_CONTAINER"
SOURCE_PREFIX="$SOURCE_PREFIX"
DUMP_FILE="$DUMP_FILE"
EXPECTED_DUMP_BYTES="$EXPECTED_DUMP_BYTES"
KEY_VAULT="$KEY_VAULT"
KEY_VAULT_SECRET="$KEY_VAULT_SECRET"
POSTGRES_FQDN="$POSTGRES_FQDN"
POSTGRES_DATABASE="$POSTGRES_DATABASE"
POSTGRES_ADMIN="$POSTGRES_ADMIN"
FINAL_STATUS="FAILED"
FAILURE_STAGE="initialization"

mkdir -p "\$STATE_DIR" "\$SOURCE_DIR" "\$RESULT_DIR"
chmod 700 "\$STATE_DIR" "\$SOURCE_DIR" "\$RESULT_DIR"
exec > >(tee -a "\$RUN_LOG") 2>&1

finalize() {
    rc=\$?
    trap - EXIT
    set +e
    unset PGPASSWORD

    mkdir -p "\$RESULT_DIR"
    cp -f "\$RUN_LOG" "\$RESULT_DIR/restore-validation.log" 2>/dev/null || true

    cat > "\$RESULT_DIR/validation-summary.txt" <<SUMMARY
STATUS=\$FINAL_STATUS
FAILURE_STAGE=\$FAILURE_STAGE
EXIT_CODE=\$rc
COMPLETED_AT=\$(date -u -Is)
SOURCE_PREFIX=\$SOURCE_PREFIX
TARGET_SERVER=\$POSTGRES_FQDN
TARGET_DATABASE=\$POSTGRES_DATABASE
RESULT_PREFIX=\$RESULT_PREFIX
SUMMARY

    if [ -d "\$RESULT_DIR" ]; then
        (
            cd "\$RESULT_DIR" || exit 0
            find . -maxdepth 1 -type f ! -name result-manifest.sha256 -printf '%P\n' | sort | xargs -r sha256sum > result-manifest.sha256
        )
    fi

    upload_rc=1
    if command -v azcopy >/dev/null 2>&1; then
        export AZCOPY_AUTO_LOGIN_TYPE=MSI
        export AZCOPY_LOG_LOCATION="\$STATE_DIR/azcopy-logs"
        export AZCOPY_JOB_PLAN_LOCATION="\$STATE_DIR/azcopy-plans"

        for attempt in \$(seq 1 18); do
            if azcopy copy \
                "\$RESULT_DIR/*" \
                "https://\${STORAGE_ACCOUNT}.blob.core.windows.net/\${STORAGE_CONTAINER}/\${RESULT_PREFIX}" \
                --recursive=true \
                --overwrite=true \
                --from-to=LocalBlob \
                --log-level=WARNING; then
                upload_rc=0
                break
            fi
            echo "Result upload not ready; retrying in 10 seconds (attempt \$attempt/18)."
            sleep 10
        done
    fi

    echo "RESULT_UPLOAD_EXIT_CODE=\$upload_rc"

    if [ "\$FINAL_STATUS" = "PASSED" ] && [ "\$rc" -eq 0 ] && [ "\$upload_rc" -eq 0 ]; then
        echo "POSTGRESQL INITIAL SEED RESTORE VALIDATION PASSED"
        exit 0
    fi

    echo "POSTGRESQL INITIAL SEED RESTORE VALIDATION FAILED"
    if [ "\$rc" -eq 0 ]; then
        exit 1
    fi
    exit "\$rc"
}

trap finalize EXIT

echo "AZ-05C2B1 guest restore started at \$(date -u -Is)"

FAILURE_STAGE="installing-tools"

dnf -y install postgresql curl jq tar gzip unzip

PSQL_MAJOR="\$(psql --version | awk '{print \$3}' | cut -d. -f1)"
PG_RESTORE_MAJOR="\$(pg_restore --version | awk '{print \$3}' | cut -d. -f1)"

echo "PSQL_VERSION=\$(psql --version)"
echo "PG_RESTORE_VERSION=\$(pg_restore --version)"

[ "\$PSQL_MAJOR" = "16" ] || { echo "ERROR: PostgreSQL client major version is not 16."; exit 1; }
[ "\$PG_RESTORE_MAJOR" = "16" ] || { echo "ERROR: pg_restore major version is not 16."; exit 1; }

if ! command -v azcopy >/dev/null 2>&1; then
    AZCOPY_TMP="\$(mktemp -d)"
    curl -fsSL "https://aka.ms/downloadazcopy-v10-linux" -o "\$AZCOPY_TMP/azcopy.tar.gz"
    tar -xzf "\$AZCOPY_TMP/azcopy.tar.gz" -C "\$AZCOPY_TMP"
    AZCOPY_BIN="\$(find "\$AZCOPY_TMP" -type f -name azcopy -perm -u+x | head -n 1)"
    [ -n "\$AZCOPY_BIN" ] || { echo "ERROR: AzCopy binary was not found."; exit 1; }
    install -m 0755 "\$AZCOPY_BIN" /usr/local/bin/azcopy
    rm -rf "\$AZCOPY_TMP"
fi

azcopy --version
export AZCOPY_AUTO_LOGIN_TYPE=MSI
export AZCOPY_LOG_LOCATION="\$STATE_DIR/azcopy-logs"
export AZCOPY_JOB_PLAN_LOCATION="\$STATE_DIR/azcopy-plans"

FAILURE_STAGE="downloading-source-package"
rm -rf "\$SOURCE_DIR"/*

azcopy copy \
    "https://\${STORAGE_ACCOUNT}.blob.core.windows.net/\${STORAGE_CONTAINER}/\${SOURCE_PREFIX}/*" \
    "\$SOURCE_DIR" \
    --recursive=true \
    --overwrite=true \
    --from-to=BlobLocal \
    --log-level=WARNING

SOURCE_FILE_COUNT="\$(find "\$SOURCE_DIR" -maxdepth 1 -type f | wc -l | tr -d ' ')"
echo "SOURCE_FILE_COUNT=\$SOURCE_FILE_COUNT"
[ "\$SOURCE_FILE_COUNT" -eq 15 ] || { echo "ERROR: Expected 15 source artifacts."; exit 1; }

FAILURE_STAGE="validating-source-package"
(
    cd "\$SOURCE_DIR"
    sha256sum -c SHA256SUMS | tee "\$RESULT_DIR/checksum-verification.txt"
)

DUMP_PATH="\$SOURCE_DIR/\$DUMP_FILE"
[ -f "\$DUMP_PATH" ] || { echo "ERROR: Dump archive is missing."; exit 1; }
ACTUAL_DUMP_BYTES="\$(stat -c '%s' "\$DUMP_PATH")"
echo "DUMP_BYTES=\$ACTUAL_DUMP_BYTES"
[ "\$ACTUAL_DUMP_BYTES" = "\$EXPECTED_DUMP_BYTES" ] || { echo "ERROR: Dump archive byte count differs from the verified source."; exit 1; }

DUMP_COUNT="\$(find "\$SOURCE_DIR" -maxdepth 1 -type f -name '*.dump' | wc -l | tr -d ' ')"
[ "\$DUMP_COUNT" -eq 1 ] || { echo "ERROR: Expected exactly one custom-format dump archive."; exit 1; }

pg_restore --list "\$DUMP_PATH" > "\$RESULT_DIR/target-pg-restore-toc.txt"
[ -s "\$RESULT_DIR/target-pg-restore-toc.txt" ] || { echo "ERROR: pg_restore TOC is empty."; exit 1; }

FAILURE_STAGE="retrieving-key-vault-secret"
KV_TOKEN="\$(curl -fsS -H Metadata:true 'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fvault.azure.net' | jq -r '.access_token')"
[ -n "\$KV_TOKEN" ] && [ "\$KV_TOKEN" != "null" ] || { echo "ERROR: Key Vault access token was not returned."; exit 1; }

PGPASSWORD="\$(curl -fsS \
    -H "Authorization: Bearer \$KV_TOKEN" \
    "https://\${KEY_VAULT}.vault.azure.net/secrets/\${KEY_VAULT_SECRET}?api-version=7.4" | jq -r '.value')"
unset KV_TOKEN
[ -n "\$PGPASSWORD" ] && [ "\$PGPASSWORD" != "null" ] || { echo "ERROR: PostgreSQL password secret was not returned."; exit 1; }

export PGPASSWORD
export PGHOST="\$POSTGRES_FQDN"
export PGPORT=5432
export PGDATABASE="\$POSTGRES_DATABASE"
export PGUSER="\$POSTGRES_ADMIN"
export PGSSLMODE=require

FAILURE_STAGE="validating-target-connection"
psql -X -v ON_ERROR_STOP=1 --tuples-only --no-align --command="SELECT current_database(), current_user, inet_server_addr(), ssl FROM pg_stat_ssl WHERE pid = pg_backend_pid();" \
    > "\$RESULT_DIR/target-connection.txt"
cat "\$RESULT_DIR/target-connection.txt"

grep -q '|t$' "\$RESULT_DIR/target-connection.txt" || { echo "ERROR: PostgreSQL SSL connection was not confirmed."; exit 1; }

TARGET_OBJECT_COUNT="\$(psql -X -v ON_ERROR_STOP=1 --tuples-only --no-align --command=\"SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.relkind IN ('r','p','v','m','f','S') AND n.nspname NOT LIKE 'pg_%' AND n.nspname <> 'information_schema';\")"
echo "TARGET_PRE_RESTORE_OBJECT_COUNT=\$TARGET_OBJECT_COUNT"
[ "\$TARGET_OBJECT_COUNT" = "0" ] || { echo "ERROR: Target database is not empty; restore stopped safely."; exit 1; }

FAILURE_STAGE="restoring-database"
pg_restore \
    --dbname="\$POSTGRES_DATABASE" \
    --no-owner \
    --no-privileges \
    --exit-on-error \
    --verbose \
    "\$DUMP_PATH"

FAILURE_STAGE="analyzing-database"
psql -X -v ON_ERROR_STOP=1 --command='ANALYZE;'

FAILURE_STAGE="collecting-target-inventory"

psql -X -v ON_ERROR_STOP=1 --csv --command="
SELECT nspname AS schema, pg_get_userbyid(nspowner) AS owner
FROM pg_namespace
WHERE nspname NOT LIKE 'pg_%' AND nspname <> 'information_schema'
ORDER BY nspname;" > "\$RESULT_DIR/target-schemas.csv"

psql -X -v ON_ERROR_STOP=1 --csv --command="
SELECT n.nspname AS schema, c.relname AS table_name, c.reltuples::bigint AS estimated_rows,
       pg_total_relation_size(c.oid) AS total_bytes,
       pg_size_pretty(pg_total_relation_size(c.oid)) AS total_size
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r','p')
  AND n.nspname NOT LIKE 'pg_%'
  AND n.nspname <> 'information_schema'
ORDER BY n.nspname, c.relname;" > "\$RESULT_DIR/target-tables.csv"

{
    echo 'schema,table,row_count'
    psql -X -v ON_ERROR_STOP=1 --csv --tuples-only <<'SQL'
SELECT format(
    'SELECT %L AS schema, %L AS table, count(*) AS row_count FROM %I.%I;',
    n.nspname, c.relname, n.nspname, c.relname
)
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r','p')
  AND n.nspname NOT LIKE 'pg_%'
  AND n.nspname <> 'information_schema'
ORDER BY n.nspname, c.relname;
\gexec
SQL
} > "\$RESULT_DIR/target-row-counts.csv"

psql -X -v ON_ERROR_STOP=1 --csv --command="
SELECT extname AS extension, extversion AS version, n.nspname AS schema
FROM pg_extension e
JOIN pg_namespace n ON n.oid=e.extnamespace
ORDER BY extname;" > "\$RESULT_DIR/target-extensions.csv"

psql -X -v ON_ERROR_STOP=1 --csv --command="
SELECT schemaname AS schema, sequencename AS sequence_name, last_value, start_value,
       increment_by, max_value, cycle, cache_size
FROM pg_sequences
WHERE schemaname NOT LIKE 'pg_%'
  AND schemaname <> 'information_schema'
ORDER BY schemaname, sequencename;" > "\$RESULT_DIR/target-sequences.csv"

FAILURE_STAGE="comparing-source-and-target"

python3 - "\$SOURCE_DIR" "\$RESULT_DIR" <<'PY'
import csv
import json
import sys
from pathlib import Path

source = Path(sys.argv[1])
result = Path(sys.argv[2])
errors = []
warnings = []

def rows(path):
    with path.open(newline='', encoding='utf-8-sig') as handle:
        return list(csv.DictReader(handle))

def keyset(data, fields):
    return {tuple(str(row.get(field, '')).strip() for field in fields) for row in data}

source_schemas = keyset(rows(source / 'schemas.csv'), ['schema'])
target_schemas = keyset(rows(result / 'target-schemas.csv'), ['schema'])
if source_schemas != target_schemas:
    errors.append({'check': 'schemas', 'source_only': sorted(source_schemas - target_schemas), 'target_only': sorted(target_schemas - source_schemas)})

source_tables = keyset(rows(source / 'tables.csv'), ['schema', 'table_name'])
target_tables = keyset(rows(result / 'target-tables.csv'), ['schema', 'table_name'])
if source_tables != target_tables:
    errors.append({'check': 'tables', 'source_only': sorted(source_tables - target_tables), 'target_only': sorted(target_tables - source_tables)})

source_counts = {tuple((r.get(k) or '').strip() for k in ('schema','table')): (r.get('row_count') or '').strip() for r in rows(source / 'row-counts.csv')}
target_counts = {tuple((r.get(k) or '').strip() for k in ('schema','table')): (r.get('row_count') or '').strip() for r in rows(result / 'target-row-counts.csv')}
if source_counts != target_counts:
    mismatches = []
    for key in sorted(set(source_counts) | set(target_counts)):
        if source_counts.get(key) != target_counts.get(key):
            mismatches.append({'table': key, 'source': source_counts.get(key), 'target': target_counts.get(key)})
    errors.append({'check': 'row_counts', 'mismatches': mismatches})

source_ext_rows = rows(source / 'extensions.csv')
target_ext_rows = rows(result / 'target-extensions.csv')
source_ext = keyset(source_ext_rows, ['extension', 'schema'])
target_ext = keyset(target_ext_rows, ['extension', 'schema'])
if source_ext != target_ext:
    errors.append({'check': 'extensions', 'source_only': sorted(source_ext - target_ext), 'target_only': sorted(target_ext - source_ext)})

source_versions = {(r.get('extension','').strip(), r.get('schema','').strip()): r.get('version','').strip() for r in source_ext_rows}
target_versions = {(r.get('extension','').strip(), r.get('schema','').strip()): r.get('version','').strip() for r in target_ext_rows}
for key in sorted(set(source_versions) & set(target_versions)):
    if source_versions[key] != target_versions[key]:
        warnings.append({'check': 'extension_version_difference', 'extension': key, 'source': source_versions[key], 'target': target_versions[key]})

sequence_fields = ['schema','sequence_name','last_value','start_value','increment_by','max_value','cycle','cache_size']
source_sequences = keyset(rows(source / 'sequences.csv'), sequence_fields)
target_sequences = keyset(rows(result / 'target-sequences.csv'), sequence_fields)
if source_sequences != target_sequences:
    errors.append({'check': 'sequences', 'source_only': sorted(source_sequences - target_sequences), 'target_only': sorted(target_sequences - source_sequences)})

summary = {
    'status': 'PASSED' if not errors else 'FAILED',
    'errors': errors,
    'warnings': warnings,
    'counts': {
        'schemas': len(target_schemas),
        'tables': len(target_tables),
        'extensions': len(target_ext),
        'sequences': len(target_sequences),
    },
}

(result / 'validation-comparison.json').write_text(json.dumps(summary, indent=2, default=list) + '\n')
print(json.dumps(summary, indent=2, default=list))

if errors:
    raise SystemExit(1)
PY

unset PGPASSWORD

FAILURE_STAGE="validation-complete"
FINAL_STATUS="PASSED"
echo "RESTORE_AND_VALIDATION_STATUS=PASSED"
EOF

    SCRIPT_CONTENT="$(cat "$GUEST_SCRIPT")"

    section "Submitting asynchronous restore managed Run Command"

    if az vm run-command show -g "$RG_MIGRATION" --vm-name "$VM_NAME" --run-command-name "$RESTORE_RUN_COMMAND" --output none >/dev/null 2>&1; then
        az vm run-command update \
            --resource-group "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$RESTORE_RUN_COMMAND" \
            --location "$LOCATION" \
            --async-execution true \
            --script "$SCRIPT_CONTENT" \
            --timeout-in-seconds 7200 \
            --no-wait \
            --only-show-errors \
            --output none
        RUN_COMMAND_ACTION="updated-and-submitted"
    else
        az vm run-command create \
            --resource-group "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$RESTORE_RUN_COMMAND" \
            --location "$LOCATION" \
            --async-execution true \
            --script "$SCRIPT_CONTENT" \
            --timeout-in-seconds 7200 \
            --no-wait \
            --only-show-errors \
            --output none
        RUN_COMMAND_ACTION="created-and-submitted"
    fi

    cat > "$CONFIG_FILE" <<EOF
RESTORE_RUN_COMMAND=$RESTORE_RUN_COMMAND
RESTORE_RESULT_PREFIX=$RESULT_PREFIX
RESTORE_VM_RESOURCE_GROUP=$RG_MIGRATION
RESTORE_VM_NAME=$VM_NAME
TEMP_CONTRIBUTOR_CREATED=$TEMP_CONTRIBUTOR_CREATED
TEMP_CONTRIBUTOR_ASSIGNMENT_ID=$TEMP_CONTRIBUTOR_ASSIGNMENT_ID
TEMP_CONTRIBUTOR_SCOPE=$CONTAINER_SCOPE
EOF
    chmod 600 "$CONFIG_FILE"

    echo "RUN_COMMAND_ACTION=$RUN_COMMAND_ACTION"
    echo "RUN_COMMAND_NAME=$RESTORE_RUN_COMMAND"
    echo "RESULT_PREFIX=$RESULT_PREFIX"
    echo "TEMP_CONTRIBUTOR_CREATED=$TEMP_CONTRIBUTOR_CREATED"
    echo "RESTORE_DECISION=SUBMITTED"
    echo
    echo "Azure will continue restore and validation independently of Cloud Shell."
    echo
    echo "************************************************************"
    echo "POSTGRESQL INITIAL SEED RESTORE VALIDATION SUBMITTED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Restore submission log: $LOG"
echo "Restore state file: $CONFIG_FILE"
