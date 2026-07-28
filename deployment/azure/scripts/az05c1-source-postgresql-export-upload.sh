#!/usr/bin/env bash
set -Eeuo pipefail

# Run this script on the Oracle Linux source host.
# It creates an initial PostgreSQL 13 migration seed and uploads it to
# the private Azure Blob container by using Microsoft Entra authentication.

PRODUCT_NAME="Project Health Dashboard"
MIGRATION_PURPOSE="initial-test-seed"

TENANT_ID="535941da-da72-4a8b-8378-983a54bec342"
STORAGE_ACCOUNT="stphdtest7825cc"
STORAGE_CONTAINER="database-exports"

SOURCE_REPOSITORY="/opt/project-time-platform/app/project-time-platform-022"
SOURCE_DATABASE="${SOURCE_DATABASE:-}"

BASE_DIR="$HOME/project-health-dashboard-migration"
EXPORT_ROOT="$BASE_DIR/exports"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
EXPORT_DIR="$EXPORT_ROOT/postgresql13-$STAMP"
REMOTE_PREFIX="source-postgresql13/$STAMP"
DESTINATION="https://${STORAGE_ACCOUNT}.blob.core.windows.net/${STORAGE_CONTAINER}/${REMOTE_PREFIX}"

umask 077
mkdir -p "$EXPORT_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

require_command() {
    local command_name="$1"

    if ! command -v "$command_name" >/dev/null 2>&1; then
        echo "ERROR: Required command is unavailable: $command_name"
        exit 1
    fi
}

install_azcopy_if_needed() {
    if command -v azcopy >/dev/null 2>&1; then
        echo "Existing AzCopy found: $(command -v azcopy)"
        azcopy --version
        return
    fi

    local temp_dir archive binary
    temp_dir="$(mktemp -d)"
    archive="$temp_dir/azcopy.tar.gz"

    echo "Downloading AzCopy v10 from the Microsoft distribution endpoint."

    curl -fsSL \
        "https://aka.ms/downloadazcopy-v10-linux" \
        -o "$archive"

    tar -xzf "$archive" -C "$temp_dir"

    binary="$(find "$temp_dir" -type f -name azcopy -perm -u+x | head -n 1)"

    if [ -z "$binary" ]; then
        echo "ERROR: AzCopy executable was not found in the archive."
        rm -rf "$temp_dir"
        exit 1
    fi

    mkdir -p "$HOME/.local/bin"
    install -m 0755 "$binary" "$HOME/.local/bin/azcopy"
    rm -rf "$temp_dir"

    export PATH="$HOME/.local/bin:$PATH"

    echo "Installed AzCopy: $HOME/.local/bin/azcopy"
    azcopy --version
}

select_source_database() {
    local candidates=()
    local likely=()

    mapfile -t candidates < <(
        "${PSQL[@]}" \
            --dbname=postgres \
            --tuples-only \
            --no-align \
            --command="
                SELECT datname
                FROM pg_database
                WHERE datallowconn
                  AND NOT datistemplate
                  AND datname <> 'postgres'
                ORDER BY pg_database_size(datname) DESC;
            "
    )

    if [ -n "$SOURCE_DATABASE" ]; then
        if ! printf '%s\n' "${candidates[@]}" | grep -Fxq "$SOURCE_DATABASE"; then
            echo "ERROR: Requested SOURCE_DATABASE does not exist:"
            echo "$SOURCE_DATABASE"
            echo
            echo "Detected databases:"
            printf '  %s\n' "${candidates[@]}"
            exit 1
        fi

        return
    fi

    if [ "${#candidates[@]}" -eq 1 ]; then
        SOURCE_DATABASE="${candidates[0]}"
        return
    fi

    mapfile -t likely < <(
        printf '%s\n' "${candidates[@]}" |
            grep -Ei 'project|pulse|time|health' ||
            true
    )

    if [ "${#likely[@]}" -eq 1 ]; then
        SOURCE_DATABASE="${likely[0]}"
        return
    fi

    echo "ERROR: Source database could not be selected safely."
    echo
    echo "Detected non-template databases:"
    printf '  %s\n' "${candidates[@]}"
    echo
    echo "Rerun with the database name explicitly supplied, for example:"
    echo "SOURCE_DATABASE=<database-name> $0"
    exit 1
}

section "AZ-05C1 - Source PostgreSQL Export and Azure Upload"

echo "Product: $PRODUCT_NAME"
echo "Purpose: $MIGRATION_PURPOSE"
echo "Source host: $(hostname -f 2>/dev/null || hostname)"
echo "Export directory: $EXPORT_DIR"
echo "Azure destination: $DESTINATION"
echo "TIME=$(date -u -Is)"

section "Validating source PostgreSQL tools"

require_command sudo
require_command curl
require_command tar
require_command sha256sum
require_command python3
require_command git

PSQL=(sudo -u postgres psql -X -v ON_ERROR_STOP=1)
PG_DUMP=(sudo -u postgres pg_dump)
PG_DUMPALL=(sudo -u postgres pg_dumpall)

SOURCE_SERVER_VERSION="$(
    "${PSQL[@]}" \
        --dbname=postgres \
        --tuples-only \
        --no-align \
        --command='SHOW server_version;'
)"

PG_DUMP_VERSION="$("${PG_DUMP[@]}" --version)"
PG_RESTORE_BIN="$(command -v pg_restore || true)"

if [ -z "$PG_RESTORE_BIN" ]; then
    echo "ERROR: pg_restore is required to validate the archive."
    exit 1
fi

echo "Source PostgreSQL server: $SOURCE_SERVER_VERSION"
echo "Dump utility: $PG_DUMP_VERSION"
echo "Restore utility: $($PG_RESTORE_BIN --version)"

section "Selecting source application database"

select_source_database

echo "Selected database: $SOURCE_DATABASE"

DATABASE_EXISTS="$(
    "${PSQL[@]}" \
        --dbname=postgres \
        --tuples-only \
        --no-align \
        --command="SELECT count(*) FROM pg_database WHERE datname = '$SOURCE_DATABASE';"
)"

if [ "$DATABASE_EXISTS" != "1" ]; then
    echo "ERROR: Database validation failed: $SOURCE_DATABASE"
    exit 1
fi

DUMP_FILE="$EXPORT_DIR/${SOURCE_DATABASE}-pg13-${STAMP}.dump"
DUMP_LOG="$EXPORT_DIR/${SOURCE_DATABASE}-pg13-${STAMP}.dump.log"
TOC_FILE="$EXPORT_DIR/${SOURCE_DATABASE}-pg13-${STAMP}.toc.txt"
GLOBALS_FILE="$EXPORT_DIR/postgresql-globals-no-passwords-${STAMP}.sql"

section "Capturing source database inventory"

printf '%s\n' "$SOURCE_SERVER_VERSION" > "$EXPORT_DIR/server-version.txt"
printf '%s\n' "$PG_DUMP_VERSION" > "$EXPORT_DIR/pg-dump-version.txt"
printf '%s\n' "$($PG_RESTORE_BIN --version)" > "$EXPORT_DIR/pg-restore-version.txt"

"${PSQL[@]}" \
    --dbname="$SOURCE_DATABASE" \
    --csv \
    --command="
        SELECT
            current_database() AS database_name,
            pg_database_size(current_database()) AS size_bytes,
            pg_size_pretty(pg_database_size(current_database())) AS size_pretty,
            pg_encoding_to_char(encoding) AS encoding,
            datcollate AS collation,
            datctype AS ctype,
            pg_get_userbyid(datdba) AS owner
        FROM pg_database
        WHERE datname = current_database();
    " > "$EXPORT_DIR/database-metadata.csv"

"${PSQL[@]}" \
    --dbname="$SOURCE_DATABASE" \
    --csv \
    --command="
        SELECT
            extname AS extension,
            extversion AS version,
            n.nspname AS schema
        FROM pg_extension e
        JOIN pg_namespace n ON n.oid = e.extnamespace
        ORDER BY extname;
    " > "$EXPORT_DIR/extensions.csv"

"${PSQL[@]}" \
    --dbname="$SOURCE_DATABASE" \
    --csv \
    --command="
        SELECT
            nspname AS schema,
            pg_get_userbyid(nspowner) AS owner
        FROM pg_namespace
        WHERE nspname NOT LIKE 'pg_%'
          AND nspname <> 'information_schema'
        ORDER BY nspname;
    " > "$EXPORT_DIR/schemas.csv"

"${PSQL[@]}" \
    --dbname="$SOURCE_DATABASE" \
    --csv \
    --command="
        SELECT
            n.nspname AS schema,
            c.relname AS table_name,
            c.reltuples::bigint AS estimated_rows,
            pg_total_relation_size(c.oid) AS total_bytes,
            pg_size_pretty(pg_total_relation_size(c.oid)) AS total_size
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p')
          AND n.nspname NOT LIKE 'pg_%'
          AND n.nspname <> 'information_schema'
        ORDER BY n.nspname, c.relname;
    " > "$EXPORT_DIR/tables.csv"

{
    echo 'schema,table,row_count'

    "${PSQL[@]}" \
        --dbname="$SOURCE_DATABASE" \
        --csv \
        --tuples-only <<'SQL'
SELECT format(
    'SELECT %L AS schema, %L AS table, count(*) AS row_count FROM %I.%I;',
    n.nspname,
    c.relname,
    n.nspname,
    c.relname
)
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p')
  AND n.nspname NOT LIKE 'pg_%'
  AND n.nspname <> 'information_schema'
ORDER BY n.nspname, c.relname;
\gexec
SQL
} > "$EXPORT_DIR/row-counts.csv"

"${PSQL[@]}" \
    --dbname="$SOURCE_DATABASE" \
    --csv \
    --command="
        SELECT
            schemaname AS schema,
            sequencename AS sequence_name,
            last_value,
            start_value,
            increment_by,
            max_value,
            cycle,
            cache_size
        FROM pg_sequences
        WHERE schemaname NOT LIKE 'pg_%'
          AND schemaname <> 'information_schema'
        ORDER BY schemaname, sequencename;
    " > "$EXPORT_DIR/sequences.csv"

section "Creating custom-format PostgreSQL archive"

"${PG_DUMP[@]}" \
    --format=custom \
    --compress=9 \
    --no-owner \
    --no-privileges \
    --verbose \
    --dbname="$SOURCE_DATABASE" \
    > "$DUMP_FILE" \
    2> "$DUMP_LOG"

if [ ! -s "$DUMP_FILE" ]; then
    echo "ERROR: PostgreSQL dump is empty."
    exit 1
fi

"$PG_RESTORE_BIN" --list "$DUMP_FILE" > "$TOC_FILE"

if [ ! -s "$TOC_FILE" ]; then
    echo "ERROR: pg_restore could not produce a table of contents."
    exit 1
fi

echo "Archive created: $DUMP_FILE"
echo "Archive size: $(du -h "$DUMP_FILE" | awk '{print $1}')"

section "Exporting global objects without role passwords"

if "${PG_DUMPALL[@]}" --help 2>&1 | grep -q -- '--no-role-passwords'; then
    "${PG_DUMPALL[@]}" \
        --globals-only \
        --no-role-passwords \
        > "$GLOBALS_FILE"
else
    echo "WARNING: pg_dumpall does not support --no-role-passwords."
    echo "Global role export was skipped to prevent password hashes"
    echo "from entering the migration package."

    cat > "$GLOBALS_FILE" <<'EOF'
-- Global role export intentionally skipped because this pg_dumpall
-- version does not support --no-role-passwords.
EOF
fi

section "Recording source-code checkpoint metadata"

SOURCE_GIT_HEAD="unknown"
SOURCE_GIT_BRANCH="unknown"
SOURCE_GIT_DIRTY_COUNT="unknown"

if [ -d "$SOURCE_REPOSITORY/.git" ]; then
    SOURCE_GIT_HEAD="$(git -C "$SOURCE_REPOSITORY" rev-parse HEAD 2>/dev/null || echo unknown)"
    SOURCE_GIT_BRANCH="$(git -C "$SOURCE_REPOSITORY" branch --show-current 2>/dev/null || echo unknown)"
    SOURCE_GIT_DIRTY_COUNT="$(git -C "$SOURCE_REPOSITORY" status --porcelain 2>/dev/null | wc -l | tr -d ' ')"
fi

DUMP_SHA256="$(sha256sum "$DUMP_FILE" | awk '{print $1}')"
DUMP_SIZE_BYTES="$(stat -c '%s' "$DUMP_FILE")"

python3 - \
    "$EXPORT_DIR/manifest.json" \
    "$PRODUCT_NAME" \
    "$MIGRATION_PURPOSE" \
    "$STAMP" \
    "$(hostname -f 2>/dev/null || hostname)" \
    "$SOURCE_DATABASE" \
    "$SOURCE_SERVER_VERSION" \
    "$PG_DUMP_VERSION" \
    "$(basename "$DUMP_FILE")" \
    "$DUMP_SHA256" \
    "$DUMP_SIZE_BYTES" \
    "$SOURCE_GIT_HEAD" \
    "$SOURCE_GIT_BRANCH" \
    "$SOURCE_GIT_DIRTY_COUNT" \
    "$STORAGE_ACCOUNT" \
    "$STORAGE_CONTAINER" \
    "$REMOTE_PREFIX" <<'PY'
import json
import sys
from pathlib import Path

(
    manifest_path,
    product,
    purpose,
    timestamp,
    source_host,
    database,
    server_version,
    pg_dump_version,
    dump_file,
    dump_sha256,
    dump_size_bytes,
    git_head,
    git_branch,
    git_dirty_count,
    storage_account,
    storage_container,
    remote_prefix,
) = sys.argv[1:]

manifest = {
    "product": product,
    "purpose": purpose,
    "timestamp_utc": timestamp,
    "source": {
        "host": source_host,
        "database": database,
        "postgresql_version": server_version,
        "pg_dump_version": pg_dump_version,
        "git_head": git_head,
        "git_branch": git_branch,
        "git_dirty_file_count": git_dirty_count,
    },
    "archive": {
        "filename": dump_file,
        "sha256": dump_sha256,
        "size_bytes": int(dump_size_bytes),
        "format": "PostgreSQL custom archive",
        "owner_commands_included": False,
        "privilege_commands_included": False,
    },
    "azure_destination": {
        "storage_account": storage_account,
        "container": storage_container,
        "prefix": remote_prefix,
    },
    "security": {
        "database_passwords_in_manifest": False,
        "role_passwords_in_globals_export": False,
        "local_file_mode": "owner-only by umask 077",
    },
}

Path(manifest_path).write_text(
    json.dumps(manifest, indent=2, sort_keys=True) + "\n"
)
PY

section "Generating and validating package checksums"

(
    cd "$EXPORT_DIR"

    find . \
        -maxdepth 1 \
        -type f \
        ! -name SHA256SUMS \
        -printf '%P\0' |
        sort -z |
        xargs -0 sha256sum > SHA256SUMS

    sha256sum --check SHA256SUMS
)

section "Installing and authorizing AzCopy"

install_azcopy_if_needed

export AZCOPY_AUTO_LOGIN_TYPE=DEVICE
export AZCOPY_TENANT_ID="$TENANT_ID"

cat <<EOF
AzCopy will display a device sign-in code.
Sign in using the Azure account that has Storage Blob Data Owner
on storage account $STORAGE_ACCOUNT.
EOF

azcopy list \
    "https://${STORAGE_ACCOUNT}.blob.core.windows.net/${STORAGE_CONTAINER}"

section "Uploading migration package to Azure Blob Storage"

azcopy copy \
    "${EXPORT_DIR}/*" \
    "$DESTINATION" \
    --recursive=true \
    --overwrite=false \
    --check-length=true

section "Verifying uploaded Blob objects"

azcopy list "$DESTINATION"

section "AZ-05C1 completed successfully"

echo "Source database: $SOURCE_DATABASE"
echo "Source PostgreSQL version: $SOURCE_SERVER_VERSION"
echo "Dump file: $DUMP_FILE"
echo "Dump SHA-256: $DUMP_SHA256"
echo "Local package: $EXPORT_DIR"
echo "Azure destination: $DESTINATION"
echo
echo "This is an initial test-migration seed."
echo "The source environment remains active."
echo "A final cutover export will be required after write freeze."
echo
echo "Do not delete the local package until the Azure restore"
echo "and validation phases are complete."
echo
echo "************************************************************"
echo "SOURCE POSTGRESQL EXPORT UPLOADED"
echo "************************************************************"
