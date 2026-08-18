-- Guarded rollback for Migration 094.
--
-- This rollback stops future automatic authority promotion but deliberately does
-- not demote private SOW versions already promoted to approved or canonical.
-- Those versions may already support retained citations, FlowHive review drafts,
-- or a separately reviewed human decision. Revoking or superseding document
-- authority remains an explicit governed action, not an automatic rollback side
-- effect. Durable promotion evidence is retained for audit and safe reapply.

BEGIN;

DROP TRIGGER IF EXISTS trg_projectpulse094_reconcile_ready_work_register_sow
    ON project_intake_documents;

DROP FUNCTION IF EXISTS projectpulse094_reconcile_ready_work_register_sow_trigger();
DROP FUNCTION IF EXISTS projectpulse094_reconcile_ready_work_register_sow(UUID);

DELETE FROM schema_migrations
WHERE migration_id = '094_flowhive_canonical_sow_authority';

COMMIT;
