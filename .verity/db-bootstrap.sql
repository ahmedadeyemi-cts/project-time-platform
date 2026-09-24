-- Verity deploy bootstrap — provisioning prerequisites the migration set assumes
-- already exist (the live DB was provisioned with these; a fresh container DB is not).
-- Idempotent; runs before any migration.

-- Application role that many migrations GRANT privileges to.
DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ptp_app') THEN
    CREATE ROLE ptp_app NOLOGIN;
  END IF;
END $$;
