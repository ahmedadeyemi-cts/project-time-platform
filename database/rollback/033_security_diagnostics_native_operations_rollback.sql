-- ProjectPulse migration 033 rollback. Destructive: use only through an approved rollback window.
BEGIN;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN (
        'VIEW_SECURITY_OPERATIONS','MANAGE_SECURITY_RESPONSE',
        'VIEW_SYSTEM_DIAGNOSTICS','MANAGE_SYSTEM_REMEDIATION'
    )
);

DELETE FROM app_permissions
WHERE permission_code IN (
    'VIEW_SECURITY_OPERATIONS','MANAGE_SECURITY_RESPONSE',
    'VIEW_SYSTEM_DIAGNOSTICS','MANAGE_SYSTEM_REMEDIATION'
);

ALTER TABLE projectpulse_security_incidents
    DROP CONSTRAINT IF EXISTS fk_projectpulse_security_incident_diagnostic;

DROP TABLE IF EXISTS projectpulse_remediation_requests;
DROP TABLE IF EXISTS projectpulse_diagnostic_findings;
DROP TABLE IF EXISTS projectpulse_diagnostic_sessions;
DROP TABLE IF EXISTS projectpulse_security_response_requests;
DROP TABLE IF EXISTS projectpulse_security_incident_events;
DROP TABLE IF EXISTS projectpulse_security_incidents;
DROP TABLE IF EXISTS projectpulse_security_alerts;

ALTER TABLE projectpulse_module_audit_events
    DROP CONSTRAINT IF EXISTS ck_projectpulse_module_audit_module;

ALTER TABLE projectpulse_module_audit_events
    ADD CONSTRAINT ck_projectpulse_module_audit_module
    CHECK (module_number IN ('064','065','066','067','068','069','070','071','072','073','074'));

DELETE FROM schema_migrations
WHERE migration_id = '033_security_diagnostics_native_operations';

COMMIT;
