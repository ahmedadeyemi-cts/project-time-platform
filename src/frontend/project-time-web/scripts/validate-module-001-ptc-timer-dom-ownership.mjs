import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');
const read = (path) => {
  const absolute = resolve(root, path);
  if (!existsSync(absolute)) throw new Error(`Missing required source: ${path}`);
  return readFileSync(absolute, 'utf8');
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

const resultExecution = read('src/backend/ProjectTime.Api/Modules/Module001ResultExecutionCompatibility.cs');
const stewardV2 = read('src/backend/ProjectTime.Api/Modules/Module001TimeStewardV2Module.cs');
const project = read('src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const slotInjector = read('src/frontend/project-time-web/scripts/inject-module-001-owned-extension-slots.mjs');
const moreInjector = read('src/frontend/project-time-web/scripts/inject-react-owned-more-menu.mjs');
const timerPortal = read('src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx');
const timerView = read('src/frontend/project-time-web/src/module001/TimesheetTimerView.jsx');
const picker = read('src/frontend/project-time-web/src/module001/TimesheetTaskPicker.jsx');
const stewardPortal = read('src/frontend/project-time-web/src/module001/PtcTimesheetManagementPortal.jsx');
const gate = read('src/frontend/project-time-web/src/module001/PtcTimeStewardGate.jsx');
const main = read('src/frontend/project-time-web/src/main.jsx');
const runtimeCss = read('src/frontend/project-time-web/src/module001/module001-runtime-v2.css');
const bridge = read('src/frontend/project-time-web/src/module-availability-bridge.js');
const intuitive = read('src/frontend/project-time-web/src/intuitive-more-menu.js');
const intuitiveCss = read('src/frontend/project-time-web/src/intuitive-more-menu.css');
const module026 = read('src/frontend/project-time-web/src/CrmErpIntegrationCenter.jsx');
const module026Backend = read('src/backend/ProjectTime.Api/Modules/CrmErpAdministrationExperience.cs');
const packageJson = read('src/frontend/project-time-web/package.json');

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
  'Requests / Service Requests',
  'Project Tasks',
  'Non-Project Time',
  'canAssignExistingProjectTaskDuringMove = true',
  'canMoveToNonProjectTime = true',
  'TIME_TASK_ASSIGN',
  'Module001EnsurePtcAssignmentV2Async',
  'non_project_time_category_id = @category_id',
  "association_source = 'PTC_TIME_STEWARD'",
  'crossActivityTypeMove = true',
  'submissionOnBehalf = false'
], 'Module 001 time-steward v2 backend');

requireAll(project, [
  'app.UseModule001ResultExecutionCompatibility();',
  'app.MapModule001TimeStewardV2Endpoints();',
  'app.MapModule001TimesheetEnhancementEndpoints();',
  'app.MapModule001TimerTargetEndpoints();'
], 'Module 001 backend registration');

requireAll(slotInjector, [
  'data-projectpulse-react-owned-slot="true"',
  'module001-view-tab-host',
  'module001-active-timer-recovery-host',
  'module001-ptc-time-steward-host',
  'module001-enhancement-view-host',
  'runtimeDomInsertion=0'
], 'React-owned Module 001 slots');
rejectAll(slotInjector, ['module001-toolbar-host'], 'retired Module 001 toolbar portal');

requireAll(moreInjector, [
  'PROJECTPULSE_REACT_OWNED_MORE_MENU',
  'data-projectpulse-react-owned-menu="true"',
  'Search by page name',
  'window.ProjectPulseMoreNavigation?.filter',
  'projectpulse-more-intuitive-name',
  'projectpulse-more-intuitive-arrow',
  'runtimeChildReplacement=0'
], 'React-owned More menu generator');

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
  '/api/runtime/timesheet/steward/v2/entries/${entry.timeEntryId}/move',
  "requiredCollections: ['users']",
  "requiredCollections: ['entries', 'moveTargets', 'nonProjectCategories', 'availableProjects']",
  'Engineering, Engineering Lead, Project Management, and Project Management Lead',
  'Move time across all supported activity types',
  'assignment will be created',
  '<optgroup key={group.name} label={group.name}>',
  'Create replacement task',
  'No submission on behalf',
  'immutable audit history'
], 'React-owned PTC workspace');

for (const [label, source] of [
  ['timer portal', timerPortal],
  ['PTC portal', stewardPortal],
  ['PTC gate', gate],
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
  '<PtcTimesheetManagementPortal />',
  "'PROJECT_TEAM_COORDINATOR'",
  "'SUPER_ADMINISTRATOR'",
  'if (state.active && !state.allowed) return null'
], 'PTC effective-role gate');
rejectAll(gate, ['PtcRuntimeTaskCatalog'], 'retired nested PTC portal');

requireAll(main, [
  '<TimesheetEnhancementPortal />',
  '<PtcTimeStewardGate />'
], 'single Module 001 portal mounts');
rejectAll(main, [
  'Module001ActiveTimerRecoveryPortal',
  '<PtcRuntimeTaskCatalog />'
], 'duplicate Module 001 portal mounts');

requireAll(runtimeCss, [
  'body.projectpulse-module001-timer-mode',
  '.module001-server-timer-recovery',
  '.module001-server-timer-clock',
  '.ptc-destination-catalog',
  '.ptc-destination-groups',
  '[data-projectpulse-react-owned-slot="true"]'
], 'Module 001 runtime v2 styling');

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
requireAll(module026Backend, [
  'zendesk_sell',
  'salesforce',
  'servicenow',
  'certinia',
  'isPersisted'
], 'PR 207 Module 026 backend templates');

requireAll(packageJson, [
  'inject-module-001-owned-extension-slots.mjs',
  'inject-react-owned-more-menu.mjs',
  'validate:module001-ptc-timer-dom'
], 'permanent build gates');

console.log('MODULE_001_PTC_TIMER_DOM_OWNERSHIP=PASS');
console.log('MODULE_001_ELIGIBLE_ROLES=ENGINEERING,ENGINEERING_LEAD,PROJECT_MANAGEMENT,PROJECT_MANAGEMENT_LEAD');
console.log('MODULE_001_TIMER_GROUPS=REQUESTS_PROJECT_TASKS_NON_PROJECT');
console.log('MODULE_001_TIMER_PERSISTENCE=SERVER_AUTHORITATIVE');
console.log('PROJECTPULSE_REACT_CHILD_MUTATION=0');
console.log('MODULE_026_PR207_INCLUDED=YES');
