-- Scheduling is independent of customer document revisions and AI generation.
-- Full snapshots are append-only, including after an eligible draft is deleted.
BEGIN;
SELECT pg_advisory_xact_lock(250118);
DO $$ BEGIN
    IF to_regclass('public.module025_sow_gsd_engagements') IS NULL THEN
        RAISE EXCEPTION 'Module 025 work tracking requires migration 099';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS module025_work_tracking_events (
    tracking_event_id uuid PRIMARY KEY,
    -- Deliberately no cascading engagement FK: permitted draft deletion must
    -- not delete scheduling decisions, actor identity, or earlier snapshots.
    engagement_id uuid NOT NULL,
    engagement_number varchar(40) NOT NULL,
    tracking_revision integer NOT NULL CHECK (tracking_revision > 0),
    document_revision integer NOT NULL CHECK (document_revision > 0),
    owner_user_id uuid NOT NULL REFERENCES app_users(user_id),
    owner_display_name varchar(320) NOT NULL,
    target_date date NULL CHECK (target_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'),
    priority varchar(10) NOT NULL CHECK (priority IN ('low','normal','high','urgent')),
    blocker_reason varchar(2000) NOT NULL DEFAULT '',
    blocker_owner_user_id uuid NULL REFERENCES app_users(user_id),
    blocker_owner_display_name varchar(320) NOT NULL DEFAULT '',
    authoring_hours numeric(6,2) NULL CHECK (authoring_hours BETWEEN 0 AND 1000),
    actor_user_id uuid NOT NULL REFERENCES app_users(user_id),
    actor_display_name varchar(320) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (engagement_id,tracking_revision),
    CHECK ((length(btrim(blocker_reason))=0 AND blocker_owner_user_id IS NULL AND blocker_owner_display_name='')
        OR (length(btrim(blocker_reason))>0 AND blocker_owner_user_id IS NOT NULL AND length(btrim(blocker_owner_display_name))>0))
);

CREATE OR REPLACE FUNCTION module025_protect_work_tracking()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'Module 025 work tracking is append-only; save a new scheduling revision';
END $$;
DROP TRIGGER IF EXISTS trg_module025_protect_work_tracking ON module025_work_tracking_events;
CREATE TRIGGER trg_module025_protect_work_tracking
BEFORE UPDATE OR DELETE ON module025_work_tracking_events
FOR EACH ROW EXECUTE FUNCTION module025_protect_work_tracking();
DROP TRIGGER IF EXISTS trg_module025_protect_work_tracking_truncate ON module025_work_tracking_events;
CREATE TRIGGER trg_module025_protect_work_tracking_truncate
BEFORE TRUNCATE ON module025_work_tracking_events
FOR EACH STATEMENT EXECUTE FUNCTION module025_protect_work_tracking();

CREATE OR REPLACE FUNCTION module025_validate_work_tracking_insert()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM module025_sow_gsd_engagements e
        WHERE e.engagement_id=NEW.engagement_id AND e.engagement_number=NEW.engagement_number
          AND e.owner_user_id=NEW.owner_user_id AND e.revision=NEW.document_revision) THEN
        RAISE EXCEPTION 'Work tracking must reference the current SOW/GSD identity, owner and document revision';
    END IF;
    IF NEW.tracking_revision <> COALESCE((SELECT max(tracking_revision)
        FROM module025_work_tracking_events WHERE engagement_id=NEW.engagement_id),0)+1 THEN
        RAISE EXCEPTION 'Work tracking revisions must be sequential';
    END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_module025_validate_work_tracking_insert ON module025_work_tracking_events;
CREATE TRIGGER trg_module025_validate_work_tracking_insert
BEFORE INSERT ON module025_work_tracking_events
FOR EACH ROW EXECUTE FUNCTION module025_validate_work_tracking_insert();

COMMENT ON TABLE module025_work_tracking_events IS
    'Immutable scheduling snapshots. Authoring hours are remaining SA preparation effort, never delivery LOE. Separate revisions do not invalidate confirmed documents. Audit is retained after eligible draft deletion.';
INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('118_module025_work_tracking','Independent, immutable SA work tracking with scoped due dates, priorities, blockers and remaining authoring effort',now())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
