-- 054D - Immutable export package snapshots and audit evidence
-- Uses sidecar tables because the runtime DB user is not the owner of time_workflow_exports.

BEGIN;

CREATE TABLE IF NOT EXISTS time_workflow_export_items (
    time_workflow_export_item_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    time_workflow_export_id uuid NOT NULL,
    time_entry_id uuid NULL,
    work_date date NOT NULL,
    employee_name character varying(255) NOT NULL DEFAULT '',
    employee_email character varying(320) NOT NULL DEFAULT '',
    project_code character varying(100) NOT NULL DEFAULT '',
    project_name character varying(255) NOT NULL DEFAULT '',
    task_code character varying(100) NOT NULL DEFAULT '',
    task_name character varying(255) NOT NULL DEFAULT '',
    hours numeric(10,2) NOT NULL DEFAULT 0,
    billable boolean NOT NULL DEFAULT FALSE,
    status character varying(50) NOT NULL,
    description text NOT NULL DEFAULT '',
    snapshot_payload jsonb NULL,
    source_updated_at timestamp with time zone NULL,
    created_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS time_workflow_export_metadata (
    time_workflow_export_id uuid PRIMARY KEY,
    package_sha256 character varying(64) NULL,
    package_snapshot jsonb NULL,
    package_snapshot_item_count integer NOT NULL DEFAULT 0,
    created_at timestamp with time zone NOT NULL DEFAULT NOW(),
    updated_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_time_workflow_export_items_export_time_entry
    ON time_workflow_export_items(time_workflow_export_id, time_entry_id)
    WHERE time_entry_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_time_workflow_export_items_export
    ON time_workflow_export_items(time_workflow_export_id);

CREATE INDEX IF NOT EXISTS idx_time_workflow_export_items_work_date
    ON time_workflow_export_items(work_date);

CREATE INDEX IF NOT EXISTS idx_time_workflow_export_metadata_sha256
    ON time_workflow_export_metadata(package_sha256);

COMMIT;
