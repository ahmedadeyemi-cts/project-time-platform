BEGIN;

DROP TRIGGER IF EXISTS trg_projectpulse055d7_after_intake_commit
    ON work_register_intake_commits;

DROP TRIGGER IF EXISTS trg_projectpulse055d7_sync_task_assignment_history
    ON work_register_task_assignment_history;

DROP FUNCTION IF EXISTS projectpulse055d7_after_intake_commit();
DROP FUNCTION IF EXISTS projectpulse055d7_sync_task_assignment_history();
DROP FUNCTION IF EXISTS projectpulse055d7_finalize_intake_commit(UUID, UUID, UUID);
DROP FUNCTION IF EXISTS projectpulse055d7_can_complete_intake(UUID);
DROP FUNCTION IF EXISTS projectpulse055d7_contract_type(TEXT);
DROP FUNCTION IF EXISTS projectpulse055d7_canonical_work_type(TEXT);

DROP INDEX IF EXISTS ix_work_register_task_assignment_history_task_user;
DROP INDEX IF EXISTS ix_projects_work_type_status;

-- Intentionally retain columns and repaired business data. Dropping columns or
-- deleting synchronized assignments would be destructive and is not required
-- to roll the application back to the prior source revision.

COMMIT;
