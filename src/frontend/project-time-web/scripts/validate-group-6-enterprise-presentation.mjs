import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '../../..');
const sourceRoot = path.join(webRoot, 'src');
const enterpriseRoot = path.join(sourceRoot, 'enterprise');
const fullRepositoryContext = fs.existsSync(path.join(repositoryRoot, '.git'))
  || fs.existsSync(path.join(repositoryRoot, '.github/workflows/projectpulse-ci.yml'));

const files = {
  logo: path.join(enterpriseRoot, 'USSignalLogo.jsx'),
  presentation: path.join(enterpriseRoot, 'EnterpriseModulePresentation.jsx'),
  systemCss: path.join(enterpriseRoot, 'enterprise-module-system.css'),
  adoptionCss: path.join(enterpriseRoot, 'enterprise-module-route-adoption.css'),
  injector: path.join(scriptDirectory, 'inject-group-6-enterprise-presentation.mjs'),
  package: path.join(webRoot, 'package.json'),
  app: path.join(sourceRoot, 'App.jsx'),
  salesDelivery: path.join(enterpriseRoot, 'SalesDeliveryWorkflowCenter.jsx'),
  intakeBackend: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectIntakeModule.cs'),
  securityHardening: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/SecurityHardeningModule.cs'),
  documentation: path.join(repositoryRoot, 'docs/modules/group-6-enterprise-presentation/README.md')
};

const targetModules = Object.freeze({
  '024': 'sales-intake',
  '025': 'sow-generator',
  '027': 'signed-handoff',
  '028': 'ai-time-entry',
  '029': 'uat-validation',
  '064': 'ai-provider-configuration',
  '068': 'system-architecture',
  '069': 'qualifications-certifications',
  '071': 'oncall-scheduling',
  '072': 'oneassist-routing-directory',
  '074': 'oem-vendor-directory'
});

let checks = 0;

function read(filePath) {
  if (!fs.existsSync(filePath)) {
    throw new Error(`Required Group 6 file is missing: ${path.relative(repositoryRoot, filePath)}`);
  }
  return fs.readFileSync(filePath, 'utf8');
}

function assert(condition, message) {
  checks += 1;
  if (!condition) throw new Error(message);
}

function contains(source, marker, label) {
  assert(source.includes(marker), `${label} is missing: ${marker}`);
}

function count(source, marker) {
  return source.split(marker).length - 1;
}

const logo = read(files.logo);
const presentation = read(files.presentation);
const systemCss = read(files.systemCss);
const adoptionCss = read(files.adoptionCss);
const injector = read(files.injector);
const salesDelivery = read(files.salesDelivery);
const packageJson = JSON.parse(read(files.package));

contains(logo, "import { usSignalLogoDataUrl } from '../assets/usSignalLogoData.js';", 'official image asset');
contains(logo, 'data-official-us-signal-logo="true"', 'official logo marker');
contains(logo, '<img', 'image logo rendering');
assert(!logo.includes('<strong>US Signal</strong>'), 'The logo component must not use hanging text as a substitute for the approved image.');
assert(!logo.includes('data:image/'), 'The component must consume the one approved repository logo asset rather than embedding another image.');

for (const componentName of [
  'EnterprisePageHeader',
  'EnterpriseModuleLabel',
  'EnterpriseSummaryStrip',
  'EnterpriseStatusCard',
  'EnterpriseFilterBar',
  'EnterpriseTabs',
  'EnterpriseTable',
  'EnterpriseEmptyState',
  'EnterpriseWarning',
  'EnterprisePrintHeader',
  'EnterpriseModulePage'
]) {
  contains(presentation, `function ${componentName}`, `reusable ${componentName}`);
}

contains(presentation, "import './enterprise-module-system.css';", 'enterprise system styles');
contains(presentation, "import './enterprise-module-route-adoption.css';", 'route adoption styles');
contains(presentation, 'ROUTE_TO_MODULE', 'route-aware presentation map');
contains(presentation, 'activeRoute', 'route-aware presentation mount');
contains(presentation, 'Existing APIs, workflows, and permissions remain authoritative', 'functional-scope preservation');

for (const [moduleCode, route] of Object.entries(targetModules)) {
  contains(presentation, `'${moduleCode}': Object.freeze({`, `Module ${moduleCode} presentation metadata`);
  contains(presentation, `route: '${route}'`, `Module ${moduleCode} route metadata`);
  contains(adoptionCss, `main.route-${route}`, `Module ${moduleCode} route adoption`);
}

for (const token of [
  '--uss-navy-950',
  '--uss-navy-900',
  '--uss-cyan-600',
  '--uss-green-700',
  '--uss-amber-700',
  '--uss-red-700',
  '--uss-page-width'
]) {
  contains(systemCss, token, 'US Signal enterprise design token');
}

for (const selector of [
  '.uss-logo-lockup',
  '.uss-enterprise-page-header',
  '.uss-module-label',
  '.uss-summary-strip',
  '.uss-status-card',
  '.uss-filter-bar',
  '.uss-tabs',
  '.uss-table-wrap',
  '.uss-empty-state',
  '.uss-warning',
  '.uss-print-header'
]) {
  contains(systemCss, selector, 'enterprise presentation style');
}

contains(systemCss, ':focus-visible', 'accessible focus treatment');
contains(systemCss, '@media (max-width: 760px)', 'responsive behavior');
contains(systemCss, '@media print', 'print and export behavior');
contains(adoptionCss, '[data-logo-text-only="true"]', 'text-logo suppression contract');
contains(adoptionCss, 'overflow-x: auto', 'constrained table scrolling');
assert(!/color:\s*#fff\s*;\s*background:\s*#fff/i.test(systemCss), 'Enterprise styles must not create white-on-white contrast.');

contains(injector, "import EnterpriseModulePresentation from './enterprise/EnterpriseModulePresentation.jsx';", 'App import installation');
contains(injector, '<EnterpriseModulePresentation activeRoute={activeRoute} />', 'one route-aware presentation mount');
contains(injector, '<PageContextGuide activeRoute={activeRoute} />', 'stable additive mount anchor');
contains(injector, 'GROUP_6_ENTERPRISE_PRESENTATION_START', 'idempotent start marker');
contains(injector, 'GROUP_6_ENTERPRISE_PRESENTATION_END', 'idempotent end marker');
assert(!injector.includes('enterprise-more-navigation'), 'Group 6 must not modify the permission-aware More menu.');
assert(!injector.includes('module-availability-registry.js'), 'Group 6 must not rewrite module identity or permission registries.');

for (const marker of [
  'draftPackage',
  'uploadedFileKeys',
  'retainDraft(workingPackage)',
  'Retry resumes this package; it will not create another intake.',
  '`/api/project-intake/requests/${workingPackage.id}/documents`',
  '`/api/project-intake/requests/${workingPackage.id}/signed-handoff`'
]) {
  contains(salesDelivery, marker, 'resumable Module 024/027 intake handoff');
}
assert(
  count(salesDelivery, "request('/api/project-intake/requests',") === 1,
  'The intake uploader must have one guarded create call and reuse the retained package on retry.'
);
contains(
  salesDelivery,
  "import SowGsdWorkspace from '../module025/SowGsdWorkspace.jsx';",
  'Module 025 SOW/GSD workspace import'
);
contains(
  salesDelivery,
  "module === '025' ? <SowGsdWorkspace />",
  'Module 025 live workspace route'
);
assert(
  !salesDelivery.includes('function SowGenerator()'),
  'Module 025 live route must not retain the legacy inline SOW Generator.'
);

const predev = packageJson.scripts?.predev ?? '';
const prebuild = packageJson.scripts?.prebuild ?? '';
const build = packageJson.scripts?.build ?? '';
contains(predev, 'inject-group-6-enterprise-presentation.mjs', 'predev Group 6 installer');
contains(prebuild, 'inject-group-6-enterprise-presentation.mjs', 'prebuild Group 6 installer');
contains(build, 'validate:group6-enterprise-presentation', 'complete-build Group 6 validation');
assert(
  packageJson.scripts?.['validate:group6-enterprise-presentation']
    === 'node ./scripts/validate-group-6-enterprise-presentation.mjs',
  'The Group 6 package validator must be authoritative.'
);

if (fullRepositoryContext) {
  const documentation = read(files.documentation);
  const intakeBackend = read(files.intakeBackend);
  const securityHardening = read(files.securityHardening);
  contains(intakeBackend, 'ORDER BY uploaded_at, original_file_name', 'signed-handoff document chronology');
  assert(!intakeBackend.includes('ORDER BY created_at, original_file_name'), 'The signed-handoff query must use the real project_intake_documents uploaded_at column.');
  for (const marker of ['CanSubmitSignedHandoff', 'ACCOUNT_EXECUTIVE', 'ACCOUNT_EXECUTIVES', 'INSIDE_SALES', 'SOLUTION_ARCHITECT', '"SA"', '"SAA"', 'MANAGE_PROJECT_INTAKE', 'MANAGE_PROJECT_DOCUMENTS']) {
    contains(intakeBackend, marker, 'Module 027 submitter authority');
  }
  contains(salesDelivery, "'purchase_order'", 'purchase-order upload category');
  contains(securityHardening, '"purchase_order"', 'purchase-order upload security allowlist');
  for (const moduleCode of Object.keys(targetModules)) {
    contains(documentation, `Module ${moduleCode}`, `Module ${moduleCode} documentation`);
  }
  for (const marker of [
    'official US Signal logo',
    'More menu is excluded',
    'No migration',
    'No database permission or role-grant change',
    'No deployment'
  ]) {
    contains(documentation, marker, 'Group 6 scope documentation');
  }
}

execFileSync(process.execPath, [files.injector], {
  cwd: webRoot,
  stdio: 'inherit'
});

const generatedApp = read(files.app);
const module027Navigation = generatedApp.match(/route: "signed-handoff"[\s\S]{0,1400}/)?.[0] ?? '';
for (const marker of ['INSIDE_SALES', 'ACCOUNT_EXECUTIVE', 'ACCOUNT_EXECUTIVES', 'SOLUTION_ARCHITECT', '"SA"', '"SAA"', 'MANAGE_PROJECT_INTAKE', 'MANAGE_PROJECT_DOCUMENTS']) {
  contains(module027Navigation, marker, 'Module 027 navigation authority');
}
assert(
  count(generatedApp, "import EnterpriseModulePresentation from './enterprise/EnterpriseModulePresentation.jsx';") === 1,
  'Generated App must import the Group 6 presentation exactly once.'
);
assert(
  count(generatedApp, 'GROUP_6_ENTERPRISE_PRESENTATION_START') === 1
    && count(generatedApp, 'GROUP_6_ENTERPRISE_PRESENTATION_END') === 1,
  'Generated App must contain one Group 6 presentation block.'
);
assert(
  count(generatedApp, '<EnterpriseModulePresentation activeRoute={activeRoute} />') === 1,
  'Generated App must mount the route-aware Group 6 presentation exactly once.'
);
for (const [moduleCode, route] of Object.entries(targetModules)) {
  contains(generatedApp, route, `existing Module ${moduleCode} route preservation`);
}

console.log(`GROUP_6_VALIDATION_CHECKS=${checks}`);
console.log(`GROUP_6_FULL_REPOSITORY_CONTEXT=${fullRepositoryContext ? 'YES' : 'NO'}`);
console.log('GROUP_6_ENTERPRISE_PRESENTATION=PASS');
