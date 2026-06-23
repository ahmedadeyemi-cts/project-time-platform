-- Project Time Platform
-- Seed data: Non-project time categories.
-- Purpose: Seed initial non-project categories visible on the time entry screen.

BEGIN;

INSERT INTO non_project_time_categories (
    category_code,
    category_name,
    category_description,
    utilization_classification,
    requires_approval,
    display_order
)
VALUES
    ('ADMINISTRATIVE', 'Administrative', 'Administrative non-project work.', 'administrative', TRUE, 10),
    ('BEREAVEMENT', 'Bereavement', 'Approved bereavement time.', 'leave', TRUE, 20),
    ('COMP_TIME', 'Comp Time', 'Approved compensatory time.', 'leave', TRUE, 30),
    ('HOLIDAY', 'Holiday', 'Approved holiday time.', 'paid_time_off', FALSE, 40),
    ('JURY_DUTY', 'Jury Duty', 'Approved jury duty time.', 'leave', TRUE, 50),
    ('LTD', 'Long-Term Disability', 'Approved long-term leave category.', 'leave', TRUE, 60),
    ('PEER_SUPPORT', 'Peer Support', 'Approved peer support time.', 'non_billable', TRUE, 70),
    ('PERSONAL_HOLIDAY', 'Personal Holiday', 'Approved personal holiday time.', 'paid_time_off', TRUE, 80),
    ('FMLA_APPROVED', 'Pre-Approved FMLA', 'Approved FMLA time.', 'leave', TRUE, 90),
    ('STD', 'Short-Term Disability', 'Approved short-term leave category.', 'leave', TRUE, 100),
    ('SICK_LEAVE', 'Sick Leave', 'Approved sick leave time.', 'paid_time_off', TRUE, 110),
    ('UNPAID_TIME_OFF', 'Time off without pay', 'Approved unpaid time off.', 'unpaid_time_off', TRUE, 120),
    ('TRAINING', 'Training', 'Approved training time.', 'training', TRUE, 130),
    ('VACATION', 'Vacation', 'Approved vacation time.', 'paid_time_off', TRUE, 140),
    ('VOLUNTEER_TIME', 'Volunteer Time', 'Approved volunteer time.', 'non_billable', TRUE, 150)
ON CONFLICT (category_code) DO UPDATE SET
    category_name = EXCLUDED.category_name,
    category_description = EXCLUDED.category_description,
    utilization_classification = EXCLUDED.utilization_classification,
    requires_approval = EXCLUDED.requires_approval,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

COMMIT;
