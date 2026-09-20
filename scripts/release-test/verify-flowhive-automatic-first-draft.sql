\set ON_ERROR_STOP on
DO $$ BEGIN
  IF NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='122_flowhive_automatic_first_draft')
     OR to_regclass('public.project_flowhive_auto_plan_defaults') IS NULL
     OR to_regclass('public.project_flowhive_auto_plans') IS NULL
     OR to_regclass('public.project_flowhive_auto_plan_events') IS NULL THEN
    RAISE EXCEPTION 'FlowHive automatic draft schema is incomplete';
  END IF;
  IF NOT EXISTS(SELECT 1 FROM enterprise_notification_policies
    WHERE policy_code='FLOWHIVE_FIRST_DRAFT_READY' AND producer_contract='flowhive-first-draft-v1'
      AND recipient_strategy='subject_user' AND delivery_boundary IN ('test_only','locked')) THEN
    RAISE EXCEPTION 'FlowHive first-draft notification is missing or allows live delivery in protected Test';
  END IF;
END $$;
