-- Module 025: permit irreversible deletion only for ungenerated drafts with no retained evidence.
BEGIN;
SELECT pg_advisory_xact_lock(250110);

CREATE OR REPLACE FUNCTION module025_reject_evidence_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    engagement uuid;
    draft_delete boolean;
BEGIN
    draft_delete := COALESCE(current_setting('projectpulse.module025_allow_draft_delete', true), '') = 'on';
    engagement := CASE
        WHEN TG_TABLE_NAME = 'module025_sow_gsd_engagements' THEN OLD.engagement_id
        WHEN to_jsonb(OLD) ? 'engagement_id' THEN (to_jsonb(OLD)->>'engagement_id')::uuid
        ELSE NULL
    END;

    IF TG_OP = 'DELETE' AND draft_delete AND engagement IS NOT NULL
       AND EXISTS (
           SELECT 1 FROM module025_sow_gsd_engagements e
           WHERE e.engagement_id = engagement
             AND e.status = 'draft'
             AND e.is_active = TRUE
             AND e.last_generated_at IS NULL
       )
       AND NOT EXISTS (
           SELECT 1 FROM module025_sow_gsd_generation_snapshots s
           WHERE s.engagement_id = engagement
       )
       AND NOT EXISTS (
           SELECT 1 FROM module025_sow_gsd_versions v
           WHERE v.engagement_id = engagement
       )
       AND NOT EXISTS (
           SELECT 1 FROM module025_sow_sell_submissions s
           WHERE s.engagement_id = engagement
       ) THEN
        RETURN OLD;
    END IF;

    RAISE EXCEPTION 'Module 025 retained evidence is append-only; use a new version or archive the workspace';
END $$;

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('110_module025_ungenerated_draft_delete','Permit guarded deletion of ungenerated Module 025 drafts with no retained evidence',now())
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
