BEGIN;
CREATE TABLE IF NOT EXISTS module065_teams_configuration (
 environment text PRIMARY KEY CHECK (environment IN ('test','production')),
 enabled boolean NOT NULL DEFAULT false,
 teams_app_id uuid,
 revision integer NOT NULL DEFAULT 1,
 updated_by uuid NOT NULL,
 updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS module065_teams_delivery (
 delivery_id uuid PRIMARY KEY,
 dispatch_id uuid NOT NULL,
 environment text NOT NULL CHECK (environment IN ('test','production')),
 recipient text NOT NULL,
 status text NOT NULL CHECK (status IN ('sending','sent','failed','outcome_unknown')),
 diagnostic_code text NOT NULL DEFAULT '',
 created_at timestamptz NOT NULL DEFAULT now(),
 updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(dispatch_id, environment, recipient)
);
INSERT INTO schema_migrations (migration_id, description) VALUES ('113_module065_teams_notifications', 'Environment-scoped Teams configuration and durable delivery evidence') ON CONFLICT DO NOTHING;
COMMIT;
