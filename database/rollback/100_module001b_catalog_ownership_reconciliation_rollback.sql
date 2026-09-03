-- Rollback for Pulse migration 100.
-- Refuse to overwrite a module-owner change made after reconciliation.

BEGIN;

DO $projectpulse100_rollback$
DECLARE
    evidence RECORD;
    current_row RECORD;
BEGIN
    IF to_regclass('public.module_catalog_reconciliation_100_module001b_evidence') IS NULL THEN
        RETURN;
    END IF;

    SELECT * INTO evidence
    FROM module_catalog_reconciliation_100_module001b_evidence
    WHERE module_code = '001B';

    IF NOT FOUND THEN
        RETURN;
    END IF;

    SELECT * INTO current_row
    FROM scoped_role_policy_modules
    WHERE module_code = '001B';

    IF NOT FOUND THEN
        RETURN;
    END IF;

    IF current_row.owner_user_id IS DISTINCT FROM evidence.reconciled_owner_user_id
       OR current_row.owner_revision_number IS DISTINCT FROM evidence.reconciled_owner_revision_number THEN
        RAISE EXCEPTION 'Rollback 100 refused: Module 001B ownership changed after reconciliation.';
    END IF;

    IF evidence.was_present THEN
        UPDATE scoped_role_policy_modules
        SET module_name = evidence.previous_module_name,
            route_scope = evidence.previous_route_scope,
            current_state = evidence.previous_current_state,
            permission_notes = evidence.previous_permission_notes,
            source_url = evidence.previous_source_url,
            is_active = evidence.previous_is_active,
            owner_user_id = evidence.previous_owner_user_id,
            owner_revision_number = COALESCE(evidence.previous_owner_revision_number, 0),
            owner_updated_at = evidence.previous_owner_updated_at,
            owner_updated_by_user_id = evidence.previous_owner_updated_by_user_id
        WHERE module_code = '001B';
    ELSE
        UPDATE scoped_role_policy_modules
        SET current_state = 'Rolled back',
            permission_notes = 'Migration 100 registration rolled back. The inactive row is retained for immutable audit history.',
            is_active = FALSE
        WHERE module_code = '001B';
    END IF;
END;
$projectpulse100_rollback$;

DELETE FROM schema_migrations
WHERE migration_id = '100_module001b_catalog_ownership_reconciliation';

DROP TABLE IF EXISTS module_catalog_reconciliation_100_module001b_evidence;

COMMIT;
