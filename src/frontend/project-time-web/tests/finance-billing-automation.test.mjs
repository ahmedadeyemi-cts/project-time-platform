import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const webRoot = resolve(import.meta.dirname, '..');
const repoRoot = resolve(webRoot, '../../..');
const readWeb = path => readFileSync(resolve(webRoot, path), 'utf8');
const readRepo = path => readFileSync(resolve(repoRoot, path), 'utf8');

test('billing readiness selects customer before project', () => {
  const source = readWeb('src/BillingReadinessCenter.jsx');
  assert.ok(source.includes('const [selectedCustomerName, setSelectedCustomerName]'));
  assert.ok(source.includes('const customerProjectCandidates = useMemo'));
  assert.ok(source.includes('No projects for this customer'));
  assert.ok(source.indexOf('            Customer\n') < source.indexOf('            Project\n'));
});

test('invoice center filters by customer and keeps the selected preview in visible scope', () => {
  const source = readWeb('src/InvoiceBillingCenter.jsx');
  assert.ok(source.includes("const [customerFilter, setCustomerFilter] = useState('All')"));
  assert.ok(source.includes("customerFilter !== 'All'"));
  assert.ok(source.includes('All customers'));
  assert.ok(source.includes('filtered.some((candidate) => candidate.projectId === selectedId)'));
});

test('batch invoice automation remains partial and server validated', () => {
  const source = readWeb('src/InvoiceBillingCenter.jsx');
  assert.ok(source.includes('function candidateBatchPlan(candidate)'));
  assert.ok(source.includes("invoiceType: 'partial'"));
  assert.ok(source.includes('Fixed-price billing needs a governed milestone/expense package or Finance-entered project amount.'));
  assert.ok(source.includes('Finance must resolve a missing or multiple commercial rate selection.'));
  assert.ok(source.includes('/api/billing/projects/'));
  assert.ok(source.includes('/invoices'));
  assert.ok(source.includes('Create partial invoices only for the projects still passing server validation.'));
  assert.ok(!source.includes('Generate ready final invoices'));
});

test('delivery completion queues billing handoff and final closeout queues completion notice', () => {
  const delivery = readRepo('src/backend/ProjectTime.Api/Modules/WorkLifecycleCompletionWorkflow.cs');
  const closeout = readRepo('src/backend/ProjectTime.Api/Modules/WorkLifecycleModule.cs');
  const bridge = readRepo('src/backend/ProjectTime.Api/Modules/WorkLifecycleBillingNotificationBridge.cs');
  assert.ok(delivery.includes('"CLOSEOUT_STARTED"'));
  assert.ok(delivery.includes('"ready_for_billing"'));
  assert.ok(delivery.includes('"#invoice-billing-center"'));
  assert.ok(closeout.includes('"CLOSEOUT_COMPLETED"'));
  assert.ok(bridge.includes('EnterpriseNotificationRepository.InsertEventAsync'));
  assert.ok(bridge.includes('EnterpriseNotificationOrchestrationService.RunAsync'));
});

test('closeout policies route to all Finance billing roles', () => {
  const migration = readRepo('database/migrations/133_finance_billing_handoff_notifications.sql');
  const resolver = readRepo('src/backend/ProjectTime.Api/Modules/EnterpriseNotificationRecipientResolver.cs');
  assert.ok(migration.includes("recipient_strategy = 'billing_project_team'"));
  for (const role of ['billing', 'finance', 'accounting']) assert.ok(migration.includes(role));
  for (const role of ['"ACCOUNTING"', '"ACCOUNTING_BILLING"', '"BILLING"', '"FINANCE"']) assert.ok(resolver.includes(role));
  assert.ok(migration.includes('Ready for billing'));
});
