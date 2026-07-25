export const PERMISSION_LEVELS = Object.freeze([
  { code: 'Not Set', meaning: 'No new scoped decision is configured. Existing authorization remains in effect.' },
  { code: 'No Access', meaning: 'The role must not see or open the module.' },
  { code: 'View', meaning: 'Read-only access within the configured data scope.' },
  { code: 'Create/Edit', meaning: 'Create and update records within the configured data scope.' },
  { code: 'Approve', meaning: 'Review, approve, reject, or return governed work within scope.' },
  { code: 'Manage', meaning: 'Broad operational control, including assignment, reopening, correction, and governed removal where supported.' },
  { code: 'Administer', meaning: 'Functional module administration without unrestricted platform control.' },
  { code: 'Full Control', meaning: 'All available module actions. Super Administrator is always Full Control.' },
  { code: 'Custom', meaning: 'A mixed action or scope configuration defined permission by permission.' }
]);

const RECOGNIZED_LEVELS = new Set(PERMISSION_LEVELS.map((item) => item.code));

export const ROLE_REFERENCE = Object.freeze({
  ENGINEERING: { purpose: 'Complete assigned engineering work and maintain personal time and delivery records.', visibility: 'The engineer’s own records and assigned work.', defaultScope: 'SELF' },
  ENGINEERING_LEAD: { purpose: 'Perform engineering work while coordinating the authorized engineering team.', visibility: 'The lead and authorized members of the functional team.', defaultScope: 'FUNCTIONAL_TEAM' },
  PROJECT_MANAGEMENT: { purpose: 'Manage assigned projects, tasks, workload, time review, and project reporting.', visibility: 'Assigned projects and people working on those projects.', defaultScope: 'ASSIGNED_PROJECTS' },
  PROJECT_MANAGEMENT_LEAD: { purpose: 'Coordinate project managers and oversee their authorized delivery portfolio.', visibility: 'The lead, the managed PM team, and their authorized projects.', defaultScope: 'MANAGED_TEAM' },
  MANAGER: { purpose: 'Manage people, approve time, review workload, and oversee the manager’s organization.', visibility: 'Direct and indirect reports.', defaultScope: 'DIRECT_AND_INDIRECT_REPORTS' },
  SALES: { purpose: 'Work with sales-owned customers, opportunities, intake, and delivery handoff.', visibility: 'Assigned customers and related sales records.', defaultScope: 'ASSIGNED_CUSTOMERS' },
  INSIDE_SALES: { purpose: 'Support intake, customer reporting, quote association, and internal sales operations.', visibility: 'Assigned customers and authorized sales records.', defaultScope: 'ASSIGNED_CUSTOMERS' },
  SOLUTION_ARCHITECT: { purpose: 'Create and review SOWs, GSDs, solution designs, project information, and sales handoffs.', visibility: 'Assigned projects and related SOW, GSD, customer, and sales records.', defaultScope: 'ASSIGNED_PROJECTS' },
  EXECUTIVE: { purpose: 'Review organization-wide dashboards, performance, utilization, and delivery results.', visibility: 'Organization-wide visibility, normally read-only.', defaultScope: 'ORGANIZATION' },
  PROJECT_TEAM_COORDINATOR: {
    purpose: 'Serve as the operational time steward across teams. The PTC can select users, return submitted time to draft, correct or remove entries, move time between tasks, and create or assign the correct task.',
    visibility: 'Organization-wide operational records. Delegated changes require a reason and immutable audit evidence. The PTC does not submit another user’s timesheet and cannot perform platform configuration.',
    defaultScope: 'ORGANIZATION'
  },
  ACCOUNTING: { purpose: 'Perform billing, invoicing, reconciliation, export, and accounting review.', visibility: 'Authorized organization-wide financial and billing information.', defaultScope: 'ORGANIZATION' },
  SUPER_ADMINISTRATOR: { purpose: 'Administer the complete ProjectPulse platform, roles, modules, security, and configuration.', visibility: 'Everything. Full Control is permanent and cannot be reduced.', defaultScope: 'ORGANIZATION' }
});

function sessionToken() {
  try {
    const session = JSON.parse(localStorage.getItem('projectPulseAuthSession') || 'null');
    return session?.sessionToken || session?.token || session?.accessToken || '';
  } catch {
    return '';
  }
}

function viewAsUserId() {
  try {
    const selected = JSON.parse(localStorage.getItem('projectPulseViewAsUser') || 'null');
    return selected?.userId || localStorage.getItem('projectPulseViewAsUserId') || '';
  } catch {
    return localStorage.getItem('projectPulseViewAsUserId') || '';
  }
}

function runtimePath(path) {
  const url = new URL(path, window.location.origin);
  const exact = {
    '/api/role-policy/catalog': '/api/runtime/role-policy/catalog',
    '/api/role-policy/matrix': '/api/runtime/role-policy/matrix'
  };
  if (exact[url.pathname]) url.pathname = exact[url.pathname];
  return `${url.pathname}${url.search}`;
}

function unwrap(payload) {
  let current = payload && typeof payload === 'object' && !Array.isArray(payload) ? payload : {};
  for (let depth = 0; depth < 3; depth += 1) {
    const key = ['data', 'Data', 'result', 'Result', 'value', 'Value', 'payload', 'Payload']
      .find((candidate) => current?.[candidate] && typeof current[candidate] === 'object' && !Array.isArray(current[candidate]));
    if (!key) break;
    current = current[key];
  }
  return current;
}

export async function api(path) {
  const token = sessionToken();
  const viewAs = viewAsUserId();
  const requestPath = runtimePath(path);
  const response = await fetch(requestPath, {
    method: 'GET',
    credentials: 'include',
    cache: 'no-store',
    headers: {
      ...(token ? {
        Authorization: `Bearer ${token}`,
        'X-ProjectPulse-Session': token,
        'X-Project-Pulse-Session': token,
        'X-Session-Token': token
      } : {}),
      ...(viewAs ? { 'X-ProjectPulse-View-As-User': viewAs } : {}),
      'Cache-Control': 'no-cache',
      Pragma: 'no-cache'
    }
  });
  const raw = await response.text();
  let payload;
  try {
    payload = raw ? JSON.parse(raw) : {};
  } catch {
    const error = new Error(`${requestPath} returned non-JSON content instead of ProjectPulse API data.`);
    error.status = response.status;
    error.responsePreview = raw.slice(0, 160);
    throw error;
  }
  payload = unwrap(payload);
  if (!response.ok) {
    const error = new Error(payload.message || payload.Message || payload.detail || payload.Detail || `${requestPath} returned HTTP ${response.status}`);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
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
  if (actions.has('WORKFLOW_MANAGE') || actions.has('RECORD_ASSIGN') || actions.has('TIME_REASSIGN') || actions.has('TIME_UNSUBMIT')) return 'Manage';
  if (actions.has('APPROVAL_APPROVE') || actions.has('TIME_APPROVE')) return 'Approve';
  if (actions.has('RECORD_EDIT') || actions.has('TIME_EDIT_OWN') || actions.has('TIME_CORRECT_ON_BEHALF')) return 'Create/Edit';
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
