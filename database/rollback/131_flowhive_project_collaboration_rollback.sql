-- Roll back application code first. Never discard retained contacts/meeting evidence automatically.
BEGIN;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM project_flowhive_contacts) OR EXISTS(SELECT 1 FROM project_flowhive_meeting_drafts) THEN
  RAISE EXCEPTION 'Retained project collaboration records require an approved archive before schema removal.';
 END IF;
END $$;
DROP TABLE project_flowhive_meeting_drafts;
DROP TABLE project_flowhive_contacts;
DROP FUNCTION project_flowhive_enforce_contact_limit();
DELETE FROM schema_migrations WHERE migration_id='131_flowhive_project_collaboration';
COMMIT;
