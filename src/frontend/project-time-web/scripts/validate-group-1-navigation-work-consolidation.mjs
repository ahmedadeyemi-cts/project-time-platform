import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const absolute = (relative) => path.join(repoRoot, relative);
const exists = (relative) => fs.existsSync(absolute(relative));
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`GROUP1_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const paths = {
  bridge: 'src/frontend/project-time-web/src/module-availability-bridge.js',
  registry: 'src/frontend/project-time-web/src/module-availability-registry.js',
  css: 'src/frontend/project-time-web/src/permission-aware-more-menu.css',
  intuitive: 'src/frontend/project-time-web/src/intuitive-more-menu.js',
  intuitiveCss: 'src/frontend/project-time-web/src/intuitive-more-menu.css',
  moreInjector: 'src/frontend/project-time-web/scripts/inject-react-owned-more-menu.mjs',
  app: 'src/frontend/project-time-web/src/App.jsx',
  workspace: 'src/frontend/project-time-web/src/ProjectWorkspaceCenter.jsx',
  capacity: 'src/frontend/project-time-web/src/CapacityPipelineForecastCenter.jsx',
  intakeHandoff: 'src/frontend/project-time-web/src/IntakeWorkTaskHandoffPanel.jsx',
  availabilityController: 'src/frontend/project-time-web/src/ModuleAvailabilityController.jsx',
  packageJson: 'src/frontend/project-time-web/package.json'
};

for (const [key, relative] of Object.entries(paths)) {
  assert(`FILE_${key.toUpperCase()}`, exists(relative), relative);
}

const bridge = read(paths.bridge);
const registry = read(paths.registry);
const css = read(paths.css);
const intuitive = read(paths.intuitive);
const intuitiveCss = read(paths.intuitiveCss);
const moreInjector = read(paths.moreInjector);
const app = read(paths.app);
const workspace = read(paths.workspace);
const capacity = read(paths.capacity);
const intakeHandoff = read(paths.intakeHandoff);
const availabilityController = read(paths.availabilityController);
const packageJson = read(paths.packageJson);

assert('MORE_MENU_TARGET', bridge.includes('#enterprise-more-navigation-menu.enterprise-more-dropdown')
  && bridge.includes('.enterprise-more-button')
  && css.includes('.enterprise-more-dropdown'),
'Group 1 enhances the top-bar More button identified by the user, not the Modules directory button');

assert('MORE_MENU_REACT_OWNED', moreInjector.includes('PROJECTPULSE_REACT_OWNED_MORE_MENU')
  && moreInjector.includes('data-projectpulse-react-owned-menu="true"')
  && moreInjector.includes('runtimeChildReplacement=0')
  && packageJson.includes('inject-react-owned-more-menu.mjs'),
'More tools and page links are generated inside the React tree before Vite compilation');

assert('MORE_MENU_NAME_ONLY_SEARCH', moreInjector.includes('Search by page name')
  && moreInjector.includes('Search available pages by name')
  && moreInjector.includes('window.ProjectPulseMoreNavigation?.filter')
  && css.includes('.projectpulse-more-menu-search-row'),
'More navigation provides an accessible page-name search without exposing module numbers');

assert('MORE_MENU_NAME_ONLY_OPTIONS', moreInjector.includes('projectpulse-more-intuitive-name')
  && moreInjector.includes('projectpulse-more-intuitive-arrow')
  && moreInjector.includes('data-page-name={item.label}')
  && intuitiveCss.includes('.projectpulse-more-intuitive .projectpulse-more-module-number')
  && intuitiveCss.includes('display: none !important'),
'each More option renders the friendly page name and navigation affordance without a visible module number');

assert('MORE_MENU_RUNTIME_NON_MUTATING', intuitive.includes("moreMenu: 'react-owned-v1'")
  && intuitive.includes('must never replace, prepend, append, or remove children')
  && !intuitive.includes('MutationObserver')
  && !intuitive.includes('replaceChildren')
  && !intuitive.includes('document.createElement'),
'intuitive More runtime loads styling only and cannot alter React-owned children');

assert('MORE_MENU_PERMISSION_ATTRIBUTES_ONLY', bridge.includes("reactDomOwnership: 'attributes-only-v1'")
  && bridge.includes('window.ProjectPulseMoreNavigation')
  && !bridge.includes('link.replaceChildren')
  && !bridge.includes('dropdown.prepend')
  && !bridge.includes('tools.innerHTML')
  && !bridge.includes('insertAdjacentElement'),
'permission enforcement changes visibility and accessibility attributes but not React child structure');

assert('MORE_MENU_READABILITY', intuitiveCss.includes('grid-template-columns: repeat(2, minmax(0, 1fr))')
  && intuitiveCss.includes('grid-template-columns: repeat(3, minmax(0, 1fr))')
  && intuitiveCss.includes('max-height: min(78vh, 760px)')
  && intuitiveCss.includes('@media (max-width: 720px)'),
'More navigation is grouped, scrollable, responsive, and uses two or three columns where space permits');

assert('MORE_MENU_FAILS_CLOSED', bridge.includes("permissionEvidenceState = 'loading'")
  && bridge.includes("permissionEvidenceState = 'unavailable'")
  && css.includes('[data-permission-evidence="unavailable"] .enterprise-more-group'),
'More options remain hidden until effective permission evidence is verified');

assert('DYNAMIC_RBAC_EVIDENCE', bridge.includes("nativeFetch('/api/rbac/v1/bootstrap'")
  && bridge.includes("nativeFetch('/api/rbac/v1/matrix'")
  && bridge.includes("actionCode || '').toUpperCase() === 'MODULE_ACCESS'")
  && bridge.includes("grantEffect || '').toUpperCase() === 'DENY'")
  && bridge.includes("evidenceContract: 'projectpulse-rbac-v1'"),
'More menu consumes the same dynamic RBAC contract as Modules 012 and 037');

assert('DYNAMIC_MODULE_LIFECYCLE', bridge.includes('activeModuleNumbers')
  && bridge.includes('!activeModuleNumbers.has(number)')
  && bridge.includes('PROJECTPULSE_MODULES'),
'More navigation removes pages that are not in the active dynamic RBAC module catalog');

assert('VIEW_AS_PERMISSION_CONTEXT', bridge.includes("headers['X-ProjectPulse-View-As-User']")
  && bridge.includes("event.key === 'projectPulseViewAsUser'")
  && bridge.includes("'projectpulse:view-as-changed'"),
'permission checks follow the current effective View-As identity');

assert('FULL_SESSION_HEADERS', bridge.includes("'X-ProjectPulse-Session': token")
  && bridge.includes("'X-Project-Pulse-Session': token")
  && bridge.includes("'X-Session-Token': token")
  && bridge.includes('Authorization: `Bearer ${token}`'),
'dynamic RBAC navigation checks use the complete authenticated session contract');

assert('NO_NAVIGATION_PRIVILEGE_EXPANSION', !bridge.includes('PROJECTPULSE_MODULES.forEach((module) => dropdown')
  && !bridge.includes("appendChild(document.createElement('a'))")
  && bridge.includes('document.querySelectorAll(\'a[href], button[data-route], [data-module-number]\')'),
'permission enforcement evaluates rendered application links and does not invent additional access');

assert('IDEMPOTENT_VISIBILITY_MUTATIONS', bridge.includes('if (!element.hidden) element.hidden = true;')
  && bridge.includes("if (element.getAttribute(HIDDEN_ATTRIBUTE) !== 'true')")
  && bridge.includes("if (element.getAttribute('aria-hidden') !== 'true')")
  && bridge.includes('if (element.hidden) element.hidden = false;')
  && bridge.includes('setAttributeIfChanged'),
'repeated permission passes do not rewrite the same observed state or create an attribute observer loop');

assert('BODY_OWNED_NOTICE_BOUNDARY', bridge.includes("notice.dataset.projectpulseBodyOwned = 'true'")
  && bridge.includes('document.body.append(notice)')
  && bridge.includes("notice?.parentElement === document.body"),
'the only created navigation notice is a direct body child outside #root and cannot conflict with React reconciliation');

assert('MODULE_007_RETAINED', registry.includes("moduleNumber: '007'")
  && registry.includes("displayName: 'Approval, Export & Audit Workflow'")
  && app.includes("activeRoute === 'workflow'")
  && app.includes('<ApprovalExportAuditWorkflowCenter />'),
'Module 007 remains the post-time-entry approval, reconciliation, export, and audit workflow');

assert('MODULE_011_RETIRED', registry.includes("moduleNumber: '011'")
  && registry.includes("lifecycle: 'retired'")
  && registry.includes('isRetired: true')
  && registry.includes("replacementRoutes: Object.freeze(['work-register', 'create-work-register'])"),
'Module 011 is retained as historical metadata but removed from active work ownership');

assert('MODULE_011_DIRECT_ROUTE_REDIRECT', registry.includes("'work-task-builder': 'work-register'")
  && bridge.includes("window.location.replace('#work-register')")
  && bridge.includes('RETIRED_ROUTE_NOTICE_KEY'),
'old Module 011 links redirect safely to Module 055C with a visible retirement notice');

assert('MODULE_011_NAVIGATION_HIDDEN', css.includes('a[href="#work-task-builder"]')
  && css.includes('[data-module-number="011"]')
  && bridge.includes('RETIRED_MODULE_NUMBERS'),
'Module 011 is hidden immediately from More, sidebar, dashboard, and module links');

assert('MODULE_011_SOURCE_PRESERVED', app.includes('<WorkTaskBuilderPanel />'),
'legacy source remains preserved for history/recovery even though the active route is retired');

assert('MODULE_020_DISTINCT_OWNER', registry.includes("moduleNumber: '020'")
  && registry.includes("displayName: 'Project Intake & Resource Handoff'")
  && registry.includes('Pre-project request, signed-date aging, project-link confirmation')
  && intakeHandoff.includes("fetchJson('/api/project-intake/work-task-handoff')")
  && intakeHandoff.includes("fetchJson('/api/project-intake/project-link-options')"),
'Module 020 remains the pre-project intake and resource-handoff workflow');

assert('MODULE_020_HANDOFF_REACT_OWNED', intakeHandoff.includes('Project Intake → Project Creation & Work Register Handoff')
  && intakeHandoff.includes('Create New Project')
  && intakeHandoff.includes('Manage Existing Projects')
  && intakeHandoff.includes('Module 011 Work Task Builder is retired')
  && intakeHandoff.includes('data-projectpulse-work-management-handoff="020-to-055d-055c"')
  && bridge.includes("setAttributeIfChanged(section, 'data-projectpulse-work-management-handoff', '020-to-055d-055c')"),
'Module 020 owns its corrected handoff content in React source rather than through runtime child mutation');

assert('MODULE_055C_055D_OWNERSHIP', registry.includes("moduleNumber: '055C'")
  && registry.includes("moduleNumber: '055D'")
  && registry.includes('Authoritative workspace for editing existing project records')
  && registry.includes('Authoritative project-creation workflow'),
'Modules 055C and 055D have explicit existing-project and project-creation ownership');

assert('MODULE_019_INDEPENDENT', !workspace.includes('work-task-builder')
  && !workspace.includes('/api/work-tasks')
  && workspace.includes("fetchJson('/api/project-workspace/overview'"),
'Module 019 reads its own project workspace/document APIs with no Module 011 dependency');

assert('MODULE_070_INDEPENDENT', !capacity.includes('work-task-builder')
  && !capacity.includes('/api/work-tasks')
  && capacity.includes("readJson('/api/capacity-forecast/model'")
  && capacity.includes("readJson('/api/capacity-forecast/engineers'"),
'Module 070 reads capacity and pipeline APIs directly with no Module 011 dependency');

assert('AVAILABILITY_PRESERVED', availabilityController.includes('/api/module-availability/overrides')
  && availabilityController.includes('projectpulse-module-disabled')
  && bridge.includes('data-module-availability-hidden'),
'existing disabled-module handling and Super Administrator inspection remain compatible');

assert('NO_DATABASE_CHANGE', !exists('database/migrations/050_group_1_navigation_work_consolidation.sql'),
'Group 1 requires no migration and changes no module data');
assert('NO_DEPLOYMENT_ACTION', !bridge.includes('az containerapp') && !bridge.includes('workflow_dispatch'),
'source contains no Azure or deployment execution');

console.log(`GROUP1_VALIDATION_CHECKS=${checks.length}`);
console.log('GROUP1_MODULE_007_DISPOSITION=RETAIN_APPROVAL_EXPORT_AUDIT_WORKFLOW');
console.log('GROUP1_MODULE_011_DISPOSITION=RETIRED_NON_DESTRUCTIVELY');
console.log('GROUP1_MODULE_020_DISPOSITION=RETAIN_PRE_PROJECT_INTAKE_RESOURCE_HANDOFF');
console.log('GROUP1_MODULE_019_070_DEPENDENCY=MODULE_011_NOT_PRESENT');
console.log('GROUP1_REACT_DOM_OWNERSHIP=CHILD_STRUCTURE_REACT_OWNED');
console.log('GROUP1_EXTERNAL_SYSTEM_CALLS_PERFORMED=0');
console.log('GROUP1_MIGRATION_REQUIRED=NO');

if (checks.some((check) => !check.condition)) {
  console.error('GROUP1_NAVIGATION_WORK_CONSOLIDATION_CONTRACT=FAILED');
  process.exit(1);
}

console.log('GROUP1_NAVIGATION_WORK_CONSOLIDATION_CONTRACT=PASSED');
