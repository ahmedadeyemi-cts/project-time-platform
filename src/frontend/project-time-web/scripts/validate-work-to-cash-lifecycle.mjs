import { readFile, stat } from 'node:fs/promises';
import { resolve } from 'node:path';

// PM-approved time and void-invoice release semantics must remain identical
// between lifecycle summaries and the Module 042 invoice engine.
const root = resolve(process.cwd(), '../../..');

async function text(path) {
  return readFile(resolve(root, path), 'utf8');
}

function requireText(source, values, label) {
  for (const value of values) {
    if (!source.includes(value)) {
      throw new Error(`${label} is missing required contract: ${value}`);
    }
  }
}

const paths = {
  program: 'src/backend/ProjectTime.Api/Program.cs',
  lifecycle: 'src/backend/ProjectTime.Api/Modules/WorkLifecycleModule.cs',
  invoiceModule: 'src/backend/ProjectTime.Api/Modules/InvoiceBillingModule.cs',
  renderer: 'src/backend/ProjectTime.Api/Modules/InvoiceArtifactBrandingRenderer.cs',
  brandingAssets: 'src/backend/ProjectTime.Api/Modules/InvoiceBrandingAssets.cs',
  csproj: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj',
  migration: 'database/migrations/038_work_to_cash_lifecycle_and_audit.sql',
  rollback: 'database/rollback/038_work_to_cash_lifecycle_and_audit_rollback.sql',
  workRegister: 'src/frontend/project-time-web/src/WorkRegisterCenter.jsx',
  readiness: 'src/frontend/project-time-web/src/BillingReadinessCenter.jsx',
  closeout: 'src/frontend/project-time-web/src/ProjectCloseoutCenter.jsx',
  invoice: 'src/frontend/project-time-web/src/InvoiceBillingCenter.jsx',
  welcome: 'src/frontend/project-time-web/src/RoleWelcomeDashboard.jsx',
  app: 'src/frontend/project-time-web/src/App.jsx',
  docker: 'deployment/containers/web/Dockerfile'
};

const [
  program,
  lifecycle,
  invoiceModule,
  renderer,
  brandingAssets,
  csproj,
  migration,
  rollback,
  workRegister,
  readiness,
  closeout,
  invoice,
  welcome,
  app,
  docker
] = await Promise.all(Object.values(paths).map(text));

requireText(program, ['app.MapWorkLifecycleEndpoints();'], 'Program endpoint mapping');

requireText(lifecycle, [
  '/api/work-lifecycle/dashboard',
  '/api/work-lifecycle/projects/{projectId:guid}',
  '/billing-readiness',
  '/closeout/request',
  '/closeout/complete',
  '/closeout/reopen',
  'WorkRegisterAuthorization.GetAccessAsync',
  '"pm_approved"',
  'access.IsViewAs',
  'project.IsArchived',
  'BuildCloseoutBlockersAsync',
  'requiresInvoiceReadiness',
  'if (requiresInvoiceReadiness)',
  'if (requiresInvoiceReadiness && readiness?.ReviewStatus != "ready")',
  'billing_invoice_lines',
  'JOIN billing_invoices invoice',
  "lower(COALESCE(invoice.invoice_status, '')) <> 'void'",
  'project_tasks',
  'COALESCE(line.rate_amount, 0) > 0',
  "entry.status IN ('manager_declined', 'pm_declined')",
  'details.TryGetProperty("changedFields"',
  'changedFieldsElement.GetString()',
  "purchase_order.po_status = 'active'",
  'purchase_order.effective_start_date <= CURRENT_DATE',
  'purchase_order.effective_end_date >= CURRENT_DATE',
  'final_invoice_complete',
  'write_off_approved',
  'InsertAuditAsync',
  "SET status = 'completed'",
  'EXTRACT(ISODOW FROM CURRENT_DATE)',
  'FROM generate_series(0, 4) AS weekday_offset',
  'FROM timesheet_day_statuses day_status',
  "lower(COALESCE(submitter.manager_email, ''))",
  '@can_view_all_approvals',
  '@is_manager',
  '@is_project_manager',
  'TimeEntryExcludedRoles',
  '"PROJECT_TEAM_COORDINATOR"',
  '"SALES"',
  '"INSIDE_SALES"',
  '"EXECUTIVE"'
], 'Work lifecycle API');

if (lifecycle.includes("SET status = 'closed'")) {
  throw new Error("Project closeout must use the schema-supported 'completed' project status.");
}

if (lifecycle.includes("purchase_order.po_status IN ('draft', 'active')")) {
  throw new Error('Billing readiness must reject draft purchase orders.');
}

if ((lifecycle.match(/FROM billing_invoice_lines/g) ?? []).length !== 3
    || (lifecycle.match(/JOIN billing_invoices invoice/g) ?? []).length !== 3
    || !lifecycle.includes('AS has_live_invoice')) {
  throw new Error('Every lifecycle invoiced-time check must exclude void invoice state.');
}

requireText(invoiceModule, [
  'InvoiceEligibleStatuses',
  '"pm_approved"',
  'JOIN billing_invoices invoice',
  "lower(COALESCE(invoice.invoice_status, '')) <> 'void'"
], 'Module 042 invoice eligibility');

if ((invoiceModule.match(/FROM billing_invoice_lines invoiced/g) ?? []).length !== 3
    || (invoiceModule.match(/JOIN billing_invoices invoice/g) ?? []).length !== 3) {
  throw new Error('Every Module 042 eligibility check must release voided invoice lines.');
}

requireText(migration, [
  'BEGIN;',
  'COMMIT;',
  'CREATE TABLE IF NOT EXISTS work_billing_readiness_reviews',
  'CREATE TABLE IF NOT EXISTS work_closeout_records',
  'CREATE TABLE IF NOT EXISTS work_lifecycle_audit_events',
  'projectpulse038_reject_audit_mutation',
  'trg_projectpulse038_work_register_audit',
  'trg_projectpulse038_invoice_audit',
  'DROP INDEX IF EXISTS uq_billing_invoice_lines_time_entry',
  'uq_billing_invoice_lines_invoice_time_entry',
  'projectpulse038_guard_live_time_entry_line',
  'pg_advisory_xact_lock',
  'projectpulse038_guard_invoice_reactivation',
  'FROM work_register_change_history',
  'FROM billing_invoice_events',
  "'038_work_to_cash_lifecycle_and_audit'"
], 'Migration 038');

if ((migration.match(/\bBEGIN;/g) ?? []).length !== 1
    || (migration.match(/\bCOMMIT;/g) ?? []).length !== 1) {
  throw new Error('Migration 038 must remain one atomic transaction.');
}

requireText(rollback, [
  'trg_projectpulse038_invoice_reactivation',
  'trg_projectpulse038_live_time_entry_line',
  'DROP INDEX IF EXISTS uq_billing_invoice_lines_invoice_time_entry',
  'HAVING COUNT(*) > 1',
  'CREATE UNIQUE INDEX IF NOT EXISTS uq_billing_invoice_lines_time_entry',
  'DROP TRIGGER IF EXISTS trg_projectpulse038_invoice_audit',
  'DROP TABLE IF EXISTS work_lifecycle_audit_events',
  'DROP TABLE IF EXISTS work_closeout_records',
  'DROP TABLE IF EXISTS work_billing_readiness_reviews',
  "'038_work_to_cash_lifecycle_and_audit'"
], 'Migration 038 rollback');

if (lifecycle.includes("entry.status IN ('rejected', 'returned')")
    || lifecycle.includes('changedFields = reader.GetString(1)')) {
  throw new Error('Welcome attention and unified audit must use persisted status/detail fields.');
}

requireText(readiness, [
  '/api/work-lifecycle/projects/',
  '/billing-readiness',
  'saveBillingReadiness',
  "saveBillingReadiness('ready')",
  'Audit reason',
  'Saved:',
  'Persisted and audited'
], 'Module 039 billing readiness');

requireText(closeout, [
  '/api/work-lifecycle/projects/',
  '/closeout/reopen',
  'saveGovernedCloseout',
  "saveGovernedCloseout('request')",
  "saveGovernedCloseout('complete')",
  'Server-validated blockers',
  'Final billing disposition',
  'Audit reason',
  'Governed closeout'
], 'Module 040 closeout');

requireText(workRegister, [
  '/api/work-lifecycle/projects/',
  'Promise.allSettled',
  "lifecycleResult.status === 'fulfilled'",
  'lifecycle?.audit ?? data.changeHistory ?? []',
  'Work-to-Cash Audit History',
  'billing readiness, partial/final invoices'
], 'Module 055C unified audit');

requireText(invoice, [
  'invoiceNotes',
  'notes: invoiceNotes.trim()',
  'Invoice notes',
  "createInvoice('partial')",
  "createInvoice('final')"
], 'Module 042 invoice creation');

requireText(renderer, [
  'InvoiceBrandingAssets.LoadJpeg()',
  'InvoiceBrandingAssets.LoadPng()',
  '/Logo',
  'xl/media/image1.png'
], 'US Signal invoice branding');

requireText(brandingAssets, [
  'GetManifestResourceStream',
  'ProjectTime.Api.Assets.Branding.USSNavyStacked.jpg',
  'ProjectTime.Api.Assets.Branding.USSNavyStacked.png'
], 'US Signal embedded branding loader');

requireText(csproj, [
  'ClosedXML',
  'EmbeddedResource Include="Assets/Branding/USSNavyStacked.png"',
  'EmbeddedResource Include="Assets/Branding/USSNavyStacked.jpg"'
], 'API embedded branding resources');

for (const [path, minimumBytes] of [
  ['src/backend/ProjectTime.Api/Assets/Branding/USSNavyStacked.jpg', 30_000],
  ['src/backend/ProjectTime.Api/Assets/Branding/USSNavyStacked.png', 15_000]
]) {
  const file = await stat(resolve(root, path));
  if (file.size < minimumBytes) {
    throw new Error(`${path} is missing or too small to be the supplied US Signal artwork.`);
  }
}

requireText(welcome, [
  '/api/work-lifecycle/dashboard',
  'TIME_ENTRY_EXCLUDED_ROLES',
  "'MANAGER'",
  "'SALES'",
  "'INSIDE_SALES'",
  "'EXECUTIVE'",
  "'PROJECT_TEAM_COORDINATOR'",
  "persona-${persona}",
  'Requires attention',
  'Project health',
  'Team / billing snapshot',
  'Recent items'
], 'Role-aware welcome dashboard');

if (welcome.includes('Start Timer')) {
  throw new Error('The welcome dashboard must not expose a non-functional timer action.');
}

requireText(app, [
  "import RoleWelcomeDashboard from './RoleWelcomeDashboard.jsx';",
  '<RoleWelcomeDashboard',
  'roleCodes={currentRoleCodes}',
  'roleModules={visibleRoleModules}'
], 'Welcome dashboard integration');

requireText(docker, [
  'WorkLifecycleModule.cs',
  'InvoiceBillingModule.cs',
  'InvoiceArtifactBrandingRenderer.cs',
  'InvoiceBrandingAssets.cs',
  'ProjectTime.Api.csproj',
  'Assets/Branding/USSNavyStacked.png',
  'Assets/Branding/USSNavyStacked.jpg',
  '038_work_to_cash_lifecycle_and_audit.sql',
  '038_work_to_cash_lifecycle_and_audit_rollback.sql'
], 'Web container validation context');

console.log('Work-to-Cash lifecycle, audit, role-aware welcome page, and invoice branding contracts passed.');
