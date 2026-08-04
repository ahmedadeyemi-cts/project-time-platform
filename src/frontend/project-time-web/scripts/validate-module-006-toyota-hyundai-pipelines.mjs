import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const exists = (relative) => fs.existsSync(path.join(root, relative));
const component = read('src/frontend/project-time-web/src/ProjectRegisterCenter.jsx');
const css = read('src/frontend/project-time-web/src/module006-standalone.css');
const api = read('src/backend/ProjectTime.Api/Modules/Module006StandalonePipelineModule.cs');
const tasks = read('src/backend/ProjectTime.Api/Modules/Module006StandaloneTaskModule.cs');
const migration = read('database/migrations/068_module006_standalone_pipeline_management.sql');
const rollback = read('database/rollback/068_module006_standalone_pipeline_management_rollback.sql');
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
  'TOYOTA_HYUNDAI_PIPELINE_PROJECTS',
  'TOYOTA_HYUNDAI_PIPELINE_EVENTS'
], 'Module 006 standalone workspace');

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
  'ExpectedRevision',
  'ViewAsReadOnly'
], 'Module 006 project API');

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
  '<ProjectRegisterCenter legacyRoute={false} />'
], 'Module 006 route isolation');

const plan = read('docs/modules/module-006-toyota-hyundai-pipelines/IMPLEMENTATION-PLAN.md');
requireAll(plan, [
  'Module 055C dependency: **none**',
  'standalone tasks',
  'Add New Project',
  'append-only status updates and notes',
  'Migration `068_module006_standalone_pipeline_management`'
], 'Module 006 implementation plan');

console.log('MODULE_006_STANDALONE_PIPELINE=PASS');
console.log('MODULE_006_PROJECT_CREATE_EDIT_NOTES=PASS');
console.log('MODULE_006_STANDALONE_TASKS=PASS');
console.log('MODULE_006_NO_055C_DEPENDENCY=PASS');
console.log('MODULE_006_REVIEWED_SNAPSHOT_CONTINUITY=projects:38 events:387');
