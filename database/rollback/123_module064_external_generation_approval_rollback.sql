-- Retain approval and audit columns as historical evidence; older application
-- releases ignore them. Revoking active approvals is an audited Module 064 action.
BEGIN;
DELETE FROM schema_migrations WHERE migration_id='123_module064_external_generation_approval';
COMMIT;
