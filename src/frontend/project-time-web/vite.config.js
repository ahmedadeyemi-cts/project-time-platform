import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const webRoot = fileURLToPath(new URL('./', import.meta.url));
const celarAiProductionBackupRoot = path.join(webRoot, '.celar-ai-production-build-backup');
let celarAiProductionRestoreStarted = false;

async function restoreCelarAiProductionSources() {
  if (celarAiProductionRestoreStarted || !fs.existsSync(celarAiProductionBackupRoot)) return;
  celarAiProductionRestoreStarted = true;
  await import('./scripts/restore-celar-ai-production-sources.mjs');
}

async function prepareCelarAiProductionSources() {
  if (!fs.existsSync(celarAiProductionBackupRoot)) {
    await import('./scripts/backup-celar-ai-production-sources.mjs');
  }
  await import('./scripts/inject-celar-ai-production-platform.mjs');
}

function compiledJavascript(root) {
  if (!fs.existsSync(root)) return '';
  return fs.readdirSync(root, { withFileTypes: true })
    .flatMap((entry) => {
      const fullPath = path.join(root, entry.name);
      if (entry.isDirectory()) return [compiledJavascript(fullPath)];
      return entry.isFile() && entry.name.endsWith('.js') ? [fs.readFileSync(fullPath, 'utf8')] : [];
    })
    .join('\n');
}

function verifyFlowHiveBrowserContract() {
  const bundle = compiledJavascript(path.join(webRoot, 'dist'));
  if (!bundle) throw new Error('FLOWHIVE_BROWSER_CONTRACT_FAILED=compiled_javascript_missing');

  for (const marker of [
    '/api/project-flowhive/projects/',
    '/ai-planner/runs',
    'AI Planning Workspace',
    'data-projectpulse-055c-shared-delete',
    'active FlowHive/Project Forge evidence'
  ]) {
    if (!bundle.includes(marker)) {
      throw new Error(`FLOWHIVE_BROWSER_CONTRACT_FAILED=compiled_bundle_missing_${marker.replaceAll(/[^a-z0-9]+/gi, '_')}`);
    }
  }

  if (bundle.includes('/api/project-flowhive/ai/production-generate')) {
    throw new Error('FLOWHIVE_BROWSER_CONTRACT_FAILED=legacy_production_generate_is_browser_reachable');
  }

  console.log('FLOWHIVE_BROWSER_DURABLE_PLANNER_ROUTE=VERIFIED');
  console.log('FLOWHIVE_BROWSER_LEGACY_PLANNER_ROUTE=ABSENT');
  console.log('WORK_REGISTER_BROWSER_SOW_GSD_DELETE=VERIFIED');
}

const celarAiProductionSourceTransaction = {
  name: 'celar-ai-production-source-transaction',
  apply: 'build',
  async buildStart() {
    await prepareCelarAiProductionSources();
  },
  async buildEnd(error) {
    if (error) await restoreCelarAiProductionSources();
  },
  async closeBundle() {
    try {
      verifyFlowHiveBrowserContract();
    } finally {
      await restoreCelarAiProductionSources();
    }
  }
};

export default defineConfig({
  plugins: [react(), celarAiProductionSourceTransaction],
  server: {
    host: '127.0.0.1',
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5080',
        changeOrigin: true
      },
      '/health': {
        target: 'http://127.0.0.1:5080',
        changeOrigin: true
      }
    }
  },
  preview: {
    host: '127.0.0.1',
    port: 4173
  }
});
