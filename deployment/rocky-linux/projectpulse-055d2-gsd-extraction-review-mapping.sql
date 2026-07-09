-- 055D.2 - GSD extraction and intake review mapping

BEGIN;

ALTER TABLE work_register_intake_packages
    ADD COLUMN IF NOT EXISTS review_status varchar(60) NOT NULL DEFAULT 'not_started';

ALTER TABLE work_register_intake_packages
    ADD COLUMN IF NOT EXISTS reviewed_json jsonb NOT NULL DEFAULT '{}'::jsonb;

ALTER TABLE work_register_intake_packages
    ADD COLUMN IF NOT EXISTS reviewed_by_user_id uuid NULL;

ALTER TABLE work_register_intake_packages
    ADD COLUMN IF NOT EXISTS reviewed_at timestamp with time zone NULL;

CREATE INDEX IF NOT EXISTS idx_work_register_intake_packages_review_status
    ON work_register_intake_packages(review_status, created_at DESC);

COMMIT;
