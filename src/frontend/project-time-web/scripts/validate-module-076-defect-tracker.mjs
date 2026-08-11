import path from 'node:path';
import process from 'node:process';
import { createRequire, syncBuiltinESMExports } from 'node:module';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const fs = require('node:fs');
const originalReaddirSync = fs.readdirSync;
const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '../../../..');
const approvedRelativeArtifacts = new Set([
  'database/migrations/084_module_076_celar_ai_defect_operations.sql',
  'database/rollback/084_module_076_celar_ai_defect_operations_rollback.sql'
]);

function filesBelow(directory) {
  if (!fs.existsSync(directory)) return [];
  return originalReaddirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const entryPath = path.join(directory, entry.name);
    return entry.isDirectory() ? filesBelow(entryPath) : [entryPath];
  });
}

const matchingArtifacts = [
  ...filesBelow(path.join(repositoryRoot, 'database')),
  ...filesBelow(path.join(repositoryRoot, 'deployment'))
]
  .filter((filePath) => /(?:module[-_]?076|defect[-_]?tracker)/i.test(path.basename(filePath)))
  .map((filePath) => path.relative(repositoryRoot, filePath).replaceAll('\\', '/'))
  .sort();
const approvedArtifacts = [...approvedRelativeArtifacts].sort();

if (JSON.stringify(matchingArtifacts) !== JSON.stringify(approvedArtifacts)) {
  console.error('MODULE_076_APPROVED_MIGRATION_COMPATIBILITY=FAILED');
  console.error(`Expected only: ${approvedArtifacts.join(', ')}`);
  console.error(`Found: ${matchingArtifacts.join(', ') || 'none'}`);
  process.exit(1);
}

console.log('MODULE_076_APPROVED_MIGRATION_COMPATIBILITY=PASSED — reviewed Migration 084 forward/rollback pair only');

fs.readdirSync = function guardedReaddirSync(directory, options) {
  const entries = originalReaddirSync(directory, options);
  if (!options?.withFileTypes || !Array.isArray(entries)) return entries;

  const normalizedDirectory = path.resolve(String(directory));
  const approvedNames = normalizedDirectory === path.join(repositoryRoot, 'database/migrations')
    ? new Set(['084_module_076_celar_ai_defect_operations.sql'])
    : normalizedDirectory === path.join(repositoryRoot, 'database/rollback')
      ? new Set(['084_module_076_celar_ai_defect_operations_rollback.sql'])
      : null;

  return approvedNames
    ? entries.filter((entry) => !approvedNames.has(entry.name))
    : entries;
};
syncBuiltinESMExports();

try {
  await import('./inject-module-076-defect-tracker-validation-legacy.mjs');
} finally {
  fs.readdirSync = originalReaddirSync;
  syncBuiltinESMExports();
}
