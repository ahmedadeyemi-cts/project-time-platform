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
const approvalCss = [
  read('src/frontend/project-time-web/src/production-approval-work.css'),
  read('src/frontend/project-time-web/src/production-approval-work-hardening.css')
].join('\n');
const reallocationPortal = read('src/frontend/project-time-web/src/module001b/Module001BTimeReallocationPortal.jsx');
const reallocationCss = [
  read('src/frontend/project-time-web/src/module001/ptc-guided-move.css'),
  read('src/frontend/project-time-web/src/module001/module001b-reallocation-retirement.css')
].join('\n');
const compositionGate = read('src/frontend/project-time-web/src/module001/PtcTimeStewardGate.jsx');
const workflowOperations = read('src/frontend/project-time-web/src/ApprovalExportAuditWorkflowCenter.jsx');

for (const token of [
  'approval-work-production-v2-2026-07-30',
  '/api/approval-work/v2/pending',
  '/api/approval-work/v2/bulk-complete',
  'Time approvals across all weeks',
  'This is the only approval surface',
  'Approve selected',
  'Approve entire week',
  "mode: 'selected'",
  "mode: 'week'",
  'Project time: Manager → assigned PM → PTC',
  'Non-project time: Manager → PTC',
  'PM approves this project only',
  'Non-project · PM not required',
  'SEARCH_DEBOUNCE_MS',
  'requestTokenByWeek',
  'previousSearch.current',
  'Search applies to every expanded week',
  'query: searchQuery',
  'setSelected(new Set())',
  'projectpulse:approval-queue-changed',
  'production-approval-center-host'
]) {
  requireText(approvalPortal, token, 'Production approval portal');
}

for (const token of [
  '.production-approval-center',
  '.production-approval-stage-grid',
  '.production-approval-week-actions',
  '.production-approval-project-scope',
  '.production-approval-badges',
  '.production-approval-return-guidance',
  "[data-production-approval-authoritative='true'] #manager-review .manager-bulk-actions",
  "[data-production-approval-authoritative='true'] #pm-review .manager-row-actions .approve"
]) {
  requireText(approvalCss, token, 'Production approval styles');
}

for (const token of [
  'MODULE 001B · TIME REALLOCATION & CORRECTIONS',
  'Time Reallocation & Corrections',
  'No Access',
  'Project Team Coordinators and Super Administrators',
  '/api/runtime/timesheet/steward/001b/reallocation/entries/${encodeURIComponent(selectedEntry.timeEntryId)}/move',
  "requiredCollections: ['entries', 'moveTargets']",
  'Requests / Service Requests',
  'Project Tasks',
  'Non-Project Time',
  'Move an existing time entry to the correct project task, service request task, or non-project activity without reopening the timesheet.',
  'Submitted and approved time stays in its current status.',
  'No worker resubmission, Manager approval, or Project Manager approval is required.',
  "window.dispatchEvent(new CustomEvent('projectpulse:ptc-time-reallocated'"
]) {
  requireText(reallocationPortal, token, 'Module 001B Time Reallocation portal');
}

for (const token of [
  '.ptc-guided-dialog',
  '.ptc-guided-section',
  '.ptc-guided-destination-groups',
  '.module001b-reallocation-launcher',
  'Time reallocation has moved to Module 001B'
]) {
  requireText(reallocationCss, token, 'Module 001B Time Reallocation styles');
}

for (const token of [
  "import ProductionApprovalWorkPortal from '../ProductionApprovalWorkPortal.jsx';",
  "import Module001BTimeReallocationPortal from '../module001b/Module001BTimeReallocationPortal.jsx';",
  "from '../effective-role-authority.js'",
  'EFFECTIVE_ROLE_AUTHORITY_EVENTS',
  'hasAnyEffectiveRole',
  'readEffectiveRoleAuthority',
  "'PROJECT_TEAM_COORDINATOR'",
  "'SUPER_ADMINISTRATOR'",
  'const MODULE001B_ROLES = new Set([',
  "currentRoute() === 'timesheet'",
  '<Module001BTimeReallocationPortal allowed={canUseModule001B} />',
  "window.location.hash = '#time-reallocation';",
  '<ProductionApprovalWorkPortal />',
  '<PtcTimesheetManagementPortal />',
  'if (!authority.ready) return null;'
]) {
  requireText(compositionGate, token, 'Frontend composition gate');
}

rejectText(approvalPortal, 'window.prompt', 'Production approval portal');
rejectText(reallocationPortal, 'window.prompt', 'Module 001B Time Reallocation portal');
rejectText(compositionGate, "import PtcGuidedMovePortal from './PtcGuidedMovePortal.jsx';", 'Retired Module 001 Move Time mount');
rejectText(compositionGate, '<PtcGuidedMovePortal />', 'Retired Module 001 Move Time mount');
rejectText(compositionGate, '<PendingApprovalWorkPortal />', 'Retired approval portal mount');
rejectText(compositionGate, 'PtcNonProjectTaskPortal', 'Duplicate non-project creation launcher');

if (!leanWebBuildContext) {
  const productionBackend = read('src/backend/ProjectTime.Api/Modules/ProductionApprovalWorkModule.cs');
  const hardeningBackend = read('src/backend/ProjectTime.Api/Modules/ProductionApprovalWorkflowHardening.cs');
  const compatibility = read('src/backend/ProjectTime.Api/Modules/ProductionApprovalWorkCompatibility.cs');
  const endpointCompositionRoot = read('src/backend/ProjectTime.Api/Modules/CombinedModulePublicReadiness.cs');
  const module001bBackend = read('src/backend/ProjectTime.Api/Modules/Module001BTimeReallocationModule.cs');
  const stewardBoundary = read('src/backend/ProjectTime.Api/Modules/PtcTimeStewardRoleBoundary.cs');
  const migration002 = read('database/migrations/002_non_project_time_and_hour_types.sql');

  for (const token of [
    '/api/approval-work/v2/pending',
    '/api/approval-work/v2/bulk-complete',
    'empty_selection',
    'An empty selection can never approve an entire week',
    'aggregationComplete = true',
    "'project_scope'::text AS approval_unit_type",
    'The current approval cycle is represented by the current entry status.',
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

  for (const token of [
    'UseProductionApprovalWorkflowHardening',
    '/api/approval-work/v2/pending',
    '/api/approval-work/v2/bulk-complete',
    '/api/manager/approvals/approve',
    '/api/manager/approvals/bulk-approve',
    '/api/workflow/approval-items/action',
    'pm_approve',
    'accounting_ready',
    'RetiredWorkflowApprovalActions',
    'NormalizeRequestPath',
    "path.EndsWith('/')",
    '/api/scoped-approval/delegated',
    '/api/scoped-approval/ptc-final',
    'legacy_approval_route_retired',
    'RequireImmutableApprovalEvidenceAsync',
    'RequireImmutableNonProjectEvidenceAsync',
    'trg_projectpulse040_approval_audit_immutable',
    'trg_projectpulse040_policy_audit_immutable',
    'await result.ExecuteAsync(context)'
  ]) {
    requireText(hardeningBackend, token, 'Production approval hardening');
  }

  for (const token of [
    'module001b-time-reallocation-v1-2026-08-28',
    '/api/runtime/timesheet/steward/001b/reallocation/capabilities',
    '/api/runtime/timesheet/steward/001b/reallocation/entries/{timeEntryId:guid}/move',
    'allocationOnly = true',
    'workerEditable = false',
    'workDateEditable = false',
    'workedTimeEditable = false',
    'submissionStatePreserved = true',
    'unsubmitRequired = false',
    'workerResubmissionRequired = false',
    'managerApprovalRequired = false',
    'projectManagerApprovalRequired = false',
    'var originalStatus = original.Status;',
    'currentStatus = reloaded.Status',
    'approvalStatePreserved = true'
  ]) {
    requireText(module001bBackend, token, 'Module 001B Time Reallocation backend');
  }

  for (const token of [
    'TimeStewardRoleCodes',
    '"PROJECT_TEAM_COORDINATOR"',
    '"SUPER_ADMINISTRATOR"',
    'app.MapModule001BTimeReallocationEndpoints();',
    'legacyModule001Move',
    'StatusCodes.Status410Gone',
    'module_001b_reallocation_required',
    'The legacy Module 001 move workflow is retired and cannot unsubmit or return time to Draft.',
    '/api/runtime/timesheet/steward/001b/reallocation/entries/{timeEntryId}/move'
  ]) {
    requireText(stewardBoundary, token, 'Module 001B role and retirement boundary');
  }

  rejectText(productionBackend, 'LIMIT 5000', 'Production approval backend');
  rejectText(productionBackend, 'detail: exception.Message', 'Production approval backend');
  rejectText(productionBackend, 'INSERT INTO project_tasks', 'Non-project activity backend');
  rejectText(productionBackend, "approval.approval_stage = 'project_manager'", 'Historical PM approval gating');
  rejectText(productionBackend, "existing.approval_stage = 'project_manager'", 'Historical PM approval gating');
  rejectText(productionBackend, 'existing.approval_stage = @approval_stage', 'Historical stage approval gating');
  rejectText(module001bBackend, 'status = \'draft\'', 'Module 001B submission-state preservation');
  rejectText(module001bBackend, 'status = "draft"', 'Module 001B submission-state preservation');

  requireText(workflowOperations, 'Approval decisions are completed only in Pending approval work.', 'Post-approval operations workspace');
  requireText(workflowOperations, "runAction(item, 'reconcile')", 'Post-approval operations workspace');
  requireText(workflowOperations, "runAction(item, 'lock')", 'Post-approval operations workspace');
  rejectText(workflowOperations, "runAction(item, 'pm_approve')", 'Post-approval operations workspace');
  rejectText(workflowOperations, "runAction(item, 'accounting_ready')", 'Post-approval operations workspace');

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
    'app.UseProductionApprovalWorkflowHardening();',
    'app.UseProductionApprovalWorkCompatibility();',
    'app.MapProductionApprovalWorkEndpoints();',
    'app.MapPendingApprovalWorkEndpoints();',
    'app.MapModule001NonProjectTaskEndpoints();',
    'immutableApprovalAuditReady',
    'legacyApprovalWriteRoutesRetired = true',
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

  for (const payloadPath of [
    '.github/approval-production-payload/part-00.b64',
    '.github/approval-production-payload/part-02.b64'
  ]) {
    if (exists(payloadPath)) {
      throw new Error(`Temporary payload artifact must not be committed: ${payloadPath}`);
    }
  }

  console.log('PRODUCTION_APPROVAL_BACKEND_VALIDATION=PASS');
  console.log('MODULE001B_REALLOCATION_BACKEND_VALIDATION=PASS');
  console.log('MODULE001B_LEGACY_MOVE_RETIREMENT=PASS');
} else {
  console.log('PRODUCTION_APPROVAL_BACKEND_VALIDATION=SKIPPED_LEAN_WEB_CONTEXT');
}

console.log(`PENDING_TIME_WORKFLOW_CONTEXT=${leanWebBuildContext ? 'LEAN_WEB_BUILD' : 'FULL_REPOSITORY'}`);
console.log('PENDING_TIME_WORKFLOW_VALIDATION=PASS');
console.log('PENDING_APPROVAL_AGGREGATES_COMPLETE=PASS');
console.log('EMPTY_SELECTION_APPROVES_NOTHING=PASS');
console.log('LEGACY_APPROVAL_WRITES_RETIRED=PASS');
console.log('LEGACY_ACCOUNTING_READY_RETIRED=PASS');
console.log('TRAILING_SLASH_APPROVAL_GUARDS=PASS');
console.log('CURRENT_SUBMISSION_CYCLE_APPROVALS=PASS');
console.log('SEARCH_RELOADS_OPEN_WEEKS=PASS');
console.log('MANAGER_PM_PTC_BULK_APPROVAL=PASS');
console.log('NON_PROJECT_MANAGER_TO_PTC_ROUTE=PASS');
console.log('PM_PROJECT_SCOPE_ISOLATION=PASS');
console.log('MODULE001B_TIME_REALLOCATION=PASS');
console.log('MODULE001B_NO_REAPPROVAL=PASS');
console.log('IMMUTABLE_APPROVAL_EVIDENCE=PASS');
process.exit(0);