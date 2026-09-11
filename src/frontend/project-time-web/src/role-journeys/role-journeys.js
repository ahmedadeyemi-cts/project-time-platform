// Maintained learning content only. This catalog does not grant roles or permissions.
export const JOURNEY_VERSION = '1.0.0';
export const JOURNEY_REVIEW = 'Draft playbooks — business-owner review required';
export const JOURNEY_ROUTE = 'my-role-in-pulse';
export const LIFECYCLE = Object.freeze([
  ['intake', 'Customer need & intake'], ['scope', 'Solution & SOW'],
  ['handoff', 'Delivery handoff'], ['plan', 'Plan'], ['design', 'Design'],
  ['implement', 'Implement'], ['validate', 'Validate'], ['release', 'Release'],
  ['closeout', 'Reconcile & close']
]);

// Each tuple is [title, stage, module route, input, action, output, acceptance,
// receiving role, exception]. Step IDs are local to a versioned learning playbook.
const seed = (title, goal, value, boundary, incoming, steps) => ({
  title, goal, value, boundary, incoming,
  steps: steps.map(([name, stage, route, input, action, output, acceptance, nextOwner, exception], index) => ({
    id: `step-${index + 1}`, title: name, stage, route, input, action, output, acceptance, nextOwner, exception
  }))
});

export const ROLE_JOURNEYS = Object.freeze({
  SALES: seed('Sales / Account Executive', 'hand off customer needs and approved commitments', 'delivery understands what was sold',
    'Work within assigned customers. Do not approve technical scope or promise unconfirmed delivery capacity.', 'Customer / opportunity owner', [
    ['Capture the need', 'intake', 'sales-intake', 'Customer goals, contacts and requested timing.', 'Record the need, business outcome and known constraints. Identify missing facts.', 'An actionable intake request.', 'Customer, business need and accountable owner are identified.', 'Solution Architect', 'Return to the customer for missing requirements; do not guess.'],
    ['Coordinate the scope', 'scope', 'sow-generator', 'Intake and discovery findings.', 'Coordinate with the Solution Architect on deliverables, exclusions and estimates.', 'A reviewed scope proposal.', 'Technical review is complete and open questions have owners.', 'Inside Sales', 'Route scope questions to the Solution Architect before making commitments.'],
    ['Prepare the handoff', 'handoff', 'signed-handoff', 'Approved commercial package and required signed documents.', 'Check commitments, dependencies and document references with the receiving delivery owner.', 'A complete delivery handoff.', 'The receiving owner accepts the package or records what is missing.', 'Project Manager', 'Resolve a returned handoff with its named owner before treating it as ready.'],
    ['Follow the outcome', 'release', 'sales-insights', 'Authorized delivery updates and customer feedback.', 'Review progress and route customer changes through the approved scope-change process.', 'A traceable customer follow-up.', 'The delivery owner acknowledges any new request.', 'Project Manager / account team', 'Do not treat a new customer request as already-approved project scope.']
  ]),
  SOLUTION_ARCHITECT: seed('Solution Architect', 'develop and review a technically complete SOW', 'delivery receives achievable scope and clear acceptance requirements',
    'Use assigned engagements and existing document locations. AI output is a draft, not approval.', 'Sales / customer discovery', [
    ['Understand requirements', 'scope', 'sow-generator', 'Customer goals, discovery notes and known constraints.', 'Identify deliverables, dependencies, exclusions and unresolved questions.', 'A scoped requirements baseline.', 'Unknowns are explicit and have owners.', 'Solution Architect', 'Request missing facts rather than inventing technical requirements.'],
    ['Develop detailed scope', 'scope', 'sow-generator', 'Requirements and the selected SOW/GSD workspace.', 'Draft Plan, Design, Implement, Validate and Release activities, inputs, outputs and level of effort.', 'A detailed draft SOW and GSD.', 'Each phase has meaningful activities and justified estimates.', 'Solution Architect reviewer', 'If generation is unavailable or thin, retain the draft and resolve the gaps; do not confirm it.'],
    ['Review and confirm', 'validate', 'sow-generator', 'Draft scope, assumptions and reviewed estimates.', 'Review technical accuracy, customer responsibilities and acceptance criteria before confirmation.', 'A human-reviewed package.', 'Required review and confirmation checks pass in the authorized workspace.', 'Sales / Inside Sales', 'Resolve rejected review items and reconfirm through the existing workflow.'],
    ['Hand off the package', 'handoff', 'signed-handoff', 'Reviewed documents and the required commercial approvals.', 'Reference the approved package in its existing location and explain delivery dependencies.', 'A traceable scope handoff.', 'The receiving owner can identify the correct approved document version.', 'Project Manager', 'Do not claim a SELL upload or notification succeeded without a verified receipt.']
  ]),
  INSIDE_SALES: seed('Inside Sales Representative', 'maintain complete commercial supporting records', 'quotes and delivery handoffs remain traceable',
    'Work within assigned customers and authorized sales records. Commercial support does not grant technical approval.', 'Account Executive / Solution Architect', [
    ['Review the request', 'intake', 'sales-intake', 'Assigned opportunity and customer details.', 'Confirm the account team, requested package and missing commercial information.', 'An owned commercial request.', 'Customer and supporting contacts are correct.', 'Inside Sales', 'Return incomplete information to the Account Executive.'],
    ['Associate references', 'scope', 'customer-directory', 'Approved quote and customer references.', 'Check the source record and associate the correct supporting references using the existing workflow.', 'Traceable commercial references.', 'References identify the right customer and current package.', 'Account Executive', 'Do not substitute another customer record to work around missing data.'],
    ['Check readiness', 'handoff', 'signed-handoff', 'Scope package, commercial approvals and required documents.', 'Check completeness with Sales and the Solution Architect before delivery handoff.', 'A readiness decision with remaining gaps.', 'Required documents and approvals can be located.', 'Project Manager', 'A queued external submission is not proof that documents were uploaded.'],
    ['Resolve returned items', 'handoff', 'sales-intake', 'A returned handoff with reasons.', 'Coordinate the missing items and ask the receiving owner to review the corrected package.', 'A corrected commercial handoff.', 'The receiving owner acknowledges the corrected information.', 'Account Executive / Project Manager', 'Escalate unresolved ownership; do not mark the handoff accepted on another role’s behalf.']
  ]),
  PROJECT_MANAGEMENT: seed('Project Manager', 'turn approved scope into an accountable delivery plan', 'the team can execute and complete the agreed work',
    'Manage assigned projects. An AI draft is not an approved plan; billing rates are not internal labor costs.', 'Sales / Solution Architect', [
    ['Accept the handoff', 'handoff', 'signed-handoff', 'Approved SOW, commercial readiness and delivery dependencies.', 'Check completeness and confirm the receiving delivery owner.', 'An accepted handoff or a documented return.', 'Scope, commitments and missing items are understood.', 'Project Manager', 'Return incomplete handoffs with specific reasons and an owner.'],
    ['Build the work plan', 'plan', 'project-flowhive', 'Approved SOW in its existing location, project dates and dependencies.', 'Review a phase-aligned work breakdown across Plan, Design, Implement, Validate and Release. Resolve assumptions before adoption.', 'A reviewed work breakdown with owners and dates.', 'Work belongs in the planner work breakdown, not automatically in project milestones. Draft planning behavior still requires release-specific validation.', 'Engineering Lead / assigned engineers', 'Missing scope or failed AI generation must not overwrite a usable plan.'],
    ['Manage delivery', 'implement', 'project-risk-register', 'Progress, issues, risk evidence and authorized financial information.', 'Coordinate dependencies and risk responses. Distinguish approved budget, logged hours, approved hours and current estimate to complete.', 'Owned actions and a current delivery assessment.', 'Risks have owners; missing costs or estimates remain unknown, not zero.', 'Delivery team / Manager', 'Escalate scope, capacity and cost conflicts; do not invent rates or remaining-effort estimates.'],
    ['Validate and close', 'closeout', 'project-closeout', 'Delivery evidence, acceptance and outstanding obligations.', 'Confirm release readiness, acceptance, documentation and required financial handoff.', 'An evidenced closeout package.', 'Required acceptance and reconciliation checks are satisfied by authorized owners.', 'Accounting / customer-facing owner', 'Keep unresolved obligations visible and follow the governed reopen process when needed.']
  ]),
  PROJECT_MANAGEMENT_LEAD: seed('Project Management Lead', 'review risks across my authorized portfolio', 'project managers receive timely support and escalation decisions',
    'Oversee the managed PM team and authorized portfolio, not every project by default.', 'Project Managers', [
    ['Review the portfolio', 'plan', 'project-workload', 'Authorized projects, ownership and delivery commitments.', 'Identify unowned work, cross-project dependencies and capacity concerns.', 'A prioritized portfolio review.', 'Every exception has a named owner.', 'Project Managers', 'Resolve ownership gaps before promising dates.'],
    ['Review exceptions', 'implement', 'project-risk-register', 'Escalated risks, issues and decisions.', 'Compare business impact and agree a response with accountable owners.', 'A documented escalation decision.', 'Decision, owner and follow-up expectations are recorded.', 'Manager / Project Manager', 'Escalate decisions outside your authority.'],
    ['Coordinate recovery', 'validate', 'project-workload', 'Agreed corrective actions and current project plans.', 'Coordinate cross-project actions without replacing each Project Manager’s accountability.', 'An owned recovery plan.', 'Affected owners acknowledge dependencies and commitments.', 'Project Managers', 'Do not silently move resources or dates without the required approval.'],
    ['Verify outcomes', 'closeout', 'reporting', 'Updated evidence and portfolio results.', 'Review whether actions resolved the original delivery risks.', 'An evidence-based portfolio update.', 'Unresolved exceptions remain visible.', 'Executive / delivery leadership', 'Do not equate a green dashboard with verified acceptance.']
  ]),
  ENGINEERING: seed('Engineer', 'understand and complete my assigned technical work', 'the customer receives the agreed result with delivery evidence',
    'Work on your assignments and own time. You do not approve your own time or close the whole project.', 'Project Manager / Engineering Lead', [
    ['Understand the assignment', 'plan', 'project-workspace', 'Assigned task, approved scope and acceptance requirements.', 'Review the correct documents, dependencies and definition of done.', 'A clear understanding of the assigned work.', 'The task and required access are available; ambiguities are raised.', 'Engineer', 'Ask the Project Manager or Engineering Lead to resolve missing scope or assignment.'],
    ['Deliver and record', 'implement', 'timesheet', 'An authorized assignment and work actually performed.', 'Perform the agreed work and record accurate time against the correct task with a meaningful description.', 'Progress, work evidence and draft time.', 'Time describes actual work and references the correct assignment.', 'Engineer / Engineering Lead', 'Stop and raise scope or access blockers; do not charge time to an unrelated task.'],
    ['Submit my time', 'validate', 'timesheet', 'Reviewed draft time with complete descriptions.', 'Save, resolve validation messages and submit your own time through the existing approval workflow.', 'Submitted time for authorized review.', 'Pulse confirms submission; a saved draft alone is not submitted.', 'Authorized time approver', 'When returned, read the reason, correct your records and resubmit as required.'],
    ['Hand off evidence', 'release', 'project-workspace', 'Completed work, test results and known limitations.', 'Provide validation evidence and explain outstanding items to the delivery owner.', 'A reviewable technical handoff.', 'The receiving owner can verify the acceptance requirements.', 'Project Manager / Engineering Lead', 'Request closeout applies only to eligible request work; project closeout stays with its authorized owner.']
  ]),
  ENGINEERING_LEAD: seed('Engineering Lead', 'coordinate technical work across my authorized team', 'assignments match skills and delivery requirements',
    'Coordinate the authorized functional team. Lead responsibility does not automatically grant every approval.', 'Project Manager / Manager', [
    ['Review technical demand', 'plan', 'project-workload', 'Approved scope, requested dates and skill needs.', 'Identify technical dependencies and the skills needed for delivery.', 'A technical readiness assessment.', 'Constraints and required skills are explicit.', 'Manager / Engineering Lead', 'Raise missing requirements before assigning work.'],
    ['Coordinate assignments', 'design', 'project-workspace', 'Approved staffing decisions and authorized project assignments.', 'Coordinate suitable engineers and clarify responsibilities with the Project Manager.', 'Acknowledged technical assignments.', 'Each engineer understands scope and acceptance requirements.', 'Engineers', 'Resolve capacity or ownership conflicts through the Manager and Project Manager.'],
    ['Support execution', 'implement', 'project-workspace', 'Engineer progress and technical blockers.', 'Review technical questions and coordinate resolution without expanding scope informally.', 'Documented technical direction.', 'Decisions and affected dependencies are communicated.', 'Engineers / Project Manager', 'Route scope changes for approval rather than instructing unapproved extra work.'],
    ['Review the outcome', 'validate', 'project-workspace', 'Test evidence, documentation and outstanding issues.', 'Check technical completeness and provide the findings to the delivery owner.', 'A technical review handoff.', 'Evidence supports the agreed acceptance criteria.', 'Project Manager', 'Keep failed checks open until resolved or formally accepted by the authorized owner.']
  ]),
  MANAGER: seed('Manager', 'understand capacity, assignments and time requiring my review', 'staffing and approval decisions are supported by evidence',
    'Operate within direct and indirect reports and the module’s server-enforced scope.', 'Sales / Project Management / team members', [
    ['Review demand and capacity', 'plan', 'capacity-pipeline-forecast', 'Authorized demand, current workload and available capacity.', 'Compare upcoming needs with team availability and skills.', 'A capacity assessment.', 'Assumptions and unavailable data are identified.', 'Manager / Engineering Lead', 'Do not promise capacity from incomplete or stale information.'],
    ['Coordinate staffing', 'design', 'project-workload', 'Approved scope and staffing priorities.', 'Agree assignments with delivery and engineering owners within your authority.', 'A coordinated staffing decision.', 'Affected owners acknowledge the assignment.', 'Project Manager / Engineering Lead', 'Escalate conflicting priorities rather than silently overallocating staff.'],
    ['Review submitted time', 'validate', 'manager-approval', 'Submitted time at your authorized approval stage.', 'Review the evidence and approve or return it with a clear reason.', 'A recorded approval decision.', 'The decision is saved at the correct stage for the correct person.', 'Next authorized approver / time owner', 'Return unclear entries for correction; do not approve on assumed work.'],
    ['Address exceptions', 'implement', 'utilization', 'Authorized workload and utilization exceptions.', 'Discuss the cause, agree corrective action and review its outcome.', 'An owned management action.', 'The owner and follow-up are clear.', 'Team member / delivery leadership', 'Do not interpret utilization alone as proof of individual performance.']
  ]),
  PROJECT_TEAM_COORDINATOR: seed('Project Team Coordinator', 'resolve time-record exceptions with traceable corrections', 'operational records remain accurate and auditable',
    'Do not submit another person’s timesheet or perform platform configuration. Use only the specific authorized correction path.', 'Time owner / authorized approver', [
    ['Identify the exception', 'validate', 'time-compliance', 'The affected person, time record and correction reason.', 'Check the record, task association and supporting evidence.', 'An identified exception with an owner.', 'The intended correction is supported by evidence.', 'Project Team Coordinator', 'Ask the time owner for clarification when the record is ambiguous.'],
    ['Choose the correction path', 'validate', 'timesheet', 'The record state and required correction type.', 'Distinguish a worker-return workflow from an authorized administrative reallocation. Do not combine their rules.', 'A selected authorized correction path.', 'The path and reason match the actual exception.', 'Time owner / Project Team Coordinator', 'A worker correction may require resubmission; administrative reallocation must not be described as that workflow.'],
    ['Reallocate with evidence', 'validate', 'time-reallocation', 'An authorized administrative reallocation and valid target assignment.', 'Use Module 001B for supported reallocation and retain the required reason and before-and-after evidence.', 'A traceable reallocation receipt.', 'Administrative reallocation preserves its governed status and does not require worker resubmission or new Manager/PM approval.', 'Project Team Coordinator / reconciliation owner', 'Do not force a Draft transition, impersonate the worker or discard audit history.'],
    ['Check reconciliation', 'closeout', 'workflow', 'Correction receipt and affected downstream records.', 'Verify the result and route remaining reconciliation exceptions to their accountable owner.', 'An evidenced exception handoff.', 'The final state and any remaining issue are clear.', 'Accounting / authorized approver', 'A correction receipt is not proof that every downstream export has been reconciled.']
  ]),
  PROJECT_COORDINATOR: seed('Project Coordinator', 'coordinate delivery information and follow-ups', 'project owners receive complete and timely handoffs',
    'Project coordination is not the Project Team Coordinator’s time-steward authority. Follow explicitly granted access.', 'Project Manager', [
    ['Review assigned coordination', 'plan', 'signed-handoff', 'Assigned project, contacts and coordination request.', 'Confirm the accountable Project Manager and required information.', 'A clear coordination checklist.', 'Scope and ownership of the request are known.', 'Project Coordinator', 'Ask the Project Manager to clarify an unowned request.'],
    ['Check supporting information', 'handoff', 'signed-handoff', 'Required documents and source references.', 'Identify missing information and request it from its owner.', 'A completeness update.', 'The Project Manager can locate the required package.', 'Project Manager', 'Do not replace an approved document with an unreviewed copy.'],
    ['Track follow-ups', 'implement', 'project-intake', 'Agreed actions and requested response dates.', 'Coordinate status updates and escalate overdue dependencies.', 'An owned follow-up record.', 'Each outstanding item has an accountable owner.', 'Project Manager / assigned owner', 'Escalate rather than approving work outside your authority.'],
    ['Return the coordination result', 'closeout', 'signed-handoff', 'Updated information and unresolved items.', 'Summarize what is ready and what still needs a decision.', 'A clear handoff to the Project Manager.', 'The receiving owner acknowledges the result.', 'Project Manager', 'Do not represent coordination completion as project acceptance.']
  ]),
  ACCOUNTING: seed('Accounting', 'reconcile authorized time and commercial evidence', 'financial records and exports are supported by complete sources',
    'Use authorized financial information. Approval-stage access and billing authority remain separately governed.', 'Approved time workflow / delivery owner', [
    ['Review eligible records', 'closeout', 'workflow', 'Records that reached the required approval stage.', 'Confirm eligibility and identify incomplete or returned records.', 'A reconciliation worklist.', 'Ineligible records remain excluded.', 'Accounting', 'Return source exceptions to their owner instead of treating missing data as zero.'],
    ['Reconcile sources', 'closeout', 'billing-readiness', 'Approved time, expenses and authoritative commercial terms.', 'Check supporting evidence and distinguish internal labor cost from billing rates.', 'A reconciled financial basis.', 'Rate purpose, currency and applicable period are known.', 'Accounting / Billing', 'Unverified rates or absent source records remain explicit blockers.'],
    ['Prepare the authorized export', 'closeout', 'workflow', 'Eligible reconciled records and required export checks.', 'Prepare and validate the package through the existing governed workflow.', 'A validated export package.', 'Preflight checks pass and the package can be traced to its sources.', 'Accounting / downstream system owner', 'A downloaded file is not proof the receiving system accepted it.'],
    ['Record the outcome', 'closeout', 'invoice-billing-center', 'Verified downstream outcome and reconciliation evidence.', 'Record the result and retain unresolved exceptions for follow-up.', 'A traceable reconciliation status.', 'Accepted, rejected and unknown outcomes remain distinct.', 'Delivery owner / financial leadership', 'Do not lock or report completion before the required reconciliation is verified.']
  ]),
  BILLING: seed('Billing / Finance', 'prepare billing from eligible reconciled records', 'customer charges have a defensible evidence trail',
    'Billing duties do not grant time-entry, engineering or platform-administration access.', 'Accounting / delivery owner', [
    ['Check billing readiness', 'closeout', 'billing-readiness', 'Approved commercial terms and eligible records.', 'Confirm billing eligibility and review outstanding blockers.', 'A billing readiness assessment.', 'Required approvals and sources are present.', 'Billing', 'Hold incomplete items rather than inventing missing charges.'],
    ['Review the billing basis', 'closeout', 'invoice-billing-center', 'Contract terms, billable time and expenses.', 'Reconcile the proposed billing basis to the authorized source records.', 'A reviewable billing package.', 'Currency, rate purpose and applicable period match the contract.', 'Accounting / Billing', 'Do not substitute internal cost rates for customer billing rates.'],
    ['Process through the governed workflow', 'closeout', 'invoice-billing-center', 'Reviewed package and required authorization.', 'Perform only the billing actions granted by the current workflow.', 'A recorded billing action.', 'The application confirms the actual action taken.', 'Accounting / downstream billing owner', 'Queued or downloaded does not mean invoiced, posted or delivered.'],
    ['Resolve billing exceptions', 'closeout', 'financial-operations-workbench', 'Rejected, incomplete or uncertain billing outcomes.', 'Assign and follow up on exceptions with supporting evidence.', 'An owned resolution record.', 'The outcome is verified before closing the exception.', 'Accounting / delivery owner', 'Preserve original evidence while following the correction process.']
  ]),
  EXECUTIVE: seed('Executive', 'review reliable delivery and business indicators', 'leadership can identify risks and assign informed actions',
    'Organization-wide visibility is normally read-only; a leadership title is not an operational approval.', 'Delivery and financial leadership', [
    ['Review indicators', 'plan', 'reporting', 'Authorized portfolio, utilization and delivery reports.', 'Review outcomes, reporting dates and source limitations.', 'A focused business review.', 'Reported facts are distinguished from estimates and unavailable data.', 'Executive', 'Ask for clarification when sources are stale or incomplete.'],
    ['Investigate exceptions', 'implement', 'reporting', 'A material variance or risk.', 'Request the supporting evidence and explanation from its accountable owner.', 'An understood business exception.', 'Business impact and uncertainty are clear.', 'Delivery / financial owner', 'Do not infer root cause from a dashboard indicator alone.'],
    ['Assign a decision owner', 'validate', 'reporting', 'Evidence, options and the appropriate decision authority.', 'Agree the action and accountable owner through the established governance process.', 'An owned leadership action.', 'The owner acknowledges the decision and follow-up expectations.', 'Manager / delivery leadership', 'Do not use reporting access to bypass operational approvals.'],
    ['Review results', 'closeout', 'reporting', 'Updated evidence and the agreed outcome.', 'Review whether the action resolved the original business concern.', 'An evidence-based outcome review.', 'Unresolved risks remain visible.', 'Leadership team', 'A completed activity is not necessarily the intended business result.']
  ]),
  ADMINISTRATOR: seed('Administrator', 'apply authorized platform configuration changes', 'users receive the intended access without unintended privilege',
    'Use delegated administrative authority. This playbook does not imply the Super Administrator’s permanent Full Control.', 'Approved administrative request', [
    ['Review the request', 'intake', 'user-admin', 'An approved access or configuration request.', 'Confirm the intended user, requested change and approving owner.', 'A validated administrative request.', 'Required authorization is present.', 'Administrator', 'Return unapproved or ambiguous requests.'],
    ['Assess the boundary', 'plan', 'roles-permissions-matrix', 'Current permissions and requested scope.', 'Check the affected roles, scope and separation of duties.', 'An understood access impact.', 'The requested change stays within delegated authority.', 'Administrator / authorized security owner', 'Escalate changes outside your authority.'],
    ['Apply the approved change', 'implement', 'user-admin', 'Approved change details and the authorized control.', 'Apply the intended change using the existing administrative workflow.', 'A recorded configuration change.', 'The application confirms the change; unrelated access is preserved.', 'Administrator', 'Do not expose credentials or broaden access to work around a failure.'],
    ['Verify and retain evidence', 'validate', 'audit-history', 'Change result and audit evidence.', 'Verify intended access and retain the reason and outcome.', 'A traceable administrative handoff.', 'Both expected access and restricted actions are checked.', 'Requesting owner', 'Report failed validation; do not claim access is correct from a successful save alone.']
  ]),
  SUPER_ADMINISTRATOR: seed('Super Administrator', 'govern platform roles and configuration', 'Pulse remains consistent with approved controls',
    'Platform authority does not remove change approval, audit, or environment boundaries.', 'Approved platform / security request', [
    ['Validate authorization', 'intake', 'role-admin', 'Approved change request and intended outcome.', 'Confirm the owner, environment and exact scope of the change.', 'An authorized configuration scope.', 'The request is approved for the target environment.', 'Super Administrator', 'Do not treat a request to prepare a change as permission to deploy it.'],
    ['Assess impact', 'plan', 'roles-permissions-matrix', 'Current policy and affected roles.', 'Review least privilege, record scope and the impact on existing users.', 'A reviewed policy change.', 'Existing protected boundaries and required invariants are preserved.', 'Authorized reviewer', 'Resolve conflicts or unclear permissions before publication.'],
    ['Publish through governance', 'implement', 'role-admin', 'Reviewed policy and required validation.', 'Use the existing validation and publication controls; retain immutable policy history.', 'A governed configuration version.', 'Required validation passes and the correct version is published.', 'Super Administrator', 'Do not bypass validation, erase history or copy credentials into evidence.'],
    ['Verify the effective result', 'validate', 'audit-history', 'Published version and access evidence.', 'Check allowed and denied actions, effective-user scope and the audit trail.', 'A verified change record.', 'The result matches the approved request without transferring View-As authority.', 'Requesting owner / security owner', 'Use the governed restore process if verification fails.']
  ])
});

const ALIASES = Object.freeze({
  ENGINEER: 'ENGINEERING', ENGINEERING_TEAM_LEAD: 'ENGINEERING_LEAD', ENGINEERING_MANAGER: 'ENGINEERING_LEAD',
  PROJECT_MANAGER: 'PROJECT_MANAGEMENT', PM_TEAM_LEAD: 'PROJECT_MANAGEMENT_LEAD', PROJECT_MANAGEMENT_TEAM_LEAD: 'PROJECT_MANAGEMENT_LEAD',
  PEOPLE_MANAGER: 'MANAGER', ACCOUNT_EXECUTIVE: 'SALES', ACCOUNT_EXECUTIVES: 'SALES', SALES_MANAGER: 'SALES',
  RESALE: 'INSIDE_SALES', ARCHITECT: 'SOLUTION_ARCHITECT', SA: 'SOLUTION_ARCHITECT', SAA: 'SOLUTION_ARCHITECT',
  PTC: 'PROJECT_TEAM_COORDINATOR', ACCOUNTING_BILLING: 'BILLING', FINANCE: 'BILLING'
});
export function canonicalRole(code) {
  const normalized = String(code || '').trim().toUpperCase().replace(/[\s-]+/g, '_');
  return ALIASES[normalized] || normalized;
}
export function roleCatalog(guidance = {}, assignedCodes = []) {
  const assigned = new Set(assignedCodes.map(canonicalRole).filter(Boolean));
  const codes = new Set([...Object.keys(ROLE_JOURNEYS), ...Object.keys(guidance), ...assigned].map(canonicalRole));
  return [...codes].map((code) => {
    const journey = ROLE_JOURNEYS[code];
    const source = guidance[code] || {};
    return {
      ...journey, code, assigned: assigned.has(code),
      title: journey?.title || source.title || code.replaceAll('_', ' '),
      purpose: source.purpose || journey?.value || 'Role-specific guidance has not yet been reviewed.',
      boundary: journey?.boundary || source.boundary || 'Use only access explicitly granted to this role.',
      sourceBoundary: source.boundary || '',
      steps: journey?.steps || []
    };
  }).sort((a, b) => Number(b.assigned) - Number(a.assigned) || a.title.localeCompare(b.title));
}
export function userStory(role, step = null) {
  if (!role.goal || !role.value) return 'A reviewed user story is not yet available for this role.';
  return step
    ? `As ${/^[AEIOU]/i.test(role.title) ? 'an' : 'a'} ${role.title}, I want to ${step.title.toLowerCase()} so that I can provide ${step.output.charAt(0).toLowerCase()}${step.output.slice(1).replace(/\.$/, '')}.`
    : `As ${/^[AEIOU]/i.test(role.title) ? 'an' : 'a'} ${role.title}, I want to ${role.goal} so that ${role.value}.`;
}
export function matchesJourney(role, query) {
  const words = String(query || '').trim().toLowerCase().split(/\s+/).filter(Boolean);
  const haystack = [role.title, role.code, role.goal, role.value, ...role.steps.flatMap((step) => [step.title, step.action, step.route, step.output, step.exception])].join(' ').toLowerCase();
  return words.every((word) => haystack.includes(word));
}
export function validateJourneys(registryRoutes = null) {
  const errors = [];
  const stages = new Set(LIFECYCLE.map(([id]) => id));
  for (const [role, journey] of Object.entries(ROLE_JOURNEYS)) {
    if (!journey.goal || !journey.value || !journey.boundary || journey.steps.length < 3) errors.push(`${role}: incomplete playbook`);
    const ids = new Set();
    for (const step of journey.steps) {
      if (ids.has(step.id)) errors.push(`${role}: duplicate ${step.id}`);
      ids.add(step.id);
      for (const field of ['id', 'title', 'stage', 'route', 'input', 'action', 'output', 'acceptance', 'nextOwner', 'exception']) {
        if (typeof step[field] !== 'string' || !step[field].trim()) errors.push(`${role}/${step.id}: missing ${field}`);
      }
      if (!stages.has(step.stage)) errors.push(`${role}/${step.id}: unknown phase`);
      if (!/^[a-z0-9-]+$/.test(step.route)) errors.push(`${role}/${step.id}: invalid route`);
      if (registryRoutes && !registryRoutes.has(step.route)) errors.push(`${role}/${step.id}: unregistered route ${step.route}`);
    }
  }
  return errors;
}
