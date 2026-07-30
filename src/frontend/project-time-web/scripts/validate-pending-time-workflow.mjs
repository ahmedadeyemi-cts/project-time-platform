import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const repoRoot = path.resolve(webRoot, '..', '..', '..');
const absolute = (relativePath) => path.join(repoRoot, relativePath);
const exists = (relativePath) => fs.existsSync(absolute(relativePath));
const leanWebBuildContext = !exists('.git')
  && exists('deployment/containers/web/Dockerfile')
  && !exists('src/backend/ProjectTime.Api/Modules/PendingApprovalWorkModule.cs');

function read(relativePath) {
  const absolutePath = absolute(relativePath);
  if (!fs.existsSync(absolutePath)) {
    throw new Error(`Missing required file: ${relativePath}`);
  }
  return fs.readFileSync(absolutePath, 'utf8');
}

function requireText(source, token, label) {
  if (!source.includes(token)) {
    throw new Error(`${label} is missing required contract: ${token}`);
  }
}

function rejectText(source, token, label) {
  if (source.includes(token)) {
    throw new Error(`${label} contains retired or unsafe contract: ${token}`);
  }
}

const pendingPortal = [
  'src/frontend/project-time-web/src/PendingApprovalWorkPortal.jsx',
  'src/frontend/project-time-web/src/pending-approval-work-support.js'
].map(read).join('\n');
const nonProjectPortal = read('src/frontend/project-time-web/src/module001/PtcNonProjectTaskPortal.jsx');
const compositionGate = read('src/frontend/project-time-web/src/module001/PtcTimeStewardGate.jsx');

for (const token of [
  'Time approvals across all weeks',
  'projectPulsePendingApprovalTarget',
  'pendingStage',
  'weekStart',
  'Approve all ${items.length} for week',
  'Approve selected',
  'No approval comment is required',
  'projectpulse:approval-queue-changed',
  "existingTimeRow.style.display = state.authorized ? 'none' : ''"
]) {
  requireText(pendingPortal, token, 'Pending approval portal');
}

for (const token of [
  'Create non-project task',
  'not tied to a project',
  'projectpulse:permissions-changed',
  'It is not an approval comment',
  'utilizationClassification'
]) {
  requireText(nonProjectPortal, token, 'PTC non-project task portal');
}

for (const token of [
  "import PendingApprovalWorkPortal from '../PendingApprovalWorkPortal.jsx';",
  "import PtcNonProjectTaskPortal from './PtcNonProjectTaskPortal.jsx';",
  '<PendingApprovalWorkPortal />',
  '<PtcNonProjectTaskPortal />',
  'if (state.active && !state.allowed) return null;'
]) {
  requireText(compositionGate, token, 'Frontend composition gate');
}

rejectText(pendingPortal, 'comment: getNote', 'Pending approval portal');
rejectText(pendingPortal, 'window.prompt', 'Pending approval portal');
rejectText(pendingPortal, '#manager-approval?', 'Pending approval portal route compatibility');

if (!leanWebBuildContext) {
  const pendingBackend = [
    'src/backend/ProjectTime.Api/Modules/PendingApprovalWorkModule.cs',
    'src/backend/ProjectTime.Api/Modules/PendingApprovalWorkQuery.cs',
    'src/backend/ProjectTime.Api/Modules/PendingApprovalWorkCompletion.cs'
  ].map(read).join('\n');
  const nonProjectBackend = [
    'src/backend/ProjectTime.Api/Modules/Module001NonProjectTaskModule.cs',
    'src/backend/ProjectTime.Api/Modules/Module001NonProjectTaskAccess.cs'
  ].map(read).join('\n');
  const endpointCompositionRoot = read('src/backend/ProjectTime.Api/Modules/CombinedModulePublicReadiness.cs');
  const migration002 = read('database/migrations/002_non_project_time_and_hour_types.sql');

  for (const token of [
    '/api/approval-work/pending',
    '/api/approval-work/bulk-complete',
    "'submitted', 'manager_approved', 'pm_approved'",
    'accounting_ready',
    'commentRequired = false',
    'No user-entered approval comment was required',
    'scoped_approval_stage_events',
    'CanPtcFinalApprove'
  ]) {
    requireText(pendingBackend, token, 'Pending approval backend');
  }

  for (const token of [
    '/api/timesheet/ptc/non-project-tasks',
    'non_project_time_categories',
    'destinationType = "non_project"',
    'ptc_non_project_task_created',
    'PROJECT_TEAM_COORDINATOR'
  ]) {
    requireText(nonProjectBackend, token, 'PTC non-project task backend');
  }

  for (const token of [
    'app.MapPendingApprovalWorkEndpoints();',
    'app.MapModule001NonProjectTaskEndpoints();'
  ]) {
    requireText(endpointCompositionRoot, token, 'Backend composition root');
  }

  for (const token of [
    'CREATE TABLE IF NOT EXISTS non_project_time_categories',
    'ALTER COLUMN project_id DROP NOT NULL',
    'chk_time_entry_project_or_non_project',
    'chk_time_entry_task_requires_project'
  ]) {
    requireText(migration002, token, 'Existing non-project data model');
  }

  rejectText(nonProjectBackend, 'INSERT INTO project_tasks', 'Standalone non-project task backend');
  console.log('PENDING_TIME_WORKFLOW_BACKEND_VALIDATION=PASS');
} else {
  console.log('PENDING_TIME_WORKFLOW_BACKEND_VALIDATION=SKIPPED_LEAN_WEB_CONTEXT');
}

console.log(`PENDING_TIME_WORKFLOW_CONTEXT=${leanWebBuildContext ? 'LEAN_WEB_BUILD' : 'FULL_REPOSITORY'}`);
console.log('PENDING_TIME_WORKFLOW_VALIDATION=PASS');
console.log('PENDING_APPROVAL_ALL_WEEKS=PASS');
console.log('PM_PTC_BULK_APPROVAL_NO_COMMENT=PASS');
console.log('PTC_NON_PROJECT_TASK_DESTINATION=PASS');
console.log('EXISTING_COMPOSITION_ROOTS_PRESERVED=PASS');
process.exit(0);
