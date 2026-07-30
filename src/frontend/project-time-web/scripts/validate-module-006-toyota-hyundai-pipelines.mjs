import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const exists = (relative) => fs.existsSync(path.join(root, relative));
const component = read('src/frontend/project-time-web/src/ProjectRegisterCenter.jsx');
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
  'Toyota &amp; Hyundai Pipelines',
  '/api/work-register/overview',
  '/api/work-register/projects/${item.workId}/details',
  '/api/work-lifecycle/projects/${item.workId}',
  "window.history.replaceState(window.history.state, '', '#toyota-hyundai-pipelines')",
  'Archived / historical',
  'Immutable lifecycle and audit evidence'
], 'Module 006 workspace');
rejectAll(component, ["method: 'POST'", "method: 'PUT'", "method: 'PATCH'", "method: 'DELETE'"], 'Module 006 read foundation');

requireAll(injector, [
  "route: 'toyota-hyundai-pipelines'",
  "href: '#toyota-hyundai-pipelines'",
  "title: 'Toyota & Hyundai Pipelines'",
  "activeRoute === 'psa-modules'",
  "activeRoute === 'project-register'",
  'MODULE_006_TOYOTA_HYUNDAI_PIPELINES_GENERATION=PASS'
], 'Module 006 generation');
requireAll(registry, [
  "moduleNumber: '006', route: 'toyota-hyundai-pipelines', displayName: 'Toyota & Hyundai Pipelines'",
  "'psa-modules': 'toyota-hyundai-pipelines'",
  "'project-register': 'toyota-hyundai-pipelines'"
], 'Module 006 registry');
requireAll(compatibility, ["'006': 'Toyota & Hyundai Pipelines'"], 'Module 006 RBAC compatibility');
requireAll(packageJson, ['validate:module006-pipelines', 'validate-module-006-toyota-hyundai-pipelines.mjs'], 'Module 006 build chain');

if (exists('docs/modules/module-006-toyota-hyundai-pipelines/IMPLEMENTATION-PLAN.md')) {
  const doc = read('docs/modules/module-006-toyota-hyundai-pipelines/IMPLEMENTATION-PLAN.md');
  requireAll(doc, ['Toyota & Hyundai Pipelines', '#toyota-hyundai-pipelines'], 'Module 006 implementation plan');
} else {
  console.log('MODULE_006_DOCUMENTATION_CHECK=SKIPPED_LEAN_WEB_CONTEXT');
}

const generated = 'src/frontend/project-time-web/src/App.Module001.g.jsx';
if (exists(generated)) {
  requireAll(read(generated), [
    "route: 'toyota-hyundai-pipelines'",
    "title: 'Toyota & Hyundai Pipelines'",
    '<ProjectRegisterCenter legacyRoute={activeRoute !== \'toyota-hyundai-pipelines\'} />'
  ], 'Generated Module 006 app');
}

console.log('MODULE_006_TOYOTA_HYUNDAI_PIPELINES_CONTRACT=PASS');
console.log('MODULE_006_CANONICAL_ROUTE=toyota-hyundai-pipelines');
console.log('MODULE_006_COMPATIBILITY_ROUTES=psa-modules,project-register');
