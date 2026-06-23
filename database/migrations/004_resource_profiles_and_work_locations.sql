-- Project Time Platform
-- Migration: 004_resource_profiles_and_work_locations.sql
-- Purpose: Add work locations, resource profiles, functions, and qualifications.

BEGIN;

CREATE TABLE IF NOT EXISTS work_location_groups (
    work_location_group_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    group_code VARCHAR(100) NOT NULL UNIQUE,
    group_name VARCHAR(255) NOT NULL,
    group_description TEXT,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    display_order INTEGER NOT NULL DEFAULT 100,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS work_locations (
    work_location_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    work_location_group_id UUID REFERENCES work_location_groups(work_location_group_id),
    location_code VARCHAR(100) NOT NULL UNIQUE,
    location_name VARCHAR(255) NOT NULL,
    city VARCHAR(255),
    state_region VARCHAR(255),
    country VARCHAR(255) NOT NULL DEFAULT 'United States',
    time_zone VARCHAR(100),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    display_order INTEGER NOT NULL DEFAULT 100,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS resource_profiles (
    resource_profile_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL UNIQUE REFERENCES app_users(user_id) ON DELETE CASCADE,
    resource_number VARCHAR(100) UNIQUE,
    resource_type VARCHAR(100) NOT NULL DEFAULT 'full_time',
    primary_function VARCHAR(255),
    time_zone VARCHAR(100),
    work_location_id UUID REFERENCES work_locations(work_location_id),
    availability_status VARCHAR(50) NOT NULL DEFAULT 'online',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_resource_type CHECK (resource_type IN ('full_time', 'part_time', 'contractor', 'temporary')),
    CONSTRAINT chk_resource_availability_status CHECK (availability_status IN ('online', 'offline', 'away', 'inactive'))
);

CREATE TABLE IF NOT EXISTS resource_functions (
    resource_function_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    function_name VARCHAR(255) NOT NULL,
    is_primary BOOLEAN NOT NULL DEFAULT FALSE,
    effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_end_date DATE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_resource_function_effective UNIQUE (user_id, function_name, effective_start_date),
    CONSTRAINT chk_resource_function_dates CHECK (effective_end_date IS NULL OR effective_end_date >= effective_start_date)
);

CREATE TABLE IF NOT EXISTS resource_qualifications (
    resource_qualification_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    qualification_category VARCHAR(255) NOT NULL,
    qualification_name VARCHAR(255) NOT NULL,
    competency VARCHAR(100),
    years_of_experience NUMERIC(5,2),
    effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_end_date DATE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_resource_qualification_years CHECK (years_of_experience IS NULL OR years_of_experience >= 0),
    CONSTRAINT chk_resource_qualification_dates CHECK (effective_end_date IS NULL OR effective_end_date >= effective_start_date)
);

ALTER TABLE time_entries
    ADD COLUMN IF NOT EXISTS work_location_group_id UUID REFERENCES work_location_groups(work_location_group_id),
    ADD COLUMN IF NOT EXISTS work_location_id UUID REFERENCES work_locations(work_location_id);

DROP TRIGGER IF EXISTS trg_work_location_groups_updated_at ON work_location_groups;
CREATE TRIGGER trg_work_location_groups_updated_at
    BEFORE UPDATE ON work_location_groups
    FOR EACH ROW
    EXECUTE FUNCTION set_updated_at();

DROP TRIGGER IF EXISTS trg_work_locations_updated_at ON work_locations;
CREATE TRIGGER trg_work_locations_updated_at
    BEFORE UPDATE ON work_locations
    FOR EACH ROW
    EXECUTE FUNCTION set_updated_at();

DROP TRIGGER IF EXISTS trg_resource_profiles_updated_at ON resource_profiles;
CREATE TRIGGER trg_resource_profiles_updated_at
    BEFORE UPDATE ON resource_profiles
    FOR EACH ROW
    EXECUTE FUNCTION set_updated_at();

DROP TRIGGER IF EXISTS trg_resource_qualifications_updated_at ON resource_qualifications;
CREATE TRIGGER trg_resource_qualifications_updated_at
    BEFORE UPDATE ON resource_qualifications
    FOR EACH ROW
    EXECUTE FUNCTION set_updated_at();

CREATE INDEX IF NOT EXISTS idx_work_location_groups_active ON work_location_groups(is_active);
CREATE INDEX IF NOT EXISTS idx_work_locations_group ON work_locations(work_location_group_id);
CREATE INDEX IF NOT EXISTS idx_work_locations_active ON work_locations(is_active);
CREATE INDEX IF NOT EXISTS idx_resource_profiles_user ON resource_profiles(user_id);
CREATE INDEX IF NOT EXISTS idx_resource_profiles_location ON resource_profiles(work_location_id);
CREATE INDEX IF NOT EXISTS idx_resource_functions_user ON resource_functions(user_id);
CREATE INDEX IF NOT EXISTS idx_resource_qualifications_user ON resource_qualifications(user_id);
CREATE INDEX IF NOT EXISTS idx_time_entries_work_location ON time_entries(work_location_id);

INSERT INTO schema_migrations (migration_id, description)
VALUES ('004_resource_profiles_and_work_locations', 'Add work locations, resource profiles, functions, and qualifications')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
