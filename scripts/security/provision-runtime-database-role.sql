-- Run only as the provisioning identity against the explicitly selected Test DB.
-- Creates disabled identities; credential activation is a separate guarded step.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='30s';
DO $$
BEGIN
  IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='ptp_app') THEN
    CREATE ROLE ptp_app NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
  END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='ptp_runtime') THEN
    CREATE ROLE ptp_runtime NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS INHERIT;
  END IF;
  IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname IN ('ptp_app','ptp_runtime')
      AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls))
     OR EXISTS(SELECT 1 FROM pg_class c JOIN pg_roles r ON r.oid=c.relowner WHERE r.rolname IN ('ptp_app','ptp_runtime'))
     OR EXISTS(SELECT 1 FROM pg_namespace n JOIN pg_roles r ON r.oid=n.nspowner WHERE r.rolname IN ('ptp_app','ptp_runtime'))
     OR EXISTS(SELECT 1 FROM pg_database d JOIN pg_roles r ON r.oid=d.datdba WHERE r.rolname IN ('ptp_app','ptp_runtime'))
     OR EXISTS(SELECT 1 FROM pg_auth_members m JOIN pg_roles member ON member.oid=m.member
        JOIN pg_roles parent ON parent.oid=m.roleid WHERE member.rolname IN ('ptp_app','ptp_runtime')
        AND (member.rolname<>'ptp_runtime' OR parent.rolname<>'ptp_app' OR m.admin_option)) THEN
    RAISE EXCEPTION 'Existing runtime role has ownership or elevated authority; explicit reconciliation required';
  END IF;
END $$;

GRANT ptp_app TO ptp_runtime;
GRANT USAGE ON SCHEMA public TO ptp_app;
-- Runtime cannot create/shadow objects, including through inherited PUBLIC rights.
REVOKE CREATE ON SCHEMA public FROM PUBLIC,ptp_app,ptp_runtime;

DO $$
DECLARE relation record;
BEGIN
  FOR relation IN SELECT c.relname,c.relkind FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='public' AND c.relkind IN ('r','p','v','m')
        AND NOT EXISTS(SELECT 1 FROM pg_depend d WHERE d.classid='pg_class'::regclass AND d.objid=c.oid AND d.deptype='e')
  LOOP
    -- Provisioning ledgers, credential backups and role-restoration evidence are
    -- excluded from application writes. No TRUNCATE or ownership is granted.
    IF relation.relname IN ('schema_migrations','verity_schema_migrations','security_integrity_repair_events')
       OR relation.relname LIKE 'security_credential_transition_%'
       OR relation.relname LIKE '%role%before_image%'
       OR relation.relname LIKE '%role%backup%' THEN
      EXECUTE format('REVOKE ALL ON TABLE public.%I FROM ptp_app,ptp_runtime',relation.relname);
      IF relation.relname IN ('schema_migrations','verity_schema_migrations') THEN
        EXECUTE format('GRANT SELECT ON TABLE public.%I TO ptp_app',relation.relname);
      END IF;
    ELSIF relation.relkind IN ('v','m') THEN
      EXECUTE format('GRANT SELECT ON TABLE public.%I TO ptp_app',relation.relname);
    ELSIF relation.relname IN (
        'module025_sow_gsd_generation_snapshots','module025_sow_gsd_versions',
        'module025_sow_gsd_artifact_issuance','module025_sow_sell_submissions',
        'module025_sow_sell_links','module025_sow_sell_receipts','module025_sow_sell_notification_outbox',
        'module025_sow_gsd_handoffs')
        OR EXISTS(SELECT 1 FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid
            WHERE t.tgrelid=format('public.%I',relation.relname)::regclass AND NOT t.tgisinternal
              AND (p.proname LIKE '%immutable%' OR p.proname ~ 'block_.*(audit|evidence|event|history|acceptance).*mutation')) THEN
      EXECUTE format('GRANT SELECT,INSERT ON TABLE public.%I TO ptp_app',relation.relname);
      EXECUTE format('REVOKE UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER ON TABLE public.%I FROM ptp_app,ptp_runtime',relation.relname);
    ELSE
      EXECUTE format('GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.%I TO ptp_app',relation.relname);
      EXECUTE format('REVOKE TRUNCATE,REFERENCES,TRIGGER ON TABLE public.%I FROM ptp_app,ptp_runtime',relation.relname);
    END IF;
  END LOOP;
END $$;
GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO ptp_app;
DO $$
BEGIN
  IF has_schema_privilege('ptp_runtime','public','CREATE')
     OR has_database_privilege('ptp_runtime',current_database(),'CREATE') THEN
    RAISE EXCEPTION 'Runtime retains schema/database creation privilege';
  END IF;
END $$;
COMMIT;
