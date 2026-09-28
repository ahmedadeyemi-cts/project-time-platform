-- Preserve notification evidence. Roll back application code first; do not drop live ledgers.
BEGIN;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM module065_teams_outbox) THEN
  RAISE EXCEPTION 'Retain Teams delivery evidence; use an approved archival rollback before schema removal.';
 END IF;
END $$;
DROP TABLE IF EXISTS module065_teams_outbox_actions;
DROP TABLE IF EXISTS module065_teams_outbox_attempts;
DROP TABLE IF EXISTS module065_teams_outbox;
DROP TABLE IF EXISTS module065_teams_outbox_events;
DELETE FROM schema_migrations WHERE migration_id='128_module065_email_teams_notification_parity';
COMMIT;
