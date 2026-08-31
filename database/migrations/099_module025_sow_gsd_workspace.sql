BEGIN;

CREATE SEQUENCE IF NOT EXISTS module025_sow_gsd_number_seq START WITH 1 INCREMENT BY 1;

CREATE TABLE IF NOT EXISTS module025_sow_gsd_engagements (
    engagement_id uuid PRIMARY KEY,
    engagement_number varchar(40) NOT NULL UNIQUE DEFAULT (
        'SOW-' || to_char(CURRENT_DATE, 'YYYY') || '-' || lpad(nextval('module025_sow_gsd_number_seq')::text, 6, '0')
    ),
    owner_user_id uuid NOT NULL REFERENCES app_users(user_id),
    owner_display_name varchar(320) NOT NULL DEFAULT '',
    owner_department_name varchar(255) NOT NULL DEFAULT '',
    owner_team_name varchar(255) NOT NULL DEFAULT '',
    customer_id uuid NULL REFERENCES clients(client_id),
    customer_name varchar(500) NOT NULL DEFAULT '',
    customer_entry_mode varchar(30) NOT NULL DEFAULT 'directory'
        CHECK (customer_entry_mode IN ('directory', 'manual')),
    commercial_model varchar(40) NOT NULL DEFAULT 'time_and_materials'
        CHECK (commercial_model IN ('time_and_materials', 'fixed')),
    customer_program varchar(30) NOT NULL DEFAULT 'standard'
        CHECK (customer_program IN ('standard', 'toyota', 'hyundai')),
    gsd_template_key varchar(120) NOT NULL DEFAULT 'standard_gsd',
    account_executive_user_id uuid NULL REFERENCES app_users(user_id),
    account_executive_name varchar(320) NOT NULL DEFAULT '',
    resale_user_id uuid NULL REFERENCES app_users(user_id),
    resale_name varchar(320) NOT NULL DEFAULT '',
    service_overview text NOT NULL DEFAULT '',
    sow_sections jsonb NOT NULL DEFAULT '{}'::jsonb,
    ai_metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    status varchar(30) NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft', 'review_ready', 'confirmed', 'archived')),
    is_active boolean NOT NULL DEFAULT TRUE,
    revision integer NOT NULL DEFAULT 1 CHECK (revision > 0),
    last_generated_at timestamptz NULL,
    confirmed_at timestamptz NULL,
    archived_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT NOW(),
    updated_at timestamptz NOT NULL DEFAULT NOW(),
    CONSTRAINT module025_customer_selection_ck CHECK (
        (customer_entry_mode = 'manual' AND customer_name <> '')
        OR customer_entry_mode = 'directory'
    ),
    CONSTRAINT module025_gsd_template_ck CHECK (
        (customer_program = 'standard' AND gsd_template_key = 'standard_gsd')
        OR (customer_program IN ('toyota', 'hyundai') AND gsd_template_key = 'haea_staff_aug_gsd_kus_uvo_telematics_1')
    )
);

CREATE TABLE IF NOT EXISTS module025_sow_gsd_phases (
    engagement_id uuid NOT NULL REFERENCES module025_sow_gsd_engagements(engagement_id) ON DELETE CASCADE,
    phase_code varchar(20) NOT NULL CHECK (phase_code IN ('plan', 'design', 'implement', 'validate', 'release')),
    sort_order smallint NOT NULL CHECK (sort_order BETWEEN 1 AND 5),
    suggested_hours numeric(10,2) NOT NULL DEFAULT 0 CHECK (suggested_hours >= 0),
    final_hours numeric(10,2) NOT NULL DEFAULT 0 CHECK (final_hours >= 0),
    objective text NOT NULL DEFAULT '',
    detailed_activities jsonb NOT NULL DEFAULT '[]'::jsonb,
    technical_tasks jsonb NOT NULL DEFAULT '[]'::jsonb,
    deliverables jsonb NOT NULL DEFAULT '[]'::jsonb,
    customer_responsibilities jsonb NOT NULL DEFAULT '[]'::jsonb,
    us_signal_responsibilities jsonb NOT NULL DEFAULT '[]'::jsonb,
    prerequisites jsonb NOT NULL DEFAULT '[]'::jsonb,
    dependencies jsonb NOT NULL DEFAULT '[]'::jsonb,
    assumptions jsonb NOT NULL DEFAULT '[]'::jsonb,
    open_questions jsonb NOT NULL DEFAULT '[]'::jsonb,
    acceptance_criteria jsonb NOT NULL DEFAULT '[]'::jsonb,
    validation_steps jsonb NOT NULL DEFAULT '[]'::jsonb,
    risks jsonb NOT NULL DEFAULT '[]'::jsonb,
    loe_rationale text NOT NULL DEFAULT '',
    source_citation_ids jsonb NOT NULL DEFAULT '[]'::jsonb,
    ai_generated boolean NOT NULL DEFAULT FALSE,
    updated_at timestamptz NOT NULL DEFAULT NOW(),
    PRIMARY KEY (engagement_id, phase_code),
    UNIQUE (engagement_id, sort_order)
);

CREATE TABLE IF NOT EXISTS module025_sow_gsd_events (
    event_id bigserial PRIMARY KEY,
    engagement_id uuid NOT NULL REFERENCES module025_sow_gsd_engagements(engagement_id) ON DELETE CASCADE,
    event_type varchar(80) NOT NULL,
    actor_user_id uuid NOT NULL REFERENCES app_users(user_id),
    engagement_revision integer NOT NULL,
    summary varchar(1000) NOT NULL DEFAULT '',
    evidence_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_module025_sow_gsd_owner_active
    ON module025_sow_gsd_engagements(owner_user_id, is_active, updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_module025_sow_gsd_customer
    ON module025_sow_gsd_engagements(lower(customer_name), updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_module025_sow_gsd_status
    ON module025_sow_gsd_engagements(status, updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_module025_sow_gsd_events_engagement
    ON module025_sow_gsd_events(engagement_id, created_at DESC);

CREATE OR REPLACE FUNCTION module025_protect_sow_gsd_identity()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.engagement_number IS DISTINCT FROM OLD.engagement_number THEN
        RAISE EXCEPTION 'Module 025 engagement_number is immutable';
    END IF;
    IF NEW.owner_user_id IS DISTINCT FROM OLD.owner_user_id THEN
        RAISE EXCEPTION 'Module 025 owner_user_id is immutable';
    END IF;
    NEW.updated_at := NOW();
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_module025_protect_sow_gsd_identity ON module025_sow_gsd_engagements;
CREATE TRIGGER trg_module025_protect_sow_gsd_identity
BEFORE UPDATE ON module025_sow_gsd_engagements
FOR EACH ROW
EXECUTE FUNCTION module025_protect_sow_gsd_identity();

COMMENT ON TABLE module025_sow_gsd_engagements IS
    'Module 025 persistent SOW/GSD workspace. One immutable engagement number is retained for the life of each SOW/GSD package.';
COMMENT ON COLUMN module025_sow_gsd_phases.suggested_hours IS
    'Celar AI proposed phase effort retained for future estimate-quality and similar-work analysis.';
COMMENT ON COLUMN module025_sow_gsd_phases.final_hours IS
    'Solution Architect reviewed/final phase effort used by generated GSD output.';

COMMIT;
