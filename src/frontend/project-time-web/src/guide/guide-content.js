import time from './guide-time.js';
import delivery from './guide-delivery.js';
import sales from './guide-sales.js';
import administration from './guide-admin.js';
import operations from './guide-operations.js';
import { topic, how, ALL } from './guide-model.js';
import { ROLE_GUIDANCE } from '../role-permission-model.js';
import { roleCatalog } from '../role-journeys/role-journeys.js';
import { assignedRolePlaybooks, journeyRoleCode } from '../role-journeys/role-journey-playbooks.js';

export const GUIDE_TOPICS = Object.freeze([...time, ...delivery, ...sales, ...administration, ...operations,
  topic('dashboard', ALL, 'Start with the work and attention items authorized for your current identity.',
    ['A verified signed-in session and current navigation/access context.'], [
      how('Start your day and find the owning workspace', ['Confirm the signed-in name and any View-As banner.', 'Review your role-aware attention items, assigned work and permitted shortcuts.', 'Open My Role in Pulse for your assigned visual playbook, or Modules/More for available workspaces.', 'Use Search to find authorized records and verify the selected customer/project.', 'Complete changes in the owning module; the dashboard is not a substitute for an approval or save receipt.'], 'You begin in the correct identity and workspace with an accountable next action.', 'Signed-in user → owning workflow.')
    ], ['Module numbers identify workspaces, not security grants. A missing card may reflect access or readiness.'], ['RoleWelcomeDashboard.jsx', 'WorkspaceNavigationPortal.jsx', 'EnterpriseExperienceController.jsx']),
  topic('user-guide', ALL, 'Use reviewed role responsibilities and module procedures as learning guidance, not as permission to act.',
    ['A signed-in session; verified access is required before the guide offers workspace launch links.'], [
      how('Find a role-specific how-to', ['Review My responsibilities for the role codes confirmed by the server.', 'Choose an assigned role or deliberately open All role reference to learn about another role without changing your assignment.', 'Search by module number, role, control, workflow or business term; narrow Category/Audience or show only your available workspaces.', 'Expand the relevant procedure and review prerequisites, steps, expected result, next owner and limitations.', 'Use Open workspace only when current access has been verified. Print the filtered guide when a reference copy is needed.'], 'The procedure and its authority boundary are clear before you act.', 'Learner → responsible role owner or support for unresolved questions.')
    ], ['Reference roles are not assigned roles. Unknown/custom roles or newly registered routes are explicitly marked as needing reviewed instructions.'], ['SystemUserGuide.jsx', 'role-journeys/RoleJourneyGuideRouter.jsx'])
]);
export const GUIDE_BY_ROUTE = Object.freeze(Object.fromEntries(GUIDE_TOPICS.map(guide => [guide.route, guide])));

// Include the build-injected runtime page and the two installed operational
// routes. These are documentation identities, never additions to authorization.
export const EXTRA_GUIDE_MODULES = Object.freeze([
  { moduleNumber: '084', route: 'celar-ai-runtime-version', displayName: 'Celar AI Runtime & Version Center', group: 'Platform Operations' },
  { route: 'dashboard', displayName: 'Dashboard and navigation', group: 'Getting Started' },
  { route: 'production-readiness', displayName: 'Production Readiness Command Center', group: 'Platform Operations' },
  { route: 'production-data-readiness', displayName: 'Production Data Readiness Center', group: 'Platform Operations' }
]);
export const GUIDE_ROLE_CODES = Object.freeze([...new Set([...roleCatalog(ROLE_GUIDANCE).map(role => role.code), 'SOLUTION_ARCHITECT_MANAGER'])]);
// Project the existing learning catalog, stripping synthetic assignment flags.
// No value from this reference catalog is used as navigation or action authority.
export const ROLE_REFERENCE = Object.freeze(assignedRolePlaybooks(ROLE_GUIDANCE, { accessReady: true, roleCodes: GUIDE_ROLE_CODES })
  .map(({ assigned, ...role }) => Object.freeze({ ...role, steps: role.steps.map(step => {
    // The older learning touchpoint was conditional. The current guide states
    // the actual 055D creator boundary explicitly without changing role grants.
    if (role.code === 'PROJECT_MANAGEMENT' && step.id === 'pm-project-record') return {
      ...step, title: 'Coordinate creation and maintain your assigned project', route: 'work-register',
      action: 'Request canonical project creation from the PTC or authorized administrator in Module 055D. Use Module 055C to maintain an existing project assigned to you.',
      exception: 'The Project Manager role alone does not grant Module 055D creation or edits to unrelated projects.'
    };
    return { ...step };
  }) })));
export function assignedGuideRoles(context) {
  if (!context?.accessReady || context.state !== 'ready') return [];
  const codes = [...new Set((context.roleCodes || []).map(journeyRoleCode))];
  return codes.map(code => ROLE_REFERENCE.find(role => role.code === code) || {
    code, title: code.replaceAll('_', ' '), purpose: 'A reviewed handbook has not yet been published for this custom or newly assigned role.',
    boundary: 'Use only server-granted modules/actions and request reviewed responsibilities from the role owner.', steps: [], unreviewed: true
  });
}
export const ROLE_NOTES = Object.freeze({
  ENGINEERING: ['Start with assigned projects/tasks and source documents. Record your own actual time and completion evidence.', 'Request-closeout eligibility does not grant project closure, invoice issuance or editing another worker’s time.'],
  ENGINEERING_LEAD: ['Coordinate the authorized engineering practice/team, workloads, qualifications and available on-call controls.', 'Team visibility does not automatically grant financial rates, unrelated projects or platform administration.'],
  PROJECT_MANAGEMENT: ['Own scope-to-plan execution, reviewed WBS/baselines, project team coordination, risks, project time review and closeout.', 'Module 055D creation belongs to PTC/authorized administrators. Use Module 055C for existing projects assigned to you.'],
  PROJECT_MANAGEMENT_LEAD: ['Coordinate the permitted PM portfolio and team exceptions using server-returned PM options.', 'Broader portfolio visibility does not automatically grant editing every project; individual module write scope still applies.'],
  PROJECT_TEAM_COORDINATOR: ['Own approved new-project creation, operational/resource handoff, permitted time stewardship and final-review responsibilities.', 'Distinguish worker-return corrections from Module 001B administrative reallocation. Do not submit a timesheet for another user.', 'Final-stage approval, delegation, contract extension and import approval require the specific current action permission.'],
  PROJECT_COORDINATOR: ['Coordinate accepted handoffs, evidence and follow-up within the scope actually granted.', 'Project Coordinator and Project Team Coordinator are separate learning roles; do not assume the broader PTC time-steward or project-creation authority.'],
  MANAGER: ['Review direct/indirect reporting scope according to each module, approve eligible time, oversee workload and resolve people/coverage exceptions.', 'An SA manager view can be read-only even when a bounded ownership-transfer action is offered.'],
  SALES: ['Own customer needs, opportunity facts, AE responsibility and approved commercial handoff.', 'Do not treat a pipeline estimate, generated SOW or quoted price as signed customer acceptance.'],
  INSIDE_SALES: ['Coordinate customer/quote references, SOW/GSD package readiness and receiving-owner follow-up.', 'A healthy ConnectWise SELL quote API does not establish customer sync, pricing import or document-publisher readiness.'],
  SOLUTION_ARCHITECT: ['Own the Service Scope, human-reviewed overview, five phase objectives/task hours, after-hours mapping and confirmed SOW/GSD package.', 'System SA and Collaboration & Networking SA practice assignments follow actual organizational relationships. They are not new grants created by this guide.', 'Use reviewed versions, due-work tracking and eligible transfers rather than duplicating packages during PTO.'],
  SOLUTION_ARCHITECT_MANAGER: ['Oversee the currently assigned SA reporting team, due work and authorized coverage/transfers.', 'Keep authoring/review responsibility with the assigned SA unless you explicitly hold the required action authority.'],
  ACCOUNTING: ['Reconcile approved time/expenses, verify commercial inputs, prepare controlled exports/invoices and retain financial evidence.', 'Imported, draft, finalized, exported, posted, delivered and paid are not interchangeable outcomes.'],
  BILLING: ['Prepare and process permitted billing scope with correct rates, currency, periods and prepaid offsets.', 'Financial access does not imply time-entry, project-creation or security-administration authority.'],
  EXECUTIVE: ['Review organization-wide indicators where granted, question incomplete/stale sources and assign accountable decision owners.', 'Leadership visibility is normally read-only; operational approvals require their own grant.'],
  ADMINISTRATOR: ['Apply approved delegated account, configuration and integration changes; verify both intended access and expected denials.', 'Administrator is not the permanent Super Administrator role. Remain within delegated and environment boundaries.'],
  SUPER_ADMINISTRATOR: ['Govern platform identity, role policy, modules, integrations, AI settings and operational controls with immutable evidence.', 'Permanent Full Control does not bypass View-As write blocks, configured adapter readiness, external-processing consent or protected deployment approval.']
});

export const PLATFORM_GUIDES = Object.freeze([
  { id: 'sign-in', title: 'Sign in, save work and handle session expiry', steps: ['Use the authentication method configured for this environment, such as the offered Microsoft SSO or local sign-in.', 'Confirm the signed-in identity before opening work. Save in the owning module and wait for its receipt before leaving.', 'When a session expires or is invalidated, sign in again; verify the last saved record before retrying a write.', 'Sign out on shared devices. Never share a password, session token or browser storage.'], outcome: 'A valid session and verified saved work, without borrowing another user’s identity.' },
  { id: 'navigation', title: 'Modules, More, favorites, recent work and My Role in Pulse', steps: ['Use Modules/More to find the workspaces authorized by current navigation.', 'Search or filter the directory and use favorites/recent work where offered.', 'Open My Role in Pulse for assigned graphical playbooks; use step navigation, examples and motion controls as learning aids.', 'A playbook’s animation is illustrative, not a live project-status stream. Use its permitted workspace links for real work.'], outcome: 'You reach the owning workspace without treating a menu or learning role as a permission grant.' },
  { id: 'search', title: 'Use Pulse Search', steps: ['Select Search or press Ctrl+K on Windows/Linux or Command+K on macOS.', 'Enter at least two characters and review record type, title and supporting context.', 'Choose a result with the pointer or keyboard and verify the destination customer/project.', 'No result may mean scope, source readiness or no matching record; it is not permission to change roles.'], outcome: 'The selected result opens an authorized destination.' },
  { id: 'appearance', title: 'Use appearance, layout, mobile and motion preferences', steps: ['Open Appearance/display preferences and select light or dark mode.', 'Choose the supported table, enterprise or classic presentation and check the current workspace.', 'Use the available compact/mobile timesheet view for smaller screens.', 'Pause walkthrough animation or honor reduced-motion preferences when needed.', 'Report any unreadable label with route, theme and viewport details; appearance never changes access.'], outcome: 'A readable personal presentation without changing records or security roles.' },
  { id: 'profile', title: 'Profile, photo and identity details', steps: ['Open the profile/avatar menu or account surface.', 'Review the displayed identity, role and source information.', 'Update only supported personal preferences or photo fields and save.', 'Ask the directory/account owner to correct source-owned name, manager or organizational data.', 'Do not interpret profile edits as role assignment.'], outcome: 'Personal preferences are saved while directory/role ownership remains intact.' },
  { id: 'session-intelligence', title: 'Use Session Intelligence', steps: ['Open the Session Intelligence drawer.', 'Review actual/effective identity, permission context and diagnostic information available to your role.', 'Use the route, timestamp and non-secret diagnostic references for an access investigation.', 'Close the drawer to return to work; it is not an impersonation or privilege-grant tool.'], outcome: 'You can explain the active session context without exposing credentials.' },
  { id: 'view-as', title: 'Understand Administrator View-As', steps: ['Only an authorized administrator selects the intended effective user in View-As.', 'Wait for verified navigation/role context and check the preview banner.', 'Review only the effective user’s available workspaces. Writes, generation, submission, publication and sensitive exports remain blocked according to the owning module.', 'Exit View-As and re-verify your actual identity before an approved administrative change.', 'Where a module does not support effective-user preview, accept its unavailable/blocked message rather than using administrator data as that user’s result.'], outcome: 'Read-only access preview is kept separate from actual-user operational authority.' },
  { id: 'help', title: 'Ask Help and select answer detail', steps: ['Open Help/Ask Celar AI for a focused question or search this System User Guide for a procedure.', 'Choose the saved answer-detail preference and optional repository context, assumptions and citations.', 'Use /concise, /detailed, /highly-detailed, /technical, /executive or /step-by-step for a question-specific override.', 'Check the source hierarchy: reviewed guide, module/API metadata, repository documentation, permission-aware retrieval, then escalation.', 'When evidence is missing, report the gap rather than treating a plausible answer as an implemented feature.'], outcome: 'Guidance is tied to verified sources and an appropriate level of detail.' },
  { id: 'save-conflict', title: 'Save, autosave, version conflicts and uncertain writes', steps: ['Read required-field and current-state messages before Save, Submit, Approve or Confirm.', 'Wait for Saved/success or the returned receipt; a visible local edit is not proof of persistence.', 'For a revision conflict, preserve your intended edits, reload the current version and reconcile rather than overwrite another user.', 'For an unknown write outcome, inspect the record/history before retrying.', 'Save/discard work before changing project, role, record or browser session.'], outcome: 'One traceable intended change, without lost edits or duplicate operations.' },
  { id: 'documents', title: 'Upload, prepare, download and share documents', steps: ['Choose the correct project/package and supported file type/size from the owning module.', 'Wait for upload and any malware-scan, extraction/OCR or indexing result.', 'Use document/version identifiers rather than filename alone to select the correct evidence.', 'Download through the authenticated control and verify the file, version, branding and required formulas.', 'Share only the approved reviewed version through the authorized channel; an export is not approval or delivery.'], outcome: 'The intended document version remains traceable and access controlled.' },
  { id: 'ai-safety', title: 'Interpret AI progress, evidence and failures', steps: ['Check source/document readiness and the capability’s saved Module 064 configuration.', 'Follow progress and per-phase timers instead of launching overlapping requests.', 'Review citations, exclusions, quantities, versions, dates and hours; structural validation does not prove semantic correctness.', 'Preserve a usable draft after timeout/invalid output and follow the offered bounded retry/resume path.', 'Treat a safety refusal as terminal for the prohibited action, not a reason to try a different provider.', 'Do not assume all externally processed fields are anonymized; full Service Scope has explicit separate approval.'], outcome: 'AI remains an evidenced proposal under human review and the current privacy/routing policy.' },
  { id: 'notifications', title: 'Distinguish notification configuration from delivery', steps: ['Check the saved source event, accountable project relationships and intended recipients.', 'Inspect the dispatch/attempt record for queued, held, sending, sent, failed or suppressed status.', 'Resolve missing ownership, provider readiness, quiet hours or delivery-boundary issues with the appropriate administrator.', 'Reconcile an uncertain provider result before retrying to avoid duplicates.', 'Do not claim a customer or teammate received a message solely because the initiating action saved successfully.'], outcome: 'Notifications are reported according to their actual transport evidence.' },
  { id: 'errors', title: 'Troubleshoot and escalate safely', steps: ['Record the route/module, action, timestamp, selected record and exact message.', '401 usually requires a valid session; 403 indicates an access/action boundary; 400 indicates validation; 409 indicates a conflict; unavailable/5xx responses require source/service investigation.', 'Check the latest saved record before one safe refresh or retry.', 'Use the correlation/request reference and authorized Network status when available, without copying tokens or private payloads.', 'Create a defect with expected versus actual behavior, steps and sanitized screenshots; distinguish a configuration gap from a broken implemented action.'], outcome: 'Support receives reproducible evidence without secrets or unnecessary customer data.' }
]);

export const HANDOFFS = Object.freeze([
  { title: 'Customer request to delivered project', stages: [
    ['SALES / INSIDE_SALES', 'opportunities', 'Qualify the customer request and identify AE/SA ownership.'],
    ['SOLUTION_ARCHITECT', 'sow-generator', 'Create and confirm the human-reviewed SOW/GSD; resolve publication readiness.'],
    ['SA / INSIDE SALES → PTC', 'signed-handoff', 'Pass signed/approved scope and obtain an explicit receiving-owner acknowledgement.'],
    ['PROJECT_TEAM_COORDINATOR / ADMINISTRATOR', 'create-work-register', 'Create one canonical project from the reviewed GSD/SELL source.'],
    ['PROJECT_MANAGEMENT', 'project-flowhive', 'Prepare the reviewed WBS, schedule and baseline; coordinate eligible engineering owners.'],
    ['ENGINEERING / ENGINEERING_LEAD', 'project-workspace', 'Perform assigned work, record time and retain delivery evidence.'],
    ['MANAGER / PM / PTC', 'manager-approval', 'Complete only the permitted review stages.'],
    ['ACCOUNTING / BILLING', 'billing-readiness', 'Resolve financial blockers and use governed invoicing/export.'],
    ['PROJECT_MANAGEMENT / CUSTOMER OWNER', 'customer-delivery-acceptance', 'Retain actual customer acceptance and final closeout evidence.']
  ] },
  { title: 'Returned time versus administrative reallocation', stages: [
    ['AUTHORIZED REVIEWER', 'manager-approval', 'Return inaccurate worker-entered time with a specific reason.'],
    ['TIME OWNER', 'timesheet', 'Correct and resubmit the returned draft through normal review.'],
    ['PTC / SUPER ADMINISTRATOR', 'time-reallocation', 'For administrative reassignment instead, use the distinct audited no-resubmission path; do not create duplicate time.'],
    ['ACCOUNTING / PROJECT OWNER', 'workflow', 'Verify affected downstream totals and any governed lock/correction requirements.']
  ] },
  { title: 'SA absence, due work and temporary coverage', stages: [
    ['SA MANAGER / ASSIGNED SA', 'sow-generator', 'Review Team Work/My Work and save pending tracking changes.'],
    ['AUTHORIZED TRANSFER OWNER', 'sow-generator', 'Choose eligible teammate, coverage type, return date and reason.'],
    ['RECEIVING SA', 'sow-generator', 'Continue the same record, preserve reviewed versions and record return handoff notes.'],
    ['NOTIFICATION OWNER', 'notification-delivery-monitor', 'Verify assignment/due/return communication rather than infer delivery.']
  ] },
  { title: 'Financial and notification exception recovery', stages: [
    ['PM / ACCOUNTING', 'financial-operations-workbench', 'Identify the failed source or workflow blocker and owner.'],
    ['SOURCE OWNER', 'work-register', 'Correct the source record through its own authorized workflow.'],
    ['INTEGRATION / MAIL ADMINISTRATOR', 'notification-delivery-monitor', 'Reconcile unknown outcomes; retry only eligible operations.'],
    ['ACCOUNTING / BILLING', 'invoice-billing-center', 'Verify the real financial outcome and retain resolution evidence.']
  ] },
  { title: 'Issue to verified protected release', stages: [
    ['REPORTER', 'defect-tracker', 'Provide sanitized, reproducible expected/actual behavior.'],
    ['AUTHORIZED OPERATOR', 'system-diagnostics', 'Diagnose the bounded target and prepare any approved remediation.'],
    ['DEVELOPER / REVIEWER', 'cicd-pipeline', 'Review the exact change and passing checks.'],
    ['RELEASE OWNER', 'release-deployment-control', 'Use the protected target workflow, immutable artifacts and rollback baseline.'],
    ['TESTER / SERVICE OWNER', 'uat-validation', 'Verify the actual installed release and required positive/negative acceptance.']
  ] }
]);

export const GLOSSARY = Object.freeze([
  ['AE / Sales Rep / Account Executive', 'The accountable sales role for an assigned customer/opportunity. A different display title is not a new grant.'],
  ['SA / SAA', 'Solution-architecture learning aliases where mapped by the current role model; actual organizational assignment and action authority remain server controlled.'],
  ['PTC', 'Project Team Coordinator, with specific operational stewardship and project-creation responsibilities; distinct from Project Coordinator.'],
  ['Service Scope / Service Overview', 'Author-entered source requirements versus the generated or manually reviewed narrative. They must not overwrite each other implicitly.'],
  ['SOW / GSD / LOE', 'Statement of Work, the paired estimate document, and level of effort. Reviewed task data drives both artifacts.'],
  ['WBS / baseline / working draft', 'Work breakdown structure, an explicitly reviewed reference version, and an editable planning copy. Generation is not baseline approval.'],
  ['Plan / Design / Implement / Validate / Release', 'The five delivery phases used by the SOW/GSD and sequential planning workflows.'],
  ['Allocation / logged / available hours', 'Assigned capacity, recorded effort and their remaining balance. These are not automatically approved effort or an estimate to complete.'],
  ['BoH / Block of Hours', 'A selected prepaid funding contract. Entered/submitted usage is distinguished from approved consumption and projected remaining balance.'],
  ['Normal / Afterhours', 'Separate work-time classifications governed by the company and approval policy; after-hours work is not an implicit approval.'],
  ['Not Set / No Access', 'Existing authorization behavior is retained versus an explicit denial. Neither is interchangeable with a permission grant.'],
  ['View / Create/Edit / Approve / Manage / Administer / Full Control', 'Role-policy presets; each module still checks the specific action, current record state and data scope.'],
  ['Actual user / effective user / View-As', 'The authenticated actor, the identity whose permitted view is evaluated, and a read-only administrative preview.'],
  ['Draft / confirmed / archived', 'Editable or proposed work, a completed review gate, and retained inactive history. Exact transitions depend on the owning module.'],
  ['Preview / save / adopt / publish', 'Calculate without commitment, persist a record, explicitly apply a reviewed proposal, or complete a governed publication. They are different actions.'],
  ['Configured / available / ready / verified', 'Settings exist, a tested endpoint responded, prerequisites are satisfied, or a particular outcome was checked. One does not prove the others.'],
  ['Queued / held / sending / sent / failed / suppressed', 'Notification processing states; only actual transport evidence supports a delivery claim, and uncertain sends require reconciliation.'],
  ['Source unavailable / partial / no data', 'A failed source, incomplete source coverage, or a successful query with no matching records. Do not substitute zero.'],
  ['Revision / immutable audit / correlation ID', 'A record version, retained attributable action evidence and a reference used to trace related requests or events.'],
  ['Test / Protected UAT / Production', 'Different operating environments. An approved Test action never automatically authorizes Production.']
]);
