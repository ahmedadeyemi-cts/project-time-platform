-- 019M-CI Production Operations Acknowledgments + Sign-Off Evidence

CREATE TABLE IF NOT EXISTS production_operations_acknowledgments (
    production_operations_acknowledgment_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    route_key text NOT NULL,
    operation_key text NOT NULL,
    operation_title text NOT NULL,
    acknowledgment_status text NOT NULL DEFAULT 'acknowledged',
    acknowledgment_note text NULL,
    acknowledged_by_user_id uuid NULL,
    acknowledged_by_email text NULL,
    acknowledged_at timestamptz NOT NULL DEFAULT now(),
    evidence_snapshot jsonb NOT NULL DEFAULT '{}'::jsonb,
    is_active boolean NOT NULL DEFAULT true
);

CREATE INDEX IF NOT EXISTS idx_prod_ops_ack_route_key
    ON production_operations_acknowledgments(route_key);

CREATE INDEX IF NOT EXISTS idx_prod_ops_ack_operation_key
    ON production_operations_acknowledgments(operation_key);

CREATE INDEX IF NOT EXISTS idx_prod_ops_ack_acknowledged_at
    ON production_operations_acknowledgments(acknowledged_at DESC);

CREATE INDEX IF NOT EXISTS idx_prod_ops_ack_active
    ON production_operations_acknowledgments(is_active)
    WHERE is_active = true;

-- Runtime grants for the ProjectPulse API database login roles.
DO $$
DECLARE
    role_record record;
BEGIN
    FOR role_record IN
        SELECT rolname
        FROM pg_roles
        WHERE rolcanlogin = true
          AND rolname <> 'postgres'
    LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA public TO %I', role_record.rolname);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.production_operations_acknowledgments TO %I', role_record.rolname);
    END LOOP;
END $$;
