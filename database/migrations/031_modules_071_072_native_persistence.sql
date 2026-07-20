-- ProjectPulse migration 031: native persistence for Modules 071 and 072.
BEGIN;

CREATE TABLE IF NOT EXISTS projectpulse_module_audit_events
(
    event_id uuid PRIMARY KEY,
    module_number varchar(3) NOT NULL,
    entity_type varchar(100) NOT NULL,
    entity_id text NULL,
    action_code varchar(100) NOT NULL,
    actor_user_id uuid NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    evidence_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_projectpulse_module_audit_module
        CHECK (module_number IN ('064','065','066','067','068','069','070','071','072','073','074')),
    CONSTRAINT ck_projectpulse_module_audit_evidence
        CHECK (jsonb_typeof(evidence_json) = 'object')
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_module_audit_lookup
    ON projectpulse_module_audit_events(module_number, entity_type, occurred_at DESC);

CREATE TABLE IF NOT EXISTS projectpulse_oncall_schedule_versions
(
    schedule_version_id uuid PRIMARY KEY,
    revision_number bigint GENERATED ALWAYS AS IDENTITY,
    schedule_json jsonb NOT NULL,
    entries_count integer NOT NULL DEFAULT 0,
    is_current boolean NOT NULL DEFAULT false,
    saved_by uuid NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    saved_at timestamptz NOT NULL DEFAULT now(),
    restored_from_schedule_version_id uuid NULL
        REFERENCES projectpulse_oncall_schedule_versions(schedule_version_id) ON DELETE SET NULL,
    change_reason varchar(250) NOT NULL DEFAULT 'schedule_saved',
    CONSTRAINT ck_projectpulse_oncall_schedule_json CHECK (jsonb_typeof(schedule_json) = 'object'),
    CONSTRAINT ck_projectpulse_oncall_entries_count CHECK (entries_count >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_projectpulse_oncall_one_current
    ON projectpulse_oncall_schedule_versions(is_current)
    WHERE is_current = true;

CREATE INDEX IF NOT EXISTS ix_projectpulse_oncall_history
    ON projectpulse_oncall_schedule_versions(saved_at DESC);

CREATE TABLE IF NOT EXISTS projectpulse_oncall_roster_members
(
    department_code varchar(100) NOT NULL,
    user_id uuid NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    routing_phone varchar(50) NOT NULL DEFAULT '',
    sort_order integer NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    updated_by uuid NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (department_code, user_id),
    CONSTRAINT ck_projectpulse_oncall_department CHECK (length(trim(department_code)) BETWEEN 1 AND 100),
    CONSTRAINT ck_projectpulse_oncall_sort_order CHECK (sort_order >= 0)
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_oncall_roster_active
    ON projectpulse_oncall_roster_members(department_code, sort_order, user_id)
    WHERE is_active = true;

CREATE TABLE IF NOT EXISTS projectpulse_oncall_acknowledgements
(
    schedule_version_id uuid NOT NULL
        REFERENCES projectpulse_oncall_schedule_versions(schedule_version_id) ON DELETE CASCADE,
    department_code varchar(100) NOT NULL,
    user_id uuid NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    acknowledged_at timestamptz NOT NULL DEFAULT now(),
    acknowledged_by uuid NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    acknowledgement_note varchar(500) NULL,
    PRIMARY KEY (schedule_version_id, department_code, user_id)
);

CREATE TABLE IF NOT EXISTS projectpulse_oneassist_routes
(
    route_id varchar(100) PRIMARY KEY,
    customer_name varchar(200) NOT NULL,
    routing_pin varchar(5) NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    created_by uuid NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_by uuid NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_projectpulse_oneassist_route_id CHECK (length(trim(route_id)) BETWEEN 1 AND 100),
    CONSTRAINT ck_projectpulse_oneassist_customer CHECK (length(trim(customer_name)) BETWEEN 1 AND 200),
    CONSTRAINT ck_projectpulse_oneassist_pin CHECK (routing_pin ~ '^[0-9]{5}$'),
    CONSTRAINT ck_projectpulse_oneassist_sort_order CHECK (sort_order >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_projectpulse_oneassist_active_pin
    ON projectpulse_oneassist_routes(routing_pin)
    WHERE is_active = true;

CREATE INDEX IF NOT EXISTS ix_projectpulse_oneassist_active_name
    ON projectpulse_oneassist_routes(lower(customer_name))
    WHERE is_active = true;

CREATE TABLE IF NOT EXISTS projectpulse_oneassist_route_revisions
(
    revision_id uuid PRIMARY KEY,
    revision_number bigint GENERATED ALWAYS AS IDENTITY,
    routes_json jsonb NOT NULL,
    route_count integer NOT NULL DEFAULT 0,
    saved_by uuid NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    saved_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_projectpulse_oneassist_revision_json CHECK (jsonb_typeof(routes_json) = 'array'),
    CONSTRAINT ck_projectpulse_oneassist_revision_count CHECK (route_count >= 0)
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_oneassist_revisions
    ON projectpulse_oneassist_route_revisions(saved_at DESC);

COMMENT ON TABLE projectpulse_oncall_schedule_versions IS
    'Versioned Module 071 on-call schedules. Exactly one row may be current.';
COMMENT ON TABLE projectpulse_oncall_roster_members IS
    'Module 071 identity-backed on-call rotation roster.';
COMMENT ON TABLE projectpulse_oneassist_routes IS
    'Module 072 active and archived OneAssist routing identifiers.';
COMMENT ON TABLE projectpulse_oneassist_route_revisions IS
    'Immutable Module 072 directory revision snapshots.';

COMMIT;
