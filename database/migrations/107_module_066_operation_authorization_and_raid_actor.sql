-- Module 066 forward repair: bind immutable RAID deletion evidence to the
-- authenticated transaction actor and separate meeting/reminder writes from
-- broad planner administration. Migration 103 remains unchanged.
BEGIN;
SELECT pg_advisory_xact_lock(660107);

DO $prerequisites$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id = '103_module_066_flowhive_enterprise_psa_revamp') THEN
        RAISE EXCEPTION 'Migration 107 requires Module 066 migration 103';
    END IF;
    IF to_regclass('public.project_flowhive_raid_items') IS NULL
       OR to_regclass('public.project_flowhive_raid_events') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL THEN
        RAISE EXCEPTION 'Migration 107 requires Module 066 PSA and role-permission tables';
    END IF;
END $prerequisites$;

CREATE OR REPLACE FUNCTION projectpulse103_capture_raid_event()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse107_capture_raid_event_body$
DECLARE
    actor UUID;
    transaction_actor TEXT := current_setting('projectpulse.current_actor', true);
BEGIN
    IF transaction_actor ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' THEN
        actor := transaction_actor::uuid;
    ELSE
        actor := CASE WHEN TG_OP = 'DELETE' THEN OLD.updated_by_user_id ELSE NEW.updated_by_user_id END;
    END IF;
    INSERT INTO project_flowhive_raid_events(raid_item_id,project_id,action_code,actor_user_id,prior_json,new_json)
    VALUES (
        CASE WHEN TG_OP = 'DELETE' THEN OLD.raid_item_id ELSE NEW.raid_item_id END,
        CASE WHEN TG_OP = 'DELETE' THEN OLD.project_id ELSE NEW.project_id END,
        CASE TG_OP WHEN 'INSERT' THEN 'created' WHEN 'UPDATE' THEN 'updated' ELSE 'deleted' END,
        actor,
        CASE WHEN TG_OP IN ('UPDATE','DELETE') THEN to_jsonb(OLD) ELSE NULL END,
        CASE WHEN TG_OP IN ('INSERT','UPDATE') THEN to_jsonb(NEW) ELSE NULL END);
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END;
$projectpulse107_capture_raid_event_body$;

WITH desired(role_code, permission_code) AS (
    VALUES
        ('SUPER_ADMINISTRATOR', 'MANAGE_FLOWHIVE_MEETINGS_066'),
        ('SUPER_ADMINISTRATOR', 'MANAGE_FLOWHIVE_TASK_REMINDERS_066'),
        ('SYSTEM_ADMINISTRATOR', 'MANAGE_FLOWHIVE_MEETINGS_066'),
        ('SYSTEM_ADMINISTRATOR', 'MANAGE_FLOWHIVE_TASK_REMINDERS_066'),
        ('ADMINISTRATOR', 'MANAGE_FLOWHIVE_MEETINGS_066'),
        ('ADMINISTRATOR', 'MANAGE_FLOWHIVE_TASK_REMINDERS_066'),
        ('PROJECT_MANAGER', 'MANAGE_FLOWHIVE_MEETINGS_066'),
        ('PROJECT_MANAGER', 'MANAGE_FLOWHIVE_TASK_REMINDERS_066'),
        ('PROJECT_MANAGEMENT', 'MANAGE_FLOWHIVE_MEETINGS_066'),
        ('PROJECT_MANAGEMENT', 'MANAGE_FLOWHIVE_TASK_REMINDERS_066'),
        ('PROJECT_MANAGEMENT_LEAD', 'MANAGE_FLOWHIVE_MEETINGS_066'),
        ('PROJECT_MANAGEMENT_LEAD', 'MANAGE_FLOWHIVE_TASK_REMINDERS_066'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD', 'MANAGE_FLOWHIVE_MEETINGS_066'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD', 'MANAGE_FLOWHIVE_TASK_REMINDERS_066'),
        ('PM_TEAM_LEAD', 'MANAGE_FLOWHIVE_MEETINGS_066'),
        ('PM_TEAM_LEAD', 'MANAGE_FLOWHIVE_TASK_REMINDERS_066')
), candidates AS (
    SELECT role.app_role_id, permission.app_permission_id
    FROM desired
    JOIN app_roles role ON upper(role.role_code) = desired.role_code AND role.is_active = TRUE
    JOIN app_permissions permission ON permission.permission_code = desired.permission_code
)
INSERT INTO app_role_permissions(app_role_id, app_permission_id, created_at)
SELECT app_role_id, app_permission_id, NOW()
FROM candidates
ON CONFLICT(app_role_id, app_permission_id) DO NOTHING;

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('107_module_066_operation_authorization_and_raid_actor',
       'Bind RAID deletion audit to the authenticated transaction actor and grant operation-specific meeting/reminder permissions',NOW())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
