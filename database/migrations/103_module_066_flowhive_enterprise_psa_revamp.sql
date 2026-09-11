-- ProjectPulse migration 103
-- Module 066 Project FlowHive enterprise PSA revamp.
--
-- Adds immutable RAID change history, project meeting/recording metadata,
-- customer-visible meeting controls, and task-due reminder preferences/evidence.
-- Existing canonical projects, FlowHive plans, financials, notifications, and
-- customer-share authority remain the systems of record for their domains.

BEGIN;

DO $projectpulse103_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.project_flowhive_raid_items') IS NULL
       OR to_regclass('public.project_flowhive_customer_shares') IS NULL
       OR to_regclass('public.project_notification_dispatches') IS NULL THEN
        RAISE EXCEPTION 'Migration 103 requires migrations 050 and 086 plus canonical project and identity foundations.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '086_module_066_flowhive_enterprise_pm'
    ) THEN
        RAISE EXCEPTION 'Migration 103 requires migration 086.';
    END IF;
END;
$projectpulse103_prerequisites$;

CREATE TABLE IF NOT EXISTS project_flowhive_raid_events (
    raid_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    raid_item_id UUID NOT NULL,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    action_code VARCHAR(24) NOT NULL CHECK (action_code IN ('created','updated','deleted')),
    actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    prior_json JSONB NULL,
    new_json JSONB NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (prior_json IS NOT NULL OR new_json IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS ix_project_flowhive_raid_events_project
    ON project_flowhive_raid_events(project_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_project_flowhive_raid_events_item
    ON project_flowhive_raid_events(raid_item_id, occurred_at DESC);

CREATE TABLE IF NOT EXISTS project_flowhive_meetings (
    meeting_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    title VARCHAR(240) NOT NULL CHECK (length(btrim(title)) >= 3),
    meeting_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    original_file_name VARCHAR(260) NOT NULL,
    storage_relative_path TEXT NOT NULL,
    content_type VARCHAR(120) NOT NULL DEFAULT 'video/mp4',
    size_bytes BIGINT NOT NULL CHECK (size_bytes > 0),
    sha256 CHAR(64) NOT NULL CHECK (sha256 ~ '^[0-9a-f]{64}$'),
    customer_visible BOOLEAN NOT NULL DEFAULT FALSE,
    transcript_status VARCHAR(32) NOT NULL DEFAULT 'queued' CHECK (transcript_status IN (
        'not_requested','queued','processing','completed','failed','unavailable'
    )),
    transcript_text TEXT NOT NULL DEFAULT '',
    transcript_language VARCHAR(32) NOT NULL DEFAULT '',
    action_items JSONB NOT NULL DEFAULT '[]'::JSONB CHECK (jsonb_typeof(action_items) = 'array'),
    transcription_diagnostic VARCHAR(240) NOT NULL DEFAULT '',
    uploaded_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(project_id, sha256)
);

CREATE INDEX IF NOT EXISTS ix_project_flowhive_meetings_project
    ON project_flowhive_meetings(project_id, meeting_at DESC, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_project_flowhive_meetings_transcription
    ON project_flowhive_meetings(transcript_status, created_at)
    WHERE transcript_status IN ('queued','processing');

CREATE TABLE IF NOT EXISTS project_flowhive_meeting_events (
    meeting_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    meeting_id UUID NOT NULL,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    event_code VARCHAR(40) NOT NULL CHECK (event_code IN (
        'uploaded','metadata_updated','transcription_started','transcription_completed',
        'transcription_failed','internal_downloaded','customer_downloaded','customer_visibility_changed'
    )),
    actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    detail_json JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(detail_json) = 'object'),
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_flowhive_meeting_events_meeting
    ON project_flowhive_meeting_events(meeting_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_project_flowhive_meeting_events_project
    ON project_flowhive_meeting_events(project_id, occurred_at DESC);

CREATE TABLE IF NOT EXISTS project_flowhive_task_reminder_preferences (
    project_id UUID PRIMARY KEY REFERENCES projects(project_id) ON DELETE CASCADE,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    lead_days SMALLINT[] NOT NULL DEFAULT ARRAY[2,1]::SMALLINT[],
    include_project_manager BOOLEAN NOT NULL DEFAULT TRUE,
    include_assigned_team_members BOOLEAN NOT NULL DEFAULT TRUE,
    include_overdue BOOLEAN NOT NULL DEFAULT TRUE,
    timezone_name VARCHAR(100) NOT NULL DEFAULT 'America/Chicago',
    quiet_hours_start TIME NULL DEFAULT TIME '20:00',
    quiet_hours_end TIME NULL DEFAULT TIME '06:00',
    delivery_boundary VARCHAR(40) NOT NULL DEFAULT 'test_only' CHECK (delivery_boundary IN (
        'test_only','production_governed','locked'
    )),
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (cardinality(lead_days) BETWEEN 1 AND 8),
    CHECK (0 <= ALL(lead_days) AND 60 >= ALL(lead_days))
);

CREATE TABLE IF NOT EXISTS project_flowhive_task_reminder_events (
    reminder_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    task_wbs VARCHAR(64) NOT NULL,
    task_name VARCHAR(500) NOT NULL,
    due_date DATE NOT NULL,
    reminder_date DATE NOT NULL,
    event_key VARCHAR(320) NOT NULL UNIQUE,
    notification_dispatch_id UUID NULL REFERENCES project_notification_dispatches(project_notification_dispatch_id) ON DELETE SET NULL,
    delivery_status VARCHAR(40) NOT NULL CHECK (delivery_status IN (
        'queued','sent','suppressed','failed','preview_ready'
    )),
    recipient_count INTEGER NOT NULL DEFAULT 0 CHECK (recipient_count >= 0),
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_flowhive_task_reminder_events_project
    ON project_flowhive_task_reminder_events(project_id, due_date, created_at DESC);

CREATE OR REPLACE FUNCTION projectpulse103_touch_timestamp()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse103_touch_timestamp_body$
BEGIN
    NEW.updated_at := NOW();
    RETURN NEW;
END;
$projectpulse103_touch_timestamp_body$;

DROP TRIGGER IF EXISTS trg_project_flowhive_meetings_touch_103
    ON project_flowhive_meetings;
CREATE TRIGGER trg_project_flowhive_meetings_touch_103
BEFORE UPDATE ON project_flowhive_meetings
FOR EACH ROW EXECUTE FUNCTION projectpulse103_touch_timestamp();

DROP TRIGGER IF EXISTS trg_project_flowhive_task_reminder_preferences_touch_103
    ON project_flowhive_task_reminder_preferences;
CREATE TRIGGER trg_project_flowhive_task_reminder_preferences_touch_103
BEFORE UPDATE ON project_flowhive_task_reminder_preferences
FOR EACH ROW EXECUTE FUNCTION projectpulse103_touch_timestamp();

CREATE OR REPLACE FUNCTION projectpulse103_capture_raid_event()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse103_capture_raid_event_body$
DECLARE
    actor UUID;
BEGIN
    actor := CASE WHEN TG_OP = 'DELETE' THEN OLD.updated_by_user_id ELSE NEW.updated_by_user_id END;
    INSERT INTO project_flowhive_raid_events(
        raid_item_id,
        project_id,
        action_code,
        actor_user_id,
        prior_json,
        new_json)
    VALUES (
        CASE WHEN TG_OP = 'DELETE' THEN OLD.raid_item_id ELSE NEW.raid_item_id END,
        CASE WHEN TG_OP = 'DELETE' THEN OLD.project_id ELSE NEW.project_id END,
        CASE TG_OP WHEN 'INSERT' THEN 'created' WHEN 'UPDATE' THEN 'updated' ELSE 'deleted' END,
        actor,
        CASE WHEN TG_OP IN ('UPDATE','DELETE') THEN to_jsonb(OLD) ELSE NULL END,
        CASE WHEN TG_OP IN ('INSERT','UPDATE') THEN to_jsonb(NEW) ELSE NULL END);
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END;
$projectpulse103_capture_raid_event_body$;

DROP TRIGGER IF EXISTS trg_project_flowhive_raid_audit_103
    ON project_flowhive_raid_items;
CREATE TRIGGER trg_project_flowhive_raid_audit_103
AFTER INSERT OR UPDATE OR DELETE ON project_flowhive_raid_items
FOR EACH ROW EXECUTE FUNCTION projectpulse103_capture_raid_event();

CREATE OR REPLACE FUNCTION projectpulse103_block_immutable_evidence()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse103_immutable_body$
BEGIN
    RAISE EXCEPTION 'Project FlowHive PSA audit evidence is immutable.';
END;
$projectpulse103_immutable_body$;

DROP TRIGGER IF EXISTS trg_project_flowhive_raid_events_immutable_103
    ON project_flowhive_raid_events;
CREATE TRIGGER trg_project_flowhive_raid_events_immutable_103
BEFORE UPDATE OR DELETE ON project_flowhive_raid_events
FOR EACH ROW EXECUTE FUNCTION projectpulse103_block_immutable_evidence();

DROP TRIGGER IF EXISTS trg_project_flowhive_meeting_events_immutable_103
    ON project_flowhive_meeting_events;
CREATE TRIGGER trg_project_flowhive_meeting_events_immutable_103
BEFORE UPDATE OR DELETE ON project_flowhive_meeting_events
FOR EACH ROW EXECUTE FUNCTION projectpulse103_block_immutable_evidence();

DROP TRIGGER IF EXISTS trg_project_flowhive_task_reminder_events_immutable_103
    ON project_flowhive_task_reminder_events;
CREATE TRIGGER trg_project_flowhive_task_reminder_events_immutable_103
BEFORE UPDATE OR DELETE ON project_flowhive_task_reminder_events
FOR EACH ROW EXECUTE FUNCTION projectpulse103_block_immutable_evidence();

INSERT INTO app_permissions(
    permission_code, permission_name, module_code, permission_description)
VALUES
    ('MANAGE_FLOWHIVE_MEETINGS_066', 'Manage FlowHive project meetings', '066', 'Upload and manage project meeting recordings, customer visibility, transcripts, and action-item evidence within authorized FlowHive projects.'),
    ('MANAGE_FLOWHIVE_TASK_REMINDERS_066', 'Manage FlowHive task reminders', '066', 'Configure due-date reminders for Project Managers and assigned project team members within authorized FlowHive projects.'),
    ('VIEW_FLOWHIVE_AUDIT_066', 'View FlowHive immutable audit', '066', 'View immutable RAID, meeting, reminder, version, review, and customer-share evidence for authorized FlowHive projects.')
ON CONFLICT(permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES (
    '103_module_066_flowhive_enterprise_psa_revamp',
    'Module 066 enterprise PSA revamp: immutable RAID history, project meetings, customer-visible recordings, transcription state, and task-due reminder evidence',
    NOW()
)
ON CONFLICT(migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
