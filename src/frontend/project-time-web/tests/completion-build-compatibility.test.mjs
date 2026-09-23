import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { normalizeCompletionCommercialRegion, createSourceTransactionPlugin } from '../scripts/completion-build-compatibility.mjs';
const invoice = readFileSync(new URL('../src/InvoiceBillingCenter.jsx', import.meta.url), 'utf8');
const config = readFileSync(new URL('../vite.config.js', import.meta.url), 'utf8');

test('the actual billing disclosure retains its summary and gets a neutral source label', () => {
  const transformed = normalizeCompletionCommercialRegion(invoice, 'InvoiceBillingCenter.jsx');
  assert.ok(transformed.includes('<details className="m0423-commercial" aria-label="Commercial source">'));
  assert.ok(transformed.includes('<summary>Commercial setup for invoices created in Pulse</summary>'));
  assert.equal(transformed.replace('aria-label="Commercial source"', 'aria-label="ConnectWise SELL commercial source"'), invoice);
  assert.ok(config.includes('code = normalizeCompletionCommercialRegion(code, id);'));
});
test('missing, duplicated, already transformed and wrong-element anchors fail closed', () => {
  for (const source of ['', invoice + invoice, invoice.replace('<details className="m0423-commercial"', '<section className="m0423-commercial"'), normalizeCompletionCommercialRegion(invoice, 'test')]) {
    assert.throws(() => normalizeCompletionCommercialRegion(source, 'test'), /Expected exactly one/);
  }
});
test('successful compilation still requires the full bundle verifier and source restoration', async () => {
  const calls = [];
  const plugin = createSourceTransactionPlugin({ prepare: async () => calls.push('prepare'), restore: async () => calls.push('restore'), verify: () => calls.push('verify') });
  await plugin.buildStart(); await plugin.buildEnd(); await plugin.closeBundle();
  assert.deepEqual(calls, ['prepare', 'verify', 'restore']);
  assert.ok(config.includes('verify: verifyFlowHiveBrowserContract'));
  assert.ok(config.includes("FLOWHIVE_BROWSER_CONTRACT_FAILED=compiled_javascript_missing"));
});
test('a prior transform failure is not masked by a secondary missing-output error', async () => {
  const calls = [];
  const plugin = createSourceTransactionPlugin({ prepare: async () => calls.push('prepare'), restore: async () => calls.push('restore'), verify: () => { throw new Error('secondary missing output'); } });
  await plugin.buildStart(); await plugin.buildEnd(new Error('original transform failure')); await plugin.closeBundle();
  assert.deepEqual(calls, ['prepare', 'restore', 'restore']);
});
test('a bad bundle after successful compilation still fails and restores sources', async () => {
  let restored = 0;
  const failure = new Error('bundle contract failed');
  const plugin = createSourceTransactionPlugin({ prepare: async () => {}, restore: async () => { restored++; }, verify: () => { throw failure; } });
  await plugin.buildStart(); await plugin.buildEnd();
  await assert.rejects(plugin.closeBundle(), error => error === failure);
  assert.equal(restored, 1);
});
test('a subsequent build re-enables verification after an earlier failure', async () => {
  let verified = 0;
  const plugin = createSourceTransactionPlugin({ prepare: async () => {}, restore: async () => {}, verify: () => { verified++; } });
  await plugin.buildStart(); await plugin.buildEnd(new Error('failed')); await plugin.closeBundle();
  await plugin.buildStart(); await plugin.buildEnd(); await plugin.closeBundle();
  assert.equal(verified, 1);
});
