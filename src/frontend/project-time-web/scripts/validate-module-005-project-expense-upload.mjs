import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');
const path = (value) => resolve(root, value);
const text = (value) => readFile(path(value), 'utf8');
const optional = async (value) => existsSync(path(value)) ? text(value) : '';
const requireAll = (source, values, label) => {
  for (const value of values) if (!source.includes(value)) throw new Error(`${label} missing contract: ${value}`);
};

const [module005, module038, portal, main, registry, parser, data, commands, mail, certify, migration, rollback, project] = await Promise.all([
  text('src/frontend/project-time-web/src/ProjectAllocationInfoPanel.jsx'),
  text('src/frontend/project-time-web/src/CertifyIntegrationCenter.jsx'),
  text('src/frontend/project-time-web/src/ProjectExpenseCrossModulePortal.jsx'),
  text('src/frontend/project-time-web/src/main.jsx'),
  text('src/frontend/project-time-web/src/module-availability-registry.js'),
  optional('src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseParsing.cs'),
  optional('src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseData.cs'),
  optional('src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseCommands.cs'),
  optional('src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseMail.cs'),
  optional('src/backend/ProjectTime.Api/Modules/Module038CertifyConnectionModule.cs'),
  optional('database/migrations/044_project_expense_upload_certify_connection.sql'),
  optional('database/rollback/044_project_expense_upload_certify_connection_rollback.sql'),
  optional('src/backend/ProjectTime.Api/ProjectTime.Api.csproj')
]);

requireAll(module005, [
  'MODULE 005', 'Project Expense Upload',
  'Select customer', 'Select project', 'Select expense owner',
  'Upload CSV / Excel', 'Import from Certify',
  '.xlsx,.xlsm,.csv', '/api/project-expenses/upload',
  '/api/project-expenses/import/certify',
  'Delete', 'Retry email', 'Module 067 Global Mail Configuration',
  'pass_through_invoice', 'included_fixed_price'
], 'Module 005 UI');

requireAll(module038, [
  'MODULE 038', 'Certify Connection & Sync Center',
  '/api/certify/connection', '/api/certify/connection/test',
  'PROJECTPULSE_CERTIFY_API_KEY', 'PROJECTPULSE_CERTIFY_API_SECRET',
  'automaticSyncEnabled', 'Automatic sync is locked',
  'Secret values remain in environment configuration',
  '#project-allocation-info'
], 'Module 038 UI');

requireAll(portal, [
  "['invoice-billing-center', 'work-register']",
  '/api/project-expenses/projects/${projectId}/summary',
  'Project expenses', 'Invoice eligible', 'Fixed-price included cost',
  '#project-allocation-info'
], 'Module 042 and 055C expense visibility');

requireAll(main, [
  "import ProjectExpenseCrossModulePortal from './ProjectExpenseCrossModulePortal.jsx';",
  '<ProjectExpenseCrossModulePortal />'
], 'Cross-module mount');

requireAll(registry, [
  "moduleNumber: '005', route: 'project-allocation-info', displayName: 'Project Expense Upload'",
  "moduleNumber: '038', route: 'certify-integration', displayName: 'Certify Connection & Sync Center'"
], 'Module registry');

const externalAvailable = [parser, data, commands, mail, certify, migration, rollback, project].every(Boolean);
if (externalAvailable) {
  requireAll(parser, [
    'Department Name', 'Department Code', 'GL Code', 'Reimb Amount',
    'Airfare', 'Car Rental', 'Hotel', 'Meals', 'Parking/Tolls', 'Mileage', 'Miscellaneous',
    'gl_dimension', 'category_summary', 'csv_gl_dimension', 'csv_category_summary',
    'ParseCertifyResponse'
  ], 'Expense format normalization');

  requireAll(data, [
    'LoadAccessibleProjectsAsync', 'LoadEligibleOwnersAsync',
    'Only Project Management, PM Leads, and Super Administrators may upload on behalf',
    'ENGINEERING', 'ENGINEERING_LEAD',
    'project_expense_summary_loaded'
  ], 'Project and role scope');

  requireAll(commands, [
    'project_expense_uploads', 'project_expense_lines',
    'UPLOAD_SUPERSEDED', 'UPLOAD_DELETED', 'PRIOR_VERSION_RESTORED',
    'source_file_bytes', 'uploaded_at',
    'QueueExpenseNotificationAsync'
  ], 'Upload version workflow');

  requireAll(mail, [
    'PROJECTPULSE_MAIL_PROVIDER', 'PROJECTPULSE_EMAIL_PROVIDER',
    'PROJECTPULSE_M365_SENDER_MAILBOX', 'PROJECTPULSE_BREVO_API_KEY',
    'PROJECTPULSE_SMTP_FROM',
    'Module 067 Global Mail Configuration',
    'Expense summary sent through Module 067',
    'cc_addresses'
  ], 'Global mail delivery');

  requireAll(certify, [
    'https://api.certify.com/v1/',
    'X-Certify-API-Key', 'X-Certify-API-Secret',
    'expensereports/${Uri.EscapeDataString(request.CertifyReportId.Trim())}/expenses',
    'automatic_sync_enabled', 'connection_status',
    'secretsReturned = false'
  ], 'Certify connection and import');

  requireAll(migration, [
    '044_project_expense_upload_certify_connection',
    'project_expense_uploads', 'project_expense_lines', 'project_expense_events',
    'project_expense_mail_outbox', 'certify_connection_profiles', 'certify_expense_import_runs',
    'VIEW_PROJECT_EXPENSE_UPLOAD', 'UPLOAD_PROJECT_EXPENSE_SELF',
    'UPLOAD_PROJECT_EXPENSE_ON_BEHALF', 'IMPORT_PROJECT_EXPENSE_CERTIFY',
    'VIEW_PROJECT_EXPENSE_INVOICE_CONTEXT', 'MANAGE_CERTIFY_CONNECTION',
    'pass_through_invoice', 'included_fixed_price',
    'Project Expense Upload', 'Certify Connection & Sync Center'
  ], 'Migration 044');

  requireAll(rollback, [
    'Rollback 044 is blocked because project expense upload records exist.',
    'Rollback 044 is blocked because Certify import audit records exist.',
    'DROP TABLE IF EXISTS project_expense_uploads',
    "migration_id = '044_project_expense_upload_certify_connection'"
  ], 'Migration 044 rollback');

  requireAll(project, [
    'app.MapModule005ProjectExpenseUploadEndpoints();',
    'app.MapModule038CertifyConnectionEndpoints();'
  ], 'API registration');
} else {
  console.log('MODULE_005_EXTERNAL_SOURCE_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

console.log('Module 005 Project Expense Upload and Module 038 Certify contracts passed.');
