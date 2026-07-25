export const PERMISSION_LEVELS = Object.freeze([
  { code: 'Not Set', meaning: 'No new decision is configured. Existing authorization remains in effect.' },
  { code: 'No Access', meaning: 'The role must not see or open the module.' },
  { code: 'View', meaning: 'Read-only access within the configured data scope.' },
  { code: 'Create/Edit', meaning: 'Create and update records within the configured data scope.' },
  { code: 'Approve', meaning: 'Review, approve, reject, or return governed work within scope.' },
  { code: 'Manage', meaning: 'Broad operational control, assignment, reopening, and workflow management within scope.' },
  { code: 'Administer', meaning: 'Functional module administration and configuration without unrestricted platform control.' },
  { code: 'Full Control', meaning: 'All module actions. Super Administrator is always Full Control.' },
  { code: 'Custom', meaning: 'A mixed action or scope configuration that requires detailed notes.' }
]);

const RECOGNIZED_LEVELS = new Set(PERMISSION_LEVELS.map((item) => item.code));

export const ROLE_REFERENCE = Object.freeze({
  ENGINEERING: {
    purpose: 'Complete assigned engineering work and maintain personal time and delivery records.',
    visibility: 'Only information pertaining to the engineer.',
    defaultScope: 'SELF'
  },
  ENGINEERING_LEAD: {
    purpose: 'Perform engineering work while coordinating and reviewing the engineering team.',
    visibility: 'The lead and members of the lead’s authorized team.',
    defaultScope: 'FUNCTIONAL_TEAM'
  },
  PROJECT_MANAGEMENT: {
    purpose: 'Manage assigned projects, tasks, workload, time review, and project reporting.',
    visibility: 'The project manager and projects assigned to that project manager.',
    defaultScope: 'ASSIGNED_PROJECTS'
  },
  PROJECT_MANAGEMENT_LEAD: {
    purpose: 'Manage projects and coordinate the project managers on the lead’s team.',
    visibility: 'The lead, the lead’s projects, and authorized project managers on the team.',
    defaultScope: 'MANAGED_PROJECTS'
  },
  MANAGER: {
    purpose: 'Manage people, approve time, review workload, and oversee the manager’s organization.',
    visibility: 'The manager and the manager’s direct and indirect reports.',
    defaultScope: 'DIRECT_AND_INDIRECT_REPORTS'
  },
  SALES: {
    purpose: 'Work with sales-owned customers, opportunities, intake, and delivery handoff.',
    visibility: 'Sales information and customers assigned to the salesperson.',
    defaultScope: 'ASSIGNED_CUSTOMERS'
  },
  INSIDE_SALES: {
    purpose: 'Support sales intake, quote association, customer reporting, and internal sales operations.',
    visibility: 'Authorized sales and customer information based on the selected module scope.',
    defaultScope: 'ASSIGNED_CUSTOMERS'
  },
  SOLUTION_ARCHITECT: {
    purpose: 'Create and review SOWs, GSDs, solution designs, project information, and sales handoffs.',
    visibility: 'Authorized SOW, GSD, project, customer, and sales records.',
    defaultScope: 'ASSIGNED_PROJECTS'
  },
  EXECUTIVE: {
    purpose: 'Review organization-wide dashboards, performance, utilization, and delivery results.',
    visibility: 'Organization-wide read-only information unless a module grants more.',
    defaultScope: 'ORGANIZATION'
  },
  PROJECT_TEAM_COORDINATOR: {
    purpose: 'Coordinate delivery operations across teams and act on behalf of operational roles when necessary.',
    visibility: 'Organization-wide operational information. Delegated changes require a reason and audit history. System configuration remains excluded.',
    defaultScope: 'ORGANIZATION'
  },
  ACCOUNTING: {
    purpose: 'Perform billing, invoicing, reconciliation, export, and accounting review.',
    visibility: 'Authorized organization-wide financial and billing information.',
    defaultScope: 'ORGANIZATION'
  },
  SUPER_ADMINISTRATOR: {
    purpose: 'Administer the complete ProjectPulse platform, roles, modules, security, and configuration.',
    visibility: 'Everything. Full Control is permanent and cannot be reduced.',
    defaultScope: 'ORGANIZATION'
  }
});

function headers() {
  try {
    const session = JSON.parse(localStorage.getItem('projectPulseAuthSession') || 'null');
    return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
  } catch {
    return {};
  }
}

export async function api(path) {
  const response = await fetch(path, {
    method: 'GET',
    cache: 'no-store',
    headers: { ...headers(), 'Cache-Control': 'no-cache', Pragma: 'no-cache' }
  });
  const raw = await response.text();
  let payload = {};
  try { payload = raw ? JSON.parse(raw) : {}; } catch { payload = { message: raw }; }
  if (!response.ok) throw new Error(payload.message || payload.detail || `${path} returned HTTP ${response.status}`);
  return payload;
}

function csvEscape(value) {
  const text = String(value ?? '');
  return /[",\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

export function downloadCsv(filename, rows) {
  const content = rows.map((row) => row.map(csvEscape).join(',')).join('\n');
  const blob = new Blob([content], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export function formatDate(value) {
  if (!value) return 'Not recorded';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
}

function conditionsOf(grant) {
  return grant?.conditions && typeof grant.conditions === 'object' ? grant.conditions : {};
}

export function inferLevel(grants, roleCode) {
  if (roleCode === 'SUPER_ADMINISTRATOR') return 'Full Control';
  if (!grants?.length) return 'Not Set';
  if (grants.every((grant) => grant.inherited || grant.actionCode === 'LEGACY_FALLBACK')) return 'Not Set';
  if (grants.some((grant) => (grant.grantEffect === 'DENY' || grant.explicitDeny) && grant.actionCode === 'MODULE_ACCESS')) return 'No Access';

  for (const grant of grants) {
    const conditions = conditionsOf(grant);
    const candidate = conditions.permissionLevel || conditions.designation || grant.sourceDesignation;
    if (RECOGNIZED_LEVELS.has(candidate)) return candidate;
  }

  const actions = new Set(grants.filter((grant) => grant.grantEffect === 'GRANT' || grant.granted).map((grant) => grant.actionCode));
  if (actions.has('POLICY_PUBLISH') || actions.has('MODULE_CONFIGURE')) return 'Administer';
  if (actions.has('WORKFLOW_MANAGE') || actions.has('RECORD_ASSIGN') || actions.has('TIME_REASSIGN')) return 'Manage';
  if (actions.has('APPROVAL_APPROVE') || actions.has('TIME_APPROVE')) return 'Approve';
  if (actions.has('RECORD_EDIT') || actions.has('TIME_EDIT_OWN')) return 'Create/Edit';
  if (actions.has('MODULE_VIEW') || actions.has('MATRIX_VIEW') || actions.has('TIME_VIEW') || actions.has('UTILIZATION_VIEW')) return 'View';
  return 'Custom';
}

export function inferScope(grants, roleCode) {
  if (roleCode === 'SUPER_ADMINISTRATOR') return 'ORGANIZATION';
  const scopes = [...new Set((grants || []).map((grant) => grant.scopeCode).filter(Boolean))];
  if (!scopes.length) return 'LEGACY';
  if (scopes.length === 1) return scopes[0];
  return 'MIXED';
}

export function levelClass(level) {
  return `rpm-level rpm-level-${level.toLowerCase().replaceAll(/[^a-z0-9]+/g, '-')}`;
}
