-- Pulse migration 084
-- Ask Celar AI troubleshooting, guided defect intake, durable Module 076
-- operations, availability thresholds, deduplication, recovery, and outbox.
--
-- This migration creates only Pulse-owned data structures. It does not create
-- GitHub, Azure, Oracle, mail, AI-provider, or external-system credentials.

BEGIN;

DO $pulse084_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.app_users') IS NULL THEN
        RAISE EXCEPTION 'Migration 084 requires schema_migrations and app_users.';
    END IF;
END;
$pulse084_prerequisites$;

CREATE SEQUENCE IF NOT EXISTS module076_defect_number_sequence
    AS BIGINT START WITH 1 INCREMENT BY 1 NO CYCLE;

CREATE TABLE IF NOT EXISTS module076_defects (
    defect_id UUID PRIMARY KEY,
    defect_number VARCHAR(32) NOT NULL UNIQUE,
    title VARCHAR(180) NOT NULL CHECK (btrim(title) <> ''),
    description TEXT NOT NULL CHECK (char_length(description) BETWEEN 1 AND 8000),
    category VARCHAR(40) NOT NULL CHECK (category IN (
        'Bug','Regression','User Interface','API','Authentication',
        'Authorization','Data','Integration','Performance','Documentation',
        'Feature Gap','Availability','Security','Other'
    )),
    priority VARCHAR(20) NOT NULL CHECK (priority IN ('Critical','High','Medium','Low')),
    status VARCHAR(24) NOT NULL DEFAULT 'Open' CHECK (status IN (
        'Open','In Progress','Blocked','Resolved','Closed','Reopened'
    )),
    source_channel VARCHAR(40) NOT NULL CHECK (source_channel IN (
        'ask_celar_ai','module076','availability_monitor','github',
        'claude_github','chatgpt_github','watchdog_replay'
    )),
    environment VARCHAR(32) NOT NULL DEFAULT 'unknown',
    affected_system VARCHAR(120) NOT NULL DEFAULT 'Pulse',
    affected_module VARCHAR(20) NOT NULL DEFAULT '',
    affected_route VARCHAR(500) NOT NULL DEFAULT '',
    expected_behavior TEXT NOT NULL DEFAULT '',
    actual_behavior TEXT NOT NULL DEFAULT '',
    reproduction_steps JSONB NOT NULL DEFAULT '[]'::JSONB CHECK (jsonb_typeof(reproduction_steps)='array'),
    business_impact TEXT NOT NULL DEFAULT '',
    workaround TEXT NOT NULL DEFAULT '',
    actual_reporter_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    effective_reporter_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    reporter_display_name VARCHAR(240) NOT NULL DEFAULT 'Governed monitoring service',
    reporter_email VARCHAR(320) NOT NULL DEFAULT '',
    assignee_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    assignee_display_name VARCHAR(240) NOT NULL DEFAULT 'Ahmed Adeyemi',
    assignee_email VARCHAR(320) NOT NULL DEFAULT 'ahmed.adeyemi@ussignal.com',
    machine_created BOOLEAN NOT NULL DEFAULT FALSE,
    user_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    fingerprint CHAR(64) NULL CHECK (fingerprint IS NULL OR fingerprint ~ '^[0-9a-f]{64}$'),
    idempotency_key VARCHAR(240) NOT NULL UNIQUE,
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    release_sha CHAR(40) NULL CHECK (release_sha IS NULL OR release_sha ~ '^[0-9a-f]{40}$'),
    first_observed_at TIMESTAMPTZ NULL,
    last_observed_at TIMESTAMPTZ NULL,
    occurrence_count INTEGER NOT NULL DEFAULT 1 CHECK (occurrence_count >= 1),
    flapping_count INTEGER NOT NULL DEFAULT 0 CHECK (flapping_count >= 0),
    date_added TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    date_resolved TIMESTAMPTZ NULL,
    resolution_seconds BIGINT GENERATED ALWAYS AS (
        CASE
            WHEN date_resolved IS NULL THEN NULL
            ELSE GREATEST(0, floor(extract(epoch FROM (date_resolved - date_added)))::BIGINT)
        END
    ) STORED,
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    metadata JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(metadata)='object'),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_module076_resolution_state CHECK (
        (status IN ('Resolved','Closed') AND date_resolved IS NOT NULL)
        OR (status NOT IN ('Resolved','Closed') AND date_resolved IS NULL)
    ),
    CONSTRAINT ck_module076_machine_identity CHECK (
        machine_created = FALSE
        OR source_channel IN ('availability_monitor','watchdog_replay')
    )
);

CREATE INDEX IF NOT EXISTS ix_module076_defects_status
    ON module076_defects(status, priority, date_added DESC);
CREATE INDEX IF NOT EXISTS ix_module076_defects_assignee
    ON module076_defects(assignee_user_id, status, date_added DESC);
CREATE INDEX IF NOT EXISTS ix_module076_defects_reporter
    ON module076_defects(actual_reporter_user_id, date_added DESC);
CREATE INDEX IF NOT EXISTS ix_module076_defects_module
    ON module076_defects(affected_module, status, date_added DESC);
CREATE INDEX IF NOT EXISTS ix_module076_defects_fingerprint
    ON module076_defects(fingerprint, status, last_observed_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_module076_active_machine_fingerprint
    ON module076_defects(environment, fingerprint)
    WHERE machine_created = TRUE
      AND fingerprint IS NOT NULL
      AND status IN ('Open','In Progress','Blocked','Reopened');

CREATE TABLE IF NOT EXISTS module076_defect_comments (
    comment_id UUID PRIMARY KEY,
    defect_id UUID NOT NULL REFERENCES module076_defects(defect_id) ON DELETE RESTRICT,
    comment_type VARCHAR(32) NOT NULL CHECK (comment_type IN (
        'user','diagnostic','monitor_occurrence','recovery','resolution','system'
    )),
    body TEXT NOT NULL CHECK (char_length(body) BETWEEN 1 AND 4000),
    actual_actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    effective_actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    actor_display_name VARCHAR(240) NOT NULL DEFAULT 'Governed monitoring service',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_module076_defect_comments_defect
    ON module076_defect_comments(defect_id, created_at);

CREATE TABLE IF NOT EXISTS module076_defect_events (
    event_id UUID PRIMARY KEY,
    defect_id UUID NOT NULL REFERENCES module076_defects(defect_id) ON DELETE RESTRICT,
    event_code VARCHAR(80) NOT NULL,
    previous_status VARCHAR(24) NULL,
    next_status VARCHAR(24) NULL,
    reason TEXT NOT NULL DEFAULT '',
    actual_actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    effective_actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    event_document JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(event_document)='object'),
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_module076_defect_events_defect
    ON module076_defect_events(defect_id, occurred_at);

CREATE TABLE IF NOT EXISTS module076_defect_evidence (
    evidence_id UUID PRIMARY KEY,
    defect_id UUID NOT NULL REFERENCES module076_defects(defect_id) ON DELETE RESTRICT,
    evidence_type VARCHAR(60) NOT NULL,
    source_code VARCHAR(100) NOT NULL,
    source_reference VARCHAR(500) NOT NULL DEFAULT '',
    checksum_sha256 CHAR(64) NULL CHECK (checksum_sha256 IS NULL OR checksum_sha256 ~ '^[0-9a-f]{64}$'),
    sanitized_summary TEXT NOT NULL CHECK (char_length(sanitized_summary) BETWEEN 1 AND 8000),
    evidence_document JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(evidence_document)='object'),
    contains_secret BOOLEAN NOT NULL DEFAULT FALSE CHECK (contains_secret=FALSE),
    raw_private_content_stored BOOLEAN NOT NULL DEFAULT FALSE CHECK (raw_private_content_stored=FALSE),
    observed_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_module076_defect_evidence_defect
    ON module076_defect_evidence(defect_id, observed_at);

CREATE TABLE IF NOT EXISTS module076_intake_sessions (
    intake_session_id UUID PRIMARY KEY,
    actual_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    effective_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    conversation_id UUID NULL,
    source_channel VARCHAR(40) NOT NULL DEFAULT 'ask_celar_ai' CHECK (source_channel='ask_celar_ai'),
    status VARCHAR(24) NOT NULL DEFAULT 'draft' CHECK (status IN (
        'draft','ready_for_review','submitted','cancelled','expired'
    )),
    current_step VARCHAR(60) NOT NULL DEFAULT 'location',
    draft_document JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(draft_document)='object'),
    diagnostic_evidence JSONB NOT NULL DEFAULT '[]'::JSONB CHECK (jsonb_typeof(diagnostic_evidence)='array'),
    matched_defect_id UUID NULL REFERENCES module076_defects(defect_id) ON DELETE RESTRICT,
    submitted_defect_id UUID NULL REFERENCES module076_defects(defect_id) ON DELETE RESTRICT,
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL DEFAULT (NOW() + INTERVAL '24 hours'),
    CONSTRAINT ck_module076_intake_actual_authority CHECK (actual_user_id=effective_user_id),
    CONSTRAINT ck_module076_intake_expiry CHECK (expires_at>created_at)
);
CREATE INDEX IF NOT EXISTS ix_module076_intake_sessions_user
    ON module076_intake_sessions(actual_user_id, status, updated_at DESC);

CREATE TABLE IF NOT EXISTS module076_incident_occurrences (
    occurrence_id UUID PRIMARY KEY,
    defect_id UUID NOT NULL REFERENCES module076_defects(defect_id) ON DELETE RESTRICT,
    fingerprint CHAR(64) NOT NULL CHECK (fingerprint ~ '^[0-9a-f]{64}$'),
    component_code VARCHAR(100) NOT NULL,
    probe_code VARCHAR(120) NOT NULL,
    state VARCHAR(24) NOT NULL CHECK (state IN ('failed','degraded','recovered')),
    failure_code VARCHAR(120) NOT NULL DEFAULT '',
    sanitized_detail TEXT NOT NULL DEFAULT '',
    latency_ms INTEGER NULL CHECK (latency_ms IS NULL OR latency_ms >= 0),
    http_status INTEGER NULL CHECK (http_status IS NULL OR http_status BETWEEN 100 AND 599),
    observed_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_module076_incident_occurrences_defect
    ON module076_incident_occurrences(defect_id, observed_at);

CREATE TABLE IF NOT EXISTS module076_monitor_policies (
    policy_code VARCHAR(120) PRIMARY KEY,
    display_name VARCHAR(240) NOT NULL,
    component_code VARCHAR(100) NOT NULL,
    environment VARCHAR(32) NOT NULL DEFAULT 'test',
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    consecutive_failure_threshold INTEGER NOT NULL CHECK (consecutive_failure_threshold BETWEEN 1 AND 60),
    evaluation_window_seconds INTEGER NOT NULL CHECK (evaluation_window_seconds BETWEEN 30 AND 86400),
    consecutive_success_threshold INTEGER NOT NULL DEFAULT 3 CHECK (consecutive_success_threshold BETWEEN 1 AND 60),
    recovery_stability_seconds INTEGER NOT NULL DEFAULT 900 CHECK (recovery_stability_seconds BETWEEN 0 AND 86400),
    initial_priority VARCHAR(20) NOT NULL CHECK (initial_priority IN ('Critical','High','Medium','Low')),
    maximum_new_defects_per_hour INTEGER NOT NULL DEFAULT 10 CHECK (maximum_new_defects_per_hour BETWEEN 1 AND 100),
    flapping_window_seconds INTEGER NOT NULL DEFAULT 3600 CHECK (flapping_window_seconds BETWEEN 60 AND 86400),
    flapping_reopen_threshold INTEGER NOT NULL DEFAULT 3 CHECK (flapping_reopen_threshold BETWEEN 1 AND 20),
    machine_creation_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    updated_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS module076_probe_results (
    probe_result_id UUID PRIMARY KEY,
    policy_code VARCHAR(120) NOT NULL REFERENCES module076_monitor_policies(policy_code) ON DELETE RESTRICT,
    component_code VARCHAR(100) NOT NULL,
    probe_code VARCHAR(120) NOT NULL,
    status VARCHAR(24) NOT NULL CHECK (status IN ('healthy','degraded','failed','suppressed','unknown')),
    failure_code VARCHAR(120) NOT NULL DEFAULT '',
    sanitized_detail TEXT NOT NULL DEFAULT '',
    latency_ms INTEGER NULL CHECK (latency_ms IS NULL OR latency_ms >= 0),
    http_status INTEGER NULL CHECK (http_status IS NULL OR http_status BETWEEN 100 AND 599),
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    release_sha CHAR(40) NULL CHECK (release_sha IS NULL OR release_sha ~ '^[0-9a-f]{40}$'),
    observed_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_module076_probe_results_policy
    ON module076_probe_results(policy_code, observed_at DESC);
CREATE INDEX IF NOT EXISTS ix_module076_probe_results_component
    ON module076_probe_results(component_code, observed_at DESC);

CREATE TABLE IF NOT EXISTS module076_monitor_suppressions (
    suppression_id UUID PRIMARY KEY,
    environment VARCHAR(32) NOT NULL,
    component_code VARCHAR(100) NOT NULL,
    reason TEXT NOT NULL CHECK (char_length(reason) BETWEEN 3 AND 2000),
    owner_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    starts_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_module076_suppression_expiry CHECK (expires_at>starts_at)
);
CREATE INDEX IF NOT EXISTS ix_module076_monitor_suppressions_active
    ON module076_monitor_suppressions(environment, component_code, starts_at, expires_at);

CREATE TABLE IF NOT EXISTS module076_notification_outbox (
    outbox_id UUID PRIMARY KEY,
    defect_id UUID NOT NULL REFERENCES module076_defects(defect_id) ON DELETE RESTRICT,
    event_code VARCHAR(80) NOT NULL CHECK (event_code IN (
        'defect_opened','defect_recovered','defect_resolved','defect_reopened','defect_escalated'
    )),
    recipient_policy VARCHAR(80) NOT NULL,
    idempotency_key VARCHAR(240) NOT NULL UNIQUE,
    payload JSONB NOT NULL CHECK (jsonb_typeof(payload)='object'),
    status VARCHAR(24) NOT NULL DEFAULT 'pending' CHECK (status IN (
        'pending','dispatched','failed','dead_letter','cancelled'
    )),
    attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    available_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    dispatched_at TIMESTAMPTZ NULL,
    last_error_code VARCHAR(120) NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_module076_outbox_dispatch CHECK (
        (status='dispatched' AND dispatched_at IS NOT NULL)
        OR (status<>'dispatched' AND dispatched_at IS NULL)
    )
);
CREATE INDEX IF NOT EXISTS ix_module076_notification_outbox_queue
    ON module076_notification_outbox(status, available_at);

CREATE OR REPLACE FUNCTION pulse084_append_only_defect_evidence()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse084_immutable$
BEGIN
    RAISE EXCEPTION 'Module 076 evidence, events, occurrences, and probe results are append-only.';
END;
$pulse084_immutable$;

DROP TRIGGER IF EXISTS trg_module076_defect_events_immutable_084 ON module076_defect_events;
CREATE TRIGGER trg_module076_defect_events_immutable_084
BEFORE UPDATE OR DELETE ON module076_defect_events
FOR EACH ROW EXECUTE FUNCTION pulse084_append_only_defect_evidence();

DROP TRIGGER IF EXISTS trg_module076_defect_evidence_immutable_084 ON module076_defect_evidence;
CREATE TRIGGER trg_module076_defect_evidence_immutable_084
BEFORE UPDATE OR DELETE ON module076_defect_evidence
FOR EACH ROW EXECUTE FUNCTION pulse084_append_only_defect_evidence();

DROP TRIGGER IF EXISTS trg_module076_incident_occurrences_immutable_084 ON module076_incident_occurrences;
CREATE TRIGGER trg_module076_incident_occurrences_immutable_084
BEFORE UPDATE OR DELETE ON module076_incident_occurrences
FOR EACH ROW EXECUTE FUNCTION pulse084_append_only_defect_evidence();

DROP TRIGGER IF EXISTS trg_module076_probe_results_immutable_084 ON module076_probe_results;
CREATE TRIGGER trg_module076_probe_results_immutable_084
BEFORE UPDATE OR DELETE ON module076_probe_results
FOR EACH ROW EXECUTE FUNCTION pulse084_append_only_defect_evidence();

INSERT INTO module076_monitor_policies(
    policy_code,display_name,component_code,environment,enabled,
    consecutive_failure_threshold,evaluation_window_seconds,
    consecutive_success_threshold,recovery_stability_seconds,initial_priority,
    maximum_new_defects_per_hour,flapping_window_seconds,
    flapping_reopen_threshold,machine_creation_enabled)
VALUES
    ('pulse_web','Pulse web availability','pulse_web','test',TRUE,3,180,3,900,'Critical',10,3600,3,FALSE),
    ('pulse_api','Pulse API availability','pulse_api','test',TRUE,3,180,3,900,'Critical',10,3600,3,FALSE),
    ('pulse_database','Pulse database availability','pulse_database','test',TRUE,3,180,3,900,'Critical',10,3600,3,FALSE),
    ('pulse_sso','Pulse SSO availability','pulse_sso','test',TRUE,3,180,3,900,'Critical',10,3600,3,FALSE),
    ('all_ai_targets','All Celar AI answer targets','all_ai_targets','test',TRUE,3,300,3,900,'Critical',10,3600,3,FALSE),
    ('private_inference','Private inference','private_inference','test',TRUE,3,300,3,900,'High',10,3600,3,FALSE),
    ('private_embeddings','Private embeddings','private_embeddings','test',TRUE,3,300,3,900,'High',10,3600,3,FALSE),
    ('private_ocr','Private OCR','private_ocr','test',TRUE,3,300,3,900,'High',10,3600,3,FALSE),
    ('private_malware_scan','Private malware scanning','private_malware_scan','test',TRUE,3,300,3,900,'High',10,3600,3,FALSE),
    ('module064','Module 064 provider routing','module064','test',TRUE,3,300,3,900,'High',10,3600,3,FALSE),
    ('github_api','GitHub API and repository access','github_api','test',TRUE,3,600,3,900,'High',10,3600,3,FALSE),
    ('github_actions','GitHub Actions during release','github_actions','test',TRUE,2,300,3,900,'Critical',10,3600,3,FALSE),
    ('module067','Module 067 notification delivery','module067','test',TRUE,5,900,3,900,'High',10,3600,3,FALSE),
    ('tls_certificate','Celar AI TLS certificate','tls_certificate','test',TRUE,2,86400,3,900,'Critical',10,3600,3,FALSE),
    ('clamav_signatures','ClamAV signature freshness','clamav_signatures','test',TRUE,1,86400,3,900,'High',10,3600,3,FALSE)
ON CONFLICT (policy_code) DO NOTHING;

INSERT INTO schema_migrations(migration_id,applied_at)
VALUES('084_module_076_celar_ai_defect_operations',NOW())
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
