-- Project Health Dashboard
-- Rollback: 010_psa_demo_assignment_visibility_rollback.sql

BEGIN;

UPDATE project_assignments pa
SET effective_start_date = DATE '2026-07-06'
FROM projects p,
     app_users u
WHERE pa.project_id = p.project_id
  AND pa.user_id = u.user_id
  AND p.project_code = 'USS-PSA-2026'
  AND u.email = 'ahmed.adeyemi@ussignal.com'
  AND pa.effective_start_date = DATE '2026-06-21';

DELETE FROM schema_migrations
WHERE migration_id = '010_psa_demo_assignment_visibility';

COMMIT;
