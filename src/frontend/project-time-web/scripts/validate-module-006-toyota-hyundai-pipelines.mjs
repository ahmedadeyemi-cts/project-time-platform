import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const exists = (relative) => fs.existsSync(path.join(root, relative));
const component = read('src/frontend/project-time-web/src/ProjectRegisterCenter.jsx');
const snapshotFiles = [
  'src/frontend/project-time-web/src/toyota-hyundai-pipeline-snapshot.js',
  'src/frontend/project-time-web/src/toyota-hyundai-pipeline-metadata.js',
  'src/frontend/project-time-web/src/toyota-hyundai-pipeline-projects.js',
  ...Array.from({ length: 8 }, (_, index) => `src/frontend/project-time-web/src/toyota-hyundai-pipeline-events-${String(index + 1).padStart(2, '0')}.js`)
];
const snapshot = snapshotFiles.map(read).join('\n');
const css = read('src/frontend/project-time-web/src/project-register-center.css');
const injector = read('src/frontend/project-time-web/scripts/inject-module-006-toyota-hyundai-pipeline.mjs');
const registry = read('src/frontend/project-time-web/src/module-availability-registry.js');
const compatibility = read('src/frontend/project-time-web/src/scoped-rbac-catalog-compatibility.js');
const packageJson = read('src/frontend/project-time-web/package.json');

const requireAll = (source, values, label) => values.forEach((value) => {
  if (!source.includes(value)) throw new Error(`${label} missing: ${value}`);
});
const rejectAll = (source, values, label) => values.forEach((value) => {
  if (source.includes(value)) throw new Error(`${label} contains forbidden marker: ${value}`);
});

requireAll(component, [
  'data-module="006"',
  'data-module-name="Toyota & Hyundai Pipelines"',
  'data-canonical-route="toyota-hyundai-pipelines"',
  'data-project-register-contract="reviewed-workbook-snapshot-v1"',
  'Toyota &amp; Hyundai Pipelines',
  'TOYOTA_HYUNDAI_PIPELINE_PROJECTS',
  'TOYOTA_HYUNDAI_PIPELINE_EVENTS',
  'TOYOTA_HYUNDAI_SNAPSHOT_METADATA',
  "window.history.replaceState(window.history.state, '', '#toyota-hyundai-pipelines')",
  'Archived / historical',
  'Export Excel',
  'Print / Save PDF',
  'application/vnd.ms-excel',
  'Logs and Audit',
  'Quotes and SELL',
  'Immutable pipeline ID',
  'Rows',
  'PAGE_SIZES',
  'visibleProjects'
], 'Module 006 workbook workspace');

rejectAll(component, [
  '/api/work-register/overview',
  '/api/work-register/projects/',
  '/api/work-lifecycle/projects/',
  "method: 'POST'",
  "method: 'PUT'",
  "method: 'PATCH'",
  "method: 'DELETE'"
], 'Module 006 source-snapshot boundary');

requireAll(snapshot, [
  'module006-reviewed-workbook-snapshot-v1-20260730',
  '"activeProjectCount": 26',
  '"archivedProjectCount": 12',
  '"eventCount": 387',
  'TOYOTA_HYUNDAI_PIPELINE_PROJECTS',
  'TOYOTA_HYUNDAI_PIPELINE_EVENTS',
  '"customer": "Toyota"',
  '"customer": "Hyundai"',
  '"pipelineEntryId"',
  '"eventId"',
  '"sourceProjectCode"',
  '"sourceSheet"',
  '"sourceRow"'
], 'Reviewed Toyota/Hyundai snapshot');
rejectAll(snapshot, [
  '"customer": "Turion"',
  '"customer": "No Updates"'
], 'Reviewed Toyota/Hyundai snapshot');

requireAll(css, [
  'max-height: min(62vh, 46rem)',
  '.project-register-pagination',
  '.project-register-timeline',
  'max-height: 52vh',
  '@media print'
], 'Module 006 bounded scrolling and export presentation');

requireAll(injector, [
  "route: 'toyota-hyundai-pipelines'",
  "href: '#toyota-hyundai-pipelines'",
  "title: 'Toyota & Hyundai Pipelines'",
  "return route === 'psa-modules' || route === 'project-register'",
  'PR467_MODULE_006_EXCLUSIVE_ROUTE_START',
  '<ProjectRegisterCenter legacyRoute={false} />',
  'MODULE_006_TOYOTA_HYUNDAI_PIPELINES_GENERATION=PASS',
  'MODULE_006_AUTHORITATIVE_MODULE_DIRECTORY_PATCH',
  'PROJECTPULSE_MODULES',
  'moduleForRoute(route)?.moduleNumber',
  'superAdministratorModuleCatalog',
  'isSuperAdministrator ? superAdministratorModuleCatalog(modules) : modules',
  'Full Control · Organization-wide'
], 'Module 006 generation and Modules directory authority');

requireAll(registry, [
  "moduleNumber: '006', route: 'toyota-hyundai-pipelines', displayName: 'Toyota & Hyundai Pipelines'",
  "'psa-modules': 'toyota-hyundai-pipelines'",
  "'project-register': 'toyota-hyundai-pipelines'",
  "moduleNumber: '066', route: 'project-flowhive', displayName: 'Project FlowHive'"
], 'Module registry');
requireAll(compatibility, ["'006': 'Toyota & Hyundai Pipelines'"], 'Module 006 RBAC compatibility');
requireAll(packageJson, ['validate:module006-pipelines', 'validate-module-006-toyota-hyundai-pipelines.mjs'], 'Module 006 build chain');

if (exists('docs/modules/module-006-toyota-hyundai-pipelines/IMPLEMENTATION-PLAN.md')) {
  const doc = read('docs/modules/module-006-toyota-hyundai-pipelines/IMPLEMENTATION-PLAN.md');
  requireAll(doc, [
    'Toyota & Hyundai Pipelines',
    '#toyota-hyundai-pipelines',
    'reviewed workbook snapshot',
    '26 active',
    '12 archived',
    '387'
  ], 'Module 006 implementation plan');
} else {
  console.log('MODULE_006_DOCUMENTATION_CHECK=SKIPPED_LEAN_WEB_CONTEXT');
}

const generated = 'src/frontend/project-time-web/src/App.Module001.g.jsx';
if (exists(generated)) {
  const generatedApp = read(generated);
  requireAll(generatedApp, [
    "route: 'toyota-hyundai-pipelines'",
    "title: 'Toyota & Hyundai Pipelines'",
    'PR467_MODULE_006_EXCLUSIVE_ROUTE_START',
    'PR467_MODULE_006_EXCLUSIVE_ROUTE_END',
    '<ProjectRegisterCenter legacyRoute={false} />'
  ], 'Generated Module 006 app');
  if ((generatedApp.match(/<ProjectRegisterCenter/g) || []).length !== 1) {
    throw new Error('PR467 Module 006 must have exactly one generated mount.');
  }
}

const portal = 'src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx';
if (exists(portal) && read(portal).includes('MODULE_006_AUTHORITATIVE_MODULE_DIRECTORY_PATCH')) {
  requireAll(read(portal), [
    'PROJECTPULSE_MODULES',
    'moduleForRoute(route)?.moduleNumber',
    'superAdministratorModuleCatalog',
    'Full Control · Organization-wide'
  ], 'Generated authoritative Modules directory');
}

console.log('MODULE_006_TOYOTA_HYUNDAI_PIPELINES_CONTRACT=PASS');
console.log('MODULE_006_REVIEWED_SNAPSHOT=projects:38 events:387 excluded:Turion,No-Updates');
console.log('MODULE_006_PAGINATION_AND_EXPORT=PASS');
console.log('MODULES_DIRECTORY_AUTHORITATIVE_NUMBERS=PASS');
console.log('SUPER_ADMINISTRATOR_FULL_CATALOG=PASS');
console.log('MODULE_006_CANONICAL_ROUTE=toyota-hyundai-pipelines');
console.log('MODULE_006_COMPATIBILITY_ROUTES=psa-modules,project-register');
