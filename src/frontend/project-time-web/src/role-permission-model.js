export const LEVELS = [
  ['Not Set', 'Keep existing authorization until a decision is published.'],
  ['No Access', 'Hide the module and deny route access.'],
  ['View', 'View records within the selected scope.'],
  ['Create/Edit', 'View, create, and update records within scope.'],
  ['Approve', 'View and complete approval decisions within scope.'],
  ['Manage', 'Create, edit, assign, reopen, and manage workflow.'],
  ['Administer', 'Manage functional configuration without platform control.'],
  ['Full Control', 'All module functions. Super Administrator is always Full Control.'],
  ['Custom', 'Use a mixed action and scope configuration.']
];
const LEVEL_NAMES = new Set(LEVELS.map(([name]) => name));
export const ROLE_SCOPES = {
  ENGINEERING: 'SELF', ENGINEERING_LEAD: 'FUNCTIONAL_TEAM',
  PROJECT_MANAGEMENT: 'ASSIGNED_PROJECTS', PROJECT_MANAGEMENT_LEAD: 'MANAGED_PROJECTS',
  MANAGER: 'DIRECT_AND_INDIRECT_REPORTS', SALES: 'ASSIGNED_CUSTOMERS',
  INSIDE_SALES: 'ASSIGNED_CUSTOMERS', SOLUTION_ARCHITECT: 'ASSIGNED_PROJECTS',
  EXECUTIVE: 'ORGANIZATION', PROJECT_TEAM_COORDINATOR: 'ORGANIZATION',
  ACCOUNTING: 'ORGANIZATION', SUPER_ADMINISTRATOR: 'ORGANIZATION'
};
const GENERIC = {
  View: ['MODULE_VIEW'],
  'Create/Edit': ['MODULE_VIEW', 'RECORD_CREATE', 'RECORD_EDIT'],
  Approve: ['MODULE_VIEW', 'APPROVAL_VIEW', 'APPROVAL_APPROVE', 'APPROVAL_REJECT'],
  Manage: ['MODULE_VIEW', 'RECORD_CREATE', 'RECORD_EDIT', 'RECORD_ASSIGN', 'RECORD_REOPEN', 'WORKFLOW_MANAGE'],
  Administer: ['MODULE_VIEW', 'RECORD_CREATE', 'RECORD_EDIT', 'RECORD_ASSIGN', 'RECORD_REOPEN', 'WORKFLOW_MANAGE', 'MODULE_CONFIGURE', 'POLICY_DELEGATE', 'AUDIT_VIEW'],
  'Full Control': ['MODULE_VIEW', 'RECORD_CREATE', 'RECORD_EDIT', 'RECORD_ASSIGN', 'RECORD_REOPEN', 'WORKFLOW_MANAGE', 'APPROVAL_VIEW', 'APPROVAL_APPROVE', 'APPROVAL_REJECT', 'MODULE_CONFIGURE', 'POLICY_DELEGATE', 'EXPORT_DATA', 'AUDIT_VIEW', 'AUDIT_RECORD', 'DELEGATED_ACTION']
};
const SPECIAL = {
  '001': {
    View: ['MODULE_VIEW', 'TIME_VIEW'],
    'Create/Edit': ['MODULE_VIEW', 'TIME_VIEW', 'TIME_EDIT_OWN', 'TIME_SUBMIT'],
    Approve: ['MODULE_VIEW', 'TIME_VIEW', 'TIME_APPROVE', 'TIME_REJECT'],
    Manage: ['MODULE_VIEW', 'TIME_VIEW', 'TIME_EDIT_OWN', 'TIME_SUBMIT', 'TIME_REOPEN', 'TIME_REASSIGN', 'TIME_CORRECT_ON_BEHALF', 'TIME_APPROVE', 'TIME_REJECT'],
    Administer: ['MODULE_VIEW', 'TIME_VIEW', 'TIME_EDIT_OWN', 'TIME_SUBMIT', 'TIME_REOPEN', 'TIME_REASSIGN', 'TIME_CORRECT_ON_BEHALF', 'TIME_APPROVE', 'TIME_REJECT', 'AUDIT_VIEW'],
    'Full Control': ['MODULE_VIEW', 'TIME_VIEW', 'TIME_EDIT_OWN', 'TIME_SUBMIT', 'TIME_REOPEN', 'TIME_REASSIGN', 'TIME_CORRECT_ON_BEHALF', 'TIME_APPROVE', 'TIME_REJECT', 'AUDIT_VIEW', 'AUDIT_RECORD']
  },
  '002': {
    View: ['MODULE_VIEW', 'APPROVAL_VIEW'], 'Create/Edit': ['MODULE_VIEW', 'APPROVAL_VIEW'],
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
};
export const arr = (value) => Array.isArray(value) ? value : [];
export const pick = (obj, camel, pascal, fallback = undefined) => Object.prototype.hasOwnProperty.call(obj || {}, camel) ? obj[camel] : Object.prototype.hasOwnProperty.call(obj || {}, pascal) ? obj[pascal] : fallback;
function authHeaders(json = false) {
  try {
    const session = JSON.parse(localStorage.getItem('projectPulseAuthSession') || 'null');
    return { ...(json ? { 'Content-Type': 'application/json' } : {}), ...(session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {}) };
  } catch { return json ? { 'Content-Type': 'application/json' } : {}; }
}
export async function api(path, options = {}) {
  const response = await fetch(path, { ...options, cache: 'no-store', headers: { ...authHeaders(Boolean(options.body)), ...(options.headers || {}), 'Cache-Control': 'no-cache' } });
  const text = await response.text();
  let payload = {}; try { payload = text ? JSON.parse(text) : {}; } catch { payload = { message: text }; }
  if (!response.ok) throw new Error(payload.message || payload.detail || `${path} returned HTTP ${response.status}`);
  return payload;
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
  if (grants.some((g) => g.effect === 'DENY' && g.actionCode === 'MODULE_ACCESS')) return 'No Access';
  for (const g of grants) {
    const value = g.conditions?.permissionLevel || g.conditions?.designation || g.sourceDesignation;
    if (LEVEL_NAMES.has(value)) return value;
  }
  const actions = new Set(grants.filter((g) => g.effect === 'GRANT').map((g) => g.actionCode));
  if (actions.has('MODULE_CONFIGURE') || actions.has('POLICY_PUBLISH')) return 'Administer';
  if (actions.has('WORKFLOW_MANAGE') || actions.has('RECORD_ASSIGN')) return 'Manage';
  if (actions.has('APPROVAL_APPROVE') || actions.has('TIME_APPROVE')) return 'Approve';
  if (actions.has('RECORD_EDIT') || actions.has('TIME_EDIT_OWN')) return 'Create/Edit';
  if (actions.size) return 'View';
  return 'Custom';
}
export function inferScope(grants, role) {
  const scopes = [...new Set(grants.map((g) => g.scopeCode).filter(Boolean))];
  return scopes.length === 1 ? scopes[0] : ROLE_SCOPES[role] || 'SELF';
}
function flags(action, role) {
  const delegated = role === 'PROJECT_TEAM_COORDINATOR' && ['DELEGATED_ACTION', 'POLICY_DELEGATE', 'APPROVAL_DELEGATE_MANAGER', 'APPROVAL_DELEGATE_PROJECT_MANAGER', 'TIME_CORRECT_ON_BEHALF', 'TIME_REASSIGN'].includes(action);
  return { delegatedAuthority: delegated, reasonRequired: delegated || ['RECORD_REOPEN', 'TIME_REOPEN', 'TIME_CORRECT_ON_BEHALF', 'TIME_REASSIGN', 'APPROVAL_RETURN_FOR_CORRECTION'].includes(action), auditRequired: action !== 'MODULE_VIEW', isActive: true };
}
export function grantsFor(moduleCode, role, level, scope) {
  if (role === 'SUPER_ADMINISTRATOR') level = 'Full Control';
  if (level === 'Not Set') return [];
  const conditions = { source: 'Module 012 intuitive permission editor', designation: level, permissionLevel: level, scopeCode: scope, moduleScopedOnly: level === 'No Access', superAdministratorInvariant: role === 'SUPER_ADMINISTRATOR' };
  if (level === 'No Access') return [{ actionCode: 'MODULE_ACCESS', scopeCode: 'ORGANIZATION', effect: 'DENY', conditions, delegatedAuthority: false, reasonRequired: false, auditRequired: true, isActive: true }];
  return (SPECIAL[moduleCode]?.[level] || GENERIC[level] || []).map((actionCode) => ({ actionCode, scopeCode: scope, effect: 'GRANT', conditions, ...flags(actionCode, role) }));
}
export function stable(grants) {
  return JSON.stringify(arr(grants).map((g) => ({ actionCode: g.actionCode, scopeCode: g.scopeCode, effect: g.effect, conditions: g.conditions || {}, delegatedAuthority: !!g.delegatedAuthority, reasonRequired: !!g.reasonRequired, auditRequired: g.auditRequired !== false, isActive: g.isActive !== false })).sort((a, b) => `${a.actionCode}|${a.scopeCode}|${a.effect}`.localeCompare(`${b.actionCode}|${b.scopeCode}|${b.effect}`)));
}
export function unavailable(moduleCode, role, level) {
  if (role === 'SUPER_ADMINISTRATOR') return level !== 'Full Control';
  return ['003', '012', '037'].includes(moduleCode) && !['Not Set', 'No Access', 'View', 'Custom'].includes(level);
}
