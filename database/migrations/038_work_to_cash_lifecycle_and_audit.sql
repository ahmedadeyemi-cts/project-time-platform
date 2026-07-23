-- ProjectPulse Work-to-Cash lifecycle persistence and unified audit
-- Modules 039, 040, 042, 055C, and 055D

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS work_billing_readiness_reviews (
    work_billing_readiness_review_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    billing_period_start DATE NOT NULL,
    billing_period_end DATE NOT NULL,
    package_type TEXT NOT NULL,
    review_status TEXT NOT NULL DEFAULT 'draft',
    checklist_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    notes TEXT NOT NULL DEFAULT '',
    reviewed_by_user_id UUID NULL REFERENCES app_users(user_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_work_billing_readiness_review
        UNIQUE (project_id, billing_period_start, billing_period_end, package_type),
    CONSTRAINT ck_work_billing_readiness_period
        CHECK (billing_period_end >= billing_period_start),
    CONSTRAINT ck_work_billing_readiness_status
        CHECK (review_status IN ('draft', 'blocked', 'ready'))
);

CREATE INDEX IF NOT EXISTS idx_work_billing_readiness_project
    ON work_billing_readiness_reviews(project_id, updated_at DESC);

CREATE TABLE IF NOT EXISTS work_closeout_records (
    project_id UUID PRIMARY KEY REFERENCES projects(project_id) ON DELETE CASCADE,
    closeout_status TEXT NOT NULL DEFAULT 'not_started',
    billing_disposition TEXT NOT NULL DEFAULT '',
    delivery_complete BOOLEAN NOT NULL DEFAULT FALSE,
    customer_acceptance_complete BOOLEAN NOT NULL DEFAULT FALSE,
    time_expense_complete BOOLEAN NOT NULL DEFAULT FALSE,
    billing_complete BOOLEAN NOT NULL DEFAULT FALSE,
    reason TEXT NOT NULL DEFAULT '',
    notes TEXT NOT NULL DEFAULT '',
    prior_project_status TEXT NOT NULL DEFAULT '',
    requested_by_user_id UUID NULL REFERENCES app_users(user_id),
    requested_at TIMESTAMPTZ NULL,
    closed_by_user_id UUID NULL REFERENCES app_users(user_id),
    closed_at TIMESTAMPTZ NULL,
    reopened_by_user_id UUID NULL REFERENCES app_users(user_id),
    reopened_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_work_closeout_status
        CHECK (closeout_status IN ('not_started', 'requested', 'ready', 'closed', 'reopened')),
    CONSTRAINT ck_work_closeout_billing_disposition
        CHECK (
            billing_disposition IN (
                '',
                'final_invoice_complete',
                'no_further_billing',
                'non_billable',
                'write_off_approved'
            )
        )
);

CREATE TABLE IF NOT EXISTS work_lifecycle_audit_events (
    work_lifecycle_audit_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    process_area TEXT NOT NULL,
    event_type TEXT NOT NULL,
    prior_state TEXT NOT NULL DEFAULT '',
    new_state TEXT NOT NULL DEFAULT '',
    summary TEXT NOT NULL,
    reason TEXT NOT NULL DEFAULT '',
    actor_user_id UUID NULL REFERENCES app_users(user_id),
    related_entity_type TEXT NOT NULL DEFAULT '',
    related_entity_id UUID NULL,
    source_table TEXT NOT NULL DEFAULT '',
    source_id UUID NULL,
    event_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_work_lifecycle_process_area
        CHECK (
            process_area IN (
                'work_creation',
                'work_edit',
                'billing_readiness',
                'invoice',
                'closeout',
                'archive'
            )
        )
);

CREATE INDEX IF NOT EXISTS idx_work_lifecycle_audit_project
    ON work_lifecycle_audit_events(project_id, created_at DESC);

CREATE UNIQUE INDEX IF NOT EXISTS uq_work_lifecycle_audit_source
    ON work_lifecycle_audit_events(source_table, source_id)
    WHERE source_table <> '' AND source_id IS NOT NULL;

CREATE OR REPLACE FUNCTION projectpulse038_reject_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Work lifecycle audit events are immutable.';
END;
$$;

DROP TRIGGER IF EXISTS trg_projectpulse038_audit_immutable
    ON work_lifecycle_audit_events;
CREATE TRIGGER trg_projectpulse038_audit_immutable
BEFORE UPDATE OR DELETE ON work_lifecycle_audit_events
FOR EACH ROW
EXECUTE FUNCTION projectpulse038_reject_audit_mutation();

CREATE OR REPLACE FUNCTION projectpulse038_capture_work_register_change()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_process_area TEXT;
BEGIN
    v_process_area := CASE
        WHEN lower(NEW.action) LIKE '%archive%' OR lower(NEW.action) LIKE '%restore%'
            THEN 'archive'
        WHEN lower(NEW.action) LIKE '%creat%'
          OR lower(NEW.action) LIKE '%intake%'
          OR lower(NEW.action) LIKE '%import%'
            THEN 'work_creation'
        ELSE 'work_edit'
    END;

    INSERT INTO work_lifecycle_audit_events (
        project_id,
        process_area,
        event_type,
        summary,
        actor_user_id,
        related_entity_type,
        related_entity_id,
        source_table,
        source_id,
        event_json,
        created_at
    )
    VALUES (
        NEW.work_id,
        v_process_area,
        NEW.action,
        COALESCE(NULLIF(NEW.change_summary, ''), NEW.action),
        NEW.changed_by_user_id,
        'work_register_change',
        NEW.work_register_change_history_id,
        'work_register_change_history',
        NEW.work_register_change_history_id,
        jsonb_build_object(
            'changedFields', COALESCE(NEW.changed_fields_csv, ''),
            'oldValue', COALESCE(NEW.old_value_json, '{}'::jsonb),
            'newValue', COALESCE(NEW.new_value_json, '{}'::jsonb)
        ),
        NEW.changed_at
    )
    ON CONFLICT (source_table, source_id) WHERE source_table <> '' AND source_id IS NOT NULL
    DO NOTHING;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse038_capture_invoice_event()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_project_id UUID;
    v_invoice_number TEXT;
BEGIN
    SELECT invoice.project_id, invoice.invoice_number
    INTO v_project_id, v_invoice_number
    FROM billing_invoices invoice
    WHERE invoice.billing_invoice_id = NEW.billing_invoice_id;

    IF v_project_id IS NULL THEN
        RETURN NEW;
    END IF;

    INSERT INTO work_lifecycle_audit_events (
        project_id,
        process_area,
        event_type,
        prior_state,
        new_state,
        summary,
        reason,
        actor_user_id,
        related_entity_type,
        related_entity_id,
        source_table,
        source_id,
        event_json,
        created_at
    )
    VALUES (
        v_project_id,
        'invoice',
        NEW.event_type,
        COALESCE(NEW.prior_status, ''),
        COALESCE(NEW.new_status, ''),
        COALESCE(
            NULLIF(NEW.event_reason, ''),
            'Invoice ' || COALESCE(v_invoice_number, '') || ': ' || NEW.event_type
        ),
        COALESCE(NEW.event_reason, ''),
        NEW.actor_user_id,
        'billing_invoice',
        NEW.billing_invoice_id,
        'billing_invoice_events',
        NEW.billing_invoice_event_id,
        COALESCE(NEW.event_json, '{}'::jsonb)
            || jsonb_build_object('invoiceNumber', COALESCE(v_invoice_number, '')),
        NEW.created_at
    )
    ON CONFLICT (source_table, source_id) WHERE source_table <> '' AND source_id IS NOT NULL
    DO NOTHING;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_projectpulse038_work_register_audit
    ON work_register_change_history;
CREATE TRIGGER trg_projectpulse038_work_register_audit
AFTER INSERT ON work_register_change_history
FOR EACH ROW
EXECUTE FUNCTION projectpulse038_capture_work_register_change();

DROP TRIGGER IF EXISTS trg_projectpulse038_invoice_audit
    ON billing_invoice_events;
CREATE TRIGGER trg_projectpulse038_invoice_audit
AFTER INSERT ON billing_invoice_events
FOR EACH ROW
EXECUTE FUNCTION projectpulse038_capture_invoice_event();

INSERT INTO work_lifecycle_audit_events (
    project_id,
    process_area,
    event_type,
    summary,
    actor_user_id,
    related_entity_type,
    related_entity_id,
    source_table,
    source_id,
    event_json,
    created_at
)
SELECT
    history.work_id,
    CASE
        WHEN lower(history.action) LIKE '%archive%' OR lower(history.action) LIKE '%restore%'
            THEN 'archive'
        WHEN lower(history.action) LIKE '%creat%'
          OR lower(history.action) LIKE '%intake%'
          OR lower(history.action) LIKE '%import%'
            THEN 'work_creation'
        ELSE 'work_edit'
    END,
    history.action,
    COALESCE(NULLIF(history.change_summary, ''), history.action),
    history.changed_by_user_id,
    'work_register_change',
    history.work_register_change_history_id,
    'work_register_change_history',
    history.work_register_change_history_id,
    jsonb_build_object(
        'changedFields', COALESCE(history.changed_fields_csv, ''),
        'oldValue', COALESCE(history.old_value_json, '{}'::jsonb),
        'newValue', COALESCE(history.new_value_json, '{}'::jsonb)
    ),
    history.changed_at
FROM work_register_change_history history
JOIN projects project ON project.project_id = history.work_id
ON CONFLICT (source_table, source_id) WHERE source_table <> '' AND source_id IS NOT NULL
DO NOTHING;

INSERT INTO work_lifecycle_audit_events (
    project_id,
    process_area,
    event_type,
    prior_state,
    new_state,
    summary,
    reason,
    actor_user_id,
    related_entity_type,
    related_entity_id,
    source_table,
    source_id,
    event_json,
    created_at
)
SELECT
    invoice.project_id,
    'invoice',
    event.event_type,
    COALESCE(event.prior_status, ''),
    COALESCE(event.new_status, ''),
    COALESCE(
        NULLIF(event.event_reason, ''),
        'Invoice ' || invoice.invoice_number || ': ' || event.event_type
    ),
    COALESCE(event.event_reason, ''),
    event.actor_user_id,
    'billing_invoice',
    invoice.billing_invoice_id,
    'billing_invoice_events',
    event.billing_invoice_event_id,
    COALESCE(event.event_json, '{}'::jsonb)
        || jsonb_build_object('invoiceNumber', invoice.invoice_number),
    event.created_at
FROM billing_invoice_events event
JOIN billing_invoices invoice
  ON invoice.billing_invoice_id = event.billing_invoice_id
ON CONFLICT (source_table, source_id) WHERE source_table <> '' AND source_id IS NOT NULL
DO NOTHING;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '038_work_to_cash_lifecycle_and_audit',
    'Persist billing readiness and closeout decisions and unify Work Register, invoice, and lifecycle audit events',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
