import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const failures = [];
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const requireText = (source, text, label) => {
  if (!source.includes(text)) failures.push(`${label}: missing ${JSON.stringify(text)}`);
};
const rejectText = (source, text, label) => {
  if (source.includes(text)) failures.push(`${label}: forbidden ${JSON.stringify(text)}`);
};

const migration = read('database/migrations/094_project_planning_collaboration_access.sql');
const rollback = read('database/rollback/094_project_planning_collaboration_access_rollback.sql');
const resolver = read('src/backend/ProjectTime.Api/Modules/ProjectPlanningAccessResolver.cs');
const module = read('src/backend/ProjectTime.Api/Modules/ProjectPlanningCollaborationModule.cs');
const program = read('src/backend/ProjectTime.Api/Program.cs');
const flowHive = read('src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs');
const flowHiveCore = read('src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs');
const forge = read('src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs');
const flowHiveUi = read('src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx');
const forgeUi = read('src/frontend/project-time-web/src/ProjectForgeCenter.jsx');

[
  'project_planning_collaborators',
  'project_planning_collaboration_audit_events',
  'projectpulse094_project_scope_reason',
  'projectpulse094_can_view_project',
  'projectpulse094_can_edit_planner',
  'projectpulse094_can_administer_planner',
  'PROJECT_PLANNING_COLLABORATION_V1'
].forEach((text) => requireText(migration, text, 'Migration 094 collaboration foundation'));

[
  'account_executive_user_id',
  'solution_architect_user_id',
  'active_project_assignment',
  'engineering_lead_team_scope',
  'assigned_project_manager',
  'project_manager_lead_scope'
].forEach((text) => requireText(migration, text, 'Migration 094 project scope'));

const permissionCodes = [
  'VIEW_ASSOCIATED_PROJECT_FORGE_033',
  'REVIEW_PROJECT_FORGE_PLAN_033',
  'EDIT_PROJECT_FORGE_REVIEW_PLAN_033',
  'MANAGE_PROJECT_FORGE_CANONICAL_TASKS_033',
  'ADOPT_PROJECT_FORGE_PLAN_033',
  'VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066',
  'REVIEW_FLOWHIVE_PLANNER_066',
  'EDIT_FLOWHIVE_PLANNER_066',
  'ADMINISTER_FLOWHIVE_PROJECT_066'
];
permissionCodes.forEach((text) => requireText(migration, text, 'Migration 094 permissions'));

for (const role of [
  'PROJECT_MANAGER',
  'PROJECT_MANAGER_LEAD',
  'ENGINEER',
  'ENGINEERING',
  'ENGINEERING_LEAD',
  'ACCOUNT_EXECUTIVE',
  'SOLUTION_ARCHITECT'
]) {
  requireText(migration, `('${role}'`, `Migration 094 role policy for ${role}`);
}

[
  'Rollback 094 refused: project-planning collaborator records exist.',
  'Rollback 094 refused: immutable project-planning collaboration evidence exists.'
].forEach((text) => requireText(rollback, text, 'Guarded rollback'));

[
  'public const string PolicyVersion = "PROJECT_PLANNING_COLLABORATION_V1"',
  'CanReviewPlanner',
  'CanEditPlanner',
  'CanAdministerPlanner',
  'CanManageCanonicalTasks',
  'CanAdoptPlan',
  'CanViewFinancials',
  'CanCreateCustomerShare',
  'projectpulse094_project_scope_reason',
  'projectpulse094_can_view_project',
  'projectpulse094_can_edit_planner',
  'projectpulse094_can_administer_planner',
  'ResolveProjectForgePlanProjectIdAsync',
  'ResolveProjectForgePlanTaskProjectIdAsync'
].forEach((text) => requireText(resolver, text, 'Shared project-planning resolver'));

[
  '/api/project-planning/projects',
  '/api/project-planning/projects/{projectId:guid}/access',
  '/api/project-planning/projects/{projectId:guid}/collaborators',
  'ProjectPlanningAccessResolver.ViewAsWriteBlocked()',
  'project_planning_collaborator_revision_conflict',
  'Only a Project Manager Lead collaboration may administer project planning.'
].forEach((text) => requireText(module, text, 'Collaboration API'));

requireText(program, 'app.MapProjectPlanningCollaborationEndpoints();', 'Endpoint registration');

[
  'planningAccess = await ProjectPlanningAccessResolver.ResolveAsync',
  'flowhive_planner_edit',
  'planningAccess,',
  'PROJECT_PLANNING_COLLABORATION_V1'
].forEach((text) => requireText(flowHive, text, 'FlowHive collaboration wiring'));
requireText(
  `${flowHive}\n${flowHiveCore}`,
  'projectpulse094_can_view_project',
  'FlowHive associated-project scope'
);

[
  'planningAccess = selectedProjectId.HasValue',
  'project_forge_review_plan_edit',
  'ResolveProjectForgePlanProjectIdAsync',
  'ResolveProjectForgePlanTaskProjectIdAsync',
  'projectpulse094_can_view_project'
].forEach((text) => requireText(forge, text, 'Project Forge collaboration wiring'));

requireText(flowHiveUi, 'PROJECT_PLANNING_COLLABORATION_V1', 'FlowHive capability-driven UI');
requireText(forgeUi, 'PROJECT_PLANNING_COLLABORATION_V1', 'Project Forge capability-driven UI');

for (const source of [resolver, module, flowHive, forge]) {
  rejectText(source, 'module.owner_user_id', 'Module owner must not grant project access');
  rejectText(source, 'scoped_role_policy_modules.owner', 'Module owner must not grant project access');
}

if (failures.length) {
  console.error('Project-planning collaboration validation failed:');
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exit(1);
}

console.log('Project-planning collaboration validation passed.');
console.log('project_planning_policy=PROJECT_PLANNING_COLLABORATION_V1');
console.log('project_planning_ownership=descriptive_only');
console.log('project_planning_view_as=read_only');
