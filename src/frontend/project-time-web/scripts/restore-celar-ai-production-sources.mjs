import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = fileURLToPath(new URL('../', import.meta.url));
const sourceRoot = path.join(webRoot, 'src');
const backupRoot = path.join(webRoot, '.celar-ai-production-build-backup');
const files = [
  'WorkTaskBuilderPanel.jsx',
  'HelpAssistant.jsx',
  'ProjectFlowHiveCenter.jsx',
  'CelarAiProductionPlatform.jsx',
  'DefectTrackerCenter.jsx'
];

if (!fs.existsSync(backupRoot)) {
  console.log('CELAR_AI_PRODUCTION_BUILD_TRANSACTION=NO_BACKUP');
  process.exit(0);
}

for (const relative of files) {
  const backup = path.join(backupRoot, relative);
  if (!fs.existsSync(backup)) throw new Error(`CELAR_AI_PRODUCTION_RESTORE_SOURCE_MISSING=${relative}`);
  fs.copyFileSync(backup, path.join(sourceRoot, relative));
}
fs.rmSync(backupRoot, { recursive: true, force: true });
console.log(`CELAR_AI_PRODUCTION_BUILD_RESTORED_FILES=${files.length}`);
console.log('CELAR_AI_PRODUCTION_BUILD_TRANSACTION=CLOSED');
