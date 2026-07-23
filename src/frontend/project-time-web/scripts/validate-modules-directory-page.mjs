import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');

async function text(path) {
  return readFile(resolve(root, path), 'utf8');
}

function requireText(source, values, label) {
  for (const value of values) {
    if (!source.includes(value)) {
      throw new Error(`${label} is missing required contract: ${value}`);
    }
  }
}

const paths = {
  main: 'src/frontend/project-time-web/src/main.jsx',
  portal: 'src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx',
  css: 'src/frontend/project-time-web/src/modules-directory-page.css',
  packageJson: 'src/frontend/project-time-web/package.json'
};

const [main, portal, css, packageJson] = await Promise.all(Object.values(paths).map(text));

requireText(main, [
  "import ModulesDirectoryPortal from './ModulesDirectoryPortal.jsx';",
  '<ModulesDirectoryPortal />',
  '<App />'
], 'Application root integration');

if (main.indexOf('<ModulesDirectoryPortal />') < main.indexOf('<App />')) {
  throw new Error('The Modules directory portal must render after App so the authorized navigation model already exists.');
}

requireText(portal, [
  "const MODULES_ROUTE = 'modules';",
  "const MODULES_HASH = '#modules';",
  'const CANONICAL_MODULE_NUMBER_BY_ROUTE = Object.freeze({',
  'function moduleNumberForRoute(route, source)',
  'CANONICAL_MODULE_NUMBER_BY_ROUTE[route]',
  'moduleNumber: moduleNumberForRoute(route, moduleNumberSource)',
  'module.moduleNumber ? `Module ${module.moduleNumber}`',
  'Search by module number, name, route, or category',
  "navigation.querySelector('#projectpulse-modules-navigation-link')",
  "candidate.getAttribute('href') === '#dashboard'",
  "dashboardLink.insertAdjacentElement('afterend', link)",
  "document.querySelectorAll('.enterprise-sidebar-section')",
  "cleanText(section.querySelector('.enterprise-sidebar-section-title')?.textContent).toLowerCase() === 'pinned'",
  "pinnedSection?.querySelectorAll('.enterprise-sidebar-links:not(.nested) > a[href^=\"#\"]')",
  "document.querySelectorAll('.enterprise-sidebar-group')",
  "document.querySelectorAll('.enterprise-sidebar-group-toggle')",
  "groupElement.querySelectorAll('.enterprise-sidebar-links.nested a[href^=\"#\"]')",
  "route === 'dashboard' || route === MODULES_ROUTE",
  "document.querySelector('main.app-shell.enterprise-nav-enabled')",
  'mutationOriginatesInsidePortal',
  'mutations.every(mutationOriginatesInsidePortal)',
  'moduleListsMatch(current, nextModules)',
  "window.addEventListener('projectpulse:view-as-changed', refresh)",
  "window.addEventListener('hashchange', handleHashChange)",
  'restoreNavigationGroups(expandedForDirectory.current)',
  'Search modules',
  'All categories',
  'Open module →',
  'createPortal('
], 'Role-aware Modules directory');

const canonicalRouteNumbers = {
  timesheet: '001',
  'manager-approval': '002',
  utilization: '003',
  'holiday-admin': '004',
  'project-allocation-info': '005',
  'psa-modules': '006',
  workflow: '007',
  'audit-history': '008',
  'user-admin': '009',
  'azure-admin': '010',
  'work-task-builder': '011',
  'role-admin': '012',
  'service-control': '013',
  'backup-dr': '014',
  'restore-validation': '015',
  'backup-retention': '016',
  'replication-sync': '017',
  'project-workload': '018',
  'project-workspace': '019',
  'project-intake': '020',
  'customer-directory': '021',
  'cost-alerts': '022',
  'time-compliance': '023',
  'sales-intake': '024',
  'sow-generator': '025',
  'crm-integration': '026',
  'signed-handoff': '027',
  'ai-time-entry': '028',
  'uat-validation': '029',
  reporting: '030',
  'sales-insights': '036',
  'roles-permissions-matrix': '037',
  'certify-integration': '038',
  'billing-readiness': '039',
  'project-closeout': '040',
  'closeout-email': '041',
  'invoice-billing-center': '042',
  'rate-card-administration': '055B',
  'work-register': '055C',
  'create-work-register': '055D',
  'calendar-capacity': '057',
  'cicd-pipeline': '058',
  contracts: '060',
  opportunities: '063',
  'ai-provider-configuration': '064',
  'entra-secret-administration': '065',
  'project-flowhive': '066',
  'global-mail-configuration': '067',
  'system-architecture': '068',
  'qualifications-certifications': '069',
  'capacity-pipeline-forecast': '070',
  'oncall-scheduling': '071',
  'oneassist-routing-directory': '072',
  'sales-coverage-alignment': '073',
  'oem-vendor-directory': '074',
  'integration-event-gateway': '075',
  'defect-tracker': '076',
  'release-deployment-control': '077',
  'observability-slo-health': '078',
  'data-governance-retention': '079',
  'customer-delivery-acceptance': '080',
  'security-operations': '997',
  'system-diagnostics': '998',
  'user-guide': '999'
};

for (const [route, moduleNumber] of Object.entries(canonicalRouteNumbers)) {
  const quotedEntry = `'${route}': '${moduleNumber}'`;
  const bareEntry = /^[A-Za-z_$][\w$]*$/.test(route)
    ? `${route}: '${moduleNumber}'`
    : '';

  if (!portal.includes(quotedEntry) && (!bareEntry || !portal.includes(bareEntry))) {
    throw new Error(`Modules directory is missing canonical number ${moduleNumber} for route ${route}.`);
  }
}

if (portal.includes('getInstalledProjectPulseModuleRegistry') || /const\s+modules\s*=\s*\[\s*\{/.test(portal)) {
  throw new Error('The Modules page must reuse authorized navigation output instead of duplicating the module registry.');
}

if (portal.includes('observer.observe(document.body')) {
  throw new Error('The Modules observer must not watch the whole body and retrigger itself from portal card rendering.');
}

if (portal.includes("module.moduleNumber ? `Module ${module.moduleNumber}` : module.group")) {
  throw new Error('A missing module number must never silently display the category as if it were the module identifier.');
}

requireText(css, [
  '#projectpulse-modules-navigation-link',
  'main.app-shell.route-modules > *:not(.top-bar):not(.enterprise-sidebar):not(#modules-directory-portal-host)',
  'main.app-shell.route-modules > #modules-directory-portal-host',
  '.modules-directory-page',
  '.modules-directory-grid',
  '.modules-directory-card'
], 'Modules page route isolation and styling');

requireText(packageJson, [
  'validate:modules-directory',
  'node ./scripts/validate-modules-directory-page.mjs',
  'npm run validate:modules-directory'
], 'Production build wiring');

console.log('Persistent Dashboard and Modules navigation, role-aware directory, canonical module numbers, filtering, shell lifecycle, and route-isolation contracts passed.');
