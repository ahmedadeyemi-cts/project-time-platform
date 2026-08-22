import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const root = process.cwd();
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');
const requireFile = (relativePath) => {
  const absolutePath = path.join(root, relativePath);
  if (!fs.existsSync(absolutePath) || fs.statSync(absolutePath).size === 0) {
    throw new Error(`Required shared-planning file is missing: ${relativePath}`);
  }
  return read(relativePath);
};
const requireText = (text, expected, label) => {
  if (!text.includes(expected)) {
    throw new Error(`${label}: missing ${JSON.stringify(expected)}`);
  }
};
const rejectText = (text, rejected, label) => {
  if (text.includes(rejected)) {
    throw new Error(`${label}: prohibited legacy contract remains: ${JSON.stringify(rejected)}`);
  }
};

const resolver = requireFile('src/backend/ProjectTime.Api/Modules/ProjectPlanningDocumentResolver.cs');
const orchestrator = requireFile('src/backend/ProjectTime.Api/Modules/ProjectPlanningAiOrchestrator.cs');
const flowHive = requireFile('src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs');
const forge = requireFile('src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs');
const planner = requireFile('src/backend/ProjectTime.Api/Modules/ProjectFlowHiveDetailedPlanBuilder.cs');
const migration = requireFile('database/migrations/096_project_planning_document_authority.sql');
const rollback = requireFile('database/rollback/096_project_planning_document_authority_rollback.sql');
const flowHiveUi = requireFile('src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx');

for (const [needle, label] of [
  ['ProjectId', 'exact selected project identity'],
  ['DocumentId', 'document identity'],
  ['statement_of_work', 'SOW authority'],
]) {
  requireText(resolver, needle, `ProjectPlanningDocumentResolver ${label}`);
}
if (!/\bgsd\b|general solution design/i.test(resolver)) {
  throw new Error('ProjectPlanningDocumentResolver: current GSD authority is missing.');
}
requireText(orchestrator, 'citation', 'ProjectPlanningAiOrchestrator citation contract');
requireText(flowHive, 'ProjectPlanningDocumentResolver', 'FlowHive shared resolver');
requireText(flowHive, 'ProjectPlanningAiOrchestrator', 'FlowHive server-owned orchestration');
requireText(forge, 'ProjectPlanningDocumentResolver', 'Project Forge shared resolver');
requireText(forge, 'ProjectPlanningAiOrchestrator', 'Project Forge server-owned orchestration');

for (const phase of ['Plan', 'Design', 'Implement', 'Validate', 'Release']) {
  requireText(planner, phase, `Detailed planner ${phase} phase`);
}
requireText(planner, 'project_end_exceeded', 'schedule variance contract');
rejectText(planner, 'FitPackageChainsToSelectedWindow', 'silent schedule compression');

for (const needle of [
  'project_planning_document_authority',
  'document_version_id',
  'source_sha256',
  'is_current',
  'statement_of_work',
  "'gsd'",
]) {
  requireText(migration, needle, 'Migration 096 authority contract');
}
requireText(rollback, 'DROP TABLE IF EXISTS project_planning_document_authority', 'Migration 096 rollback');

rejectText(flowHiveUi, 'Approved SOW excerpt', 'legacy pasted SOW UI');
rejectText(flowHiveUi, 'Approved GSD excerpt', 'legacy pasted GSD UI');
rejectText(flowHiveUi, 'Prepare / queue processing', 'legacy manual processing control');

console.log('SHARED_PROJECT_DOCUMENT_PLANNING=PASSED');
console.log('PRODUCTION_MUTATION=NONE');
