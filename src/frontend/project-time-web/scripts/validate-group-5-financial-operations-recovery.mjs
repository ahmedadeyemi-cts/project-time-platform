import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '../../..');
const sourceRoot = path.join(webRoot, 'src');
const read = (filePath) => fs.readFileSync(filePath, 'utf8');
const count = (source, marker) => source.split(marker).length - 1;
let checks = 0;

function assert(condition, message) {
  checks += 1;
  if (!condition) throw new Error(message);
}

function contains(source, marker, label) {
  assert(source.includes(marker), `${label} is missing: ${marker}`);
}

const paths = {
  component: path.join(sourceRoot, 'FinancialOperationsRecoveryWorkspace.jsx'),
  css: path.join(sourceRoot, 'financial-operations-recovery-workspace.css'),
  injector: path.join(scriptDirectory, 'inject-group-5-financial-operations-recovery.mjs'),
  package: path.join(webRoot, 'package.json'),
  app: path.join(sourceRoot, 'App.jsx'),
  registry: path.join(sourceRoot, 'module-availability-registry.js'),
  billingReadiness: path.join(sourceRoot, 'BillingReadinessCenter.jsx'),
  sourceLoader: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/FinancialOperationsSourceLoader.cs'),
  reportEngine: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/FinancialOperationsReportEngine.cs'),
  module: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/FinancialOperationsRecoveryModule.cs'),
  migration: path.join(repositoryRoot, 'database/migrations/051_financial_operations_reporting_recovery.sql'),
  rollback: path.join(repositoryRoot, 'database/rollback/051_financial_operations_reporting_recovery_rollback.sql')
};

for (const [label, filePath] of Object.entries(paths)) {
  assert(fs.existsSync(filePath), `Required Group 5 ${label} file is missing: ${filePath}`);
}

const component = read(paths.component);
const css = read(paths.css);
const injector = read(paths.injector);
const packageJson = JSON.parse(read(paths.package));

for (const marker of [
  "import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';",
  'data-projectpulse-group5="financial-recovery"',
  'Actual report catalog',
  'Search, preview, run, and export',
  'Report run history',
  'Source health and recovery',
  'Financial Operations Workbench',
  'Current expense drill-down',
  'Group 4 routing and Module 065 delivery',
  'Module 005 remains a separate upload workspace',
  'GROUP_5_AUTHENTICATED_REPORT_EXPORT_START',
  'GROUP_5_AUTHENTICATED_REPORT_EXPORT_END'
]) contains(component, marker, 'Group 5 enterprise workspace');

for (const endpoint of [
  '/api/financial-operations/reports/catalog',
  '/api/financial-operations/reports/history',
  '/api/financial-operations/reports/runs/${runId}/export',
  '/api/financial-operations/sources/',
  '/api/financial-operations/workbench',
  '/api/financial-operations/modules/'
]) contains(component, endpoint, 'Group 5 frontend API');
assert(!component.includes('CertifyIntegrationCenter'), 'Group 5 must not mount or replace Module 038.');

for (const marker of [
  '.group5-financial-operations',
  '.group5-hero',
  '.group5-report-picker',
  '.group5-table-wrap',
  '.group5-source-grid',
  '.group5-work-item-list',
  '.group5-module-project-grid',
  '@media print'
]) contains(css, marker, 'Group 5 styling');

for (const marker of [
  'GROUP_5_FINANCIAL_OPERATIONS_ROUTES_START',
  'GROUP_5_MODULE_039_RECOVERY_PANEL',
  'GROUP_5_MODULE_040_RECOVERY_PANEL',
  'GROUP_5_MODULE_041_RECOVERY_PANEL',
  'GROUP_5_MODULE_042_RECOVERY_PANEL',
  'module040=guided_closeout'
]) contains(injector, marker, 'Group 5 compatibility injector');

const prebuild = packageJson.scripts?.prebuild ?? '';
const build = packageJson.scripts?.build ?? '';
contains(prebuild, 'inject-group-5-financial-operations-recovery.mjs', 'Group 5 prebuild installation');
contains(build, 'validate:group5-financial-operations', 'Group 5 complete-build validation');
assert(
  packageJson.scripts?.['validate:group5-financial-operations'] === 'node ./scripts/validate-group-5-financial-operations-recovery.mjs',
  'Group 5 package validator must remain authoritative.'
);

for (const marker of [
  'approved_time_entries',
  'billing_readiness_reviews',
  'project_closeout_records',
  'project_notification_dispatches',
  'Other healthy financial content remains visible'
]) contains(read(paths.sourceLoader), marker, 'Group 5 source loader');
for (const report of [
  'project_financial_health',
  'project_hours_consumption',
  'project_expense_status',
  'billing_readiness',
  'project_closeout_readiness',
  'notification_delivery'
]) contains(read(paths.reportEngine), report, 'Group 5 report catalog');
for (const endpoint of [
  '/api/financial-operations/reports/catalog',
  '/api/financial-operations/reports/run',
  '/api/financial-operations/reports/history',
  '/api/financial-operations/sources',
  '/api/financial-operations/workbench',
  '/api/financial-operations/modules/{moduleCode}'
]) contains(read(paths.module), endpoint, 'Group 5 API');
for (const table of ['financial_report_runs', 'financial_operations_work_items', 'financial_operations_actions']) {
  contains(read(paths.migration), table, 'Migration 051');
  contains(read(paths.rollback), `DROP TABLE IF EXISTS ${table}`, 'Migration 051 rollback');
}

const appBefore = read(paths.app);
execFileSync(process.execPath, [paths.injector], { cwd: webRoot, stdio: 'inherit' });
const appAfter = read(paths.app);
const registry = read(paths.registry);
assert(appBefore === appAfter, 'Group 5 injector must leave integrated tracked App source unchanged.');
assert(count(appAfter, "import FinancialOperationsRecoveryWorkspace from './FinancialOperationsRecoveryWorkspace.jsx';") === 1, 'Group 5 App import must be unique.');
assert(count(appAfter, 'GROUP_5_FINANCIAL_OPERATIONS_ROUTES_START') === 1, 'Group 5 route block must be unique.');
assert(count(appAfter, '<FinancialOperationsRecoveryWorkspace mode="workbench" authSession={authSession} />') === 1, 'Module 031 mount must be unique.');
assert(
  count(appAfter, '<FinancialOperationsRecoveryWorkspace moduleCode="039" authSession={authSession} compact />') === 0,
  'Module 039 must not duplicate its canonical billing readiness surface.'
);
assert(
  count(appAfter, '<FinancialOperationsRecoveryWorkspace moduleCode="041" authSession={authSession} />') === 0,
  'Module 041 must not duplicate its canonical closeout notification surface.'
);
for (const moduleCode of ['042']) {
  assert(
    count(appAfter, `<FinancialOperationsRecoveryWorkspace moduleCode="${moduleCode}" authSession={authSession} />`) === 1,
    `Module ${moduleCode} recovery mount must be unique.`
  );
}
assert(count(appAfter, '<FinancialOperationsRecoveryWorkspace moduleCode="040" authSession={authSession} />') === 0, 'Module 040 must not mount the retired recovery surface.');
assert(count(appAfter, '<ProjectCloseoutCenter />') === 1, 'Module 040 guided closeout mount must be unique.');
assert(count(registry, "moduleNumber: '031'") === 1, 'Module 031 registry entry must be unique.');
assert(count(registry, "moduleNumber: '030'") === 1, 'Module 030 registry entry must remain unique.');
assert(!appAfter.includes('GROUP_5_MODULE_038'), 'Group 5 must not include a Module 038 mount.');

console.log(`GROUP_5_VALIDATION_CHECKS=${checks}`);
console.log('GROUP_5_FINANCIAL_OPERATIONS_RECOVERY=PASS module040=guided_closeout source_isolation=preserved');


const billingReadinessSource = read(paths.billingReadiness);
const combinedPr467Source = `${component}
${appAfter}
${billingReadinessSource}`;
for (const marker of [
  'PR467_COMPACT_SOURCE_HEALTH',
  'PR467_BILLING_CLOSEOUT_HANDOFFS',
  'Complete project information — Module 055C',
  'Open Project Closeout — Module 040',
  'Continue to Invoice & Billing — Module 042'
]) {
  if (!combinedPr467Source.includes(marker)) throw new Error(`PR467 Module 039 marker missing: ${marker}`);
}
console.log('PR467_MODULE039_CANONICAL_BILLING_READINESS=PASS');
