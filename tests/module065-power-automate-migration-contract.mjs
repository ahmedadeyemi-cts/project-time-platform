import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const workflowPath = new URL('../.github/workflows/projectpulse-deploy-test.yml', import.meta.url);
const migrationPath = new URL('../database/migrations/126_module065_power_automate_teams_delivery.sql', import.meta.url);

test('Protected UAT migrator includes and verifies Module 065 migration 126', async () => {
  const workflow = await readFile(workflowPath, 'utf8');
  const migration = await readFile(migrationPath, 'utf8');

  for (const expected of [
    '126_module065_power_automate_teams_delivery.sql',
    "migration_id IN ('112_optional_ai_providers','113_module065_teams_notifications','114_module064_private_admin_override','126_module065_power_automate_teams_delivery')",
    "column_name='delivery_mode'",
    "column_name='workflow_trigger_url'",
    "column_name='workflow_audience'",
    'MIGRATIONS_112_113_114_126=APPLIED_AND_VERIFIED',
  ]) assert.ok(workflow.includes(expected), expected);

  for (const expected of [
    'ADD COLUMN IF NOT EXISTS delivery_mode',
    'ADD COLUMN IF NOT EXISTS workflow_trigger_url',
    'ADD COLUMN IF NOT EXISTS workflow_audience',
    "'126_module065_power_automate_teams_delivery'",
  ]) assert.ok(migration.includes(expected), expected);
});

test('Protected UAT evidence names migration 126', async () => {
  const workflow = await readFile(workflowPath, 'utf8');
  const evidenceOccurrences = workflow.match(/126_module065_power_automate_teams_delivery/g) || [];
  assert.ok(evidenceOccurrences.length >= 5, 'migration 126 should be copied, executed, verified, and recorded in evidence');
});
