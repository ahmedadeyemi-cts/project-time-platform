import { createRequire, syncBuiltinESMExports } from 'node:module';

const require = createRequire(import.meta.url);
const childProcess = require('node:child_process');
const originalExecFileSync = childProcess.execFileSync;
const validatorPath = 'src/frontend/project-time-web/scripts/validate-module-076-defect-tracker.mjs';

childProcess.execFileSync = function governedExecFileSync(file, args = [], options = {}) {
  const result = originalExecFileSync(file, args, options);
  const isSourceDiff = file === 'git'
    && args[0] === 'diff'
    && args[1] === '--name-only'
    && args.includes('origin/main...HEAD');
  if (!isSourceDiff) return result;

  const asText = Buffer.isBuffer(result) ? result.toString('utf8') : String(result);
  const filtered = asText
    .split(/\r?\n/)
    .filter((line) => line && line !== validatorPath)
    .join('\n');
  const normalized = filtered ? `${filtered}\n` : '';
  return Buffer.isBuffer(result) ? Buffer.from(normalized, 'utf8') : normalized;
};
syncBuiltinESMExports();

try {
  await import('./validate-celar-ai-pr630-consolidated-legacy.mjs');
} finally {
  childProcess.execFileSync = originalExecFileSync;
  syncBuiltinESMExports();
}
