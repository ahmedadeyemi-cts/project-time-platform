-- Content-free postconditions; fail the protected migration job on any mismatch.
BEGIN READ ONLY;
SET LOCAL statement_timeout='15s';
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='130_security_integrity_boundaries')
     OR to_regclass('public.security_integrity_repair_events') IS NULL
     OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='app_users' AND column_name='profile_photo_data_url')
     OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='app_users' AND column_name='profile_photo_updated_at')
     OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='email_notification_outbox' AND column_name='queued_by_user_id') THEN
    RAISE EXCEPTION 'Security integrity schema is incomplete';
  END IF;
  IF EXISTS (SELECT 1 FROM project_intake_documents d JOIN work_register_documents w
      ON w.work_register_document_id=d.work_register_document_id
      WHERE d.engineering_visible AND lower(COALESCE(w.visibility,'')) NOT IN ('project_team','engineering_team','all')) THEN
    RAISE EXCEPTION 'Restricted document visibility remains inconsistent';
  END IF;
  IF EXISTS (SELECT 1 FROM app_role_permissions rp JOIN app_roles r ON r.app_role_id=rp.app_role_id
      JOIN app_permissions p ON p.app_permission_id=rp.app_permission_id
      WHERE upper(r.role_code) IN ('PROJECT_MANAGER','PROJECT_MANAGEMENT','PROJECT_MANAGEMENT_LEAD','PROJECT_MANAGEMENT_TEAM_LEAD','PM_TEAM_LEAD')
      AND p.permission_code IN ('VIEW_PROJECT_ALLOCATION_INFO','MANAGE_PROJECT_ALLOCATION_INFO')) THEN
    RAISE EXCEPTION 'Retired allocation grants remain';
  END IF;
  BEGIN
    PERFORM projectpulse073_add_working_days(DATE '2026-09-28',2147483647);
    RAISE EXCEPTION 'Working-day limit was not enforced';
  EXCEPTION WHEN SQLSTATE '22023' THEN NULL;
  END;
  BEGIN
    PERFORM projectpulse073_working_day_delta(DATE '2026-09-28',DATE '9999-12-31');
    RAISE EXCEPTION 'Date-delta limit was not enforced';
  EXCEPTION WHEN SQLSTATE '22023' THEN NULL;
  END;
  BEGIN
    PERFORM projectpulse073_working_day_duration(DATE '2026-09-28',DATE '9999-12-31');
    RAISE EXCEPTION 'Duration limit was not enforced';
  EXCEPTION WHEN SQLSTATE '22023' THEN NULL;
  END;
  BEGIN
    PERFORM projectpulse055d4d_get_or_create_stakeholder_user('Security validation invalid identity','ACCOUNT_EXECUTIVE','','','',NULL);
    RAISE EXCEPTION 'Free-text stakeholder was accepted';
  EXCEPTION WHEN SQLSTATE '22023' THEN NULL;
  END;
END $$;
COMMIT;
