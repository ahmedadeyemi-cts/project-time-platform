-- Reviewed reconciliation of a generated candidate; no existing work is rewritten by migration.
BEGIN;
DO $prerequisites$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='104_flowhive_bounded_ai_execution') THEN
        RAISE EXCEPTION 'Reviewed regeneration requires migration 104.';
    END IF;
END;
$prerequisites$;
CREATE TABLE IF NOT EXISTS project_flowhive_ai_plan_reviews (
    review_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id UUID NOT NULL UNIQUE REFERENCES project_flowhive_ai_planner_runs(run_id) ON DELETE RESTRICT,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
    actor_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    expected_row_version UUID NULL,
    applied_row_version UUID NOT NULL,
    applied_revision INTEGER NOT NULL CHECK(applied_revision>0),
    preview_fingerprint VARCHAR(64) NOT NULL CHECK(preview_fingerprint ~ '^[a-f0-9]{64}$'),
    prior_plan JSONB NOT NULL CHECK(jsonb_typeof(prior_plan)='object'),
    candidate_plan JSONB NOT NULL CHECK(jsonb_typeof(candidate_plan)='object'),
    applied_plan JSONB NOT NULL CHECK(jsonb_typeof(applied_plan)='object'),
    decisions JSONB NOT NULL CHECK(jsonb_typeof(decisions)='array'),
    review_note TEXT NOT NULL CHECK(char_length(review_note) BETWEEN 10 AND 4000),
    reviewed_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX IF NOT EXISTS ix_flowhive_105_project_reviews ON project_flowhive_ai_plan_reviews(project_id,reviewed_at DESC);
CREATE OR REPLACE FUNCTION projectpulse105_immutable_plan_review()
RETURNS TRIGGER LANGUAGE plpgsql AS $body$
BEGIN
    RAISE EXCEPTION 'Reviewed regeneration evidence is immutable.';
END;
$body$;
DROP TRIGGER IF EXISTS trg_flowhive_105_immutable_review ON project_flowhive_ai_plan_reviews;
CREATE TRIGGER trg_flowhive_105_immutable_review BEFORE UPDATE OR DELETE ON project_flowhive_ai_plan_reviews
FOR EACH ROW EXECUTE FUNCTION projectpulse105_immutable_plan_review();
INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('105_flowhive_reviewed_regeneration','Immutable prior/candidate/applied snapshots and explicit milestone-preserving plan review receipts',NOW())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
