-- ProjectPulse 098 — Module 025 persistent SOW/GSD workspace.
-- Stores immutable engagement references, detailed AI/reviewed P/D/I/V/R scope,
-- explainable suggested/final LOE, commercial/person metadata, and archive history.

BEGIN;

DO $projectpulse098_prerequisites$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '097_project_planning_identity_safe_admission'
    ) THEN
        RAISE EXCEPTION 'Migration 098 requires migration 097 first.';
    END IF;
END;
$projectpulse098_prerequisites$;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE SEQUENCE IF NOT EXISTS sow_gsd_reference_sequence START WITH 1 INCREMENT BY 1;

CREATE OR REPLACE FUNCTION projectpulse_next_sow_gsd_reference()
RETURNS text
LANGUAGE sql
VOLATILE
AS $$
    SELECT 'SOWGSD-' || to_char(CURRENT_DATE, 'YYYY') || '-' || lpad(nextval('sow_gsd_reference_sequence')::text, 6, '0');
$$;

CREATE TABLE IF NOT EXISTS sow_gsd_workspaces (
    sow_gsd_workspace_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    sow_gsd_reference text NOT NULL UNIQUE DEFAULT projectpulse_next_sow_gsd_reference(),
    owner_solution_architect_user_id uuid NOT NULL REFERENCES app_users(user_id),
    customer_id uuid NULL REFERENCES clients(client_id),
    customer_name text NOT NULL,
    customer_source text NOT NULL DEFAULT 'DIRECTORY',
    opportunity_reference text NULL,
    project_code text NULL,
    project_name text NOT NULL,
    service_overview text NOT NULL DEFAULT '',
    contract_type text NOT NULL DEFAULT 'T_AND_M',
    account_executive_user_id uuid NULL REFERENCES app_users(user_id),
    account_executive_name text NULL,
    resale_user_id uuid NULL REFERENCES app_users(user_id),
    resale_name text NULL,
    oem_customer_type text NOT NULL DEFAULT 'STANDARD',
    gsd_template_code text NOT NULL DEFAULT 'STANDARD',
    status text NOT NULL DEFAULT 'DRAFT',
    ai_draft jsonb NOT NULL DEFAULT '{}'::jsonb,
    phase_details jsonb NOT NULL DEFAULT '{}'::jsonb,
    suggested_plan_hours numeric(10,2) NULL,
    suggested_design_hours numeric(10,2) NULL,
    suggested_implement_hours numeric(10,2) NULL,
    suggested_validate_hours numeric(10,2) NULL,
    suggested_release_hours numeric(10,2) NULL,
    final_plan_hours numeric(10,2) NULL,
    final_design_hours numeric(10,2) NULL,
    final_implement_hours numeric(10,2) NULL,
    final_validate_hours numeric(10,2) NULL,
    final_release_hours numeric(10,2) NULL,
    generation_provider text NULL,
    generation_citations jsonb NOT NULL DEFAULT '[]'::jsonb,
    generation_warnings jsonb NOT NULL DEFAULT '[]'::jsonb,
    generation_missing_evidence jsonb NOT NULL DEFAULT '[]'::jsonb,
    generation_confidence numeric(5,4) NULL,
    review_confirmed_at timestamptz NULL,
    review_confirmed_by_user_id uuid NULL REFERENCES app_users(user_id),
    archived_at timestamptz NULL,
    archived_by_user_id uuid NULL REFERENCES app_users(user_id),
    last_autosaved_at timestamptz NULL,
    revision_number integer NOT NULL DEFAULT 1,
    created_by_user_id uuid NOT NULL REFERENCES app_users(user_id),
    updated_by_user_id uuid NOT NULL REFERENCES app_users(user_id),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT sow_gsd_customer_source_check CHECK (customer_source IN ('DIRECTORY', 'MANUAL')),
    CONSTRAINT sow_gsd_contract_type_check CHECK (contract_type IN ('T_AND_M', 'FIXED')),
    CONSTRAINT sow_gsd_oem_customer_type_check CHECK (oem_customer_type IN ('STANDARD', 'TOYOTA', 'HYUNDAI')),
    CONSTRAINT sow_gsd_template_code_check CHECK (gsd_template_code IN ('STANDARD', 'HAEA_STAFF_AUG_KUS_UVO')),
    CONSTRAINT sow_gsd_status_check CHECK (status IN ('DRAFT', 'READY_FOR_REVIEW', 'CONFIRMED', 'ARCHIVED')),
    CONSTRAINT sow_gsd_customer_directory_binding_check CHECK (
        customer_source = 'MANUAL' OR customer_id IS NOT NULL
    ),
    CONSTRAINT sow_gsd_hours_nonnegative_check CHECK (
        COALESCE(suggested_plan_hours, 0) >= 0
        AND COALESCE(suggested_design_hours, 0) >= 0
        AND COALESCE(suggested_implement_hours, 0) >= 0
        AND COALESCE(suggested_validate_hours, 0) >= 0
        AND COALESCE(suggested_release_hours, 0) >= 0
        AND COALESCE(final_plan_hours, 0) >= 0
        AND COALESCE(final_design_hours, 0) >= 0
        AND COALESCE(final_implement_hours, 0) >= 0
        AND COALESCE(final_validate_hours, 0) >= 0
        AND COALESCE(final_release_hours, 0) >= 0
    )
);

CREATE INDEX IF NOT EXISTS ix_sow_gsd_workspaces_owner_status_updated
    ON sow_gsd_workspaces(owner_solution_architect_user_id, status, updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_sow_gsd_workspaces_customer_updated
    ON sow_gsd_workspaces(customer_id, updated_at DESC)
    WHERE customer_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_sow_gsd_workspaces_reference_search
    ON sow_gsd_workspaces(lower(sow_gsd_reference));
CREATE INDEX IF NOT EXISTS ix_sow_gsd_workspaces_customer_name_search
    ON sow_gsd_workspaces(lower(customer_name));
CREATE INDEX IF NOT EXISTS ix_sow_gsd_workspaces_project_name_search
    ON sow_gsd_workspaces(lower(project_name));

CREATE OR REPLACE FUNCTION projectpulse_sow_gsd_guard_immutable_reference()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.sow_gsd_reference IS DISTINCT FROM OLD.sow_gsd_reference THEN
        RAISE EXCEPTION 'sow_gsd_reference is immutable';
    END IF;
    NEW.updated_at = now();
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_sow_gsd_guard_immutable_reference ON sow_gsd_workspaces;
CREATE TRIGGER trg_sow_gsd_guard_immutable_reference
BEFORE UPDATE ON sow_gsd_workspaces
FOR EACH ROW
EXECUTE FUNCTION projectpulse_sow_gsd_guard_immutable_reference();

CREATE TABLE IF NOT EXISTS sow_gsd_workspace_events (
    sow_gsd_workspace_event_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    sow_gsd_workspace_id uuid NOT NULL REFERENCES sow_gsd_workspaces(sow_gsd_workspace_id) ON DELETE CASCADE,
    revision_number integer NOT NULL,
    event_type text NOT NULL,
    event_detail jsonb NOT NULL DEFAULT '{}'::jsonb,
    actor_user_id uuid NOT NULL REFERENCES app_users(user_id),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT sow_gsd_workspace_event_type_check CHECK (
        event_type IN ('CREATED', 'GENERATED', 'CONFIRMED', 'ARCHIVED', 'RESTORED')
    )
);

CREATE INDEX IF NOT EXISTS ix_sow_gsd_workspace_events_workspace_created
    ON sow_gsd_workspace_events(sow_gsd_workspace_id, created_at DESC);

COMMENT ON TABLE sow_gsd_workspaces IS
'Persistent Module 025 SOW/GSD authoring workspaces. AI-suggested LOE is stored separately from the Solution Architect final editable LOE for future estimate-versus-actual analysis.';
COMMENT ON COLUMN sow_gsd_workspaces.sow_gsd_reference IS
'Immutable business reference used for search, audit, and record keeping across multiple SOW/GSDs for the same customer.';
COMMENT ON COLUMN sow_gsd_workspaces.phase_details IS
'Editable detailed Plan/Design/Implement/Validate/Release scope including activities, steps, inputs, outputs, responsibilities, validation, risks, questions, and LOE rationale.';

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '098_module_025_sow_gsd_workspace',
    'Create persistent Module 025 SOW/GSD workspaces with immutable references, detailed P/D/I/V/R scope, reviewed LOE, archive state, and audit events',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
