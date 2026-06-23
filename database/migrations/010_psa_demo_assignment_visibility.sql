-- Project Pulse
-- Migration: 010_psa_demo_assignment_visibility.sql
-- Purpose: Make seeded PSA project task assignments visible during current validation weeks.
--
-- The PSA charter project timeline begins July 6, 2026. The current validation
-- timesheet week is June 21, 2026, so the seeded assignments from migration 009
-- are not visible in Open Tasks for the current test week. This migration moves
-- the seeded assignment effective start date earlier for validation purposes.

BEGIN;

UPDATE project_assignments pa
SET effective_start_date = DATE '2026-06-21'
FROM projects p,
     app_users u
WHERE pa.project_id = p.project_id
  AND pa.user_id = u.user_id
  AND p.project_code = 'USS-PSA-2026'
  AND u.email = 'ahmed.adeyemi@ussignal.com'
  AND pa.effective_start_date > DATE '2026-06-21';

INSERT INTO schema_migrations (migration_id, description)
VALUES ('010_psa_demo_assignment_visibility', 'Make seeded PSA assignments visible for the current validation timesheet weeks')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
