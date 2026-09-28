-- Read-only Protected UAT schema verification. Never enable a policy or send a message.
DO $verify$
BEGIN
 IF (SELECT count(*) FROM schema_migrations WHERE migration_id IN (
   '126_module065_power_automate_teams_delivery','128_module065_email_teams_notification_parity','129_enterprise_reminder_delivery_sources')) <> 3 THEN
   RAISE EXCEPTION 'Module065 notification parity migration ledger is incomplete';
 END IF;
 IF to_regclass('module065_teams_outbox_events') IS NULL OR to_regclass('module065_teams_outbox') IS NULL
   OR to_regclass('module065_teams_outbox_attempts') IS NULL OR to_regclass('module065_teams_outbox_actions') IS NULL
   OR to_regclass('ix_module065_teams_outbox_due') IS NULL OR to_regclass('ix_module065_teams_outbox_attempt_window') IS NULL THEN
   RAISE EXCEPTION 'Module065 notification parity schema or durable queue index is missing';
 END IF;
 IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema()
   AND table_name='module065_teams_configuration' AND column_name IN ('delivery_mode','workflow_trigger_url','workflow_audience')) <> 3 THEN
   RAISE EXCEPTION 'Module065 workflow configuration migration is missing';
 END IF;
 IF (SELECT count(*) FROM enterprise_notification_policies WHERE producer_contract='enterprise-reminder-v1'
   AND policy_code IN ('TIME_NOT_SUBMITTED','TIME_NOT_SUBMITTED_ESCALATION','COMPANY_HOLIDAY_UPCOMING','PM_MONTH_END_REMINDER')
   AND delivery_boundary IN ('test_only','locked')) <> 4 THEN
   RAISE EXCEPTION 'Protected UAT reminder policies must exist and cannot permit live recipients';
 END IF;
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='ptp_app') AND EXISTS(
   SELECT 1 FROM (VALUES ('module065_teams_outbox','SELECT'),('module065_teams_outbox','INSERT'),
      ('module065_teams_outbox','UPDATE'),('module065_teams_outbox_actions','SELECT'),('module065_teams_outbox_actions','INSERT'))
      required_privilege(table_name,privilege_name)
   WHERE NOT has_table_privilege('ptp_app',table_name,privilege_name)) THEN
   RAISE EXCEPTION 'Module065 notification queue application privileges are incomplete';
 END IF;
END
$verify$;
