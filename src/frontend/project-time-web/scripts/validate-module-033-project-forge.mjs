import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const frontend = process.cwd();
const repository = path.resolve(frontend, '../../..');
const read = (relative) => fs.readFileSync(path.join(repository, relative), 'utf8');
const requireText = (source, text, label) => {
  if (!source.includes(text)) throw new Error(`${label} is missing ${JSON.stringify(text)}`);
};

const center = read('src/frontend/project-time-web/src/ProjectForgeCenter.jsx');
const views = read('src/frontend/project-time-web/src/project-forge/ProjectForgeViews.jsx');
const dialog = read('src/frontend/project-time-web/src/project-forge/ProjectForgeTaskDialog.jsx');
const api = read('src/frontend/project-time-web/src/project-forge/projectForgeApi.js');
const model = read('src/frontend/project-time-web/src/project-forge/projectForgeModel.js');
const interactiveFrontend = [center, views, dialog, api, model].join('\n');
const css = read('src/frontend/project-time-web/src/project-forge-center.css');
const app = read('src/frontend/project-time-web/src/App.jsx');
const frontendRegistry = read('src/frontend/project-time-web/src/module-availability-registry.js');
const roleGovernance = read('src/frontend/project-time-web/src/role-workspace-governance.js');
const backendRegistry = read('src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs');
const backend = read('src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs');
const interactiveBackend = read('src/backend/ProjectTime.Api/Modules/ProjectForgeInteractiveModule.cs');
const capability = read('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs');
const aiContracts = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiContracts.cs');
const enterpriseContracts = read('src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformContracts.cs');
const enterpriseService = read('src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformService.cs');
const privateRagContracts = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagContracts.cs');
const privateRagService = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs');
const knowledgeFabric = read('src/backend/ProjectTime.Api/Ai/CelarAiKnowledgeFabricService.cs');
const externalReasoning = read('src/backend/ProjectTime.Api/Ai/CelarAiExternalReasoningService.cs');
const compileTargets = read('src/backend/ProjectTime.Api/Directory.Build.targets');
const migration = read('database/migrations/070_module_033_project_forge.sql');

const workbookTabs = [
  'Instructions', 'Setup', 'Overall Dashboard', 'Monthly Calendar', 'Weekly Calendar',
  'Project Overview', 'Project Manager', 'Project Budget', 'Variable Tasks',
  'Recurring Tasks', 'Tasks Schedule', 'Tasks Filter', 'Decision Matrix',
  'Kanban Board', 'Gantt Chart'
];

for (const tab of workbookTabs) requireText(center, `'${tab}'`, `Project Forge ${tab} tab`);
if ((center.match(/^\s*\['[^']+', '[^']+'\],?$/gm) || []).length < workbookTabs.length) {
  throw new Error('Project Forge must retain one explicit application tab for all 15 workbook sheets.');
}

for (const source of [app, frontendRegistry, backendRegistry]) {
  requireText(source, 'project-forge', 'Module 033 route registration');
  requireText(source, 'Project Forge', 'Module 033 display registration');
}
requireText(frontendRegistry, "moduleNumber: '033'", 'Frontend Module 033 registry');
requireText(backendRegistry, '["033"] = Module("033", "project-forge"', 'Backend Module 033 registry');
requireText(roleGovernance, "'project-forge'", 'Project Management role workspace baseline');

for (const token of [
  '/api/project-forge/bootstrap',
  '/api/project-forge/projects/{projectId:guid}/ai-drafts',
  '/api/project-forge/plan-tasks/{planTaskId:guid}/estimate',
  '/api/project-forge/plans/{planId:guid}/adopt',
  'ProjectPulseIsViewAs',
  'IsEligibleEngineerReviewerAsync',
  'sourceKind == "ai_generated"',
  'assignedReviews != planTaskCount',
  'PROJECT_MANAGEMENT_LEAD',
  'PM_TEAM_LEAD',
  'project_forge_plans',
  'project_forge_plan_tasks',
  'project_tasks',
  'project_assignments',
  'CelarAiEnterprisePlatformService',
  'ProjectFlowHiveScheduleEngine',
  'enterprise_notification_events'
]) requireText(backend, token, 'Project Forge backend contract');

for (const token of [
  'ProjectPulseAiFeatures.ProjectForgePlanEstimate',
  'Project Forge plan, tasks, and estimates',
  '["011", "033"]'
]) requireText(capability, token, 'Module 064 Project Forge capability');
requireText(aiContracts, 'ProjectForgePlanEstimate = "project_forge_plan_estimate"', 'Module 064 executable Project Forge feature');
requireText(enterpriseContracts, 'string? CapabilityCode = null', 'Celar AI capability propagation contract');
for (const token of [
  'var capability = ResolveCapability(mode, request);',
  'request.CapabilityCode?.Trim()',
  'CelarAiCapabilityCatalog.ProjectForgePlanEstimate'
]) requireText(enterpriseService, token, 'Celar AI enterprise capability propagation');
requireText(externalReasoning, 'ProjectPulseAiFeatures.ProjectForgePlanEstimate', 'Module 064 Project Forge execution route');
requireText(compileTargets, 'CelarAiCapabilityRouter', 'Compiled Module 064 persisted capability router');
requireText(compileTargets, "grep -Fq 'ExternalCapsulePurpose: serverOwnedPurposeCategory'", 'Compiled Module 064 external capability execution');
requireText(compileTargets, 'DestinationFiles="$(CelarAiExternalReasoningGenerated)"', 'Compiled Module 064 external capability copy');

for (const token of [
  'hasRecurrence',
  'isReviewerEligible',
  'allowSanitizedExternalFallback: true',
  'expectedRevision',
  'function aiDraftNotice(result)',
  'result?.compositionStatus || result?.status',
  'result?.selectedTarget',
  'result?.primaryExecutionPath',
  'setNotice(aiDraftNotice(result))',
  'supplied only separate generic assistance through the governed route',
  'The route ended at governed local, whose output did not replace the private project evidence or artifact.'
]) {
  requireText(interactiveFrontend, token, 'Project Forge reviewer and AI UI contract');
}
if (center.includes("setNotice('Celar AI created a private, document-grounded review draft.")) {
  throw new Error('Project Forge AI notice must derive the actual status and selected target from the backend response.');
}

for (const token of [
  "workspace = 'canonical'",
  "value=\"canonical\">Live Project",
  "value=\"review_plan\">Review Plan",
  "query.set('workspace', workspace)",
  "query.set('planId', planId)",
  "{ id: 'backlog', label: 'Backlog' }",
  "{ id: 'ready', label: 'Ready' }",
  "{ id: 'in_progress', label: 'In Progress' }",
  "{ id: 'blocked', label: 'Blocked' }",
  "{ id: 'review', label: 'Review' }",
  "{ id: 'done', label: 'Done' }",
  'draggable={canMove}',
  'Move due date',
  'Move ${taskName(task)} to Kanban column',
  'Move ${taskName(task)} to decision quadrant',
  "const ZOOM_LEVELS = Object.freeze({ day: 2, week: 14, month: 45 })",
  "'resize_end'",
  'role="dialog"',
  'aria-modal="true"',
  "event.key !== 'Tab'",
  'Next occurrences (projection)',
  'This preview does not create duplicate task rows.',
  "mutationError.status === 409",
  'clientMutationId()',
  'canUpdateAssignedTaskStatus',
  "task?.[capability] === undefined",
  "orderOnly ? 'Task order saved.'",
  'canViewFinancials',
  'expectedPlanRevision'
]) requireText(interactiveFrontend, token, 'Project Forge interactive frontend contract');

for (const token of [
  'groupCurrencyTotals(currentExpenses)',
  'Project Forge does not convert or sum unlike currencies.',
  'Uploads without a valid currency remain individual amounts and are never combined.',
  'Planned-cost variance unavailable for this total',
  'plannedVarianceAvailable',
  'projectForgeApi.updateCompositeTask(value, changes)',
  '/api/project-forge/tasks/${taskId(task)}/composite',
  'Task changes saved atomically.'
]) requireText(interactiveFrontend, token, 'Project Forge atomic save and currency safety contract');

for (const token of [
  'calendarTasksInRange(tasks, visibleStart, visibleEnd)',
  'calendarTasksInRange(tasks, toDateOnly(days[0]), toDateOnly(days[6]))',
  'Projected occurrence',
  'recurrenceOccurrenceDate',
  'task.recurrenceCanonicalTask || task',
  'projectedOccurrenceDatesInRange'
]) requireText(interactiveFrontend, token, 'Project Forge recurring calendar integration');

for (const endpoint of [
  '/api/project-forge/projects/${projectId}/tasks',
  '/api/project-forge/tasks/${taskId(task)}/details',
  '/api/project-forge/tasks/${taskId(task)}/workflow',
  '/api/project-forge/tasks/${taskId(task)}/schedule',
  '/api/project-forge/tasks/${taskId(task)}/decision',
  '/api/project-forge/tasks/${taskId(task)}/assignee',
  '/api/project-forge/projects/${projectId}/task-dependencies',
  '/api/project-forge/task-dependencies/${dependencyId}',
  '/api/project-forge/plans/${planId}/tasks/${taskId(task)}/review-completion'
]) requireText(api, endpoint, 'Project Forge persisted interaction endpoint');

for (const token of ['setGeneratedDraft(null)', "setReviewerId('')", 'setSelectedTask(null)']) {
  requireText(center, token, 'Project Forge stale workspace-state reset');
}
if (center.includes('plannedTotalProjectCost || currentProject?.plannedCost')) {
  throw new Error('Project Forge must preserve an authoritative zero planned cost with nullish selection.');
}
if (center.includes("approvalStatus || item.status || 'submitted'")) {
  throw new Error('Project Forge must not invent a submitted expense approval status.');
}

for (const token of [
  "? 'set_range'",
  "? 'resize_start' : 'resize_end'",
  'refreshReviewPlanAfter(task, changed',
  'dependencies={dependencies}',
  'const expectedTaskRevisions = Object.fromEntries(',
  'clearParentTask: true',
  'clearRecurrenceRule: true',
  'handleTabKeyDown(event, index)',
  "event.key === 'Home'",
  "event.key === 'End'",
  'aria-controls={`forge-panel-${tab.id}`}',
  'tabIndex={activeTab === tab.id ? 0 : -1}',
  'role="tabpanel"',
  "['in_progress', 'in_review', 'active', 'started', 'review']",
  "{ id: 'decide', label: 'Decide / Schedule', help: 'Important, not urgent', important: true, urgent: false }",
  "{ id: 'delegate', label: 'Delegate', help: 'Urgent, not important', important: false, urgent: true }"
]) requireText(interactiveFrontend, token, 'Project Forge QA interaction contract');

for (const token of [
  ".project-forge [draggable='true']",
  '.forge-gantt-viewport',
  'overflow-x: auto'
]) requireText(css, token, 'Project Forge scoped drag and Gantt viewport');

for (const token of ['"move", "resize_start", "resize_end", "set_range"', 'ClearParentTask', 'ClearRecurrenceRule']) {
  requireText(interactiveBackend, token, 'Project Forge backend interaction support');
}
if (dialog.includes('<option value="">Unassigned</option>')) {
  throw new Error('Project Forge must not offer unsupported task unassignment.');
}
if (center.includes('projectForgeApi.updateDetails(created')) {
  throw new Error('Project Forge canonical task creation must remain atomic instead of applying a follow-up details PATCH.');
}
for (const token of [
  'percentComplete: Number(form.percentComplete || 0)',
  'blockedReason: form.blockedReason || null',
  'durationWorkingDays',
  'max="730"',
  'min="0.01" max="100"',
  "aria-describedby={reviewEditsDirty ? 'forge-review-save-warning' : undefined}",
  'Save task changes before completing the review or requesting changes.'
]) requireText(interactiveFrontend, token, 'Project Forge task-editor safety contract');

const saveEstimateStart = api.indexOf('saveEstimate(task, estimate)');
const completeReviewStart = api.indexOf('completeReview(', saveEstimateStart);
const saveEstimateSource = api.slice(saveEstimateStart, completeReviewStart < 0 ? api.length : completeReviewStart);
if (saveEstimateStart < 0 || /startDate|dueDate|plannedStartDate|plannedEndDate/.test(saveEstimateSource)) {
  throw new Error('Project Forge estimate-only saves must not transmit task schedule fields.');
}

const refusalGate = backend.indexOf('var compositionRefused = string.Equals(');
const evidenceGate = backend.indexOf('var groundedStatus = composition.Status is');
const projectEvidencePreflight = backend.indexOf('var projectEvidence = await LoadProjectEvidenceReadinessAsync(connection, projectId, cancellationToken);');
const compositionCall = backend.indexOf('var composition = await enterprise.ComposeAsync(');
const taskProjection = backend.indexOf('var generatedTasks = (composition.FlowHivePlan?.Tasks ?? [])');
if (refusalGate < 0 || evidenceGate < 0 || taskProjection < 0 || refusalGate > taskProjection || evidenceGate > taskProjection) {
  throw new Error('Project Forge must refuse unsafe or ungrounded composition before projecting or persisting plan tasks.');
}
if (projectEvidencePreflight < 0 || compositionCall < 0 || projectEvidencePreflight > compositionCall) {
  throw new Error('Project Forge must verify citation-ready evidence for the selected project before calling any AI target.');
}
for (const token of [
  'status = "ai_plan_generation_refused"',
  '"celar_ai_solution_draft_completed" or',
  '"celar_ai_solution_draft_partial"',
  'composition.FlowHivePlan.Tasks.Count > 0',
  'composition.FlowHivePlan.CitationIds',
  'composition.FlowHivePlan.Tasks.SelectMany(task => task.CitationIds)',
  'composition.FlowHivePlan.Milestones.SelectMany(milestone => milestone.CitationIds)',
  'planCitationIds.Length > 0',
  'composition.Citations.Count > 0',
  'status = "ai_plan_evidence_insufficient"',
  'stateChanged = false'
]) requireText(backend, token, 'Project Forge fail-closed AI persistence gate');
for (const token of [
  'projectReadyDocumentCount = projectEvidence.ReadyDocumentCount',
  'projectActiveChunkCount = projectEvidence.ActiveChunkCount',
  'projectEvidence.ReadyDocumentCount == 0',
  'PulseAiPrivateRagPolicy.FlowHiveCategories',
  'ANY(@flowhive_categories)',
  'No AI target was called and no draft was saved.'
]) requireText(backend, token, 'Project Forge selected-project evidence preflight');
for (const token of [
  'const projectEvidenceMissing = Boolean(',
  'aiConnection.projectReadyDocumentCount',
  'projectEvidenceMissing ? \'Project evidence required\''
]) requireText(center, token, 'Project Forge selected-project evidence UI gate');
if (backend.includes('composition.FlowHivePlan.CitationIds.Count > 0')) {
  throw new Error('Project Forge must accept citations attached to tasks or milestones, not only top-level plan citations.');
}
if ((backend.split('compositionStatus = composition.Status').length - 1) < 5) {
  throw new Error('Project Forge AI success/error responses must disclose the private artifact composition status.');
}
if (backend.includes('groundedStatus && string.Equals(composition.SelectedTarget')) {
  throw new Error('Project Forge must preserve a citation-backed private scaffold when a separate external/local assistance target finishes the route.');
}

for (const token of [
  'public bool CanProjectForge',
  'IReadOnlyList<string>? DetailedSteps',
  'IReadOnlyList<string>? Inputs',
  'IReadOnlyList<string>? Outputs',
  'IReadOnlyList<string>? AcceptanceCriteria',
  'IReadOnlyList<string>? ValidationSteps',
  'IReadOnlyList<string>? CustomerResponsibilities',
  'IReadOnlyList<string>? UsSignalResponsibilities',
  'IReadOnlyList<string>? Prerequisites',
  'IReadOnlyList<string>? Risks',
  'IReadOnlyList<string>? OpenQuestions',
  'decimal? EstimatedHours'
]) requireText(privateRagContracts, token, 'Project Forge comprehensive private task contract');

for (const token of [
  'CelarAiCapabilityCatalog.ProjectForgePlanEstimate => access.CanProjectForge',
  'Automatically fill every supported section.',
  'Every detailed step must identify the actor, action, required input or prerequisite, expected output, validation or evidence, and completion condition.'
]) requireText([privateRagService, enterpriseService].join('\n'), token, 'Project Forge capability-aware private planning');

for (const token of [
  'var planningCapability = ResolveCapability(mode, request);',
  'FeatureCode: planningCapability'
]) requireText(enterpriseService, token, 'Project Forge centrally resolved planning capability');

for (const token of [
  'PlanningDescription(task)',
  'AppendPlanningSection(value, "Detailed procedure"',
  'AppendPlanningSection(value, "Acceptance criteria"',
  'task.EstimatedHours ?? task.EstimatedDurationDays * 8m',
  'CelarAiKnowledgeFabricService knowledgeFabricService',
  'module064Connection = new',
  'privateKnowledgeReady = forgeConnection?.PrivateKnowledgeReady == true'
]) requireText(backend, token, 'Project Forge comprehensive task projection and Module 064 connection evidence');

for (const token of [
  'Project Forge is connected',
  'Your Project Forge permission, governed route, private inference, and current document knowledge are ready.',
  'automatically fills each customer-facing task'
]) requireText(center, token, 'Project Forge visible Module 064 connection and customer-ready generation');

for (const token of [
  'CelarAiCapabilityCatalog.Definitions',
  'connected_private_knowledge_ready',
  'private_inference',
  'private_database',
  'project -> document -> authoritative version -> section or worksheet -> chunk -> citation'
]) requireText(knowledgeFabric, token, 'Project Forge knowledge-fabric evidence');
for (const token of [
  'composition.SelectedTarget',
  'composition.AttemptedTargets',
  'composition.SkippedTargets',
  'composition.TargetDecisions',
  'composition.PrimaryExecutionPath'
]) {
  const occurrences = backend.split(token).length - 1;
  if (occurrences < 4) throw new Error(`Project Forge AI success/error responses must include truthful route metadata: ${token}`);
}

for (const token of [
  '070_module_033_project_forge',
  'project_forge_plans',
  'project_forge_plan_tasks',
  'project_forge_plan_assignments',
  'project_forge_task_dependencies',
  'project_forge_task_details',
  'project_forge_audit_events',
  'PROJECT_FORGE_REVIEW_ASSIGNED',
  'PROJECT_FORGE_TASK_ASSIGNED',
  'PROJECT_FORGE_TASK_UPDATED',
  'PROJECT_FORGE_PLAN_UPDATED',
  'project_forge_plan_estimate',
  'VIEW_PROJECT_FORGE_033',
  'MANAGE_PROJECT_FORGE_033',
  'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'
]) requireText(migration, token, 'Migration 070 Project Forge contract');

for (const forbidden of [
  'smtp', 'brevo', 'sendgrid', 'mailkit',
  'INSERT INTO projects', 'INSERT INTO clients', 'INSERT INTO app_users'
]) {
  if (migration.toLowerCase().includes(forbidden.toLowerCase())) {
    throw new Error(`Migration 070 contains forbidden manual/provider coupling: ${forbidden}`);
  }
}

for (const selector of ['.forge-tabs', '.forge-kanban', '.forge-gantt', '@media (max-width: 720px)']) {
  requireText(css, selector, 'Project Forge responsive workbook UI');
}

const sharedStylesheetImport = center.indexOf("import './projectpulse-module-standard.css';");
const forgeStylesheetImport = center.indexOf("import './project-forge-center.css';");
if (sharedStylesheetImport < 0 || forgeStylesheetImport < 0 || sharedStylesheetImport > forgeStylesheetImport) {
  throw new Error('Project Forge must load the shared module baseline before its scoped light/dark theme layer.');
}

for (const token of [
  '--forge-ink: var(--text)',
  '--forge-muted: var(--muted)',
  '--forge-panel: var(--surface)',
  '--forge-panel-2: var(--surface-strong)',
  '--forge-line: var(--border)',
  '--forge-blue: var(--brand-blue)',
  '--forge-cyan: var(--brand-cyan)',
  '--forge-green: var(--brand-green)',
  '--forge-accent-ink: var(--brand-blue-strong)',
  '--pp-module-ink: var(--text)',
  '--pp-module-muted: var(--muted)',
  '--pp-module-line: var(--border)',
  '--pp-module-surface: var(--surface)',
  '--pp-module-shadow: var(--shadow)',
  ":root[data-theme='dark'] .project-forge",
  'color-scheme: light',
  'color-scheme: dark',
  'background: var(--forge-panel)',
  'background: var(--forge-subtle)',
  'background: var(--forge-accent-soft)',
  'border: 1px solid var(--forge-line)',
  'color-mix(in srgb, var(--forge-red)',
  'color-mix(in srgb, var(--forge-green)'
]) requireText(css, token, 'Project Forge shared light/dark theme contract');

for (const staleColor of [
  '#0c1721', '#132b3c', '#101c28', '#153344', '#12202d', '#182a39',
  '#172837', '#172a39', '#1b4055', '#101d27', '#1b2b37', '#2a3d4c'
]) {
  if (css.toLowerCase().includes(staleColor)) {
    throw new Error(`Project Forge reintroduced its fixed dark-blue palette: ${staleColor}`);
  }
}
if (/\b(?:background|background-color|border(?:-color)?|color)\s*:[^;{}]*#[0-9a-f]{3,8}/i.test(css)) {
  throw new Error('Project Forge visual rules must use shared or semantic theme tokens instead of direct color literals.');
}

console.log(`MODULE_033_PROJECT_FORGE=PASS tabs=${workbookTabs.length} liveData=canonical ai=module064 notifications=module065 scope=server theme=light-dark-shared`);
