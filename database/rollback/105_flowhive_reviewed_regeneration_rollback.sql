BEGIN;
DO $guard$
BEGIN
    IF EXISTS(SELECT 1 FROM project_flowhive_ai_plan_reviews) THEN
        RAISE EXCEPTION 'Rollback refused: immutable reviewed-regeneration evidence must be retained.';
    END IF;
END;
$guard$;
DROP TABLE project_flowhive_ai_plan_reviews;
DROP FUNCTION projectpulse105_immutable_plan_review();
DELETE FROM schema_migrations WHERE migration_id='105_flowhive_reviewed_regeneration';
COMMIT;
