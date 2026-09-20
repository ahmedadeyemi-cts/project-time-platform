-- Preserve the SOW identity and all retained evidence while allowing an audited
-- working-draft handoff. Application authorization uses reporting relationships,
-- never free-text department/team labels. No existing record is rewritten.
BEGIN;
SELECT pg_advisory_xact_lock(250116);
DO $$ BEGIN
    IF to_regclass('public.module025_sow_gsd_engagements') IS NULL
        OR to_regclass('public.module025_sow_sell_dispatch') IS NULL
        OR NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='106_module025_sow_sell_register')
        OR NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='110_module025_ungenerated_draft_delete') THEN
        RAISE EXCEPTION 'Governed SOW/GSD transfers require migrations 099, 106 and 110';
    END IF;
END $$;

CREATE OR REPLACE FUNCTION module025_protect_sow_gsd_identity()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.engagement_number IS DISTINCT FROM OLD.engagement_number THEN
        RAISE EXCEPTION 'Module 025 engagement_number is immutable';
    END IF;
    IF NEW.owner_user_id IS DISTINCT FROM OLD.owner_user_id THEN
        IF NOT OLD.is_active OR OLD.status IN ('archived','confirmed')
            OR NEW.revision <> OLD.revision + 1
            OR NOT EXISTS (
                SELECT 1 FROM module025_sow_gsd_events event
                WHERE event.engagement_id=OLD.engagement_id
                    AND event.event_type='ownership_transferred'
                    AND event.engagement_revision=NEW.revision
                    AND event.evidence_json->>'transferTransactionId'=txid_current()::text
                    AND event.evidence_json->>'previousOwnerUserId'=OLD.owner_user_id::text
                    AND event.evidence_json->>'newOwnerUserId'=NEW.owner_user_id::text
                    AND event.evidence_json->>'previousRevision'=OLD.revision::text
                    AND event.evidence_json->>'newRevision'=NEW.revision::text
                    AND event.evidence_json->>'newOwnerDisplayName'=NEW.owner_display_name
                    AND event.evidence_json->>'newOwnerDepartmentName'=NEW.owner_department_name
                    AND event.evidence_json->>'newOwnerTeamName'=NEW.owner_team_name
                    AND event.evidence_json->>'transferredByUserId'=event.actor_user_id::text
                    AND length(btrim(event.evidence_json->>'reason')) BETWEEN 5 AND 1000
                    AND event.evidence_json->>'scope'='same_reporting_manager'
            ) THEN
            RAISE EXCEPTION 'Module 025 owner change requires immutable transfer evidence in the same transaction';
        END IF;
    END IF;
    NEW.updated_at := NOW();
    RETURN NEW;
END;
$$;

-- Ungenerated drafts remain deletable until a handoff creates retained audit.
-- Migration 110's ordinary draft exception must never erase transfer evidence.
CREATE OR REPLACE FUNCTION module025_reject_evidence_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE engagement uuid; draft_delete boolean;
BEGIN
    draft_delete := COALESCE(current_setting('projectpulse.module025_allow_draft_delete', true), '') = 'on';
    engagement := CASE
        WHEN TG_TABLE_NAME = 'module025_sow_gsd_engagements' THEN OLD.engagement_id
        WHEN to_jsonb(OLD) ? 'engagement_id' THEN (to_jsonb(OLD)->>'engagement_id')::uuid
        ELSE NULL
    END;
    IF TG_OP = 'DELETE' AND draft_delete AND engagement IS NOT NULL
       AND EXISTS(SELECT 1 FROM module025_sow_gsd_engagements e WHERE e.engagement_id=engagement
           AND e.status='draft' AND e.is_active=TRUE AND e.last_generated_at IS NULL)
       AND NOT EXISTS(SELECT 1 FROM module025_sow_gsd_generation_snapshots s WHERE s.engagement_id=engagement)
       AND NOT EXISTS(SELECT 1 FROM module025_sow_gsd_versions v WHERE v.engagement_id=engagement)
       AND NOT EXISTS(SELECT 1 FROM module025_sow_sell_submissions s WHERE s.engagement_id=engagement)
       AND NOT EXISTS(SELECT 1 FROM module025_sow_gsd_events e WHERE e.engagement_id=engagement AND e.event_type='ownership_transferred') THEN
        RETURN OLD;
    END IF;
    RAISE EXCEPTION 'Module 025 retained evidence is append-only; use a new version or archive the workspace';
END $$;

COMMENT ON COLUMN module025_sow_gsd_engagements.owner_user_id IS
    'Current authoring owner; handoffs require matching immutable transfer evidence and a new working revision. Original authors remain recorded in retained events and versions.';

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES ('116_module025_governed_ownership_transfer','Audited same-reporting-team SOW/GSD working-draft handoffs without changing retained evidence',NOW())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
