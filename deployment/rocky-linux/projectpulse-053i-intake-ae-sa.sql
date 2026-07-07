-- 053I - Intake Account Executive and Solution Architect
-- Adds AE/SA ownership to project intake and projects so closeout can resolve the correct recipients.
-- Note: project_intake_project_links trigger creation is intentionally avoided because the runtime DB user
-- is not the owner of that table in this environment. Project-link sync is handled in backend code.

BEGIN;

ALTER TABLE project_intake_requests
    ADD COLUMN IF NOT EXISTS account_executive_user_id uuid NULL,
    ADD COLUMN IF NOT EXISTS solution_architect_user_id uuid NULL;

ALTER TABLE projects
    ADD COLUMN IF NOT EXISTS account_executive_user_id uuid NULL,
    ADD COLUMN IF NOT EXISTS solution_architect_user_id uuid NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_project_intake_requests_account_executive_user'
    ) THEN
        ALTER TABLE project_intake_requests
            ADD CONSTRAINT fk_project_intake_requests_account_executive_user
            FOREIGN KEY (account_executive_user_id)
            REFERENCES app_users(user_id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_project_intake_requests_solution_architect_user'
    ) THEN
        ALTER TABLE project_intake_requests
            ADD CONSTRAINT fk_project_intake_requests_solution_architect_user
            FOREIGN KEY (solution_architect_user_id)
            REFERENCES app_users(user_id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_projects_account_executive_user'
    ) THEN
        ALTER TABLE projects
            ADD CONSTRAINT fk_projects_account_executive_user
            FOREIGN KEY (account_executive_user_id)
            REFERENCES app_users(user_id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_projects_solution_architect_user'
    ) THEN
        ALTER TABLE projects
            ADD CONSTRAINT fk_projects_solution_architect_user
            FOREIGN KEY (solution_architect_user_id)
            REFERENCES app_users(user_id);
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS idx_project_intake_requests_account_executive_user_id
    ON project_intake_requests(account_executive_user_id);

CREATE INDEX IF NOT EXISTS idx_project_intake_requests_solution_architect_user_id
    ON project_intake_requests(solution_architect_user_id);

CREATE INDEX IF NOT EXISTS idx_projects_account_executive_user_id
    ON projects(account_executive_user_id);

CREATE INDEX IF NOT EXISTS idx_projects_solution_architect_user_id
    ON projects(solution_architect_user_id);

-- Backfill existing linked projects where permissions allow normal SELECT/UPDATE.
UPDATE projects p
SET account_executive_user_id = COALESCE(p.account_executive_user_id, pir.account_executive_user_id),
    solution_architect_user_id = COALESCE(p.solution_architect_user_id, pir.solution_architect_user_id),
    updated_at = NOW()
FROM project_intake_project_links link
JOIN project_intake_requests pir
  ON pir.project_intake_request_id = link.project_intake_request_id
WHERE link.project_id = p.project_id
  AND COALESCE(link.is_active, TRUE) = TRUE
  AND (
      pir.account_executive_user_id IS NOT NULL
      OR pir.solution_architect_user_id IS NOT NULL
  );

COMMIT;
