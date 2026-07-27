-- ProjectPulse Modules 021 and 026
-- Additive SELL customer synchronization linkage and audit history.
-- This source migration is not applied by this commit.

BEGIN;

CREATE TABLE IF NOT EXISTS customer_directory_source_links (
    source_system VARCHAR(50) NOT NULL,
    source_record_id VARCHAR(200) NOT NULL,
    client_id UUID NOT NULL REFERENCES clients(client_id) ON DELETE CASCADE,
    source_record_type VARCHAR(50) NOT NULL DEFAULT 'organization',
    source_name VARCHAR(255) NOT NULL,
    source_customer_status VARCHAR(80) NOT NULL DEFAULT '',
    source_prospect_status VARCHAR(80) NOT NULL DEFAULT '',
    source_updated_at TIMESTAMPTZ,
    source_payload_hash VARCHAR(64) NOT NULL DEFAULT '',
    last_synced_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID REFERENCES app_users(user_id),
    updated_by UUID REFERENCES app_users(user_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (source_system, source_record_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_directory_source_links_client
    ON customer_directory_source_links(source_system, client_id);

CREATE INDEX IF NOT EXISTS ix_customer_directory_source_links_sync
    ON customer_directory_source_links(source_system, last_synced_at DESC);

CREATE TABLE IF NOT EXISTS customer_directory_sync_runs (
    customer_directory_sync_run_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_key TEXT NOT NULL REFERENCES crm_integration_providers(provider_key),
    source_system VARCHAR(50) NOT NULL,
    requested_by UUID NOT NULL REFERENCES app_users(user_id),
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ,
    status VARCHAR(50) NOT NULL DEFAULT 'started',
    page_requested INTEGER NOT NULL DEFAULT 1,
    page_size INTEGER NOT NULL DEFAULT 100,
    search_text VARCHAR(200) NOT NULL DEFAULT '',
    source_records_seen INTEGER NOT NULL DEFAULT 0,
    organizations_seen INTEGER NOT NULL DEFAULT 0,
    imported_count INTEGER NOT NULL DEFAULT 0,
    updated_count INTEGER NOT NULL DEFAULT 0,
    linked_count INTEGER NOT NULL DEFAULT 0,
    skipped_count INTEGER NOT NULL DEFAULT 0,
    failed_count INTEGER NOT NULL DEFAULT 0,
    error_code VARCHAR(100) NOT NULL DEFAULT '',
    message TEXT NOT NULL DEFAULT '',
    evidence_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT ck_customer_directory_sync_runs_status
        CHECK (status IN ('started', 'previewed', 'completed', 'completed_with_failures', 'failed')),
    CONSTRAINT ck_customer_directory_sync_runs_counts
        CHECK (
            page_requested > 0
            AND page_size BETWEEN 1 AND 100
            AND source_records_seen >= 0
            AND organizations_seen >= 0
            AND imported_count >= 0
            AND updated_count >= 0
            AND linked_count >= 0
            AND skipped_count >= 0
            AND failed_count >= 0
        )
);

CREATE INDEX IF NOT EXISTS ix_customer_directory_sync_runs_provider
    ON customer_directory_sync_runs(provider_key, started_at DESC);

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '049_module_021_sell_customer_sync',
    'Module 021 customer-directory synchronization from the Module 026 SELL connection with source linkage and audit history',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

GRANT SELECT, INSERT, UPDATE ON TABLE customer_directory_source_links TO "ptp_app";
GRANT SELECT, INSERT, UPDATE ON TABLE customer_directory_sync_runs TO "ptp_app";

COMMIT;
