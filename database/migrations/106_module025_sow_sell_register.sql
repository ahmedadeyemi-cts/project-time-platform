-- Module 025: one SOW identity, immutable generations/releases and SELL evidence.
-- Additive only. This migration does not enable any external integration.
BEGIN;
SELECT pg_advisory_xact_lock(250106);
DO $$ BEGIN
    IF to_regclass('public.module025_sow_gsd_engagements') IS NULL
       OR to_regclass('public.module025_sow_gsd_events') IS NULL
       OR to_regclass('public.module025_sow_gsd_phases') IS NULL THEN
        RAISE EXCEPTION 'Module 025 workspace migration 099 must be applied first';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS module025_sow_gsd_generation_snapshots (
    event_id bigint PRIMARY KEY REFERENCES module025_sow_gsd_events(event_id),
    engagement_id uuid NOT NULL REFERENCES module025_sow_gsd_engagements(engagement_id),
    source_json jsonb NOT NULL,
    source_sha256 varchar(64) NOT NULL CHECK (source_sha256 ~ '^[a-f0-9]{64}$'),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS module025_sow_gsd_versions (
    version_id uuid PRIMARY KEY,
    engagement_id uuid NOT NULL REFERENCES module025_sow_gsd_engagements(engagement_id),
    version_number integer NOT NULL CHECK (version_number > 0),
    source_revision integer NOT NULL CHECK (source_revision > 0),
    content_sha256 varchar(64) NOT NULL CHECK (content_sha256 ~ '^[a-f0-9]{64}$'),
    source_json jsonb NOT NULL,
    sow_content bytea NOT NULL CHECK (octet_length(sow_content) BETWEEN 1 AND 16777216),
    gsd_content bytea NOT NULL CHECK (octet_length(gsd_content) BETWEEN 1 AND 16777216),
    sow_sha256 text GENERATED ALWAYS AS (encode(sha256(sow_content), 'hex')) STORED,
    gsd_sha256 text GENERATED ALWAYS AS (encode(sha256(gsd_content), 'hex')) STORED,
    actor_user_id uuid NOT NULL REFERENCES app_users(user_id),
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (engagement_id, version_number),
    UNIQUE (engagement_id, source_revision),
    UNIQUE (version_id, engagement_id)
);

-- A download is not a new SOW or a new version. Retain first issuance only;
-- an HTTP response is not proof the user finished saving a file locally.
CREATE TABLE IF NOT EXISTS module025_sow_gsd_artifact_issuance (
    version_id uuid NOT NULL REFERENCES module025_sow_gsd_versions(version_id),
    artifact_kind varchar(3) NOT NULL CHECK (artifact_kind IN ('sow','gsd')),
    actor_user_id uuid NOT NULL REFERENCES app_users(user_id),
    first_served_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (version_id, artifact_kind)
);

CREATE TABLE IF NOT EXISTS module025_sow_sell_submissions (
    submission_id uuid PRIMARY KEY,
    engagement_id uuid NOT NULL REFERENCES module025_sow_gsd_engagements(engagement_id),
    version_id uuid NOT NULL,
    destination_key varchar(80) NOT NULL CHECK (destination_key = 'zendesk_sell'),
    runtime_environment varchar(20) NOT NULL CHECK (runtime_environment IN ('test','production')),
    actor_user_id uuid NOT NULL REFERENCES app_users(user_id),
    recipients_json jsonb NOT NULL CHECK (jsonb_typeof(recipients_json) = 'array' AND jsonb_array_length(recipients_json) = 3),
    created_at timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (version_id, engagement_id) REFERENCES module025_sow_gsd_versions(version_id, engagement_id),
    UNIQUE (version_id, destination_key, runtime_environment),
    UNIQUE (submission_id, engagement_id)
);

-- Mutable operational state is deliberately separate from immutable evidence.
CREATE TABLE IF NOT EXISTS module025_sow_sell_dispatch (
    submission_id uuid PRIMARY KEY,
    engagement_id uuid NOT NULL,
    sell_status varchar(40) NOT NULL CHECK (sell_status IN ('blocked','queued','publishing','published','failed','needs_reconciliation')),
    mail_status varchar(40) NOT NULL DEFAULT 'awaiting_sell' CHECK (mail_status IN ('awaiting_sell','queued','sending','provider_accepted','suppressed','failed','needs_reconciliation')),
    diagnostic_code varchar(160) NOT NULL DEFAULT '',
    claimed_at timestamptz NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (submission_id, engagement_id) REFERENCES module025_sow_sell_submissions(submission_id, engagement_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_module025_one_uncertain_sell_write
    ON module025_sow_sell_dispatch(engagement_id)
    WHERE sell_status IN ('publishing','needs_reconciliation');
CREATE INDEX IF NOT EXISTS ix_module025_sell_dispatch_queue
    ON module025_sow_sell_dispatch(sell_status, updated_at);

CREATE TABLE IF NOT EXISTS module025_sow_sell_links (
    engagement_id uuid NOT NULL REFERENCES module025_sow_gsd_engagements(engagement_id),
    destination_key varchar(80) NOT NULL,
    runtime_environment varchar(20) NOT NULL,
    sell_record_id varchar(200) NOT NULL CHECK (length(trim(sell_record_id)) > 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (engagement_id, destination_key, runtime_environment),
    UNIQUE (destination_key, runtime_environment, sell_record_id)
);

CREATE TABLE IF NOT EXISTS module025_sow_sell_receipts (
    submission_id uuid PRIMARY KEY REFERENCES module025_sow_sell_submissions(submission_id),
    sell_record_id varchar(200) NOT NULL CHECK (length(trim(sell_record_id)) > 0),
    sow_document_id varchar(200) NOT NULL CHECK (length(trim(sow_document_id)) > 0),
    gsd_document_id varchar(200) NOT NULL CHECK (length(trim(gsd_document_id)) > 0),
    sow_sha256 varchar(64) NOT NULL,
    gsd_sha256 varchar(64) NOT NULL,
    provider_receipt_id varchar(200) NOT NULL CHECK (length(trim(provider_receipt_id)) > 0),
    verified_at timestamptz NOT NULL DEFAULT now(),
    CHECK (sow_document_id <> gsd_document_id)
);

CREATE TABLE IF NOT EXISTS module025_sow_sell_notification_outbox (
    submission_id uuid PRIMARY KEY REFERENCES module025_sow_sell_receipts(submission_id),
    subject varchar(500) NOT NULL,
    text_body text NOT NULL,
    recipients_json jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE OR REPLACE FUNCTION module025_reject_evidence_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'Module 025 retained evidence is append-only; use a new version or archive the workspace';
END $$;

DO $triggers$
DECLARE table_name text;
BEGIN
    FOREACH table_name IN ARRAY ARRAY[
        'module025_sow_gsd_events', 'module025_sow_gsd_generation_snapshots',
        'module025_sow_gsd_versions', 'module025_sow_gsd_artifact_issuance',
        'module025_sow_sell_submissions', 'module025_sow_sell_links',
        'module025_sow_sell_receipts', 'module025_sow_sell_notification_outbox'
    ] LOOP
        EXECUTE format('DROP TRIGGER IF EXISTS module025_evidence_no_change ON %I', table_name);
        EXECUTE format('CREATE TRIGGER module025_evidence_no_change BEFORE UPDATE OR DELETE ON %I FOR EACH ROW EXECUTE FUNCTION module025_reject_evidence_mutation()', table_name);
        EXECUTE format('DROP TRIGGER IF EXISTS module025_evidence_no_truncate ON %I', table_name);
        EXECUTE format('CREATE TRIGGER module025_evidence_no_truncate BEFORE TRUNCATE ON %I FOR EACH STATEMENT EXECUTE FUNCTION module025_reject_evidence_mutation()', table_name);
    END LOOP;
END $triggers$;
DROP TRIGGER IF EXISTS module025_root_no_delete ON module025_sow_gsd_engagements;
CREATE TRIGGER module025_root_no_delete BEFORE DELETE ON module025_sow_gsd_engagements
    FOR EACH ROW EXECUTE FUNCTION module025_reject_evidence_mutation();
DROP TRIGGER IF EXISTS module025_root_no_truncate ON module025_sow_gsd_engagements;
CREATE TRIGGER module025_root_no_truncate BEFORE TRUNCATE ON module025_sow_gsd_engagements
    FOR EACH STATEMENT EXECUTE FUNCTION module025_reject_evidence_mutation();

-- Generation completion is already committed atomically with its phase writes.
-- This trigger seals that exact result, not the later editable working copy.
CREATE OR REPLACE FUNCTION module025_capture_generation_snapshot()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE snapshot jsonb;
BEGIN
    IF NEW.event_type = 'ai_generation_completed' THEN
        SELECT jsonb_build_object('engagement', to_jsonb(e), 'phases',
            COALESCE((SELECT jsonb_agg(to_jsonb(p) ORDER BY p.sort_order)
                FROM module025_sow_gsd_phases p WHERE p.engagement_id=e.engagement_id), '[]'::jsonb))
        INTO snapshot FROM module025_sow_gsd_engagements e WHERE e.engagement_id=NEW.engagement_id;
        INSERT INTO module025_sow_gsd_generation_snapshots(event_id,engagement_id,source_json,source_sha256,created_at)
        VALUES(NEW.event_id,NEW.engagement_id,snapshot,encode(sha256(convert_to(snapshot::text,'UTF8')),'hex'),NEW.created_at);
    END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS module025_generation_snapshot ON module025_sow_gsd_events;
CREATE TRIGGER module025_generation_snapshot AFTER INSERT ON module025_sow_gsd_events
    FOR EACH ROW EXECUTE FUNCTION module025_capture_generation_snapshot();

-- A success receipt must reference this version's exact two byte streams.
-- First publication fixes the remote record; later versions cannot create another.
CREATE OR REPLACE FUNCTION module025_validate_sell_receipt()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s record; v record; linked_id text;
BEGIN
    SELECT * INTO STRICT s FROM module025_sow_sell_submissions WHERE submission_id=NEW.submission_id;
    SELECT * INTO STRICT v FROM module025_sow_gsd_versions WHERE version_id=s.version_id;
    IF NEW.sow_sha256 <> v.sow_sha256 OR NEW.gsd_sha256 <> v.gsd_sha256 THEN
        RAISE EXCEPTION 'SELL receipt does not attest the submitted SOW and GSD bytes';
    END IF;
    INSERT INTO module025_sow_sell_links(engagement_id,destination_key,runtime_environment,sell_record_id)
    VALUES(s.engagement_id,s.destination_key,s.runtime_environment,NEW.sell_record_id)
    ON CONFLICT (engagement_id,destination_key,runtime_environment) DO NOTHING;
    SELECT sell_record_id INTO STRICT linked_id FROM module025_sow_sell_links
    WHERE engagement_id=s.engagement_id AND destination_key=s.destination_key AND runtime_environment=s.runtime_environment;
    IF linked_id <> NEW.sell_record_id THEN
        RAISE EXCEPTION 'A revised SOW must use its existing SELL record';
    END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS module025_sell_receipt_validation ON module025_sow_sell_receipts;
CREATE TRIGGER module025_sell_receipt_validation BEFORE INSERT ON module025_sow_sell_receipts
    FOR EACH ROW EXECUTE FUNCTION module025_validate_sell_receipt();

CREATE INDEX IF NOT EXISTS ix_module025_versions_owner_history ON module025_sow_gsd_versions(engagement_id,version_number DESC);
CREATE INDEX IF NOT EXISTS ix_module025_generated_reporting ON module025_sow_gsd_events(event_type,created_at,engagement_id);
CREATE INDEX IF NOT EXISTS ix_module025_submissions_history ON module025_sow_sell_submissions(engagement_id,created_at DESC);

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('106_module025_sow_sell_register','Immutable SOW generations, released SOW/GSD bytes, versioned SELL submission and notification evidence',now())
ON CONFLICT (migration_id) DO NOTHING;

DO $grants$
DECLARE table_name text;
BEGIN
    IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='ptp_app') THEN
        FOREACH table_name IN ARRAY ARRAY[
            'module025_sow_gsd_generation_snapshots','module025_sow_gsd_versions',
            'module025_sow_gsd_artifact_issuance','module025_sow_sell_submissions',
            'module025_sow_sell_links','module025_sow_sell_receipts','module025_sow_sell_notification_outbox'
        ] LOOP
            EXECUTE format('GRANT SELECT, INSERT ON %I TO ptp_app',table_name);
            EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON %I FROM ptp_app',table_name);
        END LOOP;
        GRANT SELECT,INSERT,UPDATE ON module025_sow_sell_dispatch TO ptp_app;
    END IF;
END $grants$;
COMMIT;
