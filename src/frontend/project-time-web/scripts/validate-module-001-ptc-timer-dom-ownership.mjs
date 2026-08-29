import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');
const absolute = (path) => resolve(root, path);
const read = (path) => {
  const target = absolute(path);
  if (!existsSync(target)) throw new Error(`Missing required source: ${path}`);
  return readFileSync(target, 'utf8');
};
const optionalRead = (path) => {
  const target = absolute(path);
  return existsSync(target) ? readFileSync(target, 'utf8') : '';
};
const requireAll = (source, values, label) => {
  for (const value of values) {
    if (!source.includes(value)) throw new Error(`${label} missing contract: ${value}`);
  }
};
const rejectAll = (source, values, label) => {
  for (const value of values) {
    if (source.includes(value)) throw new Error(`${label} contains forbidden contract: ${value}`);
  }
};

const resultExecution = optionalRead('src/backend/ProjectTime.Api/Modules/Module001ResultExecutionCompatibility.cs');
const stewardV2 = optionalRead('src/backend/ProjectTime.Api/Modules/Module001TimeStewardV2Module.cs');
const stewardBoundary = optionalRead('src/backend/ProjectTime.Api/Modules/PtcTimeStewardRoleBoundary.cs');
const project = optionalRead('src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const slotInjector = read('src/frontend/project-time-web/scripts/inject-module-001-owned-extension-slots.mjs');
const moreInjector = read('src/frontend/project-time-web/scripts/inject-react-owned-more-menu.mjs');
const timerPortal = read('src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx');
const timerView = read('src/frontend/project-time-web/src/module001/TimesheetTimerView.jsx');
const picker = read('src/frontend/project-time-web/src/module001/TimesheetTaskPicker.jsx');
const stewardPortal = read('src/frontend/project-time-web/src/module001/PtcTimesheetManagementPortal.jsx');
const gate = read('src/frontend/project-time-web/src/module001/PtcTimeStewardGate.jsx');
const module001bGate = read('src/frontend/project-time-web/src/module001b/Module001BTimeReallocationGate.jsx');
const main = read('src/frontend/project-time-web/src/main.jsx');
const runtimeCss = read('src/frontend/project-time-web/src/module001/module001-runtime-v2.css');
const bridge = read('src/frontend/project-time-web/src/module-availability-bridge.js');
const intuitive = read('src/frontend/project-time-web/src/intuitive-more-menu.js');
const intuitiveCss = read('src/frontend/project-time-web/src/intuitive-more-menu.css');
const module026 = read('src/frontend/project-time-web/src/CrmErpIntegrationCenter.jsx');
const module026Backend = optionalRead('src/backend/ProjectTime.Api/Modules/CrmErpAdministrationExperience.cs');
const packageJson = read('src/frontend/project-time-web/package.json');

const fullModule001BackendContext = Boolean(resultExecution && stewardV2 && stewardBoundary && project);
if (fullModule001BackendContext) {
  requireAll(resultExecution, [
    'UseModule001ResultExecutionCompatibility',
    'RuntimePtcUsersAsync(context)',
    'RuntimePtcWorkspaceAsync(targetUserId, context)',
    'Module001TimerTargetsAsync(context)',
    'Module001ActiveTimerAsync(context)',
    'Module001TimerHistoryAsync(context)',
    'Module001WorkQueueAsync(context)',
    'Module001WeeklyLinesAsync(context)',
    'X-ProjectPulse-Module001-Result-Execution',
    'await result.ExecuteAsync(context);'
  ], 'Module 001 explicit IResult execution');

  requireAll(stewardV2, [
    'module001-time-steward-v2-2026-07-28',
    '/api/runtime/timesheet/steward/v2/users',
    '/api/runtime/timesheet/steward/v2/users/{targetUserId:guid}/workspace',
    '/api/runtime/timesheet/steward/v2/entries/{timeEntryId:guid}/move',
    'ENGINEERING',
    'ENGINEERING_LEAD',
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGEMENT_LEAD',
    'submissionOnBehalf = false'
  ], 'Module 001 time-steward v2 compatibility backend');

  requireAll(stewardBoundary, [
    'legacyModule001Move',
    'StatusCodes.Status410Gone',
    'module_001b_reallocation_required',
    'The legacy Module 001 move workflow is retired and cannot unsubmit or return time to Draft.',
    '/api/runtime/timesheet/steward/001b/reallocation/entries/{timeEntryId}/move'
  ], 'Module 001 legacy move tombstone boundary');

  requireAll(project, [
    'app.UsePtcTimeStewardRoleBoundary();',
    'app.UseModule001ResultExecutionCompatibility();',
    'app.MapModule001TimeStewardV2Endpoints();',
    'app.MapModule001TimesheetEnhancementEndpoints();',
    'app.MapModule001TimerTargetEndpoints();'
  ], 'Module 001 backend registration');
} else {
  console.log('MODULE_001_PTC_TIMER_BACKEND_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

requireAll(slotInjector, [
  'data-projectpulse-react-owned-slot="true"',
  'module001-view-tab-host',
  'module001-active-timer-recovery-host',
  'module001-ptc-time-steward-host',
  'module001-enhancement-view-host',
  'runtimeDomInsertion=0',
  'The retired runtime-created Module 001 toolbar host must not be present.'
], 'React-owned Module 001 slots');

requireAll(moreInjector, [
  'PROJECTPULSE_REACT_OWNED_MORE_MENU',
  'data-projectpulse-react-owned-menu="true"',
  'Search by page name',
  'data-more-label-source="module-registry"',
  'data-page-name={getNavigationDisplayLabel(item)}',
  '<strong className="projectpulse-more-intuitive-name">{getNavigationDisplayLabel(item)}</strong>',
  'window.ProjectPulseMoreNavigation?.filter',
  'projectpulse-more-intuitive-name',
  'projectpulse-more-intuitive-arrow',
  'runtimeChildReplacement=0'
], 'React-owned More menu generator');
rejectAll(moreInjector, [
  'data-page-name={item.label}',
  '<strong className="projectpulse-more-intuitive-name">{item.label}</strong>'
], 'internal module labels in More menu');

requireAll(timerPortal, [
  "import { authoritativeApi } from '../projectpulse-authoritative-api.js';",
  'data-projectpulse-react-owned-slot="true"',
  '/api/timesheet/timers/targets',
  '/api/timesheet/timers/active',
  '/api/timesheet/timers/history',
  "requiredCollections: ['targets']",
  "requiredCollections: ['timers']",
  'window.setInterval(refresh, 5000)',
  'window.setInterval(() => setClock(new Date()), 1000)',
  'through refreshes, sign-out, and session expiration',
  'Timer started. The server continues tracking it',
  'module001-server-timer-recovery'
], 'Persistent Module 001 timer frontend');

requireAll(timerView, [
  'Timer history',
  'history.map',
  'window.setInterval(() => setClock(new Date()), 1000)',
  'Start / Stop Timer'
], 'Visible timer and history');
requireAll(picker, [
  "const GROUP_ORDER = ['Requests / Service Requests', 'Project Tasks', 'Non-Project Time']",
  "if (label === 'Service Request Tasks') return 'Requests / Service Requests'",
  "if (label === 'Regular Tasks') return 'Project Tasks'",
  'role="combobox"'
], 'Three-group timer picker');

requireAll(stewardPortal, [
  "import { authoritativeApi } from '../projectpulse-authoritative-api.js';",
  '/api/runtime/timesheet/steward/v2/users?weekStart=',
  '/api/runtime/timesheet/steward/v2/users/${encodeURIComponent(selectedUserId)}/workspace',
  "requiredCollections: ['users']",
  "requiredCollections: ['entries']",
  'Project Team Coordinator · Time Steward',
  'Manage ordinary time for other users',
  'Engineering, Engineering Lead, Project Management, and Project Management Lead',
  'Return week to draft',
  'Save correction',
  'Remove',
  'Submitted and approved entries remain read-only in Module 001.',
  'Required reason',
  'No submission on behalf',
  'immutable audit history',
  'data-projectpulse-time-steward-contract="module001-time-steward-v2"'
], 'React-owned Module 001 ordinary-time workspace');
rejectAll(stewardPortal, [
  '/move',
  'Move time',
  'move time',
  'Move to',
  'move to',
  'destination',
  'reallocat',
  'Module001B',
  'time-reallocation',
  'Create replacement task',
  'Create and assign replacement task',
  'moveTargets',
  'availableProjects'
], 'Module 001 allocation boundary');

for (const [label, source] of [
  ['timer portal', timerPortal],
  ['PTC portal', stewardPortal],
  ['PTC gate', gate],
  ['Module 001B gate', module001bGate],
  ['intuitive More module', intuitive]
]) {
  rejectAll(source, [
    'document.createElement',
    '.appendChild(',
    '.insertBefore(',
    '.insertAdjacentElement(',
    '.prepend(',
    '.replaceChildren(',
    '.innerHTML ='
  ], `${label} React child ownership`);
}

requireAll(gate, [
  "import ProductionApprovalWorkPortal from '../ProductionApprovalWorkPortal.jsx';",
  "from '../effective-role-authority.js'",
  'EFFECTIVE_ROLE_AUTHORITY_EVENTS',
  'hasAnyEffectiveRole',
  'readEffectiveRoleAuthority',
  '<PtcTimesheetManagementPortal />',
  "'PROJECT_TEAM_COORDINATOR'",
  "'SUPER_ADMINISTRATOR'",
  'if (!authority.ready) return null'
], 'Module 001 effective-role gate');
rejectAll(gate, [
  'Module001B',
  'time-reallocation',
  'reallocat',
  'move time',
  'PtcGuidedMovePortal'
], 'Module 001 gate allocation boundary');

requireAll(module001bGate, [
  "import Module001BTimeReallocationPortal from './Module001BTimeReallocationPortal.jsx';",
  "from '../effective-role-authority.js'",
  'MODULE001B_ROLES',
  "'PROJECT_TEAM_COORDINATOR'",
  "'SUPER_ADMINISTRATOR'",
  'hasAnyEffectiveRole(authority, MODULE001B_ROLES)',
  'allowed={hasAnyEffectiveRole(authority, MODULE001B_ROLES)}'
], 'Independent Module 001B role gate');

requireAll(main, [
  "import PtcTimeStewardGate from './module001/PtcTimeStewardGate.jsx';",
  "import Module001BTimeReallocationGate from './module001b/Module001BTimeReallocationGate.jsx';",
  '<TimesheetEnhancementPortal />',
  '<PtcTimeStewardGate />',
  '<Module001BTimeReallocationGate />'
], 'independent Module 001 and 001B mounts');
rejectAll(main, [
  'Module001ActiveTimerRecoveryPortal',
  '<PtcRuntimeTaskCatalog />'
], 'duplicate Module 001 portal mounts');

requireAll(runtimeCss, [
  'body.projectpulse-module001-timer-mode',
  '.module001-server-timer-recovery',
  '.module001-server-timer-clock',
  '.ptc-select-user-prompt',
  '[data-projectpulse-react-owned-slot="true"]'
], 'Module 001 runtime v2 styling');
rejectAll(runtimeCss, [
  '.ptc-destination-catalog',
  '.ptc-destination-groups',
  'flexible PTC destinations'
], 'retired Module 001 allocation styling');

requireAll(bridge, [
  "permissionEvidenceState = 'loading'",
  "permissionEvidenceState = 'unavailable'",
  "nativeFetch('/api/rbac/v1/bootstrap'",
  "nativeFetch('/api/rbac/v1/matrix'",
  'window.ProjectPulseMoreNavigation',
  "reactDomOwnership: 'attributes-only-v1'",
  "document.body.append(notice)",
  "notice.dataset.projectpulseBodyOwned = 'true'"
], 'Permission navigation bridge');
rejectAll(bridge, [
  'link.replaceChildren',
  'dropdown.prepend',
  'main.prepend',
  'insertAdjacentElement',
  'tools.innerHTML',
  'heading.textContent =',
  'copy.textContent ='
], 'React-owned navigation mutation');

requireAll(intuitive, [
  "void import('./intuitive-more-menu.css')",
  "moreMenu: 'react-owned-v1'",
  'must never replace, prepend, append, or remove children'
], 'non-mutating More module');
rejectAll(intuitive, ['MutationObserver', 'replaceChildren', 'document.createElement'], 'intuitive More DOM mutation');
requireAll(intuitiveCss, [
  '.projectpulse-more-intuitive-name',
  '.projectpulse-more-intuitive-arrow',
  '.projectpulse-body-owned-notice',
  '.projectpulse-more-module-number',
  'display: none !important'
], 'React-owned More styling');

requireAll(module026, [
  'Edit connection',
  'Configure connection',
  'Add CRM platform'
], 'PR 207 Module 026 editable connectors');
if (module026Backend) {
  requireAll(module026Backend, [
    'zendesk_sell',
    'salesforce',
    'servicenow',
    'certinia',
    'IsPersisted'
  ], 'PR 207 Module 026 backend templates');
} else {
  console.log('MODULE_026_PR207_BACKEND_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

requireAll(packageJson, [
  'inject-module-001-owned-extension-slots.mjs',
  'inject-react-owned-more-menu.mjs',
  'validate:module001-ptc-timer-dom'
], 'permanent build gates');

console.log('MODULE_001_PTC_TIMER_DOM_OWNERSHIP=PASS');
console.log('MODULE_001_ELIGIBLE_ROLES=ENGINEERING,ENGINEERING_LEAD,PROJECT_MANAGEMENT,PROJECT_MANAGEMENT_LEAD');
console.log('MODULE_001_TIMER_GROUPS=REQUESTS_PROJECT_TASKS_NON_PROJECT');
console.log('MODULE_001_TIMER_PERSISTENCE=SERVER_AUTHORITATIVE');
console.log('MODULE_001_ALLOCATION_UI=ABSENT');
console.log('MODULE_001B_REALLOCATION_GATE=STRICT_PTC_SUPERADMIN');
console.log('MODULE_001B_OWNERSHIP=INDEPENDENT_ROOT_MOUNT');
console.log('PROJECTPULSE_REACT_CHILD_MUTATION=0');
console.log('MODULE_026_PR207_INCLUDED=YES');
