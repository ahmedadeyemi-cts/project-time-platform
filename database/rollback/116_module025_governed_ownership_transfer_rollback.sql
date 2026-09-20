-- Disable future handoffs without undoing owners or deleting transfer history.
BEGIN;
CREATE OR REPLACE FUNCTION module025_protect_sow_gsd_identity()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.engagement_number IS DISTINCT FROM OLD.engagement_number THEN
        RAISE EXCEPTION 'Module 025 engagement_number is immutable';
    END IF;
    IF NEW.owner_user_id IS DISTINCT FROM OLD.owner_user_id THEN
        RAISE EXCEPTION 'Module 025 owner_user_id is immutable';
    END IF;
    NEW.updated_at := NOW();
    RETURN NEW;
END;
$$;
DELETE FROM schema_migrations WHERE migration_id='116_module025_governed_ownership_transfer';
COMMIT;
