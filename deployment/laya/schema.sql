-- Additive, transaction-safe and re-runnable. Never changes document categories.
BEGIN;
SELECT pg_advisory_xact_lock(6404212026);
CREATE TABLE IF NOT EXISTS celar_laya_settings (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    enabled boolean NOT NULL DEFAULT false,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    changed_by uuid,
    changed_at timestamptz NOT NULL DEFAULT now()
);
INSERT INTO celar_laya_settings(singleton) VALUES(true) ON CONFLICT DO NOTHING;
CREATE TABLE IF NOT EXISTS celar_laya_settings_audit (
    version bigint PRIMARY KEY,
    enabled boolean NOT NULL,
    actor_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS celar_laya_decisions (
    decision_id uuid PRIMARY KEY,
    document_id uuid NOT NULL,
    source_sha256 text NOT NULL CHECK (source_sha256 ~ '^[0-9a-f]{64}$'),
    request_id uuid NOT NULL,
    created_by uuid NOT NULL,
    policy_version bigint NOT NULL,
    evidence jsonb NOT NULL CHECK (jsonb_typeof(evidence) = 'object'),
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE(document_id, created_by, request_id)
);
CREATE INDEX IF NOT EXISTS celar_laya_decisions_document_time
    ON celar_laya_decisions(document_id, created_at DESC);
CREATE TABLE IF NOT EXISTS celar_laya_reviews (
    decision_id uuid PRIMARY KEY REFERENCES celar_laya_decisions(decision_id),
    reviewed_label text NOT NULL CHECK (reviewed_label IN ('sow','invoice','purchase_order','other')),
    reviewed_by uuid NOT NULL,
    reviewed_at timestamptz NOT NULL DEFAULT now()
);
CREATE OR REPLACE FUNCTION celar_laya_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'Laya audit records are append-only';
END;
$$;
DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['celar_laya_settings_audit','celar_laya_decisions','celar_laya_reviews'] LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname=t||'_append_only' AND tgrelid=t::regclass) THEN
            EXECUTE format('CREATE TRIGGER %I BEFORE UPDATE OR DELETE ON %I FOR EACH ROW EXECUTE FUNCTION celar_laya_append_only()',t||'_append_only',t);
        END IF;
    END LOOP;
END;
$$;
COMMIT;
