import fs from 'node:fs';

const read = (path) => fs.readFileSync(path, 'utf8');
const failures = [];
const requireText = (source, text, label) => {
  if (!source.includes(text)) failures.push(`${label}: missing ${JSON.stringify(text)}`);
};
const rejectText = (source, text, label) => {
  if (source.includes(text)) failures.push(`${label}: forbidden ${JSON.stringify(text)}`);
};

const migration = read('database/migrations/095_project_planning_collaboration_access.sql');
const rollback = read('database/rollback/095_project_planning_collaboration_access_rollback.sql');
const resolver = read('src/backend/ProjectTime.Api/Modules/ProjectPlanningAccessResolver.cs');
const flowhive = read('src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs');
const flowhivePortfolio = read('src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs');
const flowhiveRepository = read('src/backend/ProjectTime.Api/Modules/PostgresProjectFlowHivePlanRepository.cs');
const forge = read('src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs');
const forgeInteractive = read('src/backend/ProjectTime.Api/Modules/ProjectForgeInteractiveModule.cs');
const flowhiveUi = read('src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx');
const forgeUi = read('src/frontend/project-time-web/src/ProjectForgeCenter.jsx');

[
  'project_planning_collaborators',
  'project_planning_collaboration_audit_events',
  'EDIT_FLOWHIVE_PLANNER_066',
  'EDIT_PROJECT_FORGE_REVIEW_PLAN_033',
  'VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066',
  'VIEW_ASSOCIATED_PROJECT_FORGE_033',
  "('ACCOUNT_EXECUTIVE','VIEW_PROJECT_FLOWHIVE_066')",
  "('SOLUTION_ARCHITECT','VIEW_PROJECT_FORGE_033')",
  '095_project_planning_collaboration_access'
].forEach((text) => requireText(migration, text, 'Migration 095'));
requireText(rollback, 'Rollback 095 refused: project planning collaborator assignments exist.', 'guarded rollback');
requireText(rollback, 'immutable project planning collaboration audit evidence exists', 'guarded rollback');

[
  'PROJECT_PLANNING_COLLABORATION_V1',
  'associated_account_executive',
  'associated_solution_architect',
  'assigned_engineering_team_scope',
  'CanEditPlanner',
  'CanAdministerPlanner',
  'CanAdoptBaseline',
  'CanCreateCustomerShare',
  'Project Stakeholder — Read Only'
].forEach((text) => requireText(resolver, text, 'shared planning access resolver'));
rejectText(resolver, 'scoped_role_policy_modules', 'module owner metadata must not grant planning access');

[
  'FlowHiveAccessRequirement.EditPlanner',
  'FlowHiveAccessRequirement.AdministerPlanner',
  'FlowHiveAccessRequirement.CustomerShare',
  'ProjectPlanningAccessResolver.ResolveAsync',
  'CanReviewPlanner',
  'CanEditPlanner',
  'CanAdministerPlanner',
  'CapabilityLabel',
  'RedactedControls'
].forEach((text) => requireText(flowhive, text, 'FlowHive enterprise access'));
requireText(flowhiveRepository, 'return access.CanEditPlanner;', 'FlowHive plan-version edit authority');
requireText(flowhiveRepository, 'return access.CanAdoptBaseline;', 'FlowHive baseline PM authority');
requireText(flowhivePortfolio, 'p.account_executive_user_id = @user_id', 'FlowHive AE scope');
requireText(flowhivePortfolio, 'p.solution_architect_user_id = @user_id', 'FlowHive SA scope');

[
  'VIEW_ASSOCIATED_PROJECT_FORGE_033',
  'CanEditReviewPlan',
  'CanReviewPlan',
  'IsEngineeringLead',
  'IsAccountExecutive',
  'IsSolutionArchitect',
  'authorized_engineering_members',
  'p.account_executive_user_id=@effective_user_id',
  'p.solution_architect_user_id=@effective_user_id',
  'ProjectPlanningAccessResolver.ResolveForActorAsync'
].forEach((text) => requireText(forge, text, 'Project Forge collaboration access'));
requireText(forgeInteractive, 'state.RecordSource == "review_plan" && access.CanEditReviewPlan', 'Project Forge review-plan task edit');

requireText(flowhiveUi, 'canEditPlanner', 'FlowHive capability-driven UI');
requireText(flowhiveUi, 'canAdoptBaseline', 'FlowHive baseline control');
requireText(forgeUi, 'canEditReviewPlan', 'Project Forge capability-driven UI');
requireText(forgeUi, 'canEditWorkspace', 'Project Forge workspace capability');

if (failures.length) {
  console.error('Project planning collaboration validation failed:');
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exit(1);
}
console.log('Project planning collaboration validation passed.');
