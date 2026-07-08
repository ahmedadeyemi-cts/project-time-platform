-- 055B - Rate Card Administration Foundation
-- Sidecar-safe: does not ALTER existing project/customer/user tables.

BEGIN;

CREATE TABLE IF NOT EXISTS work_rate_cards (
    rate_card_id uuid PRIMARY KEY,
    rate_card_code varchar(120) NOT NULL UNIQUE,
    rate_card_name varchar(220) NOT NULL,
    rate_card_type varchar(80) NOT NULL DEFAULT 'standard',
    client_id uuid NULL,
    customer_name_snapshot varchar(220) NULL,
    status varchar(40) NOT NULL DEFAULT 'active',
    effective_start_date date NOT NULL DEFAULT CURRENT_DATE,
    effective_end_date date NULL,
    source_system varchar(80) NOT NULL DEFAULT 'manual',
    description text NOT NULL DEFAULT '',
    is_system_seeded boolean NOT NULL DEFAULT false,
    created_by_user_id uuid NULL,
    updated_by_user_id uuid NULL,
    created_at timestamp with time zone NOT NULL DEFAULT NOW(),
    updated_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_work_rate_cards_client_id ON work_rate_cards(client_id);
CREATE INDEX IF NOT EXISTS idx_work_rate_cards_type_status ON work_rate_cards(rate_card_type, status);
CREATE INDEX IF NOT EXISTS idx_work_rate_cards_effective_dates ON work_rate_cards(effective_start_date, effective_end_date);

CREATE TABLE IF NOT EXISTS work_rate_card_lines (
    rate_line_id uuid PRIMARY KEY,
    rate_card_id uuid NOT NULL REFERENCES work_rate_cards(rate_card_id) ON DELETE CASCADE,
    sku_code varchar(160) NOT NULL,
    display_name varchar(220) NOT NULL,
    description text NOT NULL DEFAULT '',
    labor_category varchar(80) NOT NULL DEFAULT 'engineering',
    time_type varchar(80) NOT NULL DEFAULT 'normal',
    unit_type varchar(60) NOT NULL DEFAULT 'hour',
    rate_amount numeric(12,2) NOT NULL DEFAULT 0,
    minimum_billing_hours numeric(8,2) NOT NULL DEFAULT 0,
    remote_minimum_hours numeric(8,2) NOT NULL DEFAULT 0,
    onsite_minimum_hours numeric(8,2) NOT NULL DEFAULT 0,
    daytime_minimum_hours numeric(8,2) NOT NULL DEFAULT 0,
    afterhours_weekend_holiday_minimum_hours numeric(8,2) NOT NULL DEFAULT 0,
    business_hours_text varchar(220) NOT NULL DEFAULT '',
    billable_default boolean NOT NULL DEFAULT true,
    utilization_eligible_default boolean NOT NULL DEFAULT true,
    is_emergency boolean NOT NULL DEFAULT false,
    is_travel boolean NOT NULL DEFAULT false,
    override_allowed boolean NOT NULL DEFAULT true,
    is_active boolean NOT NULL DEFAULT true,
    display_order integer NOT NULL DEFAULT 100,
    notes text NOT NULL DEFAULT '',
    created_by_user_id uuid NULL,
    updated_by_user_id uuid NULL,
    created_at timestamp with time zone NOT NULL DEFAULT NOW(),
    updated_at timestamp with time zone NOT NULL DEFAULT NOW(),
    UNIQUE(rate_card_id, sku_code, time_type)
);

CREATE INDEX IF NOT EXISTS idx_work_rate_card_lines_card ON work_rate_card_lines(rate_card_id);
CREATE INDEX IF NOT EXISTS idx_work_rate_card_lines_category_time ON work_rate_card_lines(labor_category, time_type);
CREATE INDEX IF NOT EXISTS idx_work_rate_card_lines_active ON work_rate_card_lines(is_active);

CREATE TABLE IF NOT EXISTS work_rate_card_change_history (
    rate_card_change_history_id uuid PRIMARY KEY,
    entity_type varchar(80) NOT NULL,
    entity_id uuid NOT NULL,
    action varchar(120) NOT NULL,
    change_summary text NOT NULL DEFAULT '',
    changed_by_user_id uuid NULL,
    old_value_json jsonb NULL,
    new_value_json jsonb NULL,
    changed_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_work_rate_card_change_history_entity ON work_rate_card_change_history(entity_type, entity_id);
CREATE INDEX IF NOT EXISTS idx_work_rate_card_change_history_changed_at ON work_rate_card_change_history(changed_at DESC);

WITH customer_match AS (
    SELECT
        (SELECT client_id FROM clients WHERE lower(client_name) LIKE '%toyota%' OR lower(client_code) LIKE '%toyota%' ORDER BY client_name LIMIT 1) AS toyota_client_id,
        (SELECT client_name FROM clients WHERE lower(client_name) LIKE '%toyota%' OR lower(client_code) LIKE '%toyota%' ORDER BY client_name LIMIT 1) AS toyota_client_name,
        (SELECT client_id FROM clients WHERE lower(client_name) LIKE '%hyundai%' OR lower(client_code) LIKE '%hyundai%' ORDER BY client_name LIMIT 1) AS hyundai_client_id,
        (SELECT client_name FROM clients WHERE lower(client_name) LIKE '%hyundai%' OR lower(client_code) LIKE '%hyundai%' ORDER BY client_name LIMIT 1) AS hyundai_client_name
)
INSERT INTO work_rate_cards (
    rate_card_id,
    rate_card_code,
    rate_card_name,
    rate_card_type,
    client_id,
    customer_name_snapshot,
    status,
    effective_start_date,
    source_system,
    description,
    is_system_seeded
)
SELECT *
FROM (
    SELECT
        md5('055B:STANDARD_COMPANY_RATES')::uuid,
        'STANDARD_COMPANY_RATES',
        'Standard Company Rates',
        'standard',
        NULL::uuid,
        NULL::varchar,
        'active',
        CURRENT_DATE,
        '055B_seed',
        'Default company rate card used when no GSD or customer-specific rate applies.',
        true
    UNION ALL
    SELECT
        md5('055B:SERVICE_REQUEST_STANDARD')::uuid,
        'SERVICE_REQUEST_STANDARD',
        'Service Request - First Available Rates',
        'service_request',
        NULL::uuid,
        NULL::varchar,
        'active',
        CURRENT_DATE,
        '055B_seed',
        'First Available service request rates. Best effort; tickets worked in order received.',
        true
    UNION ALL
    SELECT
        md5('055B:SERVICE_REQUEST_EMERGENCY')::uuid,
        'SERVICE_REQUEST_EMERGENCY',
        'Service Request - Emergency Rates',
        'emergency_service_request',
        NULL::uuid,
        NULL::varchar,
        'active',
        CURRENT_DATE,
        '055B_seed',
        'Emergency service request rates with daytime and afterhours/weekend/holiday minimums.',
        true
    UNION ALL
    SELECT
        md5('055B:TOYOTA_SPECIAL_RATES')::uuid,
        'TOYOTA_SPECIAL_RATES',
        'Toyota Special Rates',
        'customer_specific',
        customer_match.toyota_client_id,
        COALESCE(customer_match.toyota_client_name, 'Toyota'),
        'active',
        CURRENT_DATE,
        '055B_seed',
        'Toyota customer-specific special rate card.',
        true
    FROM customer_match
    UNION ALL
    SELECT
        md5('055B:HYUNDAI_SPECIAL_RATES')::uuid,
        'HYUNDAI_SPECIAL_RATES',
        'Hyundai Special Rates',
        'customer_specific',
        customer_match.hyundai_client_id,
        COALESCE(customer_match.hyundai_client_name, 'Hyundai'),
        'active',
        CURRENT_DATE,
        '055B_seed',
        'Hyundai customer-specific special rate card. Initially seeded using Toyota rates and editable from the Rate Card Administration page.',
        true
    FROM customer_match
) seed
ON CONFLICT (rate_card_code) DO UPDATE
SET rate_card_name = EXCLUDED.rate_card_name,
    rate_card_type = EXCLUDED.rate_card_type,
    client_id = COALESCE(work_rate_cards.client_id, EXCLUDED.client_id),
    customer_name_snapshot = COALESCE(work_rate_cards.customer_name_snapshot, EXCLUDED.customer_name_snapshot),
    description = EXCLUDED.description,
    is_system_seeded = TRUE,
    updated_at = NOW();

WITH seed_lines AS (
    SELECT 'STANDARD_COMPANY_RATES' AS rate_card_code, 'PROJECT_MANAGER_NORMAL' AS sku_code, 'Project Manager' AS display_name, 'Project Manager normal hourly rate.' AS description, 'project_management' AS labor_category, 'normal' AS time_type, 'hour' AS unit_type, 190.00::numeric AS rate_amount, 0::numeric AS min_hours, 0::numeric AS remote_min, 0::numeric AS onsite_min, 0::numeric AS daytime_min, 0::numeric AS after_min, '' AS business_hours, true AS billable, true AS utilization, false AS emergency, false AS travel, 10 AS display_order
    UNION ALL SELECT 'STANDARD_COMPANY_RATES','PROJECT_COORDINATOR_NORMAL','Project Coordinator','Project Coordinator normal hourly rate.','project_management','normal','hour',135.00,0,0,0,0,0,'',true,true,false,false,20
    UNION ALL SELECT 'STANDARD_COMPANY_RATES','CONSULT_ENGINEER_NORMAL','Consulting Engineer','Consulting Engineer normal hourly rate.','engineering','normal','hour',225.00,0,0,0,0,0,'',true,true,false,false,30
    UNION ALL SELECT 'STANDARD_COMPANY_RATES','ASSOC_ENGINEER_NORMAL','Associate Engineer','Associate Engineer normal hourly rate.','engineering','normal','hour',180.00,0,0,0,0,0,'',true,true,false,false,40
    UNION ALL SELECT 'STANDARD_COMPANY_RATES','SME_ENGINEER_NORMAL','SME Engineer','SME Engineer normal hourly rate.','engineering','normal','hour',250.00,0,0,0,0,0,'',true,true,false,false,50
    UNION ALL SELECT 'STANDARD_COMPANY_RATES','ANALYST_DEV_ARCHITECT_NORMAL','Analyst / Dev / Architect','Analyst / Developer / Architect normal hourly rate.','engineering','normal','hour',295.00,0,0,0,0,0,'',true,true,false,false,60
    UNION ALL SELECT 'STANDARD_COMPANY_RATES','CONSULT_ENGINEER_AFTERHOURS','Consulting Engineer - After-Hours','Consulting Engineer after-hours hourly rate.','engineering','afterhours','hour',337.50,0,0,0,0,0,'',true,true,false,false,70
    UNION ALL SELECT 'STANDARD_COMPANY_RATES','ASSOC_ENGINEER_AFTERHOURS','Associate Engineer - After-Hours','Associate Engineer after-hours hourly rate.','engineering','afterhours','hour',270.00,0,0,0,0,0,'',true,true,false,false,80
    UNION ALL SELECT 'STANDARD_COMPANY_RATES','SME_ENGINEER_AFTERHOURS','SME Engineer - After-Hours','SME Engineer after-hours hourly rate.','engineering','afterhours','hour',375.00,0,0,0,0,0,'',true,true,false,false,90
    UNION ALL SELECT 'STANDARD_COMPANY_RATES','ANALYST_DEV_ARCHITECT_AFTERHOURS','Analyst / Dev / Architect - After-Hours','Analyst / Developer / Architect after-hours hourly rate.','engineering','afterhours','hour',442.50,0,0,0,0,0,'',true,true,false,false,100
    UNION ALL SELECT 'STANDARD_COMPANY_RATES','TRAVEL','Travel','Travel hourly rate.','travel','travel','hour',95.00,0,0,0,0,0,'',true,true,false,true,110
    UNION ALL SELECT 'SERVICE_REQUEST_STANDARD','SR_FIRST_AVAILABLE_REMOTE','First Available Remote Support','First Available best-effort remote support. 1/2 hour minimum.','service_request','first_available_remote','hour',225.00,0.5,0.5,0,0,0,'Standard Business Hours: 8:00am - 5:00pm, Monday through Friday',true,true,false,false,10
    UNION ALL SELECT 'SERVICE_REQUEST_STANDARD','SR_FIRST_AVAILABLE_ONSITE','First Available Onsite Support','First Available best-effort onsite support. 1 hour minimum.','service_request','first_available_onsite','hour',225.00,1,0,1,0,0,'Standard Business Hours: 8:00am - 5:00pm, Monday through Friday',true,true,false,false,20
    UNION ALL SELECT 'SERVICE_REQUEST_STANDARD','SR_TRAVEL','Travel','Service request travel hourly rate.','travel','travel','hour',95.00,0,0,0,0,0,'',true,true,false,true,30
    UNION ALL SELECT 'SERVICE_REQUEST_EMERGENCY','SR_EMERGENCY_DAYTIME','Emergency Daytime Support','Emergency support during standard business hours. 2 hour minimum.','service_request','emergency_daytime','hour',450.00,2,0,0,2,0,'Standard Business Hours: 8:00am - 5:00pm, Monday through Friday',true,true,true,false,10
    UNION ALL SELECT 'SERVICE_REQUEST_EMERGENCY','SR_EMERGENCY_AFTERHOURS_WEEKEND_HOLIDAY','Emergency Afterhours / Weekend / Holiday Support','Emergency support afterhours, weekends, and holidays. 4 hour minimum.','service_request','emergency_afterhours_weekend_holiday','hour',450.00,4,0,0,0,4,'Afterhours, weekends, and holidays use a 4 hour minimum.',true,true,true,false,20
    UNION ALL SELECT 'SERVICE_REQUEST_EMERGENCY','SR_EMERGENCY_TRAVEL','Travel','Emergency service request travel hourly rate.','travel','travel','hour',95.00,0,0,0,0,0,'',true,true,false,true,30
)
INSERT INTO work_rate_card_lines (
    rate_line_id,
    rate_card_id,
    sku_code,
    display_name,
    description,
    labor_category,
    time_type,
    unit_type,
    rate_amount,
    minimum_billing_hours,
    remote_minimum_hours,
    onsite_minimum_hours,
    daytime_minimum_hours,
    afterhours_weekend_holiday_minimum_hours,
    business_hours_text,
    billable_default,
    utilization_eligible_default,
    is_emergency,
    is_travel,
    display_order,
    notes
)
SELECT
    md5('055B:' || rc.rate_card_code || ':' || seed_lines.sku_code || ':' || seed_lines.time_type)::uuid,
    rc.rate_card_id,
    seed_lines.sku_code,
    seed_lines.display_name,
    seed_lines.description,
    seed_lines.labor_category,
    seed_lines.time_type,
    seed_lines.unit_type,
    seed_lines.rate_amount,
    seed_lines.min_hours,
    seed_lines.remote_min,
    seed_lines.onsite_min,
    seed_lines.daytime_min,
    seed_lines.after_min,
    seed_lines.business_hours,
    seed_lines.billable,
    seed_lines.utilization,
    seed_lines.emergency,
    seed_lines.travel,
    seed_lines.display_order,
    'Seeded by 055B rate card administration foundation.'
FROM seed_lines
JOIN work_rate_cards rc
  ON rc.rate_card_code = seed_lines.rate_card_code
ON CONFLICT (rate_card_id, sku_code, time_type) DO UPDATE
SET display_name = EXCLUDED.display_name,
    description = EXCLUDED.description,
    labor_category = EXCLUDED.labor_category,
    unit_type = EXCLUDED.unit_type,
    rate_amount = EXCLUDED.rate_amount,
    minimum_billing_hours = EXCLUDED.minimum_billing_hours,
    remote_minimum_hours = EXCLUDED.remote_minimum_hours,
    onsite_minimum_hours = EXCLUDED.onsite_minimum_hours,
    daytime_minimum_hours = EXCLUDED.daytime_minimum_hours,
    afterhours_weekend_holiday_minimum_hours = EXCLUDED.afterhours_weekend_holiday_minimum_hours,
    business_hours_text = EXCLUDED.business_hours_text,
    billable_default = EXCLUDED.billable_default,
    utilization_eligible_default = EXCLUDED.utilization_eligible_default,
    is_emergency = EXCLUDED.is_emergency,
    is_travel = EXCLUDED.is_travel,
    display_order = EXCLUDED.display_order,
    updated_at = NOW();

WITH customer_lines AS (
    SELECT card_code, sku_code, display_name, description, labor_category, time_type, unit_type, rate_amount, display_order
    FROM (
        VALUES
        ('TOYOTA_SPECIAL_RATES','ON-AS-PROJECTSCHED','Project Scheduler','Toyota project scheduler rate.','project_management','normal','hour',115.00::numeric,10),
        ('TOYOTA_SPECIAL_RATES','ON-AS-ProjectManager','Project Manager','Toyota project manager rate.','project_management','normal','hour',132.00::numeric,20),
        ('TOYOTA_SPECIAL_RATES','ON-AS-ProjectCoord','Project Coordinator','Toyota project coordinator rate.','project_management','normal','hour',115.00::numeric,30),
        ('TOYOTA_SPECIAL_RATES','ON-AS-PERDIEM','Per Diem','Toyota per diem unit rate.','perdiem','perdiem','day',1470.00::numeric,40),
        ('TOYOTA_SPECIAL_RATES','ON-AS-Materials','Materials','Toyota materials unit rate.','materials','materials','unit',75.00::numeric,50),
        ('TOYOTA_SPECIAL_RATES','ON-AS-Consult-Engineer','Consulting Engineer','Toyota consulting engineer normal rate.','engineering','normal','hour',153.00::numeric,60),
        ('TOYOTA_SPECIAL_RATES','ON-AS-SME-Engineer','SME Engineer','Toyota SME engineer normal rate.','engineering','normal','hour',180.00::numeric,70),
        ('TOYOTA_SPECIAL_RATES','ON-AS-AnalystDevArch','Analyst / Dev / Architect','Toyota analyst/developer/architect normal rate.','engineering','normal','hour',180.00::numeric,80),
        ('TOYOTA_SPECIAL_RATES','ON-AS-Consult-Engineer-AFTERHOURS','Consulting Engineer - After-Hours','Toyota consulting engineer after-hours rate.','engineering','afterhours','hour',229.50::numeric,90),
        ('TOYOTA_SPECIAL_RATES','ON-AS-SME-Engineer-AFTERHOURS','SME Engineer - After-Hours','Toyota SME engineer after-hours rate.','engineering','afterhours','hour',270.00::numeric,100),
        ('TOYOTA_SPECIAL_RATES','ON-AS-AnalystDevArch-AFTERHOURS','Analyst / Dev / Architect - After-Hours','Toyota analyst/developer/architect after-hours rate.','engineering','afterhours','hour',270.00::numeric,110),
        ('TOYOTA_SPECIAL_RATES','ON-AS-Travel','Travel','Toyota travel hourly rate.','travel','travel','hour',52.00::numeric,120),
        ('HYUNDAI_SPECIAL_RATES','ON-AS-PROJECTSCHED','Project Scheduler','Hyundai project scheduler rate seeded from Toyota rates.','project_management','normal','hour',115.00::numeric,10),
        ('HYUNDAI_SPECIAL_RATES','ON-AS-ProjectManager','Project Manager','Hyundai project manager rate seeded from Toyota rates.','project_management','normal','hour',132.00::numeric,20),
        ('HYUNDAI_SPECIAL_RATES','ON-AS-ProjectCoord','Project Coordinator','Hyundai project coordinator rate seeded from Toyota rates.','project_management','normal','hour',115.00::numeric,30),
        ('HYUNDAI_SPECIAL_RATES','ON-AS-PERDIEM','Per Diem','Hyundai per diem unit rate seeded from Toyota rates.','perdiem','perdiem','day',1470.00::numeric,40),
        ('HYUNDAI_SPECIAL_RATES','ON-AS-Materials','Materials','Hyundai materials unit rate seeded from Toyota rates.','materials','materials','unit',75.00::numeric,50),
        ('HYUNDAI_SPECIAL_RATES','ON-AS-Consult-Engineer','Consulting Engineer','Hyundai consulting engineer normal rate seeded from Toyota rates.','engineering','normal','hour',153.00::numeric,60),
        ('HYUNDAI_SPECIAL_RATES','ON-AS-SME-Engineer','SME Engineer','Hyundai SME engineer normal rate seeded from Toyota rates.','engineering','normal','hour',180.00::numeric,70),
        ('HYUNDAI_SPECIAL_RATES','ON-AS-AnalystDevArch','Analyst / Dev / Architect','Hyundai analyst/developer/architect normal rate seeded from Toyota rates.','engineering','normal','hour',180.00::numeric,80),
        ('HYUNDAI_SPECIAL_RATES','ON-AS-Consult-Engineer-AFTERHOURS','Consulting Engineer - After-Hours','Hyundai consulting engineer after-hours rate seeded from Toyota rates.','engineering','afterhours','hour',229.50::numeric,90),
        ('HYUNDAI_SPECIAL_RATES','ON-AS-SME-Engineer-AFTERHOURS','SME Engineer - After-Hours','Hyundai SME engineer after-hours rate seeded from Toyota rates.','engineering','afterhours','hour',270.00::numeric,100),
        ('HYUNDAI_SPECIAL_RATES','ON-AS-AnalystDevArch-AFTERHOURS','Analyst / Dev / Architect - After-Hours','Hyundai analyst/developer/architect after-hours rate seeded from Toyota rates.','engineering','afterhours','hour',270.00::numeric,110),
        ('HYUNDAI_SPECIAL_RATES','ON-AS-Travel','Travel','Hyundai travel hourly rate seeded from Toyota rates.','travel','travel','hour',52.00::numeric,120)
    ) AS v(card_code, sku_code, display_name, description, labor_category, time_type, unit_type, rate_amount, display_order)
)
INSERT INTO work_rate_card_lines (
    rate_line_id,
    rate_card_id,
    sku_code,
    display_name,
    description,
    labor_category,
    time_type,
    unit_type,
    rate_amount,
    minimum_billing_hours,
    billable_default,
    utilization_eligible_default,
    is_emergency,
    is_travel,
    display_order,
    notes
)
SELECT
    md5('055B:' || rc.rate_card_code || ':' || customer_lines.sku_code || ':' || customer_lines.time_type)::uuid,
    rc.rate_card_id,
    customer_lines.sku_code,
    customer_lines.display_name,
    customer_lines.description,
    customer_lines.labor_category,
    customer_lines.time_type,
    customer_lines.unit_type,
    customer_lines.rate_amount,
    0,
    true,
    CASE WHEN customer_lines.labor_category IN ('materials','perdiem') THEN false ELSE true END,
    false,
    customer_lines.labor_category = 'travel',
    customer_lines.display_order,
    'Seeded by 055B customer-specific Toyota/Hyundai rate card foundation.'
FROM customer_lines
JOIN work_rate_cards rc
  ON rc.rate_card_code = customer_lines.card_code
ON CONFLICT (rate_card_id, sku_code, time_type) DO UPDATE
SET display_name = EXCLUDED.display_name,
    description = EXCLUDED.description,
    labor_category = EXCLUDED.labor_category,
    unit_type = EXCLUDED.unit_type,
    rate_amount = EXCLUDED.rate_amount,
    billable_default = EXCLUDED.billable_default,
    utilization_eligible_default = EXCLUDED.utilization_eligible_default,
    is_travel = EXCLUDED.is_travel,
    display_order = EXCLUDED.display_order,
    updated_at = NOW();

COMMIT;
