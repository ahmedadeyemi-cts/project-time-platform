import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');

async function text(path) {
  return readFile(resolve(root, path), 'utf8');
}

function requireText(source, values, label) {
  for (const value of values) {
    if (!source.includes(value)) {
      throw new Error(`${label} is missing required contract: ${value}`);
    }
  }
}

const migration = await text(
  'database/migrations/039_work_to_cash_reactivation_lock_order.sql'
);
const rollback = await text(
  'database/rollback/039_work_to_cash_reactivation_lock_order_rollback.sql'
);

requireText(migration, [
  'BEGIN;',
  'COMMIT;',
  "WHERE migration_id = '038_work_to_cash_lifecycle_and_audit'",
  'CREATE OR REPLACE FUNCTION projectpulse038_guard_invoice_reactivation()',
  'FOR v_readiness_review_id IN',
  'ORDER BY target_line.billing_readiness_review_id',
  'hashtextextended(v_readiness_review_id::text, 38)',
  'FOR v_time_entry_id IN',
  'ORDER BY target_line.time_entry_id',
  'hashtextextended(v_time_entry_id::text, 0)',
  'replacement invoice lines exist',
  'replacement non-labor package line exists',
  "'039_work_to_cash_reactivation_lock_order'"
], 'Migration 039');

const functionBody = migration.slice(
  migration.indexOf(
    'CREATE OR REPLACE FUNCTION projectpulse038_guard_invoice_reactivation()'
  ),
  migration.indexOf('INSERT INTO schema_migrations')
);

const readinessLockIndex = functionBody.indexOf('FOR v_readiness_review_id IN');
const timeEntryLockIndex = functionBody.indexOf('FOR v_time_entry_id IN');

if (readinessLockIndex < 0
    || timeEntryLockIndex < 0
    || readinessLockIndex > timeEntryLockIndex) {
  throw new Error(
    'Invoice reactivation must acquire readiness-package locks before time-entry locks.'
  );
}

if ((migration.match(/\bBEGIN;/g) ?? []).length !== 1
    || (migration.match(/\bCOMMIT;/g) ?? []).length !== 1) {
  throw new Error('Migration 039 must remain one atomic transaction.');
}

requireText(rollback, [
  'BEGIN;',
  'COMMIT;',
  'CREATE OR REPLACE FUNCTION projectpulse038_guard_invoice_reactivation()',
  'FOR v_time_entry_id IN',
  'FOR v_readiness_review_id IN',
  "DELETE FROM schema_migrations\nWHERE migration_id = '039_work_to_cash_reactivation_lock_order';"
], 'Migration 039 rollback');

const rollbackFunctionBody = rollback.slice(
  rollback.indexOf(
    'CREATE OR REPLACE FUNCTION projectpulse038_guard_invoice_reactivation()'
  ),
  rollback.indexOf('DELETE FROM schema_migrations')
);

if (rollbackFunctionBody.indexOf('FOR v_time_entry_id IN')
    > rollbackFunctionBody.indexOf('FOR v_readiness_review_id IN')) {
  throw new Error(
    'Migration 039 rollback must restore the exact migration 038 lock order.'
  );
}

console.log('Work-to-Cash migration 039 lock-order validation passed.');
