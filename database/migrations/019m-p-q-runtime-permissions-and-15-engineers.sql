-- 019M-P/Q runtime permissions and staffing limit update.
-- Fixes HTTP 500 caused by runtime DB user lacking access to newly created tables.
-- Updates engineering resource request assignment limit from 8 to 15.

GRANT USAGE ON SCHEMA public TO "ptp_app";

GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
    app_users,
    app_roles,
    app_user_role_assignments,
    app_permissions,
    app_role_permissions,
    audit_logs,
    clients,
    projects,
    project_tasks,
    project_assignments,
    project_intake_requests,
    project_intake_documents,
    engineering_resource_requests,
    engineering_resource_request_assignments,
    resource_profiles,
    resource_functions,
    resource_qualifications,
    resource_capacity_plans
TO "ptp_app";

GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO "ptp_app";

CREATE OR REPLACE FUNCTION enforce_engineering_resource_request_assignment_limit()
RETURNS trigger AS $$
BEGIN
    IF (
        SELECT COUNT(*)
        FROM engineering_resource_request_assignments
        WHERE engineering_resource_request_id = NEW.engineering_resource_request_id
          AND engineering_resource_request_assignment_id <> COALESCE(NEW.engineering_resource_request_assignment_id, gen_random_uuid())
    ) >= 15 THEN
        RAISE EXCEPTION 'Engineering resource request cannot have more than 15 assigned engineers.';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_engineering_resource_request_assignment_limit
ON engineering_resource_request_assignments;

CREATE TRIGGER trg_engineering_resource_request_assignment_limit
BEFORE INSERT OR UPDATE ON engineering_resource_request_assignments
FOR EACH ROW
EXECUTE FUNCTION enforce_engineering_resource_request_assignment_limit();

INSERT INTO schema_migrations (migration_id, description)
VALUES ('019m_p_q_runtime_permissions_and_15_engineers', 'Grant runtime access to intake/workspace tables and raise engineer assignment limit to 15')
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = NOW();
