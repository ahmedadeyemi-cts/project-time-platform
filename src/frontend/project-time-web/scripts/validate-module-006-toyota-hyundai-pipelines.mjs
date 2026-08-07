import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const exists = (relative) => fs.existsSync(path.join(root, relative));
const component = read('src/frontend/project-time-web/src/ProjectRegisterCenter.jsx');
const css = read('src/frontend/project-time-web/src/module006-standalone.css');
const registerCss = read('src/frontend/project-time-web/src/project-register-center.css');
const api = read('src/backend/ProjectTime.Api/Modules/Module006StandalonePipelineModule.cs');
const tasks = read('src/backend/ProjectTime.Api/Modules/Module006StandaloneTaskModule.cs');
const migration = read('database/migrations/068_module006_standalone_pipeline_management.sql');
const rollback = read('database/rollback/068_module006_standalone_pipeline_management_rollback.sql');
const customerMigration = read('database/migrations/069_module006_customer_pipeline_expansion.sql');
const customerRollback = read('database/rollback/069_module006_customer_pipeline_expansion_rollback.sql');
const registry = read('src/frontend/project-time-web/src/module-availability-registry.js');
const injector = read('src/frontend/project-time-web/scripts/inject-module-006-toyota-hyundai-pipeline.mjs');
const snapshotFiles = [
  'src/frontend/project-time-web/src/toyota-hyundai-pipeline-snapshot.js',
  'src/frontend/project-time-web/src/toyota-hyundai-pipeline-metadata.js',
  'src/frontend/project-time-web/src/toyota-hyundai-pipeline-projects.js',
  ...Array.from({ length: 8 }, (_, index) => `src/frontend/project-time-web/src/toyota-hyundai-pipeline-events-${String(index + 1).padStart(2, '0')}.js`)
];
const snapshot = snapshotFiles.filter(exists).map(read).join('\n');

function requireAll(source, values, label) {
  for (const value of values) {
    if (!source.includes(value)) throw new Error(`${label} missing: ${value}`);
  }
}

function rejectAll(source, values, label) {
  for (const value of values) {
    if (source.includes(value)) throw new Error(`${label} contains forbidden marker: ${value}`);
  }
}

requireAll(component, [
  'data-project-register-contract="module006-standalone-pipeline-v1"',
  'Standalone authority',
  'Add New Project',
  'Open / edit',
  'Updates & Notes',
  'Create New Task',
  'Save Task',
  '/api/module-006/pipeline',
  '/api/module-006/tasks',
  'appendUpdate',
  'createProject',
  'createTask',
  'archiveTask',
  'Export Excel',
  'Print / Save PDF',
  'PAGE_SIZES',
  'visibleRecords',
  'Authorized live records and append-only history'
], 'Module 006 standalone workspace');

rejectAll(component, [
  'TOYOTA_HYUNDAI_PIPELINE_PROJECTS',
  'TOYOTA_HYUNDAI_PIPELINE_EVENTS',
  'snapshot_overlay'
], 'Module 006 authorized runtime-only data boundary');

requireAll(component, [
  '<th className="project-register-status-column">Status</th>',
  '<td className="project-register-status-column"><span className={`project-register-state ${record.isArchived ? \'historical\' : \'active\'}`}>{record.isArchived ? \'Historical\' : labelize(record.status || \'Active\')}</span></td>'
], 'Module 006 single status presentation');
rejectAll(component, [
  "</span><small>{labelize(record.status)}</small>"
], 'Module 006 duplicate status presentation');
requireAll(registerCss, [
  `.project-register-table .project-register-status-column {
  width: 7rem;
  min-width: 7rem;
  white-space: nowrap;
}`,
  `.project-register-status-column .project-register-state {
  margin-bottom: 0;
}`
], 'Module 006 status column presentation');

rejectAll(component, [
  '#work-register',
  'Module 055C',
  'Manage tasks in 055C',
  'Open Module 055C'
], 'Module 006 independence boundary');

requireAll(api, [
  'MapModule006StandalonePipelineEndpoints',
  'app.MapGet("/api/module-006/pipeline"',
  'app.MapPost("/api/module-006/pipeline"',
  'app.MapPut("/api/module-006/pipeline/{recordId:guid}"',
  'module006_pipeline_records',
  'module006_pipeline_updates',
  'linkedToModule055C = false',
  'customerEntryMode = "extensible"',
  'CustomerNameMaxLength = 120',
  '069_module006_customer_pipeline_expansion',
  'Customer names must contain between',
  'ExpectedRevision',
  'ViewAsReadOnly'
], 'Module 006 project API');

rejectAll(api, [
  'Module 006 accepts only Toyota or Hyundai pipeline records.'
], 'Module 006 extensible customer API');

requireAll(tasks, [
  'MapModule006StandaloneTaskEndpoints',
  'app.MapGet("/api/module-006/tasks"',
  'app.MapPost("/api/module-006/pipeline/{recordId:guid}/tasks"',
  'module006_pipeline_tasks',
  'module006_pipeline_task_events',
  'linkedToModule055C = false',
  'ExpectedRevision',
  'ViewAsReadOnly'
], 'Module 006 task API');

requireAll(migration, [
  '068_module006_standalone_pipeline_management',
  'module006_pipeline_records',
  'module006_pipeline_updates',
  'module006_pipeline_tasks',
  'module006_pipeline_task_events',
  'append-only',
  'projectpulse068_block_pipeline_history_mutation'
], 'Migration 068');
requireAll(rollback, [
  'module006_pipeline_task_events',
  'module006_pipeline_tasks',
  'module006_pipeline_updates',
  'module006_pipeline_records'
], 'Migration 068 rollback');

requireAll(customerMigration, [
  '069_module006_customer_pipeline_expansion',
  'DROP CONSTRAINT IF EXISTS module006_pipeline_records_customer_check',
  'ck_module006_pipeline_records_customer_name',
  'char_length(customer) BETWEEN 2 AND 120',
  "customer !~ '[[:cntrl:]]'",
  'ix_module006_pipeline_records_customer_name'
], 'Migration 069 customer expansion');
requireAll(customerRollback, [
  'additional-customer Module 006 records exist',
  "lower(btrim(customer)) NOT IN ('toyota', 'hyundai')",
  'module006_pipeline_records_customer_check',
  "migration_id = '069_module006_customer_pipeline_expansion'"
], 'Migration 069 safe rollback');

requireAll(css, [
  '.module006-independence-banner',
  '.module006-task-create',
  '.module006-task-card',
  '.module006-modal',
  '.module006-history',
  '@media print'
], 'Module 006 standalone presentation');

requireAll(snapshot, [
  'module006-reviewed-workbook-snapshot-v1-20260730',
  '"activeProjectCount": 26',
  '"archivedProjectCount": 12',
  '"eventCount": 387',
  '"customer": "Toyota"',
  '"customer": "Hyundai"'
], 'Reviewed snapshot continuity');

requireAll(registry, [
  "moduleNumber: '006', route: 'toyota-hyundai-pipelines', displayName: 'Toyota & Hyundai Pipelines'",
  "'psa-modules': 'toyota-hyundai-pipelines'",
  "'project-register': 'toyota-hyundai-pipelines'"
], 'Module registry');
requireAll(injector, [
  "route: 'toyota-hyundai-pipelines'",
  'PR467_MODULE_006_EXCLUSIVE_ROUTE_START',
  '<ProjectRegisterCenter legacyRoute={false} />',
  'PROJECTPULSE_RUNTIME_ROUTE_ALIASES',
  "'psa-modules': 'toyota-hyundai-pipelines'",
  "'project-register': 'toyota-hyundai-pipelines'",
  'MODULE_006_CUSTOMER_EXPANSION_START',
  'module006-customer-options',
  'All customers',
  'Project Management access required',
  'US-Signal-Customer-Pipelines-',
  'installStackedLogo',
  'stackedLogoTargetPaths',
  'USSNavyStacked.png',
  'customers=extensible stacked_logo=approved'
], 'Module 006 route, customer, and branding injection');

const plan = read('docs/modules/module-006-toyota-hyundai-pipelines/IMPLEMENTATION-PLAN.md');
requireAll(plan, [
  'Module 055C dependency: **none**',
  'standalone tasks',
  'Add New Project',
  'append-only status updates and notes',
  'Migration `068_module006_standalone_pipeline_management`',
  'Migration `069_module006_customer_pipeline_expansion`',
  'additional customer'
], 'Module 006 implementation plan');

console.log('MODULE_006_STANDALONE_PIPELINE=PASS');
console.log('MODULE_006_PROJECT_CREATE_EDIT_NOTES=PASS');
console.log('MODULE_006_STANDALONE_TASKS=PASS');
console.log('MODULE_006_NO_055C_DEPENDENCY=PASS');
console.log('MODULE_006_CUSTOMER_EXPANSION=PASS baseline=Toyota,Hyundai additional_customers=enabled migration=069');
console.log('MODULE_006_STATUS_PRESENTATION=PASS header=nowrap indicator=single');
console.log('MODULE_006_STACKED_US_SIGNAL_BRANDING=PASS');
console.log('MODULE_006_REVIEWED_SNAPSHOT_CONTINUITY=projects:38 events:387');
