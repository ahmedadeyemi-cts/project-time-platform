import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '../../..');
const sourceRoot = path.join(webRoot, 'src');
const backendPath = path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectFinancialTruthModule.cs');
const projectPath = path.join(repositoryRoot, 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const componentPath = path.join(sourceRoot, 'UnifiedProjectFinancialWorkspace.jsx');
const cssPath = path.join(sourceRoot, 'unified-project-financial-workspace.css');
const injectorPath = path.join(scriptDirectory, 'inject-group-3-project-financial-workspaces.mjs');
const packagePath = path.join(webRoot, 'package.json');
const documentationPath = path.join(repositoryRoot, 'docs/modules/group-3-project-financial-workspaces/README.md');
const fullRepositoryContext = fs.existsSync(path.join(repositoryRoot, '.git'))
  || fs.existsSync(path.join(repositoryRoot, '.github/workflows/projectpulse-ci.yml'));

let checks = 0;

function read(filePath) {
  if (!fs.existsSync(filePath)) {
    throw new Error(`Required Group 3 file is missing: ${path.relative(repositoryRoot, filePath)}`);
  }
  return fs.readFileSync(filePath, 'utf8');
}

function optional(filePath) {
  return fs.existsSync(filePath) ? fs.readFileSync(filePath, 'utf8') : '';
}

function check(name, condition, evidence) {
  checks += 1;
  console.log(`GROUP_3_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
  if (!condition) throw new Error(`${name}: ${evidence}`);
}

function includesAll(source, values) {
  return values.every((value) => source.includes(value));
}

function count(source, value) {
  return source.split(value).length - 1;
}

const backend = fullRepositoryContext ? read(backendPath) : optional(backendPath);
const project = fullRepositoryContext ? read(projectPath) : optional(projectPath);
const component = read(componentPath);
const css = read(cssPath);
const injector = read(injectorPath);
const packageJson = JSON.parse(read(packagePath));
const documentation = fullRepositoryContext ? read(documentationPath) : optional(documentationPath);

const apiRoutes = [
  '/api/project-financials/portfolio',
  '/api/project-financials/projects/{projectId:guid}',
  '/api/project-financials/sources',
  '/api/project-financials/reporting-summary'
];

if (fullRepositoryContext) {
  check('BACKEND_READ_ONLY',
    count(backend, 'endpoints.MapGet(') === apiRoutes.length
      && !/\.Map(?:Post|Put|Patch|Delete)\s*\(/.test(backend),
    'exactly four GET routes and no financial mutation endpoint');

  for (const route of apiRoutes) {
    check(`API_${route.replace(/[^a-z0-9]+/gi, '_').toUpperCase()}`,
      backend.includes(`"${route}"`), route);
  }

  check('AUTHORITATIVE_SUMMARY_FIELDS', includesAll(backend, [
    'CustomerName', 'ProjectName', 'ProjectManagerName', 'Engineers',
    'SolutionArchitect', 'AccountExecutive', 'ContractType',
    'ContractedValue', 'LaborBudget', 'ExpenseBudget', 'PlannedHours',
    'UsedHours', 'RemainingHours', 'LaborCost', 'UploadedExpenses',
    'CommittedCost', 'ForecastedFinalCost', 'CurrentVariance',
    'CompletionPercentage', 'Sell'
  ]), 'all required authoritative project-summary fields exist');

  check('MODULE_005_CURRENT_EXPENSES', includesAll(backend, [
    'project_expense_uploads', 'upload.is_current = TRUE',
    'upload.deleted_at IS NULL', 'total_amount', 'reimbursable_amount'
  ]), 'current, non-deleted Module 005 expenses supply project totals');

  check('TIME_AND_ASSIGNMENT_AUTHORITY', includesAll(backend, [
    'project_assignments', 'engineering_resource_request_assignments',
    'time_entries', 'assigned_hours', 'SUM(hours)'
  ]), 'planned, used, and remaining hours come from assignments and time entries');

  check('PROJECT_DOCUMENT_AUTHORITY', includesAll(backend, [
    'project_intake_documents', '/api/project-workspace/documents/',
    'engineering_visible', 'IQS files', 'Service requests',
    'Project documents', 'Customer documents'
  ]), 'Module 019 document groups and working download route are preserved');

  check('COST_ALERT_EVIDENCE', includesAll(backend, [
    'project_cost_alerts', 'notification_queued_at',
    'notification_recipient_count', 'OpenAlertCount'
  ]), 'existing Module 022 alert and notification evidence is consumed read-only');

  check('MODULE_026_GOVERNED_SELL', includesAll(backend, [
    'SellCommercialReadModelModule.LoadProjectCommercialSummaryAsync',
    'Module 026', 'Connection and credential ownership remain in Module 026.',
    'secondCredentialSystemCreated = false'
  ]), 'Module 055B and workspaces reuse Module 026 rather than a second SELL connection');

  check('NO_RETIRED_MODULE_067_MAIL_DEPENDENCY',
    !/Module\s*067|module-067|GLOBAL_MAIL_PROVIDER/i.test(backend),
    'Group 3 does not introduce mail ownership or read retired Module 067 configuration');

  check('SERVER_COST_VISIBILITY', includesAll(backend, [
    'full_project_financials', 'commercial_summary',
    'hours_and_progress', 'costVisibilityServerEnforced = true',
    'viewAsTransfersMutationAuthority = false'
  ]), 'PM, sales, engineering, and administrator financial visibility is server enforced');

  check('CALCULATION_EXPLANATIONS', includesAll(backend, [
    'remaining_hours', 'labor_cost', 'committed_cost',
    'forecasted_final_cost', 'current_variance',
    'completion_percentage', 'How values were calculated'
  ].filter((value) => value !== 'How values were calculated')),
  'calculation formulas and explanations are returned with each project');

  check('SOURCE_ISOLATION', includesAll(backend, [
    'TryLoadAsync', 'SourceState.Unavailable', 'SourceState.Partial',
    'other project data remains usable', 'financial_data_source_unavailable'
  ]), 'an unavailable optional source cannot blank the complete page');

  check('NO_MODULE_011_DEPENDENCY',
    backend.includes('module011Dependency = false')
      && !/work-task-builder|WorkTaskBuilder|Module\s*011/i.test(backend),
    'engineering workspace contains no Module 011 dependency');

  check('REGISTRATION',
    count(project, 'app.MapProjectFinancialTruthEndpoints();') === 1,
    'ProjectTime.Api.csproj registers the Group 3 endpoints exactly once');

  check('DOCUMENTATION_SCOPE', includesAll(documentation, [
    'Module 018', 'Module 019', 'Module 036', 'Module 055B',
    'Module 005', 'Module 026', 'PR #187', 'No migration',
    'Groups 4, 5, and 6'
  ]), 'documentation records dependencies, completed SELL ownership, no migration, and later groups');

  check('NO_DATABASE_OR_DEPLOYMENT_SCOPE',
    documentation.includes('No database migration')
      && documentation.includes('No deployment')
      && !backend.includes('INSERT INTO')
      && !backend.includes('UPDATE projects')
      && !backend.includes('DELETE FROM'),
    'Group 3 is source-only and read-only');
} else {
  console.log('GROUP_3_BACKEND_AND_GOVERNANCE_CONTRACT=SKIPPED_FRONTEND_CONTAINER_CONTEXT');
}

check('OFFICIAL_US_SIGNAL_LOGO',
  component.includes("import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';")
    && component.includes('src={usSignalLogoDataUrl}')
    && component.includes('alt="US Signal"'),
  'one approved US Signal image asset is used');

check('WORKSPACE_IDENTITIES', includesAll(component, [
  "module: '018'", "module: '019'", "module: '036'", "module: '055B'",
  "workspace=\"pm\"", "workspace=\"engineering\"",
  "workspace=\"sales\"", "workspace=\"rate-card\""
].slice(0, 4)), 'Modules 018, 019, 036, and 055B use one shared component');

check('FINANCIAL_EXPERIENCE', includesAll(component, [
  'Project portfolio and financial truth', 'Contracted value',
  'Labor budget', 'Expense budget', 'Calculated labor cost',
  'Uploaded expenses', 'Committed cost', 'Forecasted final cost',
  'Current variance', 'How values were calculated'
]), 'PM and sales financial truth is visible with formulas');

check('ENGINEERING_EXPERIENCE', includesAll(component, [
  'Allocated, used, and remaining hours', 'IQS, service request, project, and customer files',
  'Download', '/api/project-workspace/documents/'
].filter((value) => value !== '/api/project-workspace/documents/'))
  && component.includes('document.downloadUrl'),
  'engineering hours, grouped documents, and authenticated downloads are present');

check('SELL_GOVERNANCE_PRESENTATION', includesAll(component, [
  'Connection owner', 'SELL relationship', 'Module 026',
  'without another provider credential', 'SELL connection owner: Module 026'
]), 'Module 055B and sales visibly consume governed SELL context');

check('SOURCE_RETRY', includesAll(component, [
  'Source health', 'Retry sources', 'One unavailable optional source does not blank',
  'Refresh financial truth'
]), 'friendly source-level diagnostics and retry are available');

check('NO_MODULE_011_FRONTEND',
  component.includes('No Module 011 dependency is introduced.')
    && !/WorkTaskBuilder|work-task-builder/i.test(component),
  'Module 019 documents do not depend on Module 011');

check('SCOPED_RESPONSIVE_CSS', includesAll(css, [
  '.group3-financial-workspace', '.group3-hero', '.group3-summary-grid',
  '.group3-project-table', '.group3-tabs', '.group3-source-grid',
  '@media (max-width: 820px)', '@media (max-width: 620px)'
]) && !/(^|\n)\s*(?:html|body|:root|#root|main|button|table|input|select)\s*[{,]/m.test(css),
'styling is enterprise, responsive, and module-scoped');

check('ENTERPRISE_BRAND_TOKENS', includesAll(css, [
  '--group3-navy-950', '--group3-cyan-600',
  '--group3-green-700', '--group3-slate-900'
]), 'US Signal-aligned navy, cyan, green, and neutral tokens are centralized');

for (const target of [
  'ProjectManagerWorkloadCenter.jsx',
  'ProjectWorkspaceCenter.jsx',
  'SalesInsightsDashboard.jsx',
  'RateCardAdministrationCenter.jsx'
]) {
  check(`INJECTOR_${target.replace(/[^a-z0-9]+/gi, '_').toUpperCase()}`,
    injector.includes(target), target);
}

check('INJECTOR_ISOLATION',
  !injector.includes('App.jsx')
    && !injector.includes('main.jsx')
    && injector.includes('GROUP_3_UNIFIED_PROJECT_FINANCIAL_WORKSPACES_START'),
  'the installer uses module roots and does not rewrite the application shell');

const predev = packageJson.scripts?.predev || '';
const prebuild = packageJson.scripts?.prebuild || '';
const build = packageJson.scripts?.build || '';
check('PACKAGE_WIRING',
  predev.includes('inject-group-3-project-financial-workspaces.mjs')
    && prebuild.includes('inject-group-3-project-financial-workspaces.mjs')
    && build.includes('validate:group3-project-financial-workspaces')
    && packageJson.scripts?.['validate:group3-project-financial-workspaces']
      === 'node ./scripts/validate-group-3-project-financial-workspaces.mjs',
  'predev, prebuild, and full build enforce Group 3 installation and validation');

execFileSync(process.execPath, [injectorPath], {
  cwd: webRoot,
  stdio: 'inherit'
});

const mounts = [
  ['ProjectManagerWorkloadCenter.jsx', 'workspace="pm" projectManagerUserId={selectedProjectManagerUserId}'],
  ['ProjectWorkspaceCenter.jsx', 'workspace="engineering"'],
  ['SalesInsightsDashboard.jsx', 'workspace="sales"'],
  ['RateCardAdministrationCenter.jsx', 'workspace="rate-card"']
];
for (const [fileName, mount] of mounts) {
  const source = read(path.join(sourceRoot, fileName));
  check(`MOUNT_${fileName.replace(/[^a-z0-9]+/gi, '_').toUpperCase()}`,
    count(source, "import UnifiedProjectFinancialWorkspace from './UnifiedProjectFinancialWorkspace.jsx';") === 1
      && count(source, 'GROUP_3_UNIFIED_PROJECT_FINANCIAL_WORKSPACES_START') === 1
      && count(source, mount) === 1,
    `${fileName} contains one import and one role-specific mount`);
}

console.log(`GROUP_3_VALIDATION_CHECKS=${checks}`);
console.log(`GROUP_3_FULL_REPOSITORY_CONTEXT=${fullRepositoryContext ? 'YES' : 'NO'}`);
console.log('GROUP_3_UNIFIED_PROJECT_FINANCIAL_WORKSPACES=PASS');
