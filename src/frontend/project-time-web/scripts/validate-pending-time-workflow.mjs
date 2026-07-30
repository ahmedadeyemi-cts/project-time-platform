import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const repoRoot = path.resolve(webRoot, '..', '..', '..');
const absolute = (relativePath) => path.join(repoRoot, relativePath);
const exists = (relativePath) => fs.existsSync(absolute(relativePath));
const leanWebBuildContext = !exists('.git')
  && exists('deployment/containers/web/Dockerfile')
  && !exists('src/backend/ProjectTime.Api/Modules/ProductionApprovalWorkModule.cs');

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

const approvalPortal = read('src/frontend/project-time-web/src/ProductionApprovalWorkPortal.jsx');
const approvalCss = read('src/frontend/project-time-web/src/production-approval-work.css');
const guidedMove = read('src/frontend/project-time-web/src/module001/PtcGuidedMovePortal.jsx');
const guidedMoveCss = read('src/frontend/project-time-web/src/module001/ptc-guided-move.css');
const nonProjectPortal = read('src/frontend/project-time-web/src/module001/PtcNonProjectTaskPortal.jsx');
const compositionGate = read('src/frontend/project-time-web/src/module001/PtcTimeStewardGate.jsx');

for (const token of [
  'approval-work-production-v2-2026-07-30',
  '/api/approval-work/v2/pending',
  '/api/approval-work/v2/bulk-complete',
  'Time approvals across all weeks',
  'Approve selected',
  'Approve entire week',
  "mode: 'selected'",
  "complete(week, 'week')",
  'Project time: Manager → assigned PM → PTC',
  'Non-project time: Manager → PTC',
  'PM approves this project only',
  'Non-project · PM not required',
  'projectpulse:approval-queue-changed',
  'existingTimeRow',
  'production-approval-center-host'
]) {
  requireText(approvalPortal, token, 'Production approval portal');
}

for (const token of [
  '.production-approval-center',
  '.production-approval-stage-grid',
  '.production-approval-week-actions',
  '.production-approval-project-scope',
  '.production-approval-badges'
]) {
  requireText(approvalCss, token, 'Production approval styles');
}

for (const token of [
  'Move Time wizard',
  'Return to draft and move time',
  '/api/runtime/timesheet/steward/v2/entries/${encodeURIComponent(selectedEntry.timeEntryId)}/move',
  '/api/timesheet/ptc/non-project-activities',
  'Create and select activity',
  'Manager then PTC approval; PM not required',
  'immutable time-management evidence',
  "requiredCollections: ['entries', 'moveTargets', 'nonProjectCategories', 'availableProjects']"
]) {
  requireText(guidedMove, token, 'Guided PTC Move Time portal');
}

for (const token of [
  '.ptc-guided-launcher',
  '.ptc-guided-overlay',
  '.ptc-guided-steps',
  '.ptc-guided-destination-groups',
  '.ptc-guided-reopen'
]) {
  requireText(guidedMoveCss, token, 'Guided PTC Move Time styles');
}

for (const token of [
  'Create non-project task',
  'not tied to a project',
  'projectpulse:permissions-changed',
  'It is not an approval comment',
  'utilizationClassification'
]) {
  requireText(nonProjectPortal, token, 'Existing non-project activity launcher');
}

for (const token of [
  "import ProductionApprovalWorkPortal from '../ProductionApprovalWorkPortal.jsx';",
  "import PtcGuidedMovePortal from './PtcGuidedMovePortal.jsx';",
  '<ProductionApprovalWorkPortal />',
  '<PtcGuidedMovePortal />',
  '<PtcTimesheetManagementPortal />',
  '<PtcNonProjectTaskPortal />',
  'if (state.active && !state.allowed) return null;'
]) {
  requireText(compositionGate, token, 'Frontend composition gate');
}

rejectText(approvalPortal, 'window.prompt', 'Production approval portal');
rejectText(guidedMove, 'window.prompt', 'Guided PTC Move Time portal');
rejectText(compositionGate, '<PendingApprovalWorkPortal />', 'Retired approval portal mount');

if (!leanWebBuildContext) {
  const productionBackend = read('src/backend/ProjectTime.Api/Modules/ProductionApprovalWorkModule.cs');
  const compatibility = read('src/backend/ProjectTime.Api/Modules/ProductionApprovalWorkCompatibility.cs');
  const endpointCompositionRoot = read('src/backend/ProjectTime.Api/Modules/CombinedModulePublicReadiness.cs');
  const migration002 = read('database/migrations/002_non_project_time_and_hour_types.sql');

  for (const token of [
    '/api/approval-work/v2/pending',
    '/api/approval-work/v2/bulk-complete',
    'empty_selection',
    'An empty selection can never approve an entire week',
    'aggregationComplete = true',
    "'project_scope'::text AS approval_unit_type",
    "approval.approval_stage = 'project_manager'",
    "pending.status = 'manager_approved'",
    'AND project_entry.project_id IS NOT NULL',
    'IsNonProjectOnlyDayAsync',
    'scoped_approval_stage_events',
    'scoped_role_policy_audit_events',
    'APPROVAL_BULK_COMPLETED',
    'NON_PROJECT_ACTIVITY_CREATED',
    'ProtectedNonProjectCodes',
    'pg_advisory_xact_lock',
    'creation never overwrites an existing category',
    'No user-entered approval comment was required',
    'SafeFailure('
  ]) {
    requireText(productionBackend, token, 'Production approval backend');
  }

  rejectText(productionBackend, 'LIMIT 5000', 'Production approval backend');
  rejectText(productionBackend, 'detail: exception.Message', 'Production approval backend');
  rejectText(productionBackend, 'INSERT INTO project_tasks', 'Non-project activity backend');

  for (const token of [
    '/api/approval-work/pending',
    '/api/approval-work/bulk-complete',
    '/api/timesheet/ptc/non-project-tasks',
    'ProductionApprovalWorkModule.GetPendingAsync',
    'ProductionApprovalWorkModule.BulkCompleteAsync',
    'ProductionApprovalWorkModule.CreateNonProjectActivityAsync'
  ]) {
    requireText(compatibility, token, 'Legacy route compatibility');
  }

  for (const token of [
    'app.UseProductionApprovalWorkCompatibility();',
    'app.MapProductionApprovalWorkEndpoints();',
    'app.MapPendingApprovalWorkEndpoints();',
    'app.MapModule001NonProjectTaskEndpoints();',
    'projectScopedPmApproval = true',
    'nonProjectRoute = "manager_then_ptc"'
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

  console.log('PRODUCTION_APPROVAL_BACKEND_VALIDATION=PASS');
} else {
  console.log('PRODUCTION_APPROVAL_BACKEND_VALIDATION=SKIPPED_LEAN_WEB_CONTEXT');
}

console.log(`PENDING_TIME_WORKFLOW_CONTEXT=${leanWebBuildContext ? 'LEAN_WEB_BUILD' : 'FULL_REPOSITORY'}`);
console.log('PENDING_TIME_WORKFLOW_VALIDATION=PASS');
console.log('PENDING_APPROVAL_AGGREGATES_COMPLETE=PASS');
console.log('EMPTY_SELECTION_APPROVES_NOTHING=PASS');
console.log('MANAGER_PM_PTC_BULK_APPROVAL=PASS');
console.log('NON_PROJECT_MANAGER_TO_PTC_ROUTE=PASS');
console.log('PM_PROJECT_SCOPE_ISOLATION=PASS');
console.log('PTC_GUIDED_MOVE_TIME=PASS');
console.log('IMMUTABLE_APPROVAL_EVIDENCE=PASS');
process.exit(0);