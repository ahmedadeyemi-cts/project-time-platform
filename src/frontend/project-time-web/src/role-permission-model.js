export const LEVELS = Object.freeze([
  ['Not Set', 'Keep the existing authorization behavior until a new decision is published.'],
  ['No Access', 'Hide the module and deny direct route access.'],
  ['View', 'Read information within the selected data scope.'],
  ['Create/Edit', 'Create and update records within the selected data scope.'],
  ['Approve', 'Review and complete approval decisions within the selected data scope.'],
  ['Manage', 'Operate the workflow, including assignment, reopening, correction, and governed removal where supported.'],
  ['Administer', 'Manage functional configuration without unrestricted platform control.'],
  ['Full Control', 'All available module functions. Super Administrator is permanently Full Control.'],
  ['Custom', 'Choose individual actions and scopes instead of using a preset.']
]);

const LEVEL_NAMES = new Set(LEVELS.map(([name]) => name));

export const ROLE_SCOPES = Object.freeze({
  ENGINEERING: 'SELF',
  ENGINEERING_LEAD: 'FUNCTIONAL_TEAM',
  PROJECT_MANAGEMENT: 'ASSIGNED_PROJECTS',
  PROJECT_MANAGEMENT_LEAD: 'MANAGED_TEAM',
  MANAGER: 'DIRECT_AND_INDIRECT_REPORTS',
  SALES: 'ASSIGNED_CUSTOMERS',
  INSIDE_SALES: 'ASSIGNED_CUSTOMERS',
  SOLUTION_ARCHITECT: 'ASSIGNED_PROJECTS',
  EXECUTIVE: 'ORGANIZATION',
  PROJECT_TEAM_COORDINATOR: 'ORGANIZATION',
  ACCOUNTING: 'ORGANIZATION',
  SUPER_ADMINISTRATOR: 'ORGANIZATION'
});

export const ROLE_GUIDANCE = Object.freeze({
  ENGINEERING: {
    title: 'Engineer',
    purpose: 'Complete assigned engineering work and maintain personal time and delivery records.',
    boundary: 'Normally limited to the engineer’s own records and assigned work.',
    recommendedLevel: 'Create/Edit'
  },
  ENGINEERING_LEAD: {
    title: 'Engineering Lead',
    purpose: 'Perform engineering work while coordinating the authorized engineering team.',
    boundary: 'The lead’s own records plus the functional team the lead is authorized to manage.',
    recommendedLevel: 'Manage'
  },
  PROJECT_MANAGEMENT: {
    title: 'Project Management',
    purpose: 'Manage assigned projects, project tasks, workload, time review, and project reporting.',
    boundary: 'Projects assigned to the project manager and people working on those projects.',
    recommendedLevel: 'Manage'
  },
  PROJECT_MANAGEMENT_LEAD: {
    title: 'Project Management Lead',
    purpose: 'Coordinate project managers and oversee the delivery portfolio assigned to the PM team.',
    boundary: 'The lead, the managed PM team, and their authorized projects.',
    recommendedLevel: 'Manage'
  },
  MANAGER: {
    title: 'Manager',
    purpose: 'Manage people, approve time, review workload, and oversee the manager’s organization.',
    boundary: 'Direct and indirect reports, subject to the module’s server-side scope rules.',
    recommendedLevel: 'Approve'
  },
  SALES: {
    title: 'Sales',
    purpose: 'Work with sales-owned customers, opportunities, intake, and delivery handoff.',
    boundary: 'Assigned customers and related sales records.',
    recommendedLevel: 'Create/Edit'
  },
  INSIDE_SALES: {
    title: 'Inside Sales',
    purpose: 'Support intake, customer reporting, quote association, and internal sales operations.',
    boundary: 'Assigned customers and authorized sales records.',
    recommendedLevel: 'Create/Edit'
  },
  SOLUTION_ARCHITECT: {
    title: 'Solution Architect',
    purpose: 'Create and review SOWs, GSDs, solution designs, project information, and sales handoffs.',
    boundary: 'Assigned projects and related SOW, GSD, customer, and sales records.',
    recommendedLevel: 'Manage'
  },
  EXECUTIVE: {
    title: 'Executive',
    purpose: 'Review organization-wide dashboards, performance, utilization, and delivery results.',
    boundary: 'Organization-wide visibility, normally read-only unless a module explicitly grants more.',
    recommendedLevel: 'View'
  },
  PROJECT_TEAM_COORDINATOR: {
    title: 'Project Team Coordinator',
    purpose: 'Act as the operational time steward across teams: select users, reopen submitted time, correct entries, move entries between tasks, create and assign a replacement task, and remove an incorrect draft entry with immutable audit evidence.',
    boundary: 'Organization-wide operational access. Every delegated change requires a reason and audit history. The role does not submit a timesheet for another user and cannot perform platform configuration.',
    recommendedLevel: 'Manage'
  },
  ACCOUNTING: {
    title: 'Accounting',
    purpose: 'Perform billing, invoicing, reconciliation, export, and accounting review.',
    boundary: 'Authorized organization-wide financial and billing information.',
    recommendedLevel: 'Approve'
  },
  SUPER_ADMINISTRATOR: {
    title: 'Super Administrator',
    purpose: 'Administer the complete Pulse platform, roles, modules, security, and configuration.',
    boundary: 'Permanent organization-wide Full Control. This invariant cannot be reduced.',
    recommendedLevel: 'Full Control'
  }
});

export const ACTION_GUIDANCE = Object.freeze({
  MODULE_ACCESS: ['Open module', 'Controls whether the module is visible and directly accessible.'],
  MODULE_VIEW: ['View module', 'Open the module and view its main workspace.'],
  TIME_VIEW: ['View time', 'View time records within the configured data scope.'],
  TIME_VIEW_ON_BEHALF: ['Select users and view their time', 'Open another user’s time-management workspace without impersonating that user.'],
  TIME_EDIT_OWN: ['Edit own time', 'Create and update the signed-in user’s own draft time.'],
  TIME_SUBMIT: ['Submit own timesheet', 'Submit the signed-in user’s timesheet into the approval workflow.'],
  TIME_UNSUBMIT: ['Return submitted time to draft', 'Move another user’s submitted or approved day back to draft so corrections can be made and reapproved.'],
  TIME_REOPEN: ['Reopen time', 'Reopen governed time with a required reason and audit record.'],
  TIME_CORRECT_ON_BEHALF: ['Correct time for another user', 'Change hours, description, billable status, or other editable details for a selected user.'],
  TIME_REASSIGN: ['Move time to another task', 'Move a time entry to another authorized project task while preserving the before-and-after audit trail.'],
  TIME_DELETE_ON_BEHALF: ['Remove an incorrect draft entry', 'Remove a draft entry for another user after confirmation. The original entry is retained in immutable audit evidence.'],
  TIME_TASK_CREATE: ['Create a replacement task', 'Create a new task under an authorized project when the correct task does not yet exist.'],
  TIME_TASK_ASSIGN: ['Assign a task to the selected user', 'Create or confirm the project assignment needed before moving time to the task.'],
  TIME_APPROVE: ['Approve time', 'Approve time at an authorized approval stage.'],
  TIME_REJECT: ['Return or reject time', 'Return time for correction at an authorized approval stage.'],
  TIME_DELETE_PERMANENT: ['Delete without recoverable evidence', 'Protected action. Permanent deletion without retained audit evidence is not allowed.'],
  POLICY_VIEW: ['View role policy', 'View published role and permission policy.'],
  POLICY_VALIDATE: ['Validate policy changes', 'Run policy safety validation before publishing.'],
  POLICY_PUBLISH: ['Publish role policy', 'Publish a new immutable role-policy version.'],
  POLICY_RESTORE: ['Restore policy version', 'Restore an older version as a new immutable policy version.'],
  MATRIX_VIEW: ['View permission matrix', 'View the read-only role-by-permission matrix.'],
  MATRIX_EXPORT: ['Export permission matrix', 'Download the read-only permission matrix as CSV.'],
  ACCESS_EXPLAIN: ['Explain access', 'View the evidence and scope behind a permission decision.'],
  AUDIT_VIEW: ['View audit evidence', 'View relevant immutable audit history.'],
  AUDIT_RECORD: ['Create audit evidence', 'Write required immutable audit evidence for a governed action.']
});

export const PTC_TIME_STEWARD_ACTIONS = Object.freeze([
  'MODULE_VIEW',
  'TIME_VIEW',
  'TIME_VIEW_ON_BEHALF',
  'TIME_UNSUBMIT',
  'TIME_REOPEN',
  'TIME_CORRECT_ON_BEHALF',
  'TIME_REASSIGN',
  'TIME_DELETE_ON_BEHALF',
  'TIME_TASK_CREATE',
  'TIME_TASK_ASSIGN',
  'AUDIT_VIEW',
  'AUDIT_RECORD'
]);

const GENERIC = Object.freeze({
  View: ['MODULE_VIEW'],
  'Create/Edit': ['MODULE_VIEW', 'RECORD_CREATE', 'RECORD_EDIT'],
  Approve: ['MODULE_VIEW', 'APPROVAL_VIEW', 'APPROVAL_APPROVE', 'APPROVAL_REJECT'],
  Manage: ['MODULE_VIEW', 'RECORD_CREATE', 'RECORD_EDIT', 'RECORD_ASSIGN', 'RECORD_REOPEN', 'WORKFLOW_MANAGE'],
  Administer: ['MODULE_VIEW', 'RECORD_CREATE', 'RECORD_EDIT', 'RECORD_ASSIGN', 'RECORD_REOPEN', 'WORKFLOW_MANAGE', 'MODULE_CONFIGURE', 'POLICY_DELEGATE', 'AUDIT_VIEW'],
  'Full Control': ['MODULE_VIEW', 'RECORD_CREATE', 'RECORD_EDIT', 'RECORD_ASSIGN', 'RECORD_REOPEN', 'WORKFLOW_MANAGE', 'APPROVAL_VIEW', 'APPROVAL_APPROVE', 'APPROVAL_REJECT', 'MODULE_CONFIGURE', 'POLICY_DELEGATE', 'EXPORT_DATA', 'AUDIT_VIEW', 'AUDIT_RECORD', 'DELEGATED_ACTION']
});

const SPECIAL = Object.freeze({
  '001': {
    View: ['MODULE_VIEW', 'TIME_VIEW'],
    'Create/Edit': ['MODULE_VIEW', 'TIME_VIEW', 'TIME_EDIT_OWN', 'TIME_SUBMIT'],
    Approve: ['MODULE_VIEW', 'TIME_VIEW', 'TIME_APPROVE', 'TIME_REJECT'],
    Manage: ['MODULE_VIEW', 'TIME_VIEW', 'TIME_EDIT_OWN', 'TIME_SUBMIT', 'TIME_REOPEN', 'TIME_REASSIGN', 'TIME_CORRECT_ON_BEHALF', 'TIME_APPROVE', 'TIME_REJECT', 'AUDIT_VIEW'],
    Administer: ['MODULE_VIEW', 'TIME_VIEW', 'TIME_EDIT_OWN', 'TIME_SUBMIT', 'TIME_REOPEN', 'TIME_REASSIGN', 'TIME_CORRECT_ON_BEHALF', 'TIME_APPROVE', 'TIME_REJECT', 'AUDIT_VIEW', 'AUDIT_RECORD'],
    'Full Control': ['MODULE_VIEW', 'TIME_VIEW', 'TIME_EDIT_OWN', 'TIME_SUBMIT', 'TIME_REOPEN', 'TIME_REASSIGN', 'TIME_CORRECT_ON_BEHALF', 'TIME_APPROVE', 'TIME_REJECT', 'AUDIT_VIEW', 'AUDIT_RECORD']
  },
  '002': {
    View: ['MODULE_VIEW', 'APPROVAL_VIEW'],
    'Create/Edit': ['MODULE_VIEW', 'APPROVAL_VIEW'],
    Approve: ['MODULE_VIEW', 'APPROVAL_VIEW', 'APPROVAL_APPROVE', 'APPROVAL_REJECT'],
    Manage: ['MODULE_VIEW', 'APPROVAL_VIEW', 'APPROVAL_APPROVE', 'APPROVAL_REJECT', 'APPROVAL_RETURN_FOR_CORRECTION', 'APPROVAL_DELEGATE_MANAGER', 'APPROVAL_DELEGATE_PROJECT_MANAGER'],
    Administer: ['MODULE_VIEW', 'APPROVAL_VIEW', 'APPROVAL_APPROVE', 'APPROVAL_REJECT', 'APPROVAL_RETURN_FOR_CORRECTION', 'APPROVAL_DELEGATE_MANAGER', 'APPROVAL_DELEGATE_PROJECT_MANAGER', 'AUDIT_VIEW'],
    'Full Control': ['MODULE_VIEW', 'APPROVAL_VIEW', 'APPROVAL_APPROVE', 'APPROVAL_REJECT', 'APPROVAL_RETURN_FOR_CORRECTION', 'APPROVAL_DELEGATE_MANAGER', 'APPROVAL_DELEGATE_PROJECT_MANAGER', 'APPROVAL_APPROVE_PTC_FINAL', 'APPROVAL_REJECT_PTC_FINAL', 'AUDIT_VIEW', 'AUDIT_RECORD']
  },
  '003': Object.fromEntries(['View', 'Create/Edit', 'Approve', 'Manage', 'Administer', 'Full Control'].map((level) => [level, ['MODULE_VIEW', 'UTILIZATION_VIEW']])),
  '012': {
    View: ['MODULE_VIEW', 'POLICY_VIEW', 'POLICY_AUDIT_VIEW'],
    'Full Control': ['MODULE_VIEW', 'POLICY_VIEW', 'POLICY_VALIDATE', 'POLICY_PUBLISH', 'POLICY_RESTORE', 'POLICY_AUDIT_VIEW', 'MATRIX_VIEW', 'MATRIX_EXPORT', 'ACCESS_EXPLAIN']
  },
  '037': Object.fromEntries(['View', 'Create/Edit', 'Approve', 'Manage', 'Administer', 'Full Control'].map((level) => [level, ['MODULE_VIEW', 'MATRIX_VIEW', 'MATRIX_EXPORT', 'ACCESS_EXPLAIN']]))
});

export const arr = (value) => Array.isArray(value) ? value : [];
export const pick = (obj, camel, pascal, fallback = undefined) => Object.prototype.hasOwnProperty.call(obj || {}, camel) ? obj[camel] : Object.prototype.hasOwnProperty.call(obj || {}, pascal) ? obj[pascal] : fallback;

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

function readPath(path, method) {
  if (method !== 'GET') return path;
  const url = new URL(path, window.location.origin);
  const exact = {
    '/api/role-policy/summary': '/api/runtime/role-policy/summary',
    '/api/role-policy/catalog': '/api/runtime/role-policy/catalog',
    '/api/role-policy/versions': '/api/runtime/role-policy/versions',
    '/api/role-policy/matrix': '/api/runtime/role-policy/matrix'
  };
  if (exact[url.pathname]) url.pathname = exact[url.pathname];
  else if (/^\/api\/role-policy\/roles\/[^/]+$/.test(url.pathname)) {
    url.pathname = url.pathname.replace('/api/role-policy/roles/', '/api/runtime/role-policy/roles/');
  }
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

function authHeaders(json = false) {
  const token = sessionToken();
  const viewAs = viewAsUserId();
  return {
    ...(json ? { 'Content-Type': 'application/json' } : {}),
    ...(token ? {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token
    } : {}),
    ...(viewAs ? { 'X-ProjectPulse-View-As-User': viewAs } : {})
  };
}

export async function api(path, options = {}) {
  const method = String(options.method || 'GET').toUpperCase();
  const requestPath = readPath(path, method);
  const response = await fetch(requestPath, {
    ...options,
    method,
    credentials: 'include',
    cache: 'no-store',
    headers: {
      ...authHeaders(Boolean(options.body)),
      ...(options.headers || {}),
      'Cache-Control': 'no-cache',
      Pragma: 'no-cache'
    }
  });
  const text = await response.text();
  let payload;
  try {
    payload = text ? JSON.parse(text) : {};
  } catch {
    const error = new Error(`${requestPath} returned non-JSON content instead of Pulse API data.`);
    error.status = response.status;
    error.responsePreview = text.slice(0, 160);
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

export function actionLabel(actionCode) {
  return ACTION_GUIDANCE[actionCode]?.[0] || String(actionCode || '').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

export function actionDescription(actionCode, backendDescription = '') {
  return ACTION_GUIDANCE[actionCode]?.[1] || backendDescription || 'This action is governed by the selected module and data scope.';
}

export function normalizeGrant(grant = {}) {
  return {
    actionCode: pick(grant, 'actionCode', 'ActionCode', 'MODULE_VIEW'),
    scopeCode: pick(grant, 'scopeCode', 'ScopeCode', 'SELF'),
    effect: pick(grant, 'grantEffect', 'GrantEffect', pick(grant, 'effect', 'Effect', 'GRANT')),
    conditions: pick(grant, 'conditions', 'Conditions', {}) || {},
    delegatedAuthority: Boolean(pick(grant, 'delegatedAuthority', 'DelegatedAuthority', false)),
    reasonRequired: Boolean(pick(grant, 'reasonRequired', 'ReasonRequired', false)),
    auditRequired: pick(grant, 'auditRequired', 'AuditRequired', true) !== false,
    isActive: pick(grant, 'isActive', 'IsActive', true) !== false,
    sourceDesignation: pick(grant, 'sourceDesignation', 'SourceDesignation', '')
  };
}

export function inferLevel(grants, role) {
  if (role === 'SUPER_ADMINISTRATOR') return 'Full Control';
  if (!grants.length) return 'Not Set';
  if (grants.some((grant) => grant.effect === 'DENY' && grant.actionCode === 'MODULE_ACCESS')) return 'No Access';
  for (const grant of grants) {
    const value = grant.conditions?.permissionLevel || grant.conditions?.designation || grant.sourceDesignation;
    if (LEVEL_NAMES.has(value)) return value;
  }
  const actions = new Set(grants.filter((grant) => grant.effect === 'GRANT').map((grant) => grant.actionCode));
  if (actions.has('MODULE_CONFIGURE') || actions.has('POLICY_PUBLISH')) return 'Administer';
  if (actions.has('WORKFLOW_MANAGE') || actions.has('RECORD_ASSIGN') || actions.has('TIME_REASSIGN') || actions.has('TIME_UNSUBMIT')) return 'Manage';
  if (actions.has('APPROVAL_APPROVE') || actions.has('TIME_APPROVE')) return 'Approve';
  if (actions.has('RECORD_EDIT') || actions.has('TIME_EDIT_OWN') || actions.has('TIME_CORRECT_ON_BEHALF')) return 'Create/Edit';
  if (actions.size) return 'View';
  return 'Custom';
}

export function inferScope(grants, role) {
  const scopes = [...new Set(grants.map((grant) => grant.scopeCode).filter(Boolean))];
  return scopes.length === 1 ? scopes[0] : ROLE_SCOPES[role] || 'SELF';
}

function flags(action, role) {
  const delegated = role === 'PROJECT_TEAM_COORDINATOR' && [
    'DELEGATED_ACTION',
    'POLICY_DELEGATE',
    'APPROVAL_DELEGATE_MANAGER',
    'APPROVAL_DELEGATE_PROJECT_MANAGER',
    ...PTC_TIME_STEWARD_ACTIONS
  ].includes(action);
  const reasonRequired = delegated || ['RECORD_REOPEN', 'TIME_REOPEN', 'TIME_UNSUBMIT', 'TIME_CORRECT_ON_BEHALF', 'TIME_REASSIGN', 'TIME_DELETE_ON_BEHALF', 'TIME_TASK_CREATE', 'TIME_TASK_ASSIGN', 'APPROVAL_RETURN_FOR_CORRECTION'].includes(action);
  return { delegatedAuthority: delegated, reasonRequired, auditRequired: action !== 'MODULE_VIEW', isActive: true };
}

export function grantsFor(moduleCode, role, level, scope) {
  if (role === 'SUPER_ADMINISTRATOR') level = 'Full Control';
  if (level === 'Not Set') return [];
  const conditions = {
    source: 'Module 012 intuitive permission editor',
    designation: level,
    permissionLevel: level,
    scopeCode: scope,
    moduleScopedOnly: level === 'No Access',
    superAdministratorInvariant: role === 'SUPER_ADMINISTRATOR',
    operationalTimeSteward: role === 'PROJECT_TEAM_COORDINATOR' && moduleCode === '001'
  };
  if (level === 'No Access') {
    return [{ actionCode: 'MODULE_ACCESS', scopeCode: 'ORGANIZATION', effect: 'DENY', conditions, delegatedAuthority: false, reasonRequired: false, auditRequired: true, isActive: true }];
  }

  let actions;
  if (role === 'PROJECT_TEAM_COORDINATOR' && moduleCode === '001' && ['Manage', 'Administer', 'Full Control'].includes(level)) {
    actions = [...PTC_TIME_STEWARD_ACTIONS];
  } else {
    actions = SPECIAL[moduleCode]?.[level] || GENERIC[level] || [];
  }

  if (role === 'PROJECT_TEAM_COORDINATOR') {
    const excluded = new Set(['MODULE_CONFIGURE', 'POLICY_DELEGATE', 'POLICY_PUBLISH', 'POLICY_RESTORE', 'SYSTEM_CONFIGURE', 'TIME_SUBMIT', 'TIME_DELETE_PERMANENT']);
    actions = actions.filter((actionCode) => !excluded.has(actionCode));
  }

  return actions.map((actionCode) => ({ actionCode, scopeCode: scope, effect: 'GRANT', conditions, ...flags(actionCode, role) }));
}

export function stable(grants) {
  return JSON.stringify(arr(grants).map((grant) => ({
    actionCode: grant.actionCode,
    scopeCode: grant.scopeCode,
    effect: grant.effect,
    conditions: grant.conditions || {},
    delegatedAuthority: !!grant.delegatedAuthority,
    reasonRequired: !!grant.reasonRequired,
    auditRequired: grant.auditRequired !== false,
    isActive: grant.isActive !== false
  })).sort((left, right) => `${left.actionCode}|${left.scopeCode}|${left.effect}`.localeCompare(`${right.actionCode}|${right.scopeCode}|${right.effect}`)));
}

export function unavailable(moduleCode, role, level) {
  if (role === 'SUPER_ADMINISTRATOR') return level !== 'Full Control';
  if (['003', '012', '037'].includes(moduleCode)) return !['Not Set', 'No Access', 'View', 'Custom'].includes(level);
  return false;
}
