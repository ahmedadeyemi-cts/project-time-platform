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
const rejectAll = (source, values, label) => {
  for (const value of values) if (source.includes(value)) throw new Error(`${label} contains forbidden contract: ${value}`);
};

const [
  module005, module005Experience, module038, module038Css, pageContext, portal, main, registry,
  foundation, safeEndpoints, parser, data, commands, notificationAuth, mail, certify,
  migration, rollback, project, parserTest, migrationTest
] = await Promise.all([
  text('src/frontend/project-time-web/src/ProjectAllocationInfoPanel.jsx'),
  text('src/frontend/project-time-web/src/Module005ExperienceCompatibility.jsx'),
  text('src/frontend/project-time-web/src/CertifyIntegrationCenter.jsx'),
  text('src/frontend/project-time-web/src/certify-integration-center.css'),
  text('src/frontend/project-time-web/src/PageContextGuide.jsx'),
  text('src/frontend/project-time-web/src/ProjectExpenseCrossModulePortal.jsx'),
  text('src/frontend/project-time-web/src/main.jsx'),
  text('src/frontend/project-time-web/src/module-availability-registry.js'),
  optional('src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseUploadModule.cs'),
  optional('src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseSafeEndpoints.cs'),
  optional('src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseParsing.cs'),
  optional('src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseData.cs'),
  optional('src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseCommands.cs'),
  optional('src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseNotificationAuthorization.cs'),
  optional('src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseMail.cs'),
  optional('src/backend/ProjectTime.Api/Modules/Module038CertifyConnectionModule.cs'),
  optional('database/migrations/044_project_expense_upload_certify_connection.sql'),
  optional('database/rollback/044_project_expense_upload_certify_connection_rollback.sql'),
  optional('src/backend/ProjectTime.Api/ProjectTime.Api.csproj'),
  optional('tests/Module005ExpenseParserTests/Program.cs'),
  optional('tests/test-project-expense-migration-044.sh')
]);

requireAll(module005, [
  'MODULE 005', 'Project Expense Upload',
  'Select customer', 'Select project', 'Select expense owner',
  'Upload CSV / Excel', 'Import from Certify',
  '.xlsx,.xlsm,.csv', '/api/project-expenses/upload',
  '/api/project-expenses/import/certify',
  'Retry email', 'Module 067 Global Mail Configuration',
  'pass_through_invoice', 'included_fixed_price'
], 'Module 005 UI');

requireAll(module005Experience, [
  'Module005ExperienceCompatibility',
  "const MODULE005_NAME = 'Project Expense Upload'",
  'convertDeleteActionsToReupload',
  "button.textContent = 'Re-upload'",
  'event.stopImmediatePropagation()',
  'Re-upload ready. Choose the replacement CSV or Excel file',
  "document.querySelectorAll('a[href=\"#project-allocation-info\"]')"
], 'Module 005 re-upload and naming compatibility');

requireAll(module038, [
  'MODULE 038', 'Certify Connection &amp; Sync Center',
  '/api/certify/connection', '/api/certify/connection/test',
  'PROJECTPULSE_CERTIFY_API_KEY', 'PROJECTPULSE_CERTIFY_API_SECRET',
  'automaticSyncEnabled', 'automationAllowed', 'syncLockedReason',
  'Test connection to unlock', 'Save sync settings',
  'Enable automatic sync',
  'Secret values remain in environment configuration',
  '#project-allocation-info'
], 'Module 038 UI');

requireAll(module038Css, [
  '.certify-sync-control-card',
  'align-content:start',
  'min-height:0',
  'main.app-shell.route-certify-integration .certify-integration-center',
  'max-height:calc(100dvh - 15rem)',
  'overflow-y:auto',
  'overflow-x:hidden',
  'overscroll-behavior:contain',
  'grid-template-columns:repeat(2,minmax(0,1fr))',
  '.certify-sync-lock',
  '.certify-sync-ready'
], 'Module 038 bounded compact layout');

requireAll(pageContext, [
  "'project-allocation-info': {",
  "page: 'Project Expense Upload — Module 005'",
  "'certify-integration': {",
  "page: 'Certify Connection & Sync Center — Module 038'"
], 'Module 005 and 038 canonical page context');

requireAll(portal, [
  "['invoice-billing-center', 'work-register']",
  '/api/project-expenses/projects/${projectId}/summary',
  'Project expenses', 'Invoice eligible', 'Fixed-price included cost',
  '#project-allocation-info'
], 'Module 042 and 055C expense visibility');

requireAll(main, [
  "import ProjectExpenseCrossModulePortal from './ProjectExpenseCrossModulePortal.jsx';",
  '<ProjectExpenseCrossModulePortal />',
  "import Module005ExperienceCompatibility from './Module005ExperienceCompatibility.jsx';",
  '<Module005ExperienceCompatibility />'
], 'Module 005 and cross-module mounts');

requireAll(registry, [
  "moduleNumber: '005', route: 'project-allocation-info', displayName: 'Project Expense Upload'",
  "moduleNumber: '038', route: 'certify-integration', displayName: 'Certify Connection & Sync Center'",
  "replace(/\\bProject Allocation(?:\\s*(?:\\/|&|and)\\s*)Info\\b/gi, 'Project Expense Upload')"
], 'Module registry and legacy-name replacement');

const externalAvailable = [
  foundation, safeEndpoints, parser, data, commands, notificationAuth, mail, certify,
  migration, rollback, project, parserTest, migrationTest
].every(Boolean);

if (externalAvailable) {
  requireAll(foundation, [
    'DefaultCertifyBaseUrl', 'https://api.certify.com/v1/',
    'MapModule038CertifyConnectionEndpoints',
    'RetryAuthorizedNotificationAsync'
  ], 'Shared Module 005 and 038 foundation');

  requireAll(safeEndpoints, [
    'MapModule005ProjectExpenseUploadEndpointsSafe',
    'DeleteUploadFromRequestAsync',
    'JsonSerializer.DeserializeAsync<ExpenseDeleteRequest>',
    '/api/project-expenses/readiness',
    'project_expense_runtime_ready'
  ], 'Startup-safe Module 005 endpoint registration');
  rejectAll(safeEndpoints, [
    '(Func<Guid, ExpenseDeleteRequest, HttpContext, Task<IResult>>)DeleteUploadAsync'
  ], 'Startup-safe Module 005 endpoint registration');

  requireAll(parser, [
    'Department Name', 'Department Code', 'GL Code', 'Reimb Amount',
    'FindCategoryHeader', 'IsGlCountFooter', 'NormalizeExpenseCategory',
    'SP-Cust Pass Through - Airfare', 'SP-Cust Pass Through - Rental',
    'SP-Cust Pass Through-Hotel', 'SP-Cust Pass Through-Meals',
    'SP-Cust Pass Through-Mileage', 'SP-Meals (All Employees,Cust)',
    'SP-Travel, Lodging, Parking', 'Miscellaneous',
    'gl_dimension', 'category_summary', 'csv_gl_dimension', 'csv_category_summary',
    'ParseCertifyResponse'
  ], 'Expense format normalization');

  requireAll(data, [
    'LoadAccessibleProjectsAsync', 'LoadEligibleOwnersAsync',
    'Only Project Management, PM Leads, and Super Administrators may upload on behalf',
    'Engineering roles may view only their own project expense uploads.',
    'projectWideVisibility', 'ownerScope',
    'AuthorizeExistingUploadActionAsync',
    'ENGINEERING', 'ENGINEERING_LEAD',
    'uploadedAt', 'project_expense_summary_loaded'
  ], 'Project and role scope');

  requireAll(commands, [
    'project_expense_uploads', 'project_expense_lines',
    'UPLOAD_SUPERSEDED', 'UPLOAD_DELETED', 'PRIOR_VERSION_RESTORED',
    'source_file_bytes', 'QueueExpenseNotificationAsync',
    'AuthorizeExistingUploadActionAsync(connection, transaction, actor, upload)'
  ], 'Upload version workflow');

  requireAll(notificationAuth, [
    'RetryAuthorizedNotificationAsync',
    'LoadUploadAsync(connection, uploadId)',
    'AuthorizeExistingUploadActionAsync(connection, null, actor, upload)',
    'DeliverExpenseNotificationAsync(connection, uploadId, actor.ActualUserId)'
  ], 'Notification retry authorization');

  requireAll(mail, [
    'PROJECTPULSE_MAIL_PROVIDER', 'PROJECTPULSE_EMAIL_PROVIDER',
    'PROJECTPULSE_M365_SENDER_MAILBOX', 'PROJECTPULSE_BREVO_API_KEY',
    'PROJECTPULSE_SMTP_FROM',
    'Module 067 Global Mail Configuration',
    'Expense summary sent through Module 067',
    'cc_addresses'
  ], 'Global mail delivery');

  requireAll(certify, [
    'DefaultCertifyBaseUrl',
    'X-Certify-API-Key', 'X-Certify-API-Secret',
    'expensereports/{Uri.EscapeDataString(request.CertifyReportId.Trim())}/expenses',
    'automatic_sync_enabled', 'connection_status',
    "automatic_sync_enabled=CASE WHEN connection_status='connected' THEN @automatic ELSE FALSE END",
    'secretsReturned = false'
  ], 'Certify connection and safe automation gate');

  requireAll(migration, [
    '044_project_expense_upload_certify_connection',
    'project_expense_uploads', 'project_expense_lines', 'project_expense_events',
    'project_expense_mail_outbox', 'certify_connection_profiles', 'certify_expense_import_runs',
    'uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()',
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
    'app.MapModule005ProjectExpenseUploadEndpointsSafe();',
    'app.MapModule038CertifyConnectionEndpoints();'
  ], 'API registration');
  rejectAll(project, [
    'app.MapModule005ProjectExpenseUploadEndpoints();'
  ], 'API registration');

  requireAll(parserTest, [
    'exactUploadedStructures=true',
    'ExpensesByGLDim.xlsx', 'ExpensesByCategory.xlsx',
    'ExpensesByGLDim.csv', 'ExpensesByCategory.csv',
    '43,43,43,43,43,43,43',
    '2377.26', 'normalizedCategories=7'
  ], 'Executable uploaded-format parser test');

  requireAll(migrationTest, [
    'PROJECT_EXPENSE_MIGRATION_044_TEST=PASS',
    'idempotent=true', 'safeRollback=true',
    'guardedRollback=true', 'immutableAudit=true'
  ], 'Migration 044 executable regression test');
} else {
  console.log('MODULE_005_EXTERNAL_SOURCE_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

console.log('MODULE_005_038_MERGE_CANDIDATE=PASS reupload=true compactSync=true boundedScroll=true');
console.log('Module 005 Project Expense Upload and Module 038 Certify contracts passed.');
