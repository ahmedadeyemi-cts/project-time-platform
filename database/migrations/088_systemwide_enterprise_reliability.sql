-- ProjectPulse migration 088
-- System-wide enterprise reliability, authoritative intake ownership, and audit indexing.
-- Additive and idempotent. No Production execution is performed by this file.

BEGIN;

DO $projectpulse088_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.project_intake_requests') IS NULL THEN
        RAISE EXCEPTION 'Migration 088 prerequisites are not available.';
    END IF;
END
$projectpulse088_prerequisites$;

ALTER TABLE project_intake_requests
    ADD COLUMN IF NOT EXISTS account_executive_user_id UUID NULL,
    ADD COLUMN IF NOT EXISTS solution_architect_user_id UUID NULL;

ALTER TABLE projects
    ADD COLUMN IF NOT EXISTS account_executive_user_id UUID NULL,
    ADD COLUMN IF NOT EXISTS solution_architect_user_id UUID NULL;

DO $projectpulse088_foreign_keys$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_project_intake_requests_account_executive_user') THEN
        ALTER TABLE project_intake_requests
            ADD CONSTRAINT fk_project_intake_requests_account_executive_user
            FOREIGN KEY(account_executive_user_id) REFERENCES app_users(user_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_project_intake_requests_solution_architect_user') THEN
        ALTER TABLE project_intake_requests
            ADD CONSTRAINT fk_project_intake_requests_solution_architect_user
            FOREIGN KEY(solution_architect_user_id) REFERENCES app_users(user_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_projects_account_executive_user') THEN
        ALTER TABLE projects
            ADD CONSTRAINT fk_projects_account_executive_user
            FOREIGN KEY(account_executive_user_id) REFERENCES app_users(user_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_projects_solution_architect_user') THEN
        ALTER TABLE projects
            ADD CONSTRAINT fk_projects_solution_architect_user
            FOREIGN KEY(solution_architect_user_id) REFERENCES app_users(user_id);
    END IF;
END
$projectpulse088_foreign_keys$;

CREATE INDEX IF NOT EXISTS idx_project_intake_requests_account_executive_user_id
    ON project_intake_requests(account_executive_user_id);
CREATE INDEX IF NOT EXISTS idx_project_intake_requests_solution_architect_user_id
    ON project_intake_requests(solution_architect_user_id);
CREATE INDEX IF NOT EXISTS idx_projects_account_executive_user_id
    ON projects(account_executive_user_id);
CREATE INDEX IF NOT EXISTS idx_projects_solution_architect_user_id
    ON projects(solution_architect_user_id);

DO $projectpulse088_backfill$
BEGIN
    IF to_regclass('public.project_intake_project_links') IS NOT NULL THEN
        UPDATE projects project
        SET account_executive_user_id=COALESCE(project.account_executive_user_id,intake.account_executive_user_id),
            solution_architect_user_id=COALESCE(project.solution_architect_user_id,intake.solution_architect_user_id),
            updated_at=NOW()
        FROM project_intake_project_links link
        JOIN project_intake_requests intake
          ON intake.project_intake_request_id=link.project_intake_request_id
        WHERE link.project_id=project.project_id
          AND COALESCE(link.is_active,TRUE)=TRUE
          AND (intake.account_executive_user_id IS NOT NULL OR intake.solution_architect_user_id IS NOT NULL);
    END IF;
END
$projectpulse088_backfill$;

DO $projectpulse088_audit_indexes$
BEGIN
    IF to_regclass('public.projectpulse_system_audit_events') IS NOT NULL THEN
        CREATE INDEX IF NOT EXISTS idx_projectpulse_system_audit_events_time_desc
            ON projectpulse_system_audit_events(event_time DESC);
        CREATE INDEX IF NOT EXISTS idx_projectpulse_system_audit_events_category_status
            ON projectpulse_system_audit_events(category,status,event_time DESC);
        CREATE INDEX IF NOT EXISTS idx_projectpulse_system_audit_events_event_type
            ON projectpulse_system_audit_events(event_type,event_time DESC);
        CREATE INDEX IF NOT EXISTS idx_projectpulse_system_audit_events_correlation
            ON projectpulse_system_audit_events(correlation_id)
            WHERE correlation_id <> '';
        CREATE INDEX IF NOT EXISTS idx_projectpulse_system_audit_events_target
            ON projectpulse_system_audit_events(target_type,target_id,event_time DESC);
    END IF;
    IF to_regclass('public.auth_login_events') IS NOT NULL THEN
        CREATE INDEX IF NOT EXISTS idx_auth_login_events_created_desc
            ON auth_login_events(created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_auth_login_events_user_result
            ON auth_login_events(user_id,login_result,created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_auth_login_events_username_result
            ON auth_login_events(LOWER(username),login_result,created_at DESC);
    END IF;
END
$projectpulse088_audit_indexes$;

DO $projectpulse088_verify$
DECLARE
    missing_column TEXT;
BEGIN
    SELECT required.column_name INTO missing_column
    FROM (VALUES
        ('project_intake_requests','account_executive_user_id'),
        ('project_intake_requests','solution_architect_user_id'),
        ('projects','account_executive_user_id'),
        ('projects','solution_architect_user_id')
    ) AS required(table_name,column_name)
    WHERE NOT EXISTS (
        SELECT 1 FROM information_schema.columns column_row
        WHERE column_row.table_schema='public'
          AND column_row.table_name=required.table_name
          AND column_row.column_name=required.column_name)
    LIMIT 1;
    IF missing_column IS NOT NULL THEN
        RAISE EXCEPTION 'Migration 088 failed to create required column %.', missing_column;
    END IF;
END
$projectpulse088_verify$;

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES(
    '088_systemwide_enterprise_reliability',
    'Add authoritative intake ownership fields and indexed immutable authentication, change, failure, and dependency audit evidence',
    NOW())
ON CONFLICT(migration_id) DO UPDATE
SET description=EXCLUDED.description,
    applied_at=EXCLUDED.applied_at;

COMMIT;
