BEGIN;
CREATE TABLE IF NOT EXISTS module065_teams_outbox_events (
 dispatch_id uuid NOT NULL,
 environment text NOT NULL CHECK(environment IN ('test','production')),
 snapshot jsonb NOT NULL CHECK(jsonb_typeof(snapshot)='object'),
 created_at timestamptz NOT NULL DEFAULT now(),
 expires_at timestamptz NOT NULL DEFAULT now()+interval '48 hours',
 PRIMARY KEY(dispatch_id,environment)
);
CREATE TABLE IF NOT EXISTS module065_teams_outbox (
 dispatch_id uuid NOT NULL,
 environment text NOT NULL,
 recipient text NOT NULL,
 status text NOT NULL CHECK(status IN ('queued','retry_wait','sending','accepted','suppressed','failed','outcome_unknown')),
 available_at timestamptz NOT NULL DEFAULT now(),
 attempt_count integer NOT NULL DEFAULT 0 CHECK(attempt_count BETWEEN 0 AND 5),
 diagnostic_code text NOT NULL DEFAULT '',
 updated_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(dispatch_id,environment,recipient),
 FOREIGN KEY(dispatch_id,environment) REFERENCES module065_teams_outbox_events(dispatch_id,environment)
);
CREATE INDEX IF NOT EXISTS ix_module065_teams_outbox_due ON module065_teams_outbox(environment,available_at)
 WHERE status IN ('queued','retry_wait');
CREATE TABLE IF NOT EXISTS module065_teams_outbox_attempts (
 attempt_id uuid PRIMARY KEY,
 dispatch_id uuid NOT NULL,
 environment text NOT NULL,
 recipient text NOT NULL,
 started_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(dispatch_id,environment,recipient) REFERENCES module065_teams_outbox(dispatch_id,environment,recipient)
);
CREATE INDEX IF NOT EXISTS ix_module065_teams_outbox_attempt_window ON module065_teams_outbox_attempts(environment,started_at);
CREATE TABLE IF NOT EXISTS module065_teams_outbox_actions (
 action_id uuid PRIMARY KEY,
 dispatch_id uuid NOT NULL,
 environment text NOT NULL,
 recipient text NOT NULL,
 actor_user_id uuid NOT NULL,
 action text NOT NULL CHECK(action='retry_failed_teams_only'),
 occurred_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(dispatch_id,environment,recipient) REFERENCES module065_teams_outbox(dispatch_id,environment,recipient)
);
DO $grant$
BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='ptp_app') THEN
  GRANT SELECT,INSERT,UPDATE ON module065_teams_outbox_events,module065_teams_outbox,module065_teams_outbox_attempts TO ptp_app;
  GRANT SELECT,INSERT ON module065_teams_outbox_actions TO ptp_app;
 END IF;
END $grant$;
INSERT INTO schema_migrations(migration_id,description)
VALUES('128_module065_email_teams_notification_parity','Independent, throttled Teams notification mirror outbox; no live-delivery activation or historical backfill')
ON CONFLICT DO NOTHING;
COMMIT;
