-- Guarded rollback for 049_module_021_sell_customer_sync.
-- Existing source links or sync evidence must be preserved unless explicitly reviewed.

BEGIN;

DO $$
BEGIN
    IF to_regclass('public.customer_directory_source_links') IS NOT NULL
       AND EXISTS (SELECT 1 FROM customer_directory_source_links LIMIT 1) THEN
        RAISE EXCEPTION 'Rollback blocked: customer-directory SELL source links exist.';
    END IF;

    IF to_regclass('public.customer_directory_sync_runs') IS NOT NULL
       AND EXISTS (SELECT 1 FROM customer_directory_sync_runs LIMIT 1) THEN
        RAISE EXCEPTION 'Rollback blocked: customer-directory SELL sync evidence exists.';
    END IF;
END $$;

DROP TABLE IF EXISTS customer_directory_sync_runs;
DROP TABLE IF EXISTS customer_directory_source_links;

DELETE FROM schema_migrations
WHERE migration_id = '049_module_021_sell_customer_sync';

COMMIT;
