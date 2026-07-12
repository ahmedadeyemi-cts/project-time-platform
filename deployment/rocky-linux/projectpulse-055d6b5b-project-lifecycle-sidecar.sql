BEGIN;

CREATE TABLE IF NOT EXISTS work_register_project_lifecycle (
    project_id UUID PRIMARY KEY
        REFERENCES projects(project_id)
        ON DELETE CASCADE,

    is_archived BOOLEAN NOT NULL DEFAULT FALSE,

    archived_at TIMESTAMPTZ NULL,
    archived_by_user_id UUID NULL,
    archive_reason TEXT NULL,

    restored_at TIMESTAMPTZ NULL,
    restored_by_user_id UUID NULL,
    restore_reason TEXT NULL,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_work_register_project_lifecycle_archive_state
        CHECK (
            is_archived = FALSE
            OR (
                archived_at IS NOT NULL
                AND archived_by_user_id IS NOT NULL
                AND NULLIF(BTRIM(COALESCE(archive_reason, '')), '') IS NOT NULL
            )
        )
);

CREATE INDEX IF NOT EXISTS
    ix_work_register_project_lifecycle_is_archived
ON work_register_project_lifecycle (
    is_archived,
    updated_at DESC
);

COMMIT;
