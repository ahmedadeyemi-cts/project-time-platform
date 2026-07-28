#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATION="$ROOT/database/migrations/049_module_021_sell_customer_sync.sql"
ROLLBACK="$ROOT/database/rollback/049_module_021_sell_customer_sync_rollback.sql"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-postgresql://postgres:postgres@127.0.0.1:5432/projectpulse_test}"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ -f "$MIGRATION" ]] || fail "Migration 049 source is missing."
[[ -f "$ROLLBACK" ]] || fail "Migration 049 rollback source is missing."
command -v psql >/dev/null || fail "psql is required."

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ptp_app') THEN
        CREATE ROLE ptp_app;
    END IF;
END $$;

DROP TABLE IF EXISTS customer_directory_sync_runs CASCADE;
DROP TABLE IF EXISTS customer_directory_source_links CASCADE;
DROP TABLE IF EXISTS crm_integration_providers CASCADE;
DROP TABLE IF EXISTS clients CASCADE;
DROP TABLE IF EXISTS app_users CASCADE;
DROP TABLE IF EXISTS schema_migrations CASCADE;

CREATE TABLE schema_migrations (
    migration_id VARCHAR(100) PRIMARY KEY,
    description TEXT NOT NULL,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE app_users (
    user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE clients (
    client_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    client_name VARCHAR(255) NOT NULL UNIQUE,
    client_code VARCHAR(100) UNIQUE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE crm_integration_providers (
    provider_key TEXT PRIMARY KEY,
    provider_name VARCHAR(150) NOT NULL,
    auth_model VARCHAR(30) NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT FALSE
);

INSERT INTO crm_integration_providers (provider_key, provider_name, auth_model, is_enabled)
VALUES ('zendesk_sell', 'SELL (Zendesk Sell)', 'oauth2', TRUE);
SQL

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$MIGRATION"
psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$MIGRATION"

eval "$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 <<'SQL'
SELECT 'MIGRATION_COUNT=' || quote_literal(COUNT(*))
FROM schema_migrations
WHERE migration_id = '049_module_021_sell_customer_sync';
SELECT 'LINK_TABLE=' || quote_literal(to_regclass('public.customer_directory_source_links') IS NOT NULL);
SELECT 'RUN_TABLE=' || quote_literal(to_regclass('public.customer_directory_sync_runs') IS NOT NULL);
SELECT 'CLIENT_LINK_INDEX=' || quote_literal(to_regclass('public.ux_customer_directory_source_links_client') IS NOT NULL);
SELECT 'RUN_INDEX=' || quote_literal(to_regclass('public.ix_customer_directory_sync_runs_provider') IS NOT NULL);
SQL
)"

[[ "$MIGRATION_COUNT" == 1 ]] || fail "Migration 049 registration is not idempotent."
[[ "$LINK_TABLE" == true ]] || fail "Customer source-link table was not created."
[[ "$RUN_TABLE" == true ]] || fail "Customer sync-run table was not created."
[[ "$CLIENT_LINK_INDEX" == true ]] || fail "Customer source-link uniqueness index was not created."
[[ "$RUN_INDEX" == true ]] || fail "Sync-run provider index was not created."

echo "MODULE_021_SELL_CUSTOMER_SYNC_049_APPLY=PASS"

read -r ACTOR_ID CLIENT_ID <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At -F ' ' --set=ON_ERROR_STOP=1 <<'SQL'
WITH actor AS (
    INSERT INTO app_users (email, display_name)
    VALUES ('module021.actor@ussignal.local', 'Module 021 Actor')
    RETURNING user_id
), customer AS (
    INSERT INTO clients (client_name, client_code)
    VALUES ('SELL Linked Customer', 'SELLLINK')
    RETURNING client_id
)
SELECT actor.user_id, customer.client_id
FROM actor CROSS JOIN customer;
SQL
)"

[[ "$ACTOR_ID" =~ ^[0-9a-f-]{36}$ ]] || fail "Actor seed failed."
[[ "$CLIENT_ID" =~ ^[0-9a-f-]{36}$ ]] || fail "Customer seed failed."

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
  --set=actor_id="$ACTOR_ID" \
  --set=client_id="$CLIENT_ID" <<'SQL'
INSERT INTO customer_directory_source_links (
    source_system,
    source_record_id,
    client_id,
    source_record_type,
    source_name,
    source_customer_status,
    source_prospect_status,
    source_payload_hash,
    created_by,
    updated_by
)
VALUES (
    'SELL',
    '10001',
    :'client_id'::uuid,
    'organization',
    'SELL Linked Customer',
    'current',
    '',
    repeat('a', 64),
    :'actor_id'::uuid,
    :'actor_id'::uuid
);

INSERT INTO customer_directory_sync_runs (
    provider_key,
    source_system,
    requested_by,
    status,
    page_requested,
    page_size,
    source_records_seen,
    organizations_seen,
    imported_count,
    evidence_json
)
VALUES (
    'zendesk_sell',
    'SELL',
    :'actor_id'::uuid,
    'completed',
    1,
    100,
    1,
    1,
    1,
    '{"secretValuesReturned":false,"localContactsOverwritten":false}'::jsonb
);
SQL

eval "$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 <<'SQL'
SELECT 'LINK_COUNT=' || quote_literal(COUNT(*)) FROM customer_directory_source_links;
SELECT 'RUN_COUNT=' || quote_literal(COUNT(*)) FROM customer_directory_sync_runs;
SELECT 'NO_SECRET_EVIDENCE=' || quote_literal(NOT EXISTS (
    SELECT 1
    FROM customer_directory_sync_runs
    WHERE evidence_json ? 'clientSecret'
       OR evidence_json ? 'apiKey'
       OR evidence_json ? 'accessToken'
));
SELECT 'LOCAL_CONTACT_GUARD=' || quote_literal((evidence_json ->> 'localContactsOverwritten')::boolean = false)
FROM customer_directory_sync_runs
LIMIT 1;
SQL
)"

[[ "$LINK_COUNT" == 1 ]] || fail "Source link evidence was not stored."
[[ "$RUN_COUNT" == 1 ]] || fail "Sync run evidence was not stored."
[[ "$NO_SECRET_EVIDENCE" == true ]] || fail "Sync evidence exposed credential fields."
[[ "$LOCAL_CONTACT_GUARD" == true ]] || fail "Sync evidence did not preserve the local-contact boundary."

echo "MODULE_021_SELL_CUSTOMER_SYNC_049_LINKAGE=PASS"

if psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
  --set=actor_id="$ACTOR_ID" \
  --set=client_id="$CLIENT_ID" <<'SQL'
INSERT INTO customer_directory_source_links (
    source_system,
    source_record_id,
    client_id,
    source_name,
    created_by,
    updated_by
)
VALUES (
    'SELL',
    '10001',
    :'client_id'::uuid,
    'Duplicate SELL Link',
    :'actor_id'::uuid,
    :'actor_id'::uuid
);
SQL
then
  fail "Migration 049 allowed a duplicate SELL source record link."
fi

echo "MODULE_021_SELL_CUSTOMER_SYNC_049_UNIQUENESS=PASS"

if psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$ROLLBACK"
then
  fail "Rollback succeeded despite customer source links and sync evidence."
fi

echo "MODULE_021_SELL_CUSTOMER_SYNC_049_rollback_guard_verified=PASS"

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
TRUNCATE TABLE customer_directory_sync_runs;
TRUNCATE TABLE customer_directory_source_links;
SQL

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$ROLLBACK"

eval "$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 <<'SQL'
SELECT 'LINK_REMOVED=' || quote_literal(to_regclass('public.customer_directory_source_links') IS NULL);
SELECT 'RUN_REMOVED=' || quote_literal(to_regclass('public.customer_directory_sync_runs') IS NULL);
SELECT 'REGISTRATION_REMOVED=' || quote_literal(NOT EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id = '049_module_021_sell_customer_sync'
));
SQL
)"

[[ "$LINK_REMOVED" == true ]] || fail "Rollback did not remove the empty source-link table."
[[ "$RUN_REMOVED" == true ]] || fail "Rollback did not remove the empty sync-run table."
[[ "$REGISTRATION_REMOVED" == true ]] || fail "Rollback did not remove migration registration."

echo "MODULE_021_SELL_CUSTOMER_SYNC_049_TEST=PASS"
