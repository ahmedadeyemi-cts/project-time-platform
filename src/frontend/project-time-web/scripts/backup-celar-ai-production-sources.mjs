import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = fileURLToPath(new URL('../', import.meta.url));
const sourceRoot = path.join(webRoot, 'src');
const backupRoot = path.join(webRoot, '.celar-ai-production-build-backup');
const files = [
  'App.jsx',
  'module-availability-registry.js',
  'WorkTaskBuilderPanel.jsx',
  'HelpAssistant.jsx',
  'ProjectFlowHiveCenter.jsx',
  'CelarAiProductionPlatform.jsx',
  'DefectTrackerCenter.jsx'
];

function restoreExistingBackup() {
  if (!fs.existsSync(backupRoot)) return;
  for (const relative of files) {
    const backup = path.join(backupRoot, relative);
    if (fs.existsSync(backup)) fs.copyFileSync(backup, path.join(sourceRoot, relative));
  }
  fs.rmSync(backupRoot, { recursive: true, force: true });
  console.log('CELAR_AI_PRODUCTION_RECOVERED_PREVIOUS_FAILED_BUILD=YES');
}

restoreExistingBackup();
fs.mkdirSync(backupRoot, { recursive: true });
for (const relative of files) {
  const source = path.join(sourceRoot, relative);
  if (!fs.existsSync(source)) throw new Error(`CELAR_AI_PRODUCTION_BACKUP_SOURCE_MISSING=${relative}`);
  fs.copyFileSync(source, path.join(backupRoot, relative));
}
console.log(`CELAR_AI_PRODUCTION_BUILD_BACKUP_FILES=${files.length}`);
console.log('CELAR_AI_PRODUCTION_BUILD_TRANSACTION=OPEN');

// Module 084 is a build-time browser integration, matching the existing Celar
// source transaction. App.jsx and the static module registry are restored after
// the bundle closes, so ordinary builds never dirty canonical tracked source.
await import('./inject-module-084-celar-ai-runtime-version.mjs');
console.log('MODULE_084_CELAR_RUNTIME_VERSION_BUILD_INJECTION=READY');
