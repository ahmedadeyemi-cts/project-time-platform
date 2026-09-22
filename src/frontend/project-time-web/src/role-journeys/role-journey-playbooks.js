import { canonicalRole, roleCatalog } from './role-journeys.js';

export const EXPERIENCE_VERSION = '2.0.0';
// These additional spellings are present in Module025SowGsdModule.cs. They are
// educational aliases, never permission or role-assignment writes.
const CONTENT_ALIASES = Object.freeze({
  SOLUTIONS_ARCHITECT: 'SOLUTION_ARCHITECT',
  SOLUTIONS_ARCHITECT_MANAGER: 'SOLUTION_ARCHITECT_MANAGER',
  SALES_ACCOUNT_EXECUTIVE: 'SALES',
  INSIDE_SALES_REPRESENTATIVE: 'INSIDE_SALES', SALES_SUPPORT: 'INSIDE_SALES',
  SYSTEM_ADMINISTRATOR: 'ADMINISTRATOR'
});
export function journeyRoleCode(value) {
  const code = canonicalRole(value);
  return CONTENT_ALIASES[code] || code;
}
const step = (id, title, stage, route, input, action, output, acceptance, nextOwner, exception) =>
  ({ id, title, stage, route, input, action, output, acceptance, nextOwner, exception });

const SA_MANAGER = Object.freeze({
  code: 'SOLUTION_ARCHITECT_MANAGER', title: 'Solution Architect Manager',
  goal: 'oversee the SOW and GSD work in my reporting scope',
  value: 'the team has clear ownership and complete handoffs',
  purpose: 'Review scoped SA workload and coordinate authorized coverage and transfers.',
  boundary: 'Reporting-scope visibility is not permission to edit every SOW. Module 025 separately authorizes each transfer and authoring action; View-As remains read-only.',
  incoming: 'Sales / assigned Solution Architects',
  steps: [
    step('sa-manager-demand', 'Review team demand', 'scope', 'sow-generator', 'Assigned team work, due dates and unresolved requests.', 'Review the team-work view and identify unowned or overdue scope within your reporting boundary.', 'An owned workload review.', 'Outstanding work has an accountable SA and missing facts are explicit.', 'Assigned Solution Architect', 'Do not treat a missing team record as permission to view another department.'),
    step('sa-manager-coverage', 'Coordinate coverage', 'plan', 'sow-generator', 'A coverage need and eligible transfer options.', 'Use the authorized transfer or temporary-coverage control when it is offered, with a reason. Otherwise ask the authorized owner to act.', 'A traceable coverage decision.', 'The workspace confirms the change and retains its history.', 'Receiving Solution Architect', 'A manager title alone does not grant document-edit or transfer authority.'),
    step('sa-manager-review', 'Track review readiness', 'validate', 'sow-generator', 'Phase review status and the SA-owned package.', 'Identify missing Plan, Design, Implement, Validate and Release reviews and return questions to the author.', 'An explicit readiness assessment.', 'Required review evidence is visible; the author completes the applicable confirmation gate.', 'Solution Architect / account team', 'Do not confirm a package on behalf of an author without explicit workspace authority.'),
    step('sa-manager-handoff', 'Verify the handoff', 'handoff', 'signed-handoff', 'The reviewed package and receiving owner.', 'Check acceptance, returned items and notification evidence with the accountable owner.', 'An acknowledged or explicitly returned handoff.', 'Pending, failed and acknowledged outcomes are distinguished.', 'Project Manager / Inside Sales', 'An attempted notification is not proof of delivery.')
  ]
});

// Insert role-specific operational touchpoints beside the existing baseline
// steps. Keep the reviewed baseline immutable and all route names canonical.
const additions = Object.freeze({
  SOLUTION_ARCHITECT: [
    { after: 'step-3', item: step('sa-package-lifecycle', 'Manage package versions and coverage', 'handoff', 'sow-generator', 'A reviewed package, ownership and the required handoff.', 'Use available draft deletion, archive, reopen or transfer controls only for eligible records. Check ConnectWise SELL handoff and notification receipts rather than assuming success.', 'A traceable package lifecycle.', 'The correct package and owner are recorded and required review gates remain intact.', 'Inside Sales / receiving SA / Project Manager', 'Retain the usable draft after failed generation; never work around an incomplete review gate.') }
  ],
  PROJECT_MANAGEMENT: [
    { after: 'step-1', item: step('pm-project-record', 'Create or maintain the project record', 'handoff', 'create-work-register', 'An accepted handoff and the approved GSD or ConnectWise SELL source.', 'When authorized, create the project in Module 055D; use Module 055C for an existing project rather than creating a duplicate.', 'An owned project record.', 'The project references the correct customer, approved source and delivery owner.', 'Project Manager / Engineering Lead', 'Module 020 is intake and resource handoff, not the authoritative project-creation workspace.') },
    { after: 'step-3', item: step('pm-time-and-cost', 'Review project time and cost exceptions', 'validate', 'manager-approval', 'Submitted project time at an authorized approval stage and documented exceptions.', 'Review time within assigned-project scope; approve or return only when the approval control is granted. Use Cost Alerts and Project Workload to investigate delivery exposure.', 'An evidenced approval or return and owned follow-up.', 'The saved decision identifies the right person, project and approval stage.', 'Time owner / next authorized approver', 'Do not infer missing cost rates, approve personal time, or treat dashboard access as approval authority.') }
  ],
  ENGINEERING: [
    { after: 'step-4', item: step('engineer-request-closeout', 'Close eligible request work', 'release', 'engineer-task-closeout', 'An eligible assigned Service Request, Pre-Sales or Internal task and completion evidence.', 'Use Engineer Request Closeout when the task qualifies. Review the billing lock and follow the governed reopen path for corrections.', 'A request-closeout receipt.', 'The eligible task is confirmed closed and the required history is retained.', 'Project Team Coordinator / delivery owner', 'This does not close an entire project or authorize closing another engineer’s request.') }
  ],
  MANAGER: [
    { after: 'step-2', item: step('manager-sa-work', 'Review scoped SOW team work', 'scope', 'sow-generator', 'Authorized SA reporting relationships and due work.', 'Where Module 025 grants access, review team workload and coordinate eligible transfer or temporary coverage. Keep author review responsibilities with the SA.', 'An owned team-work follow-up.', 'Coverage and pending reviews are visible without widening document access.', 'Solution Architect / account team', 'Manager reporting access can be read-only. Use only explicitly offered transfer controls.') }
  ],
  PROJECT_TEAM_COORDINATOR: [
    { after: 'step-1', item: step('ptc-time-steward', 'Correct through the time-steward workflow', 'validate', 'timesheet', 'A selected user, an incorrect record and a documented reason.', 'Use only authorized reopen, correction, replacement-task or draft-removal controls. Worker-return corrections and administrative reallocation are different workflows.', 'An audited correction or return.', 'Before-and-after evidence and any required worker resubmission are retained.', 'Time owner / authorized approver', 'Never submit another person’s timesheet. Module 001B administrative reallocation preserves its separate no-resubmission rules.') }
  ],
  SUPER_ADMINISTRATOR: [
    { after: 'step-3', item: step('admin-ai-policy', 'Govern AI provider configuration', 'implement', 'ai-provider-configuration', 'An approved provider or model configuration change.', 'Use Module 064 to manage enabled providers, model choices and the governed selection order. Check health and configuration without placing keys in evidence.', 'A governed AI configuration result.', 'The saved configuration and health evidence support the intended change.', 'Platform owner / authorized AI consumers', 'A learning walkthrough does not invoke AI, alter provider order or bypass change approval.') },
    { after: 'admin-ai-policy', item: step('admin-integration-health', 'Verify integration and notification readiness', 'validate', 'service-control', 'Authorized system, integration and worker evidence.', 'Review diagnostics and the owning configuration workspaces, including Module 026 for CRM/ERP and Module 065 for Microsoft integration. Distinguish configured, queued and verified outcomes.', 'An evidenced operational readiness review.', 'Failures have owners and sensitive values remain protected.', 'Integration owner / operations', 'Readiness is not inferred from this animated guide. Do not deploy or change infrastructure from a learning step.') }
  ]
});

export function assignedRolePlaybooks(guidance = {}, context = {}) {
  if (!context.accessReady || !Array.isArray(context.roleCodes)) return [];
  const assigned = [...new Set(context.roleCodes.filter((code) => typeof code === 'string').map(journeyRoleCode).filter(Boolean))];
  const roles = roleCatalog(guidance, assigned).filter((role) => role.assigned);
  return roles.map((role) => {
    const base = role.code === SA_MANAGER.code ? { ...SA_MANAGER, assigned: true } : role;
    const steps = base.steps.map((item) => ({ ...item }));
    if (base.code === 'SOLUTION_ARCHITECT') {
      const generation = steps.find((item) => item.id === 'step-2');
      if (generation) generation.action = 'Develop Plan, Design, Implement, Validate and Release in sequence. Where phase generation is enabled, review each phase and its timer before continuing. AI provider selection follows Module 064; review task hours in all five phases before confirmation.';
    }
    for (const addition of additions[base.code] || []) {
      const index = steps.findIndex((item) => item.id === addition.after);
      if (index >= 0) steps.splice(index + 1, 0, { ...addition.item });
    }
    return { ...base, steps };
  });
}
