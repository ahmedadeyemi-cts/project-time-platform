import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const root = resolve(import.meta.dirname, '..');
const read = path => readFileSync(resolve(root, path), 'utf8');

test('finance billing workflow is explicit, ordered, and connector aware', () => {
  const workflow = read('src/FinanceBillingWorkflow.jsx');
  for (const marker of [
    'Billing from readiness through closeout',
    'Controlled manual mode',
    'Manual-capable workflow',
    'ConnectWise SELL',
    'Salesforce',
    'Certinia',
    'Creating an invoice in Pulse does not prove external delivery'
  ]) assert.ok(workflow.includes(marker), marker);

  for (const step of [
    'Confirm readiness',
    'Create invoice',
    'Deliver / hand off',
    'Reconcile',
    'Confirm fully billed',
    'Project closeout'
  ]) assert.ok(workflow.includes(step), step);
});

test('invoice center uses the stable recovery layout while preserving manual billing controls', () => {
  const source = read('src/InvoiceBillingCenter.jsx');
  const manualIndex = source.indexOf('<ManualInvoicePanel');
  const workspaceIndex = source.indexOf('className="m042-workspace"');
  assert.ok(manualIndex >= 0, 'manual billing controls missing');
  assert.ok(manualIndex < workspaceIndex, 'stable invoice layout must mount project controls before the workspace');
  assert.ok(source.includes('Create an invoice in three steps'));
  assert.ok(source.includes('Manual invoicing works without Certinia or SELL'));
});

test('manual amount billing and Certinia fallback are governed production paths', () => {
  const manual = read('src/ManualInvoicePanel.jsx');
  const certinia = read('src/CertiniaInvoiceDeliveryPanel.jsx');
  assert.ok(manual.includes('Fixed-price project amount invoice (manual)'));
  assert.ok(manual.includes('Pulse deducts prior Pulse invoices and recorded external billing'));
  assert.ok(certinia.includes('Manual delivery is the production path until Certinia is connected'));
  assert.ok(certinia.includes('Do not use a second Pulse invoice to represent the external handoff'));
  assert.ok(certinia.includes('BillingReconciliationPanel'));
});

test('billing readiness keeps the shared finance workflow while invoice center stays on the stable recovery shell', () => {
  const readiness = read('src/BillingReadinessCenter.jsx');
  const invoice = read('src/InvoiceBillingCenter.jsx');
  assert.ok(readiness.includes('<FinanceBillingWorkflow stage="readiness" />'));
  assert.ok(readiness.includes('id="billing-readiness-workflow"'));
  assert.ok(!invoice.includes('<FinanceBillingWorkflow'));
  assert.ok(invoice.includes('<ManualInvoicePanel'));
  assert.ok(invoice.includes('<ProjectCompletionChecklist'));
});
