import { existsSync } from 'node:fs';
import { resolve } from 'node:path';

const migrationRoot = resolve(process.cwd(), '../../../database/migrations/040_scoped_role_policy_versions');
const requiredFiles = ['00_schema.sql', '10_workbook_cells.sql'];
const available = requiredFiles.every((name) => existsSync(resolve(migrationRoot, name)));

if (!available) {
  console.log('MATRIX_3_BASELINE_CHECK=SKIPPED_REDUCED_WEB_CONTAINER_CONTEXT');
} else {
  await import('./validate-permissions-matrix-3-baseline.mjs');
}
