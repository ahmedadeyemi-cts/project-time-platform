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

const celarAiProductionSourceTransaction = {
  name: 'celar-ai-production-source-transaction',
  apply: 'build',
  async buildEnd(error) {
    if (error) await restoreCelarAiProductionSources();
  },
  async closeBundle() {
    await restoreCelarAiProductionSources();
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
