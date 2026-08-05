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
const css = read('src/frontend/project-time-web/src/project-forge-center.css');
const app = read('src/frontend/project-time-web/src/App.jsx');
const frontendRegistry = read('src/frontend/project-time-web/src/module-availability-registry.js');
const roleGovernance = read('src/frontend/project-time-web/src/role-workspace-governance.js');
const backendRegistry = read('src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs');
const backend = read('src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs');
const capability = read('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs');
const aiContracts = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiContracts.cs');
const enterpriseContracts = read('src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformContracts.cs');
const enterpriseService = read('src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformService.cs');
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
requireText(enterpriseService, 'CapabilityCode: request.CapabilityCode', 'Celar AI enterprise capability propagation');
requireText(externalReasoning, 'ProjectPulseAiFeatures.ProjectForgePlanEstimate', 'Module 064 Project Forge execution route');
requireText(compileTargets, 'CelarAiCapabilityRouter', 'Compiled Module 064 persisted capability router');
requireText(compileTargets, 'GenerateExternalAsync', 'Compiled Module 064 external capability execution');

for (const token of [
  'hasRecurrence',
  'item.isReviewerEligible',
  'allowSanitizedExternalFallback: allowExternalAi',
  'expectedVersion: task.revisionNumber'
]) {
  requireText(center, token, 'Project Forge reviewer and AI UI contract');
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

console.log(`MODULE_033_PROJECT_FORGE=PASS tabs=${workbookTabs.length} liveData=canonical ai=module064 notifications=module065 scope=server`);
