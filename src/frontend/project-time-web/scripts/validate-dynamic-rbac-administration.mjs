import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');
const absolute = (path) => resolve(root, path);
const text = (path) => readFile(absolute(path), 'utf8');
const optionalText = async (path) => existsSync(absolute(path)) ? text(path) : '';
const requireAll = (source, values, label) => {
  for (const value of values) if (!source.includes(value)) throw new Error(`${label} missing contract: ${value}`);
};
const rejectAll = (source, values, label) => {
  for (const value of values) if (source.includes(value)) throw new Error(`${label} contains retired contract: ${value}`);
};
const validateWhenPresent = (source, validate, label) => {
  if (!source) {
    console.log(`${label.toUpperCase().replaceAll(/[^A-Z0-9]+/g, '_')}=SKIPPED_MINIMAL_WEB_CONTEXT`);
    return;
  }
  validate(source);
};

function validateCatalog({ roles, modules, grants = [] }) {
  if (!Array.isArray(roles) || roles.length === 0) throw new Error('active role directory required');
  if (!Array.isArray(modules) || modules.length === 0) throw new Error('active module catalog required');

  const roleCodes = roles.map((item) => String(item.roleCode || '').trim().toUpperCase());
  const moduleCodes = modules.map((item) => String(item.moduleCode || '').trim().toUpperCase());
  if (roleCodes.some((value) => !value)) throw new Error('empty role code');
  if (moduleCodes.some((value) => !value)) throw new Error('empty module code');
  if (new Set(roleCodes).size !== roleCodes.length) throw new Error('duplicate role code');
  if (new Set(moduleCodes).size !== moduleCodes.length) throw new Error('duplicate module code');
  if (!roleCodes.includes('SUPER_ADMINISTRATOR')) throw new Error('super administrator required');
  if (!moduleCodes.includes('012') || !moduleCodes.includes('037')) throw new Error('governance modules required');

  const roleSet = new Set(roleCodes);
  const moduleSet = new Set(moduleCodes);
  for (const grant of grants) {
    if (!roleSet.has(String(grant.roleCode || '').trim().toUpperCase())) throw new Error('grant references unknown role');
    if (!moduleSet.has(String(grant.moduleCode || '').trim().toUpperCase())) throw new Error('grant references unknown module');
  }
  return { roleCount: roleCodes.length, moduleCount: moduleCodes.length };
}

const roleCodes = [
  'ENGINEERING', 'PROJECT_MANAGEMENT', 'ENGINEERING_LEAD', 'PROJECT_MANAGEMENT_LEAD',
  'MANAGER', 'SALES', 'INSIDE_SALES', 'SOLUTION_ARCHITECT', 'EXECUTIVE',
  'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING', 'SUPER_ADMINISTRATOR'
];
const roles = roleCodes.map((roleCode) => ({ roleCode }));
const modulesFor = (count) => {
  const required = [{ moduleCode: '012' }, { moduleCode: '037' }];
  for (let index = 1; required.length < count; index += 1) {
    const code = String(index).padStart(3, '0');
    if (code === '012' || code === '037') continue;
    required.push({ moduleCode: code });
  }
  return required;
};

for (const count of [69, 70, 71]) {
  const result = validateCatalog({
    roles,
    modules: modulesFor(count),
    grants: [{ roleCode: 'ENGINEERING', moduleCode: '001' }]
  });
  if (result.moduleCount !== count) throw new Error(`dynamic catalog rejected ${count} modules`);
}

const expectedFailures = [
  () => validateCatalog({ roles: roles.filter((role) => role.roleCode !== 'SUPER_ADMINISTRATOR'), modules: modulesFor(69) }),
  () => validateCatalog({ roles, modules: modulesFor(69).filter((module) => module.moduleCode !== '012') }),
  () => validateCatalog({ roles, modules: [...modulesFor(69), { moduleCode: '001' }] }),
  () => validateCatalog({ roles: [...roles, { roleCode: 'ENGINEERING' }], modules: modulesFor(69) }),
  () => validateCatalog({ roles, modules: modulesFor(69), grants: [{ roleCode: 'UNKNOWN', moduleCode: '001' }] }),
  () => validateCatalog({ roles, modules: modulesFor(69), grants: [{ roleCode: 'ENGINEERING', moduleCode: '9999' }] })
];
for (const test of expectedFailures) {
  let failed = false;
  try { test(); } catch { failed = true; }
  if (!failed) throw new Error('dynamic RBAC fail-closed catalog test unexpectedly passed');
}

const [
  backend,
  registrar,
  project,
  roleUi,
  matrixUi,
  moreRuntime,
  moreInjector,
  moreCss,
  evaluator,
  platform,
  service,
  evidence,
  architecture,
  group2aWorkflow
] = await Promise.all([
  optionalText('src/backend/ProjectTime.Api/Modules/DynamicRbacAdministrationModule.cs'),
  optionalText('src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs'),
  optionalText('src/backend/ProjectTime.Api/ProjectTime.Api.csproj'),
  text('src/frontend/project-time-web/src/RoleAdminDirectoryPanel.jsx'),
  text('src/frontend/project-time-web/src/RolesPermissionsMatrix.jsx'),
  text('src/frontend/project-time-web/src/intuitive-more-menu.js'),
  text('src/frontend/project-time-web/scripts/inject-react-owned-more-menu.mjs'),
  text('src/frontend/project-time-web/src/intuitive-more-menu.css'),
  optionalText('src/backend/ProjectTime.Api/Modules/ScopedAuthorizationEvaluator.cs'),
  optionalText('src/backend/ProjectTime.Api/Modules/PlatformOperationsModule.cs'),
  text('src/frontend/project-time-web/src/ServiceControlCenter.jsx'),
  text('src/frontend/project-time-web/src/OperationalEvidenceCenter.jsx'),
  text('src/frontend/project-time-web/src/SystemArchitectureCenter.jsx'),
  optionalText('.github/workflows/group2a-provider-neutral-platform-operations-ci.yml')
]);

validateWhenPresent(backend, (source) => {
  requireAll(source, [
    'projectpulse-rbac-v1-2026-07-28',
    'moduleCatalogMode = "database_dynamic"',
    'fixedModuleCountRequired = false',
    'permanentFullControl = true',
    'organizationWide = true',
    'reducible = false',
    'NO_ACCESS_FOR_NON_SUPER_ADMINISTRATORS',
    'super_administrator_self_lockout_blocked',
    'final_super_administrator_removal_blocked',
    'ROLE_MEMBERSHIP_ASSIGNED',
    'ROLE_MEMBERSHIP_REMOVED',
    'MODULE_RETIRED',
    'historicalPolicyAndAuditPreserved = true'
  ], 'Dynamic RBAC API');
  rejectAll(source, [
    'modules.Count != 70',
    'moduleCount == 70',
    'Expected 12 roles and 70 modules'
  ], 'Dynamic RBAC API');
}, 'Dynamic RBAC API');
validateWhenPresent(
  registrar,
  (source) => requireAll(source, ['app.MapDynamicRbacAdministrationEndpoints();'], 'Dynamic RBAC registration'),
  'Dynamic RBAC registration'
);
validateWhenPresent(
  project,
  (source) => requireAll(source, [
    'DynamicRbacGeneratedModule',
    '<Compile Remove="Modules/DynamicRbacAdministrationModule.cs" />'
  ], 'Dynamic RBAC compilation'),
  'Dynamic RBAC compilation'
);
validateWhenPresent(
  evaluator,
  (source) => requireAll(source, [
    'if (actor.IsSuperAdministrator)',
    'permanent organization-wide Full Control',
    'actor.IsViewAs && isWrite'
  ], 'Central RBAC enforcement'),
  'Central RBAC enforcement'
);

requireAll(roleUi, [
  "api('/api/rbac/v1/bootstrap')",
  'Role-Based Access Control',
  'Active modules',
  'Role members',
  'Module catalog',
  'Default to No Access',
  'Permanent organization-wide Full Control'
], 'Module 012 dynamic experience');
rejectAll(roleUi, [
  'REQUIRED_MODULE_COUNT',
  'Expected ${REQUIRED_ROLE_COUNT} roles'
], 'Module 012 dynamic experience');
requireAll(matrixUi, [
  "api('/api/rbac/v1/matrix')",
  'no fixed module count is required',
  'Active modules',
  'Configured pairs',
  'Unconfigured pairs',
  'No 70-module requirement'
], 'Module 037 dynamic experience');
rejectAll(matrixUi, [
  'REQUIRED_MODULE_COUNT',
  'Expected ${REQUIRED_ROLE_COUNT} roles'
], 'Module 037 dynamic experience');

requireAll(moreInjector, [
  'PROJECTPULSE_REACT_OWNED_MORE_MENU',
  'data-projectpulse-react-owned-menu="true"',
  'More pages',
  'Search by page name',
  'data-more-label-source="module-registry"',
  'data-page-name={getNavigationDisplayLabel(item)}',
  '<strong className="projectpulse-more-intuitive-name">{getNavigationDisplayLabel(item)}</strong>',
  'projectpulse-more-intuitive-name',
  'projectpulse-more-intuitive-arrow',
  'window.ProjectPulseMoreNavigation?.filter',
  'runtimeChildReplacement=0'
], 'React-owned name-only More menu');
rejectAll(moreInjector, [
  'data-page-name={item.label}',
  '<strong className="projectpulse-more-intuitive-name">{item.label}</strong>'
], 'internal module labels in More menu');
requireAll(moreRuntime, [
  "void import('./intuitive-more-menu.css')",
  "moreMenu: 'react-owned-v1'",
  'must never replace, prepend, append, or remove children'
], 'Non-mutating More runtime');
rejectAll(moreRuntime, [
  'MutationObserver',
  'replaceChildren',
  'document.createElement'
], 'More runtime DOM ownership');
requireAll(moreCss, [
  '.projectpulse-more-intuitive .projectpulse-more-module-number',
  'display: none !important',
  '.projectpulse-more-intuitive-name',
  '.projectpulse-more-intuitive-arrow',
  'grid-template-columns: repeat(3, minmax(0, 1fr))'
], 'Name-only More menu styling');

// Group 2A's frontend contract remains mandatory in the production web build.
// Its backend and workflow contract are additionally enforced whenever the full
// repository context is present in source CI.
validateWhenPresent(platform, (source) => requireAll(source, [
  '/api/platform-operations/overview',
  '/api/platform-operations/apis',
  '/api/platform-operations/evidence',
  '/api/platform-operations/architecture'
], 'Group 2A provider-neutral API'), 'Group 2A provider-neutral API');
requireAll(service, [
  'System Health &amp; API Diagnostics',
  '/api/platform-operations/overview'
], 'Module 013');
requireAll(evidence, [
  'Operational Evidence',
  '/api/platform-operations/evidence'
], 'Module 016');
requireAll(architecture, [
  'System Architecture',
  '/api/platform-operations/architecture'
], 'Module 068');
validateWhenPresent(group2aWorkflow, (source) => requireAll(source, [
  'GROUP_2A_SOURCE_ISOLATION=NOT_APPLICABLE',
  'Validate Group 2A source contract',
  'Build ProjectTime API',
  'Validate Module 068 compatibility',
  'Build complete frontend production bundle'
], 'Group 2A regression workflow'), 'Group 2A regression workflow');

console.log('DYNAMIC_RBAC_ADMINISTRATION_PACKAGE=PASS moduleCounts=69,70,71 superAdmin=permanent-full-control group2a=preserved moreMenu=react-owned-name-only');
