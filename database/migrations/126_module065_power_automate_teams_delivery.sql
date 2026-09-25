BEGIN;

ALTER TABLE module065_teams_configuration
  ADD COLUMN IF NOT EXISTS delivery_mode text NOT NULL DEFAULT 'graph_app',
  ADD COLUMN IF NOT EXISTS workflow_trigger_url text,
  ADD COLUMN IF NOT EXISTS workflow_audience text NOT NULL DEFAULT 'https://service.flow.microsoft.com/';

ALTER TABLE module065_teams_configuration
  DROP CONSTRAINT IF EXISTS module065_teams_configuration_delivery_mode_check;
ALTER TABLE module065_teams_configuration
  ADD CONSTRAINT module065_teams_configuration_delivery_mode_check
  CHECK (delivery_mode IN ('graph_app','power_automate'));

ALTER TABLE module065_teams_configuration
  DROP CONSTRAINT IF EXISTS module065_teams_configuration_workflow_trigger_url_check;
ALTER TABLE module065_teams_configuration
  ADD CONSTRAINT module065_teams_configuration_workflow_trigger_url_check
  CHECK (
    workflow_trigger_url IS NULL OR (
      char_length(workflow_trigger_url) BETWEEN 12 AND 2048
      AND workflow_trigger_url LIKE 'https://%'
    )
  );

INSERT INTO schema_migrations (migration_id, description)
VALUES ('126_module065_power_automate_teams_delivery',
        'Add centrally managed Power Automate Teams delivery mode to Module 065')
ON CONFLICT DO NOTHING;

COMMIT;
