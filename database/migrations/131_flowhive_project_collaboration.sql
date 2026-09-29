BEGIN;
-- Project contacts are NOT application accounts, customer organizations or access grants.
CREATE TABLE IF NOT EXISTS project_flowhive_contacts (
 project_contact_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 project_id uuid NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
 display_name varchar(200) NOT NULL CHECK(length(btrim(display_name))>=2),
 email varchar(320) NOT NULL CHECK(length(btrim(email))>=3),
 phone varchar(80) NOT NULL DEFAULT '', title varchar(160) NOT NULL DEFAULT '',
 organization varchar(200) NOT NULL DEFAULT '', contact_kind varchar(24) NOT NULL DEFAULT 'customer'
   CHECK(contact_kind IN ('customer','vendor','partner')),
 is_active boolean NOT NULL DEFAULT true, row_version uuid NOT NULL DEFAULT gen_random_uuid(),
 created_by_user_id uuid NOT NULL REFERENCES app_users(user_id),
 updated_by_user_id uuid NOT NULL REFERENCES app_users(user_id),
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_flowhive_project_contact_email ON project_flowhive_contacts(project_id,lower(email)) WHERE is_active;
CREATE OR REPLACE FUNCTION project_flowhive_enforce_contact_limit() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM 1 FROM projects WHERE project_id=NEW.project_id FOR UPDATE;
 IF NEW.is_active AND (SELECT count(*) FROM project_flowhive_contacts WHERE project_id=NEW.project_id
   AND is_active AND project_contact_id<>NEW.project_contact_id)>=15 THEN
   RAISE EXCEPTION 'A project can have at most 15 active external contacts.' USING ERRCODE='23514',CONSTRAINT='flowhive_project_contact_limit';
 END IF;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_flowhive_project_contact_limit ON project_flowhive_contacts;
CREATE TRIGGER trg_flowhive_project_contact_limit BEFORE INSERT OR UPDATE ON project_flowhive_contacts
 FOR EACH ROW EXECUTE FUNCTION project_flowhive_enforce_contact_limit();
CREATE TABLE IF NOT EXISTS project_flowhive_meeting_drafts (
 meeting_draft_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 project_id uuid NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
 title varchar(240) NOT NULL, agenda text NOT NULL DEFAULT '', location varchar(300) NOT NULL DEFAULT '',
 starts_at timestamptz NOT NULL, ends_at timestamptz NOT NULL CHECK(ends_at>starts_at AND ends_at<=starts_at+interval '24 hours'),
 timezone_name varchar(100) NOT NULL, attendee_references text[] NOT NULL DEFAULT '{}',
 created_by_user_id uuid NOT NULL REFERENCES app_users(user_id),created_at timestamptz NOT NULL DEFAULT now(),
 status varchar(16) NOT NULL DEFAULT 'draft' CHECK(status='draft')
);
CREATE INDEX IF NOT EXISTS ix_flowhive_meeting_drafts_project ON project_flowhive_meeting_drafts(project_id,starts_at);
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='ptp_app') THEN
  GRANT SELECT,INSERT,UPDATE ON project_flowhive_contacts TO ptp_app;
  GRANT SELECT,INSERT ON project_flowhive_meeting_drafts TO ptp_app;
 END IF;
END $$;
INSERT INTO schema_migrations(migration_id,description) VALUES('131_flowhive_project_collaboration',
 'Project-scoped external contacts (15 active maximum) and unsent meeting drafts; no accounts, access grants or invitations') ON CONFLICT DO NOTHING;
COMMIT;
