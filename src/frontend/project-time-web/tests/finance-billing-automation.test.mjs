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

test('invoice center filters by customer, keeps preview in scope, and guards empty manual billing state', () => {
  const source = readWeb('src/InvoiceBillingCenter.jsx');
  assert.ok(source.includes("const [customerFilter, setCustomerFilter] = useState('All')"));
  assert.ok(source.includes("customerFilter !== 'All'"));
  assert.ok(source.includes('All customers'));
  assert.ok(source.includes('filtered.some((candidate) => candidate.projectId === selectedId)'));
  assert.ok(source.includes('const manualBillingActive = Boolean(manualBasis)'));
  assert.ok(!source.includes("manualBasis?.projectId === selected?.projectId && (manualBasis.manualInvoicesExist"));
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

test('customer directory owns the per-customer invoice workflow notification profile', () => {
  const directory = readWeb('src/CustomerDirectoryCenter.jsx');
  const profileModule = readRepo('src/backend/ProjectTime.Api/Modules/CustomerBillingNotificationProfileModule.cs');
  const invoice = readRepo('src/backend/ProjectTime.Api/Modules/InvoiceBillingModule.cs');
  const migration = readRepo('database/migrations/134_customer_billing_notification_profiles.sql');

  assert.ok(directory.includes('Invoice-generated back-office notification'));
  assert.ok(directory.includes('Email + Teams'));
  assert.ok(directory.includes('Customer contacts'));
  assert.ok(directory.includes('Not notified'));
  assert.ok(profileModule.includes('CUSTOMER_INVOICE_WORKFLOW_ACTION_REQUIRED'));
  assert.ok(profileModule.includes('enterprise:customer-invoice-workflow:'));
  assert.ok(profileModule.includes('Customer Directory managers'));
  assert.ok(invoice.includes('CustomerBillingNotificationProfileModule.QueueInvoiceCreatedAsync'));
  assert.ok(migration.includes('customer_billing_notification_profiles'));
  assert.ok(migration.includes('module_021_042_customer_billing_profile_v1'));
  assert.ok(migration.includes('"channels":["email","teams"]'));
});

test('protected Test release applies and verifies both Finance notification migrations', () => {
  const deploy = readRepo('.github/workflows/projectpulse-deploy-test.yml');
  assert.ok(deploy.includes('database/migrations/133_finance_billing_handoff_notifications.sql'));
  assert.ok(deploy.includes('database/migrations/134_customer_billing_notification_profiles.sql'));
  assert.ok(deploy.includes("'133_finance_billing_handoff_notifications'"));
  assert.ok(deploy.includes("'134_customer_billing_notification_profiles'"));
  assert.ok(deploy.includes('MIGRATIONS_112_113_114_133_134=APPLIED_AND_VERIFIED'));
  assert.ok(deploy.includes("policy_code='CLOSEOUT_STARTED'"));
  assert.ok(deploy.includes("policy_code='CLOSEOUT_COMPLETED'"));
  assert.ok(deploy.includes("policy_code='CUSTOMER_INVOICE_WORKFLOW_ACTION_REQUIRED'"));
  assert.ok(deploy.includes("to_regclass('public.customer_billing_notification_profiles') IS NOT NULL"));
});
