import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, '../../../..');
const frontendRoot = resolve(scriptDirectory, '..');
const backendRoot = join(repositoryRoot, 'src/backend/ProjectTime.Api');

function fail(message) {
  throw new Error(`PRODUCTION_CONSISTENCY_FAILED=${message}`);
}

function text(path) {
  return readFileSync(path, 'utf8');
}

function frontendModules(source) {
  const rows = new Map();
  for (const match of source.matchAll(/moduleNumber:\s*'([^']+)'/g)) {
    const block = source.slice(match.index, match.index + 900);
    const route = block.match(/route:\s*'([^']+)'/)?.[1];
    const displayName = block.match(/displayName:\s*'([^']+)'/)?.[1];
    const group = block.match(/group:\s*'([^']+)'/)?.[1];
    if (!route || !displayName || !group) fail(`frontend_module_${match[1]}_is_incomplete`);
    if (rows.has(match[1])) fail(`frontend_module_${match[1]}_is_duplicated`);
    rows.set(match[1], { route, displayName, group });
  }
  return rows;
}

function backendModules(source) {
  const rows = new Map();
  const pattern = /\["([^"]+)"\]\s*=\s*Module\("[^"]+",\s*"([^"]+)",\s*"([^"]+)",\s*"([^"]+)"\)/g;
  for (const match of source.matchAll(pattern)) {
    if (rows.has(match[1])) fail(`backend_module_${match[1]}_is_duplicated`);
    rows.set(match[1], { route: match[2], displayName: match[3], group: match[4] });
  }
  return rows;
}

function walk(root) {
  return readdirSync(root).flatMap((name) => {
    const path = join(root, name);
    return statSync(path).isDirectory() ? walk(path) : [path];
  });
}

function luminance(hex) {
  const source = hex.replace('#', '');
  const values = [0, 2, 4].map((offset) => Number.parseInt(source.slice(offset, offset + 2), 16) / 255)
    .map((channel) => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4);
  return 0.2126 * values[0] + 0.7152 * values[1] + 0.0722 * values[2];
}

function contrast(foreground, background) {
  const a = luminance(foreground);
  const b = luminance(background);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

const frontend = frontendModules(text(join(frontendRoot, 'src/module-availability-registry.js')));
const backend = backendModules(text(join(backendRoot, 'Modules/ModuleAvailabilityModule.cs')));
if (frontend.size !== backend.size) fail(`module_catalog_count_frontend_${frontend.size}_backend_${backend.size}`);
for (const [number, expected] of frontend) {
  const actual = backend.get(number);
  if (!actual) fail(`backend_module_${number}_missing`);
  for (const field of ['route', 'displayName', 'group']) {
    if (actual[field] !== expected[field]) fail(`module_${number}_${field}_drift`);
  }
}

const sourceFiles = [
  ...walk(join(frontendRoot, 'src')),
  ...walk(join(frontendRoot, 'container-context/src')),
  ...walk(backendRoot)
]
  .filter((path) => /\.(?:cs|csproj|css|js|jsx)$/.test(path));
const retiredBrand = sourceFiles.filter((path) => /Pulse AI|PULSE AI|ProjectCelar AI/.test(text(path)));
if (retiredBrand.length) fail(`retired_brand_literal_in_${retiredBrand.map((path) => path.replace(repositoryRoot, '')).join(',')}`);

const frontendConsumers = [
  'HelpAssistant.jsx',
  'PulseAiDeepIntelligenceWorkbench.jsx',
  'PulseAiPrivateDocumentPipelineWorkbench.jsx',
  'PulseAiPrivateRagWorkbench.jsx',
  'PulseAiPrivateRuntimeWorkbench.jsx',
  'PulseAiSystemIntelligenceWorkbench.jsx'
].map((name) => join(frontendRoot, 'src', name));
for (const path of frontendConsumers) {
  if (text(path).includes('/api/pulse-ai')) fail(`frontend_consumer_uses_retired_api_${path.replace(repositoryRoot, '')}`);
}

const canonicalPrivateApiContracts = [
  ['Modules/PulseAiDeepIntelligenceModule.cs', '/api/celar-ai/v1/private-runtime/readiness'],
  ['Modules/PulseAiPrivateDocumentPipelineModule.cs', '/api/celar-ai/v1/documents/pipeline/readiness'],
  ['Modules/PulseAiPrivateRuntimeModule.cs', '/api/celar-ai/v1/documents/runtime/readiness'],
  ['Modules/PulseAiPrivateRagModule.cs', '/api/celar-ai/v1/rag/readiness'],
  ['Modules/PulseAiSystemIntelligenceModule.cs', '/api/celar-ai/v1/system/readiness']
];
for (const [relative, route] of canonicalPrivateApiContracts) {
  if (!text(join(backendRoot, relative)).includes(route)) fail(`canonical_private_api_missing_${route}`);
}
const privateRuntimeContracts = text(join(backendRoot, 'Ai/PulseAiPrivateRuntimeContracts.cs'));
if (!privateRuntimeContracts.includes('QUEUE-CELAR-AI-PRIVATE-DOCUMENT-PROCESSING')
    || privateRuntimeContracts.includes('QUEUE-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING')) {
  fail('private_runtime_confirmation_brand_drift');
}

const pageContext = text(join(frontendRoot, 'src/PageContextGuide.jsx'));
if (!pageContext.includes('moduleForRoute(activeRoute)')) fail('page_header_not_bound_to_canonical_module_registry');
if (!pageContext.includes('/api/celar-ai/v1/system/apis?module=')) fail('page_header_not_bound_to_live_api_inventory');

const main = text(join(frontendRoot, 'src/main.jsx')).trim();
const imports = main.slice(0, main.indexOf('createRoot('));
if (!imports.trim().endsWith("import './enterprise-contrast-guard.css';")) fail('contrast_guard_is_not_last_stylesheet');

const requiredContrasts = [
  ['light_text', '#172033', '#f4f7fb', 7],
  ['light_muted', '#52627a', '#ffffff', 4.5],
  ['light_link', '#005ea8', '#ffffff', 4.5],
  ['dark_text', '#f4f8ff', '#07111f', 7],
  ['dark_muted', '#b8c4d6', '#111f33', 4.5],
  ['dark_link', '#75c2ff', '#111f33', 4.5]
];
for (const [name, foreground, background, minimum] of requiredContrasts) {
  const ratio = contrast(foreground, background);
  if (ratio < minimum) fail(`${name}_contrast_${ratio.toFixed(2)}_below_${minimum}`);
}

const flowHive = text(join(frontendRoot, 'src/ProjectFlowHiveCenter.jsx'));
for (const contract of [
  '/api/project-flowhive/projects/${selectedProjectId}/ai-planner/runs',
  'runAiPlannerOperation',
  'const result = await runAiPlannerOperation();',
  '/api/project-flowhive/plans/drafts',
  'AI Planning Workspace',
  'Save immutable version',
  'Establish reviewed baseline',
  'The exact stored Module 064 order is followed for this capability.',
  'Private SOW, GSD, design, task, and assignment evidence stays inside the governed boundary.'
]) if (!flowHive.includes(contract)) fail(`flowhive_contract_missing_${contract}`);

for (const prohibited of [
  "postJson('/api/project-flowhive/ai/production-generate'",
  'postJson("/api/project-flowhive/ai/production-generate"',
  "fetch('/api/project-flowhive/ai/production-generate'",
  'fetch("/api/project-flowhive/ai/production-generate"'
]) if (flowHive.includes(prohibited)) fail('flowhive_frontend_legacy_production_generate_reachable');

const cicdFrontend = text(join(frontendRoot, 'src/CiCdPipelineCenter.jsx'));
const cicdBackend = text(join(backendRoot, 'Modules/CiCdPipelineModule.cs'));
for (const contract of [
  "workflow: 'projectpulse-ci.yml'",
  'inputs: {}',
  'Open protected workflow'
]) if (!cicdFrontend.includes(contract)) fail(`module_058_frontend_boundary_missing_${contract}`);
if (cicdFrontend.includes("workflow: 'projectpulse-deploy-test.yml'")) fail('module_058_frontend_defaults_to_protected_deployment');
for (const contract of [
  'protected_workflow_dispatch_required',
  'validation_workflow_inputs_not_allowed',
  'string.Equals(workflow, "projectpulse-ci.yml", StringComparison.Ordinal)',
  'new Dictionary<string, string>()'
]) if (!cicdBackend.includes(contract)) fail(`module_058_backend_boundary_missing_${contract}`);

const migration = text(join(repositoryRoot, 'database/migrations/074_module_066_project_flowhive_production.sql'));
for (const object of ['project_flowhive_plans', 'project_flowhive_plan_versions', 'project_flowhive_plan_reviews', 'project_flowhive_audit_events']) {
  if (!migration.includes(object)) fail(`migration_074_missing_${object}`);
}

console.log(`PRODUCTION_CONSISTENCY_MODULES=${frontend.size}`);
console.log(`PRODUCTION_CONSISTENCY_SOURCE_FILES=${sourceFiles.length}`);
console.log('PRODUCTION_CONSISTENCY_BRAND=CELAR_AI_ONLY');
console.log('PRODUCTION_CONSISTENCY_CONTRAST=WCAG_AA_OR_BETTER');
console.log('PRODUCTION_CONSISTENCY_FLOWHIVE=DURABLE_PROJECT_SCOPED_PLANNER');
console.log('PRODUCTION_CONSISTENCY=PASSED');
