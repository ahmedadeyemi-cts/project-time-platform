BEGIN;

DROP TABLE IF EXISTS ai_private_model_profile_audit;
DROP TABLE IF EXISTS ai_private_model_profiles;
DROP TABLE IF EXISTS ai_capability_route_audit;
DROP TABLE IF EXISTS ai_capability_routes;

DELETE FROM schema_migrations
WHERE migration_id = '061_celar_ai_capability_routing';

COMMIT;
