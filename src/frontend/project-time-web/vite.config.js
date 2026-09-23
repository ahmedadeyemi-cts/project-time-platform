import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import roleJourneysPlugin from './scripts/role-journeys-vite-plugin.mjs';
import { normalizeCompletionCommercialRegion, createSourceTransactionPlugin } from './scripts/completion-build-compatibility.mjs';

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

function replaceExactly(code, needle, replacement, id, label) {
  const count = code.split(needle).length - 1;
  if (count !== 1) {
    throw new Error(`[customer-source-authority] Expected exactly one ${label} anchor in ${id}; found ${count}.`);
  }
  return code.replace(needle, replacement);
}

const customerSourceAuthorityCompatibility = {
  name: 'customer-source-authority-compatibility',
  enforce: 'pre',
  transform(sourceCode, id) {
    const sourceId = id.split('?')[0];
    let code = sourceCode;

    if (sourceId.endsWith('/BillingReadinessCenter.jsx')) {
      const warningHelper = `function fulfilledSourceWarnings(source, payload) {
  if (!payload || typeof payload !== 'object') return [];
  const warnings = [];
  if (String(payload.status || '').toLowerCase() === 'partial') {
    const detail = Array.isArray(payload.warnings) && payload.warnings.length
      ? payload.warnings.join(' ')
      : 'Some authoritative records are temporarily unavailable.';
    warnings.push({ source, message: detail });
  }
  if (payload.sources && typeof payload.sources === 'object') {
    Object.entries(payload.sources).forEach(([name, state]) => {
      const normalized = typeof state === 'string' ? state : state?.status;
      if (normalized && !['healthy', 'ready', 'available', 'loaded'].includes(String(normalized).toLowerCase())) {
        warnings.push({ source: \`${'${source}'} · ${'${name}'}\`, message: state?.message || \`Source reported ${'${normalized}'}.\` });
      }
    });
  }
  return warnings;
}`;

      const sourceAwareWarningHelper = `function sourceConditionName(state, fallback) {
  const raw = state && typeof state === 'object'
    ? state.source || state.sourceName || state.name || state.displayName || state.label || state.moduleName
    : '';
  return String(raw || fallback)
    .replaceAll('_', ' ')
    .replace(/\\b\\w/g, (character) => character.toUpperCase());
}

function fulfilledSourceWarnings(source, payload) {
  if (!payload || typeof payload !== 'object') return [];
  const warnings = [];
  if (String(payload.status || '').toLowerCase() === 'partial') {
    const detail = Array.isArray(payload.warnings) && payload.warnings.length
      ? payload.warnings.join(' ')
      : 'Some authoritative records are temporarily unavailable.';
    warnings.push({ source, message: detail });
  }
  if (Array.isArray(payload.sources)) {
    payload.sources.forEach((state, index) => {
      const normalized = typeof state === 'string' ? state : state?.status;
      if (normalized && !['healthy', 'ready', 'available', 'loaded'].includes(String(normalized).toLowerCase())) {
        const name = sourceConditionName(state, \`Source ${'${index + 1}'}\`);
        warnings.push({ source: \`${'${source}'} · ${'${name}'}\`, message: state?.message || \`Source reported ${'${normalized}'}.\` });
      }
    });
  } else if (payload.sources && typeof payload.sources === 'object') {
    Object.entries(payload.sources).forEach(([name, state]) => {
      const normalized = typeof state === 'string' ? state : state?.status;
      if (normalized && !['healthy', 'ready', 'available', 'loaded'].includes(String(normalized).toLowerCase())) {
        warnings.push({ source: \`${'${source}'} · ${'${name}'}\`, message: state?.message || \`Source reported ${'${normalized}'}.\` });
      }
    });
  }
  return warnings;
}`;

      code = replaceExactly(code, warningHelper, sourceAwareWarningHelper, id, 'Module 039 source-warning helper');

    }

    if (sourceId.endsWith('/InvoiceBillingCenter.jsx')) {
      code = replaceExactly(
        code,
        "const source = commercial.commercialSource === 'SELL' ? 'ConnectWise SELL' : 'Current stored rates';",
        "const source = commercial.commercialSource === 'SELL' ? 'ConnectWise SELL' : text(commercial.commercialSource, 'Current stored rates');",
        id,
        'Module 042 commercial source label'
      );
      code = normalizeCompletionCommercialRegion(code, id);
      code = replaceExactly(
        code,
        "<strong>{selected.commercial?.commercialSource === 'SELL' ? 'ConnectWise SELL' : 'Current stored rates'}</strong>",
        "<strong>{selected.commercial?.commercialSource === 'SELL' ? 'ConnectWise SELL' : text(selected.commercial?.commercialSource, 'Current stored rates')}</strong>",
        id,
        'Module 042 selected commercial source'
      );

      code = replaceExactly(
        code,
        "<div><dt>Last ConnectWise SELL sync</dt><dd>{selected.commercial?.lastSuccessfulSyncAt ? formatDateTime(selected.commercial.lastSuccessfulSyncAt) : 'No successful ConnectWise SELL sync recorded'}</dd></div>",
        "<div><dt>Last source sync</dt><dd>{selected.commercial?.commercialSource === 'MANUAL' ? 'Not applicable for manual source' : selected.commercial?.lastSuccessfulSyncAt ? formatDateTime(selected.commercial.lastSuccessfulSyncAt) : 'No successful customer-source sync recorded'}</dd></div>",
        id,
        'Module 042 source sync detail'
      );
    }

    if (sourceId.endsWith('/CustomerDirectoryCenter.jsx')) {
      code = replaceExactly(
        code,
        'Pull authoritative customer organizations from ConnectWise SELL, then enrich each ProjectPulse customer with locally maintained contacts, relationships, addresses, and workflow context.',
        'Choose ConnectWise SELL, another configured Module 026 CRM/ERP provider, or Manual as the authoritative customer source. Local contacts, relationships, addresses, and workflow context remain managed in Module 021.',
        id,
        'Module 021 source description'
      );
    }

    return code;
  }
};

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

const celarAiProductionSourceTransaction = createSourceTransactionPlugin({
  prepare: prepareCelarAiProductionSources,
  restore: restoreCelarAiProductionSources,
  verify: verifyFlowHiveBrowserContract
});

export default defineConfig({
  plugins: [customerSourceAuthorityCompatibility, roleJourneysPlugin(), react(), celarAiProductionSourceTransaction],
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