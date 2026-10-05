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

test('invoice center requires project context before project-specific manual operations', () => {
  const source = read('src/InvoiceBillingCenter.jsx');
  const projectIndex = source.indexOf('id="finance-line-invoice"');
  const manualIndex = source.indexOf('id="finance-manual-operations"');
  assert.ok(projectIndex >= 0, 'project billing workspace anchor missing');
  assert.ok(manualIndex > projectIndex, 'manual operations must follow project selection/review');
  assert.ok(source.includes('Production manual path'));
  assert.ok(source.includes('Finish the Finance workflow for'));
  assert.ok(source.includes('Record the external invoice or handoff reference below'));
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

test('billing readiness and invoice center use the shared finance operating workflow', () => {
  const readiness = read('src/BillingReadinessCenter.jsx');
  const invoice = read('src/InvoiceBillingCenter.jsx');
  assert.ok(readiness.includes('<FinanceBillingWorkflow stage="readiness" />'));
  assert.ok(invoice.includes('<FinanceBillingWorkflow'));
  assert.ok(invoice.includes('connectorStatuses={payload.connectorStatuses}'));
  assert.ok(readiness.includes('id="billing-readiness-workflow"'));
});
