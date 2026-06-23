-- Project Time Platform
-- Seed data: Default work location groups and locations.

BEGIN;

INSERT INTO work_location_groups (group_code, group_name, group_description, display_order)
VALUES
    ('REMOTE', 'Remote', 'Remote or work-from-home locations.', 10),
    ('OFFICE', 'Office', 'Company office locations.', 20),
    ('CUSTOMER_SITE', 'Customer Site', 'Customer or project site locations.', 30),
    ('OTHER', 'Other', 'Other approved work locations.', 40)
ON CONFLICT (group_code) DO UPDATE SET
    group_name = EXCLUDED.group_name,
    group_description = EXCLUDED.group_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

INSERT INTO work_locations (
    work_location_group_id,
    location_code,
    location_name,
    city,
    state_region,
    country,
    time_zone,
    display_order
)
SELECT work_location_group_id, 'LOS_ANGELES_CA', 'Los Angeles, CA', 'Los Angeles', 'CA', 'United States', 'America/Los_Angeles', 10
FROM work_location_groups
WHERE group_code = 'OFFICE'
ON CONFLICT (location_code) DO UPDATE SET
    location_name = EXCLUDED.location_name,
    city = EXCLUDED.city,
    state_region = EXCLUDED.state_region,
    country = EXCLUDED.country,
    time_zone = EXCLUDED.time_zone,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

COMMIT;
