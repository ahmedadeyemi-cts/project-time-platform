\set ON_ERROR_STOP on
-- Read-only MINIMUM schema/access check, not complete migration verification.
-- Import an approved schema baseline, then its reviewed forward migrations.
BEGIN READ ONLY;
DO $$
DECLARE relation_name text;
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname=current_user AND (rolsuper OR rolcreatedb OR rolcreaterole)) THEN
    RAISE EXCEPTION 'Application database identity must not administer databases or roles';
  END IF;
  FOREACH relation_name IN ARRAY ARRAY[
    'app_users','app_roles','app_user_role_assignments','projects','time_entries',
    'work_lifecycle_audit_events','work_closeout_records','billing_invoices',
    'projectpulse_native_admin_documents','celar_laya_settings'
  ] LOOP
    IF to_regclass('public.' || relation_name) IS NULL THEN
      RAISE EXCEPTION 'Approved schema baseline required; missing relation: %', relation_name;
    END IF;
    IF NOT has_table_privilege(current_user,'public.' || relation_name,'SELECT') THEN
      RAISE EXCEPTION 'Runtime role lacks approved read access: %', relation_name;
    END IF;
  END LOOP;
END $$;
ROLLBACK;
