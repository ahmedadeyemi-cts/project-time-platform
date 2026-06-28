-- 019M-P production-shaped multi-engineer resource request assignments.
-- Supports up to 15 engineers per engineering resource request.

CREATE TABLE IF NOT EXISTS engineering_resource_request_assignments (
    engineering_resource_request_assignment_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    engineering_resource_request_id uuid NOT NULL REFERENCES engineering_resource_requests(engineering_resource_request_id) ON DELETE CASCADE,
    user_id uuid NOT NULL REFERENCES app_users(user_id),
    assigned_by_user_id uuid NULL REFERENCES app_users(user_id),
    assignment_status character varying(60) NOT NULL DEFAULT 'proposed',
    allocated_hours numeric NOT NULL DEFAULT 0,
    allocation_percent numeric NULL,
    assignment_notes text NULL,
    assigned_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    UNIQUE(engineering_resource_request_id, user_id)
);

CREATE INDEX IF NOT EXISTS idx_err_assignments_request
ON engineering_resource_request_assignments(engineering_resource_request_id);

CREATE INDEX IF NOT EXISTS idx_err_assignments_user
ON engineering_resource_request_assignments(user_id);

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

-- Backfill current single fulfilled engineer into the production-shaped child assignment table.
INSERT INTO engineering_resource_request_assignments (
    engineering_resource_request_id,
    user_id,
    assignment_status,
    allocated_hours,
    assignment_notes
)
SELECT
    engineering_resource_request_id,
    fulfilled_by_user_id,
    'assigned',
    requested_hours,
    'Backfilled from original fulfilled engineer field.'
FROM engineering_resource_requests
WHERE fulfilled_by_user_id IS NOT NULL
ON CONFLICT (engineering_resource_request_id, user_id) DO NOTHING;
