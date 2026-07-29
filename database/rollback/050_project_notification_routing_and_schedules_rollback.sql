-- Roll back ProjectPulse Group 4 migration 050.

BEGIN;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN (
        'VIEW_COST_ALERT_ROUTING_RULES',
        'MANAGE_COST_ALERT_ROUTING_RULES',
        'VIEW_NOTIFICATION_SCHEDULES',
        'MANAGE_NOTIFICATION_SCHEDULES',
        'VIEW_NOTIFICATION_DELIVERY_MONITOR',
        'MANAGE_NOTIFICATION_DELIVERY',
        'VIEW_CLOSEOUT_NOTIFICATION_ROUTING',
        'DELIVER_PROJECT_NOTIFICATIONS'
    )
);

DELETE FROM app_feature_catalog
WHERE feature_code IN (
    'COST_ALERT_ROUTING_RULES',
    'PROJECT_NOTIFICATION_SCHEDULING',
    'NOTIFICATION_DELIVERY_MONITOR',
    'CLOSEOUT_NOTIFICATION_ROUTING'
);

DELETE FROM app_permissions
WHERE permission_code IN (
    'VIEW_COST_ALERT_ROUTING_RULES',
    'MANAGE_COST_ALERT_ROUTING_RULES',
    'VIEW_NOTIFICATION_SCHEDULES',
    'MANAGE_NOTIFICATION_SCHEDULES',
    'VIEW_NOTIFICATION_DELIVERY_MONITOR',
    'MANAGE_NOTIFICATION_DELIVERY',
    'VIEW_CLOSEOUT_NOTIFICATION_ROUTING',
    'DELIVER_PROJECT_NOTIFICATIONS'
);

DROP TRIGGER IF EXISTS trg_projectpulse050_configuration_audit_immutable
    ON project_notification_configuration_audit;
DROP TRIGGER IF EXISTS trg_projectpulse050_delivery_attempts_immutable
    ON project_notification_delivery_attempts;
DROP FUNCTION IF EXISTS projectpulse050_block_notification_evidence_mutation();

DROP TABLE IF EXISTS project_notification_configuration_audit;
DROP TABLE IF EXISTS project_notification_delivery_attempts;
DROP TABLE IF EXISTS project_notification_dispatch_recipients;
DROP TABLE IF EXISTS project_notification_dispatches;
DROP TABLE IF EXISTS project_notification_schedules;
DROP TABLE IF EXISTS project_cost_alert_routing_rules;

DELETE FROM schema_migrations
WHERE migration_id = '050_project_notification_routing_and_schedules';

COMMIT;
