-- Guarded rollback for migration 081. Processed evidence is preserved.

BEGIN;

DO $projectpulse081_rollback_guard$
DECLARE
    service_identity_has_jobs BOOLEAN := FALSE;
BEGIN
    IF to_regclass('public.pulse_ai_document_processing_jobs') IS NOT NULL THEN
        EXECUTE $guard_query$
            SELECT EXISTS (
                SELECT 1
                FROM public.pulse_ai_document_processing_jobs
                WHERE requested_by_user_id = $1
            )
        $guard_query$
        INTO service_identity_has_jobs
        USING '08100000-0000-0000-0000-000000000001'::UUID;
    END IF;

    IF service_identity_has_jobs THEN
        RAISE EXCEPTION 'Rollback 081 refused: the Celar AI document service identity owns processing evidence.';
    END IF;
END;
$projectpulse081_rollback_guard$;

DROP TRIGGER IF EXISTS trg_projectpulse081_repair_work_register_bridge_name
    ON work_register_documents;
DROP FUNCTION IF EXISTS projectpulse081_repair_work_register_bridge_name();
DROP FUNCTION IF EXISTS projectpulse081_supported_file_name(TEXT, TEXT);

DELETE FROM app_user_role_assignments assignment
USING module081_private_runtime_records recorded
WHERE recorded.record_type = 'role_assignment'
  AND assignment.app_user_role_assignment_id = recorded.record_id;

DELETE FROM app_role_permissions role_permission
USING module081_private_runtime_records recorded
WHERE recorded.record_type = 'role_permission'
  AND role_permission.app_role_permission_id = recorded.record_id;

DELETE FROM app_roles role
USING module081_private_runtime_records recorded
WHERE recorded.record_type = 'service_role'
  AND role.app_role_id = recorded.record_id;

DELETE FROM app_users service_user
USING module081_private_runtime_records recorded
WHERE recorded.record_type = 'service_user'
  AND service_user.user_id = recorded.record_id;

DROP TABLE IF EXISTS module081_private_runtime_records;

DELETE FROM schema_migrations
WHERE migration_id = '081_celar_ai_private_runtime_activation';

COMMIT;
