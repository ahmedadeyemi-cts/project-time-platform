import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const repoRoot = path.resolve(webRoot, '..', '..', '..');
const read = (relative) => fs.readFileSync(path.join(repoRoot, relative), 'utf8');
const requireText = (source, value, label) => assert.ok(
  source.includes(value),
  `${label}: missing ${value}`
);
const rejectText = (source, value, label) => assert.ok(
  !source.includes(value),
  `${label}: forbidden ${value}`
);

const portal = read('src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx');
const timerView = read('src/frontend/project-time-web/src/module001/TimesheetTimerView.jsx');
const picker = read('src/frontend/project-time-web/src/module001/TimesheetTaskPicker.jsx');
const assistant = read('src/frontend/project-time-web/src/module001/TimesheetAiDescriptionAssistant.jsx');
const durationSource = read('src/frontend/project-time-web/src/module001/timesheet-duration.js');
const multiTimerCss = read('src/frontend/project-time-web/src/module001/module001-multi-timer.css');
const injector = read('src/frontend/project-time-web/scripts/inject-module-001-owned-extension-slots.mjs');
const backend = [
  'src/backend/ProjectTime.Api/Modules/Module001MultiTimerModule.cs',
  'src/backend/ProjectTime.Api/Modules/Module001MultiTimerFinalization.cs',
  'src/backend/ProjectTime.Api/Modules/Module001MultiTimerStart.cs',
  'src/backend/ProjectTime.Api/Modules/Module001MultiTimerStop.cs'
].map(read).join('\n');
const project = read('src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const migration = read('database/migrations/057_module_001_multi_timer_document_grounded_ai.sql');
const rollback = read('database/rollback/057_module_001_multi_timer_document_grounded_ai_rollback.sql');
const migrationTest = read('tests/test-module001-multi-timer-migration-057.sh');
const timeSuggestionService = read('src/backend/ProjectTime.Api/ProjectPulseAiTimeEntrySuggestionService.cs');
const privateRagService = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs');
const groundingService = read('src/backend/ProjectTime.Api/Ai/PulseAiDocumentGroundingService.cs');

for (const contract of [
  '/api/timesheet/timers/active-set',
  '/api/timesheet/timers/history-v2',
  '/api/timesheet/timers/start-batch',
  '/api/timesheet/timers/v2/${timer.timerSessionId}/stop',
  '/api/timesheet/timers/v2/stop-all',
  '/api/timesheet/timers/v2/${timer.timerSessionId}/discard',
  "requiredCollections: ['activeTimers', 'autoStoppedTimers']",
  "requiredCollections: ['timers']",
  "requiredCollections: ['targets']",
  'window.setInterval(refresh, 5000)',
  'window.setInterval(() => setClock(new Date()), 1000)',
  'Timer started. The server continues tracking it through refreshes, sign-out, and session expiration.',
  'No partial stop was committed.'
]) {
  requireText(portal, contract, 'multi-timer portal');
}
requireText(portal, "const MOBILE_KEY = 'projectPulseModule001MobileMode'", 'persistent mobile preference');
requireText(portal, 'module001-toolbar-host', 'static mobile toolbar slot');
requireText(portal, '<span>Mobile mode</span>', 'restored Mobile mode checkbox');
rejectText(portal, 'document.createElement', 'React-owned timer portal');
rejectText(portal, '.appendChild(', 'React-owned timer portal');
rejectText(portal, '.innerHTML =', 'React-owned timer portal');

for (const contract of [
  'Run up to five authorized activity timers at once.',
  '24-hour safety cap',
  'Stop all timers',
  'Stop this timer',
  'All selected timers will begin from the same server timestamp.',
  '<TimesheetAiDescriptionAssistant',
  'Timer history',
  'history.map',
  'window.setInterval(() => setClock(new Date()), 1000)'
]) {
  requireText(timerView, contract, 'multi-timer view');
}

for (const contract of [
  "const GROUP_ORDER = ['Requests / Service Requests', 'Project Tasks', 'Non-Project Time']",
  'role="combobox"',
  'aria-multiselectable="true"',
  'type="checkbox"',
  'selectedValues.length',
  'You can select up to',
  'Search activity, task, project, customer, or request',
  'Running'
]) {
  requireText(picker, contract, 'searchable checkbox picker');
}
rejectText(picker, '<select', 'legacy timer task selector');

for (const contract of [
  '/api/timesheets/ai-description-suggestions',
  'service_request',
  'SOW, GSD, design or architecture files, orders, proposals or quotes, and supporting documents',
  'Restricted documents remain permission scoped.',
  'Generate AI suggestion',
  'For accurate project-document grounding',
  'Type a short rough work note first'
]) {
  requireText(assistant, contract, 'document-grounded AI assistant');
}
requireText(timeSuggestionService, '_privateRag.GenerateTimesheetAsync', 'router-owned private RAG callback for Timesheet AI');
requireText(timeSuggestionService, '_grounding.BuildTimesheetContextAsync', 'document grounding fallback');
requireText(
  timeSuggestionService,
  'ExternalCapsulePurpose: CelarAiExternalCapsuleCatalog.TimesheetCustomerDescription',
  'central closed Timesheet capsule purpose'
);
requireText(timeSuggestionService, 'ExternalFactCodes: externalFactCodes', 'closed backend fact-code handoff');
requireText(timeSuggestionService, 'BuildPurposeBuiltExternalFactCodes(request)', 'private document isolation');
requireText(timeSuggestionService, 'no private document text was sent to Claude or OpenAI', 'private provider boundary');
requireText(privateRagService, 'SOW, GSD, task, request, and project documents may improve terminology and scope alignment', 'private Timesheet prompt grounding');
for (const category of [
  'sow',
  'statement_of_work',
  'gsd',
  'global_solution_design',
  'architecture',
  'design',
  'order',
  'quote',
  'proposal'
]) {
  requireText(groundingService, `'${category}'`, `project document category ${category}`);
}
requireText(groundingService, 'ELSE 90', 'other supporting project documents');
requireText(groundingService, 'engineering_resource_requests', 'service-request project resolution');
requireText(groundingService, 'ai_timesheet_context_enabled', 'Timesheet document eligibility');

for (const contract of [
  'Module001MultiTimerMaximumActive = 5',
  'Module001MultiTimerCapSeconds = 86_400',
  'Module001MultiTimerMaximumRoundedMinutes = 1_440',
  'MapModule001MultiTimerEndpoints',
  '/api/timesheet/timers/active-set',
  '/api/timesheet/timers/history-v2',
  '/api/timesheet/timers/start-batch',
  '/api/timesheet/timers/v2/stop-all',
  'AcquireModule001MultiTimerUserLockAsync',
  'SetEquals(requestedIds)',
  'atomic = true',
  'No timer was stopped.',
  'Automatically stopped at the 24-hour maximum.'
]) {
  requireText(backend, contract, 'server-authoritative multi-timer backend');
}
requireText(project, 'app.MapModule001MultiTimerEndpoints();', 'API route registration');

for (const contract of [
  "'057_module_001_multi_timer_document_grounded_ai'",
  'actual_elapsed_seconds BETWEEN 0 AND 86400',
  'rounded_minutes BETWEEN 0 AND 1440',
  'ux_module001_running_assignment',
  'ux_module001_running_non_project',
  'A maximum of five running timers is allowed per user.',
  'trg_module001_057_running_timer_limit',
  'ai_timesheet_context_enabled = TRUE',
  'project_ai_generation_grounding',
  'PROJECT_AI_CONTEXT_AUTO_QUEUED',
  "WHEN 'sow' THEN 100",
  "WHEN 'gsd' THEN 95",
  'rawDocumentSentToExternalProvider',
  'permissionScopedRetrieval'
]) {
  requireText(migration, contract, 'migration 057');
}
requireText(rollback, 'Migration 057 rollback blocked', 'fail-closed rollback');
requireText(rollback, 'HAVING COUNT(*) > 1', 'multi-timer rollback guard');
requireText(migrationTest, 'MODULE001_057_POSTGRES_TEST=PASS', 'PostgreSQL migration test');
requireText(migrationTest, 'five_running_timers_allowed', 'five-timer database test');
requireText(migrationTest, 'visible_document_ai_context_enabled', 'document policy database test');

for (const contract of [
  'module001-toolbar-host',
  'data-projectpulse-react-owned-slot="true"',
  'runtimeDomInsertion=0',
  'timerPolicy=5x24h',
  'mobileToggle=restored',
  'caps each timer at 24 hours'
]) {
  requireText(injector, contract, 'React-owned slot and guide injector');
}

for (const contract of [
  '.module001-task-results',
  '.module001-task-selection-chip',
  '.module001-active-timer-grid',
  '.module001-stop-all',
  '.module001-ai-description-assistant',
  '.module001-mobile-toggle',
  '#timesheet .details-modal',
  '#timesheet .modal-title-row',
  '#timesheet .modal-actions',
  '#timesheet .modal-close-button',
  '#timesheet.module001-mobile-mode .details-modal'
]) {
  requireText(multiTimerCss, contract, 'multi-timer/mobile/modal CSS');
}
requireText(multiTimerCss, 'grid-template-columns:minmax(0,1fr) auto;', 'contained modal title/actions layout');
requireText(multiTimerCss, 'min-height:48px;', 'mobile touch targets');

requireText(durationSource, 'MAX_TIMER_SECONDS = 24 * 60 * 60', '24-hour frontend cap');
rejectText(durationSource, 'MAX_TIMER_SECONDS = 12 * 60 * 60', 'retired frontend cap');
const duration = await import(`${pathToFileURL(path.join(webRoot, 'src/module001/timesheet-duration.js')).href}?module001=057`);
const roundingCases = [
  [1, 15],
  [12 * 3600, 720],
  [12 * 3600 + 1, 735],
  [23 * 3600 + 59 * 60 + 59, 1440],
  [24 * 3600, 1440],
  [25 * 3600, 1440]
];
for (const [seconds, expectedMinutes] of roundingCases) {
  assert.equal(duration.roundSecondsUpToQuarterHour(seconds), expectedMinutes, `rounding ${seconds}`);
}
assert.equal(duration.capElapsedSeconds(25 * 3600), 24 * 3600, 'elapsed seconds cap');

console.log('MODULE_001_TIMER_MOBILE=PASS timers=5 cap=24h picker=search-checkbox stopAll=atomic ai=document-grounded mobile=restored modal=contained');
