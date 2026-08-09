import { useEffect, useMemo, useState } from 'react';
import './role-admin-directory-panel.css';
import './role-permission-workbench.css';
import './dynamic-rbac-administration.css';
import {
  LEVELS,
  PTC_TIME_STEWARD_ACTIONS,
  ROLE_GUIDANCE,
  ROLE_SCOPES,
  actionDescription,
  actionLabel,
  api,
  arr,
  grantsFor,
  inferLevel,
  inferScope,
  normalizeGrant,
  pick,
  stable,
  unavailable
} from './role-permission-model.js';

const PTC_DENIED_ACTIONS = new Set([
  'TIME_SUBMIT',
  'TIME_DELETE_PERMANENT',
  'SYSTEM_CONFIGURE',
  'MODULE_CONFIGURE',
  'POLICY_PUBLISH',
  'POLICY_RESTORE'
]);

function uniqueBy(items, key) {
  const seen = new Set();
  return items.filter((item) => {
    const value = String(item?.[key] || '').trim().toUpperCase();
    if (!value || seen.has(value)) return false;
    seen.add(value);
    return true;
  });
}

function normalizeBootstrap(payload) {
  const roles = uniqueBy(arr(pick(payload, 'roles', 'Roles', [])).map((role) => ({
    roleCode: String(pick(role, 'roleCode', 'RoleCode', '')).toUpperCase(),
    roleName: pick(role, 'roleName', 'RoleName', ''),
    description: pick(role, 'description', 'Description', ''),
    isActive: pick(role, 'isActive', 'IsActive', true) !== false,
    activeUserCount: Number(pick(role, 'activeUserCount', 'ActiveUserCount', 0) || 0)
  })), 'roleCode');
  const modules = uniqueBy(arr(pick(payload, 'modules', 'Modules', [])).map((module) => ({
    moduleCode: String(pick(module, 'moduleCode', 'ModuleCode', '')).toUpperCase(),
    moduleName: pick(module, 'moduleName', 'ModuleName', ''),
    routeScope: pick(module, 'routeScope', 'RouteScope', ''),
    currentState: pick(module, 'currentState', 'CurrentState', ''),
    permissionNotes: pick(module, 'permissionNotes', 'PermissionNotes', ''),
    sourceUrl: pick(module, 'sourceUrl', 'SourceUrl', '')
  })), 'moduleCode');
  const actions = uniqueBy(arr(pick(payload, 'actions', 'Actions', [])).map((action) => ({
    actionCode: String(pick(action, 'actionCode', 'ActionCode', '')).toUpperCase(),
    actionDescription: pick(action, 'actionDescription', 'ActionDescription', ''),
    isNonBypassable: Boolean(pick(action, 'isNonBypassable', 'IsNonBypassable', false))
  })), 'actionCode');
  const scopes = uniqueBy(arr(pick(payload, 'scopes', 'Scopes', [])).map((scope) => ({
    scopeCode: String(pick(scope, 'scopeCode', 'ScopeCode', '')).toUpperCase(),
    scopeDescription: pick(scope, 'scopeDescription', 'ScopeDescription', '')
  })), 'scopeCode');

  if (!roles.length || !modules.length || !actions.length || !scopes.length) {
    throw new Error('The RBAC service did not return an active role directory, module catalog, permission catalog, and scope catalog.');
  }
  if (!roles.some((role) => role.roleCode === 'SUPER_ADMINISTRATOR')) {
    throw new Error('The Super Administrator role is missing from the active role directory.');
  }

  return {
    ...payload,
    roles,
    modules,
    actions,
    scopes,
    actor: pick(payload, 'actor', 'Actor', null),
    canWritePolicy: Boolean(pick(payload, 'canWritePolicy', 'CanWritePolicy', false)),
    canManageRoleMemberships: Boolean(pick(payload, 'canManageRoleMemberships', 'CanManageRoleMemberships', false)),
    canManageModuleCatalog: Boolean(pick(payload, 'canManageModuleCatalog', 'CanManageModuleCatalog', false)),
    isViewAs: Boolean(pick(payload, 'isViewAs', 'IsViewAs', false)),
    policyVersion: pick(payload, 'policyVersion', 'PolicyVersion', null),
    superAdministratorInvariant: pick(payload, 'superAdministratorInvariant', 'SuperAdministratorInvariant', null),
    summary: pick(payload, 'summary', 'Summary', {}) || {}
  };
}

function normalizeModuleCatalog(payload) {
  return uniqueBy(arr(pick(payload, 'modules', 'Modules', [])).map((module) => ({
    moduleCode: String(pick(module, 'moduleCode', 'ModuleCode', '')).toUpperCase(),
    moduleName: pick(module, 'moduleName', 'ModuleName', ''),
    routeScope: pick(module, 'routeScope', 'RouteScope', ''),
    currentState: pick(module, 'currentState', 'CurrentState', ''),
    permissionNotes: pick(module, 'permissionNotes', 'PermissionNotes', ''),
    sourceUrl: pick(module, 'sourceUrl', 'SourceUrl', ''),
    isActive: pick(module, 'isActive', 'IsActive', true) !== false,
    protectedGovernanceModule: Boolean(pick(module, 'protectedGovernanceModule', 'ProtectedGovernanceModule', false))
  })), 'moduleCode');
}

function normalizeUsers(payload) {
  return arr(pick(payload, 'users', 'Users', [])).map((user) => ({
    userId: pick(user, 'userId', 'UserId', ''),
    email: pick(user, 'email', 'Email', ''),
    displayName: pick(user, 'displayName', 'DisplayName', ''),
    roleCodes: arr(pick(user, 'roleCodes', 'RoleCodes', [])).map((value) => String(value).toUpperCase())
  })).filter((user) => user.userId);
}

function relevantAction(actionCode, moduleCode, baseline, draft) {
  if (baseline.some((grant) => grant.actionCode === actionCode) || draft.some((grant) => grant.actionCode === actionCode)) return true;
  if (['MODULE_ACCESS', 'MODULE_VIEW', 'AUDIT_VIEW', 'AUDIT_RECORD'].includes(actionCode)) return true;
  if (moduleCode === '001') return actionCode.startsWith('TIME_');
  if (moduleCode === '002') return actionCode.startsWith('APPROVAL_');
  if (moduleCode === '003') return actionCode.startsWith('UTILIZATION_');
  if (moduleCode === '012') return actionCode.startsWith('POLICY_') || actionCode === 'ROLE_ASSIGN';
  if (moduleCode === '037') return actionCode.startsWith('MATRIX_') || actionCode === 'ACCESS_EXPLAIN';
  return ['RECORD_CREATE', 'RECORD_EDIT', 'RECORD_ASSIGN', 'RECORD_REOPEN', 'WORKFLOW_MANAGE', 'EXPORT_DATA', 'MODULE_CONFIGURE'].includes(actionCode);
}

function grantPayload(grant) {
  return {
    actionCode: grant.actionCode,
    scopeCode: grant.scopeCode,
    effect: grant.effect,
    conditions: grant.conditions || {},
    delegatedAuthority: !!grant.delegatedAuthority,
    reasonRequired: !!grant.reasonRequired,
    auditRequired: grant.auditRequired !== false,
    isActive: grant.isActive !== false
  };
}

const blankModuleForm = () => ({
  moduleCode: '',
  moduleName: '',
  routeScope: '',
  currentState: 'Active',
  permissionNotes: '',
  reason: ''
});

export default function RoleAdminDirectoryPanel() {
  const [bootstrap, setBootstrap] = useState(null);
  const [versions, setVersions] = useState([]);
  const [roleCode, setRoleCode] = useState('PROJECT_TEAM_COORDINATOR');
  const [moduleCode, setModuleCode] = useState('001');
  const [moduleSearch, setModuleSearch] = useState('');
  const [permissionSearch, setPermissionSearch] = useState('');
  const [detail, setDetail] = useState(null);
  const [baseline, setBaseline] = useState([]);
  const [custom, setCustom] = useState([]);
  const [level, setLevel] = useState('Manage');
  const [scope, setScope] = useState('ORGANIZATION');
  const [reason, setReason] = useState('');
  const [notes, setNotes] = useState('');
  const [validation, setValidation] = useState(null);
  const [tab, setTab] = useState('permissions');
  const [users, setUsers] = useState([]);
  const [userSearch, setUserSearch] = useState('');
  const [selectedUserId, setSelectedUserId] = useState('');
  const [membershipReason, setMembershipReason] = useState('');
  const [moduleCatalog, setModuleCatalog] = useState([]);
  const [catalogSearch, setCatalogSearch] = useState('');
  const [moduleForm, setModuleForm] = useState(blankModuleForm);
  const [state, setState] = useState({ loading: true, busy: false, error: '', message: '' });

  const roles = arr(bootstrap?.roles);
  const modules = arr(bootstrap?.modules);
  const catalog = { actions: arr(bootstrap?.actions), scopes: arr(bootstrap?.scopes), effects: ['GRANT', 'DENY'] };
  const canWrite = Boolean(bootstrap?.canWritePolicy) && !bootstrap?.isViewAs;
  const canManageMemberships = Boolean(bootstrap?.canManageRoleMemberships) && !bootstrap?.isViewAs;
  const canManageModules = Boolean(bootstrap?.canManageModuleCatalog) && !bootstrap?.isViewAs;
  const superAdmin = roleCode === 'SUPER_ADMINISTRATOR';
  const roleGuidance = ROLE_GUIDANCE[roleCode] || {};
  const effectiveLevel = superAdmin ? 'Full Control' : level;
  const effectiveScope = superAdmin ? 'ORGANIZATION' : scope;
  const selectedRole = roles.find((role) => role.roleCode === roleCode);
  const selectedModule = modules.find((module) => module.moduleCode === moduleCode);

  const presetDraft = useMemo(
    () => grantsFor(moduleCode, roleCode, effectiveLevel, effectiveScope),
    [moduleCode, roleCode, effectiveLevel, effectiveScope]
  );

  const draft = useMemo(() => {
    const source = effectiveLevel === 'Custom' ? custom : presetDraft;
    if (roleCode !== 'PROJECT_TEAM_COORDINATOR' || moduleCode !== '001') return source;
    const next = source.filter((grant) => !PTC_DENIED_ACTIONS.has(grant.actionCode));
    for (const actionCode of ['TIME_SUBMIT', 'TIME_DELETE_PERMANENT']) {
      next.push(normalizeGrant({
        actionCode,
        scopeCode: 'ORGANIZATION',
        effect: 'DENY',
        conditions: {
          designation: effectiveLevel,
          permissionLevel: effectiveLevel,
          operationalTimeSteward: true,
          reason: actionCode === 'TIME_SUBMIT'
            ? 'PTC manages time for others but does not submit a timesheet on another user’s behalf.'
            : 'Removal is permitted only through the governed audited TIME_DELETE_ON_BEHALF action.'
        },
        delegatedAuthority: false,
        reasonRequired: false,
        auditRequired: true
      }));
    }
    return next;
  }, [custom, effectiveLevel, moduleCode, presetDraft, roleCode]);

  const pending = !superAdmin && stable(draft) !== stable(baseline);
  const visibleModules = modules.filter((module) => {
    const term = moduleSearch.trim().toLowerCase();
    return !term || `${module.moduleCode} ${module.moduleName} ${module.routeScope}`.toLowerCase().includes(term);
  });
  const actionRows = useMemo(() => catalog.actions
    .filter((action) => relevantAction(action.actionCode, moduleCode, baseline, draft))
    .filter((action) => {
      const term = permissionSearch.trim().toLowerCase();
      if (!term) return true;
      return `${action.actionCode} ${actionLabel(action.actionCode)} ${actionDescription(action.actionCode, action.actionDescription)}`.toLowerCase().includes(term);
    })
    .sort((left, right) => left.actionCode.localeCompare(right.actionCode)), [baseline, catalog.actions, draft, moduleCode, permissionSearch]);

  const assignedUsers = arr(detail?.assignedUsers);
  const unassignedUsers = users.filter((user) => !user.roleCodes.includes(roleCode));
  const visibleCatalogModules = moduleCatalog.filter((module) => {
    const term = catalogSearch.trim().toLowerCase();
    return !term || `${module.moduleCode} ${module.moduleName} ${module.routeScope} ${module.currentState}`.toLowerCase().includes(term);
  });

  async function loadFoundation() {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const [bootstrapPayload, versionPayload] = await Promise.all([
        api('/api/rbac/v1/bootstrap'),
        api('/api/role-policy/versions')
      ]);
      const next = normalizeBootstrap(bootstrapPayload);
      setBootstrap(next);
      setVersions(arr(pick(versionPayload, 'versions', 'Versions', [])));
      if (!next.roles.some((role) => role.roleCode === roleCode)) setRoleCode(next.roles[0].roleCode);
      if (!next.modules.some((module) => module.moduleCode === moduleCode)) setModuleCode(next.modules[0].moduleCode);
      setState({ loading: false, busy: false, error: '', message: '' });
    } catch (error) {
      setBootstrap(null);
      setState({ loading: false, busy: false, error: error.message || 'Unable to load RBAC administration.', message: '' });
    }
  }

  async function loadDetail() {
    if (!bootstrap || !roleCode || !moduleCode) return;
    setState((current) => ({ ...current, busy: true, error: '' }));
    try {
      const payload = await api(`/api/rbac/v1/roles/${encodeURIComponent(roleCode)}?moduleCode=${encodeURIComponent(moduleCode)}`);
      const grants = arr(pick(payload, 'grants', 'Grants', [])).map(normalizeGrant);
      setDetail({ ...payload, assignedUsers: arr(pick(payload, 'assignedUsers', 'AssignedUsers', [])) });
      setBaseline(grants);
      setCustom(grants.map((grant) => ({ ...grant })));
      const nextLevel = superAdmin ? 'Full Control' : inferLevel(grants, roleCode);
      setLevel(nextLevel === 'Not Set' && roleCode === 'PROJECT_TEAM_COORDINATOR' && moduleCode === '001' ? 'Manage' : nextLevel);
      setScope(superAdmin ? 'ORGANIZATION' : inferScope(grants, roleCode));
      setReason('');
      setNotes('');
      setValidation(null);
      setState((current) => ({ ...current, busy: false }));
    } catch (error) {
      setState((current) => ({ ...current, busy: false, error: error.message || 'Unable to load the selected role and module.' }));
    }
  }

  async function loadMembershipDirectory() {
    if (!canManageMemberships) return;
    try {
      const payload = await api(`/api/rbac/v1/users?search=${encodeURIComponent(userSearch.trim())}`);
      setUsers(normalizeUsers(payload));
    } catch (error) {
      setState((current) => ({ ...current, error: error.message || 'Unable to load the user directory.' }));
    }
  }

  async function loadModuleCatalog() {
    try {
      const payload = await api('/api/rbac/v1/modules?includeInactive=true');
      setModuleCatalog(normalizeModuleCatalog(payload));
    } catch (error) {
      setState((current) => ({ ...current, error: error.message || 'Unable to load the RBAC module catalog.' }));
    }
  }

  useEffect(() => { void loadFoundation(); }, []);
  useEffect(() => { void loadDetail(); }, [bootstrap, roleCode, moduleCode]);
  useEffect(() => { if (tab === 'members') void loadMembershipDirectory(); }, [tab, userSearch, roleCode]);
  useEffect(() => { if (tab === 'modules') void loadModuleCatalog(); }, [tab]);

  function choosePreset(name) {
    setLevel(name);
    if (name === 'Custom') setCustom(draft.map((grant) => ({ ...grant })));
    setValidation(null);
  }

  function updateAction(actionCode, decision) {
    setLevel('Custom');
    setValidation(null);
    setCustom((rows) => {
      const existing = rows.find((grant) => grant.actionCode === actionCode);
      const without = rows.filter((grant) => grant.actionCode !== actionCode);
      if (decision === 'NOT_SET') return without;
      const protectedDeny = roleCode === 'PROJECT_TEAM_COORDINATOR' && moduleCode === '001' && PTC_DENIED_ACTIONS.has(actionCode);
      const effect = protectedDeny ? 'DENY' : decision;
      return [...without, normalizeGrant({
        ...(existing || {}),
        actionCode,
        scopeCode: existing?.scopeCode || effectiveScope,
        effect,
        conditions: {
          ...(existing?.conditions || {}),
          designation: 'Custom',
          permissionLevel: 'Custom',
          operationalTimeSteward: roleCode === 'PROJECT_TEAM_COORDINATOR' && moduleCode === '001'
        },
        delegatedAuthority: roleCode === 'PROJECT_TEAM_COORDINATOR' && PTC_TIME_STEWARD_ACTIONS.includes(actionCode),
        reasonRequired: ['TIME_UNSUBMIT', 'TIME_REOPEN', 'TIME_CORRECT_ON_BEHALF', 'TIME_REASSIGN', 'TIME_DELETE_ON_BEHALF', 'TIME_TASK_CREATE', 'TIME_TASK_ASSIGN'].includes(actionCode),
        auditRequired: actionCode !== 'MODULE_VIEW'
      })];
    });
  }

  function updateActionScope(actionCode, nextScope) {
    setLevel('Custom');
    setValidation(null);
    setCustom((rows) => rows.map((grant) => grant.actionCode === actionCode ? { ...grant, scopeCode: nextScope } : grant));
  }

  function requestBody() {
    if (!reason.trim()) throw new Error('Enter a reason that explains why this role policy is changing.');
    return {
      baseVersionNumber: bootstrap?.policyVersion?.versionNumber || bootstrap?.policyVersion?.VersionNumber || 0,
      reason: reason.trim(),
      changes: [{
        roleCode,
        moduleCode,
        notes: notes.trim() || `${effectiveLevel} within ${effectiveScope}.`,
        grants: draft.map(grantPayload)
      }]
    };
  }

  async function validate() {
    setState((current) => ({ ...current, busy: true, error: '', message: 'Validating role permission changes…' }));
    try {
      const result = await api('/api/rbac/v1/policies/validate', { method: 'POST', body: JSON.stringify(requestBody()) });
      const normalized = {
        valid: Boolean(pick(result, 'valid', 'Valid', false)),
        errors: arr(pick(result, 'errors', 'Errors', [])),
        warnings: arr(pick(result, 'warnings', 'Warnings', []))
      };
      setValidation(normalized);
      setState((current) => ({ ...current, busy: false, message: normalized.valid ? 'Validation passed. Review the summary and publish when ready.' : 'Validation blocked the change.' }));
    } catch (error) {
      setState((current) => ({ ...current, busy: false, error: error.message || 'Validation failed.' }));
    }
  }

  async function publish() {
    if (!window.confirm(`Publish the ${selectedRole?.roleName || roleCode} permissions for ${selectedModule?.moduleName || moduleCode} as a new immutable policy version?`)) return;
    setState((current) => ({ ...current, busy: true, error: '', message: 'Publishing role permissions…' }));
    try {
      const result = await api('/api/rbac/v1/policies/publish', { method: 'POST', body: JSON.stringify(requestBody()) });
      setState((current) => ({ ...current, busy: false, message: `Published policy version ${pick(result, 'versionNumber', 'VersionNumber', '—')}. Module 037 will show the same decision after refresh.` }));
      window.dispatchEvent(new CustomEvent('projectpulse:permissions-changed'));
      await loadFoundation();
    } catch (error) {
      setState((current) => ({ ...current, busy: false, error: error.message || 'Publishing failed.' }));
    }
  }

  async function changeMembership(operation, userId) {
    if (!membershipReason.trim()) {
      setState((current) => ({ ...current, error: 'Enter a reason before changing a role membership.' }));
      return;
    }
    setState((current) => ({ ...current, busy: true, error: '', message: `${operation === 'assign' ? 'Assigning' : 'Removing'} role membership…` }));
    try {
      await api(`/api/rbac/v1/role-memberships/${operation}`, {
        method: 'POST',
        body: JSON.stringify({ userId, roleCode, reason: membershipReason.trim() })
      });
      setSelectedUserId('');
      setMembershipReason('');
      await Promise.all([loadDetail(), loadMembershipDirectory(), loadFoundation()]);
      setState((current) => ({ ...current, busy: false, message: `Role membership ${operation === 'assign' ? 'assigned' : 'removed'}. The change applies on the user’s next authorized request.` }));
    } catch (error) {
      setState((current) => ({ ...current, busy: false, error: error.message || 'Role membership change failed.' }));
    }
  }

  async function registerModule(event) {
    event.preventDefault();
    setState((current) => ({ ...current, busy: true, error: '', message: 'Registering the module in RBAC…' }));
    try {
      await api('/api/rbac/v1/modules/register', { method: 'POST', body: JSON.stringify(moduleForm) });
      setModuleForm(blankModuleForm());
      await Promise.all([loadFoundation(), loadModuleCatalog()]);
      window.dispatchEvent(new CustomEvent('projectpulse:permissions-changed'));
      setState((current) => ({ ...current, busy: false, message: 'Module registered. Every ordinary role defaults to No Access until explicitly configured; Super Administrator retains Full Control.' }));
    } catch (error) {
      setState((current) => ({ ...current, busy: false, error: error.message || 'Module registration failed.' }));
    }
  }

  async function changeModuleLifecycle(module, operation) {
    const reasonText = window.prompt(`${operation === 'retire' ? 'Retire' : 'Restore'} ${module.moduleName}. Enter the required reason:`);
    if (!reasonText?.trim()) return;
    setState((current) => ({ ...current, busy: true, error: '', message: `${operation === 'retire' ? 'Retiring' : 'Restoring'} module…` }));
    try {
      await api(`/api/rbac/v1/modules/${encodeURIComponent(module.moduleCode)}/${operation}`, {
        method: 'POST',
        body: JSON.stringify({ reason: reasonText.trim() })
      });
      await Promise.all([loadFoundation(), loadModuleCatalog()]);
      window.dispatchEvent(new CustomEvent('projectpulse:permissions-changed'));
      setState((current) => ({ ...current, busy: false, message: `Module ${operation === 'retire' ? 'retired' : 'restored'} successfully.` }));
    } catch (error) {
      setState((current) => ({ ...current, busy: false, error: error.message || 'Module lifecycle change failed.' }));
    }
  }

  async function restore(version) {
    const versionNumber = pick(version, 'versionNumber', 'VersionNumber', '—');
    const policyVersionId = pick(version, 'policyVersionId', 'PolicyVersionId', '');
    if (!policyVersionId) return;
    const restoreReason = window.prompt(`Restore version ${versionNumber} as a new immutable policy version. Enter the required reason:`);
    if (!restoreReason?.trim()) return;
    try {
      await api(`/api/rbac/v1/policies/versions/${encodeURIComponent(policyVersionId)}/restore`, { method: 'POST', body: JSON.stringify({ reason: restoreReason.trim() }) });
      window.dispatchEvent(new CustomEvent('projectpulse:permissions-changed'));
      await loadFoundation();
    } catch (error) {
      setState((current) => ({ ...current, error: error.message || 'Policy restore failed.' }));
    }
  }

  if (state.loading) return <section className="role-permission-workbench"><div className="rpw-loading">Loading dynamic RBAC administration…</div></section>;
  if (!bootstrap) return <section className="role-permission-workbench"><div className="rpw-foundation-error"><p className="eyebrow">Module 012</p><h2>RBAC administration did not load</h2><p>{state.error}</p><button type="button" onClick={loadFoundation}>Try again</button></div></section>;

  const actorRoles = arr(bootstrap?.actor?.roleCodes || bootstrap?.actor?.RoleCodes);

  return <section className="role-permission-workbench dynamic-rbac-admin" data-projectpulse-module="012" data-rbac-contract="projectpulse-rbac-v1">
    <header className="rpw-hero dynamic-rbac-hero">
      <div><p className="eyebrow">Module 012</p><h1>Role-Based Access Control</h1><p>Assign users to roles, configure each role’s permissions, and keep the module catalog flexible as Pulse grows.</p></div>
      <div className="rpw-kpis"><article><span>Policy</span><strong>v{bootstrap.policyVersion?.versionNumber || bootstrap.policyVersion?.VersionNumber || '—'}</strong></article><article><span>Active roles</span><strong>{roles.length}</strong></article><article><span>Active modules</span><strong>{modules.length}</strong></article></div>
    </header>

    <section className="dynamic-rbac-invariant">
      <div><strong>Super Administrator</strong><span>Permanent organization-wide Full Control across every active module. This access cannot be reduced in Module 012.</span></div>
      <div><strong>New modules</strong><span>Default to No Access for ordinary roles until an administrator publishes an explicit permission.</span></div>
      <div><strong>Role membership</strong><span>A user receives the combined permissions of every active role assigned to that user; explicit denials take precedence.</span></div>
    </section>

    <section className="rpw-session-status">
      <div><strong>Current policy session</strong><span>{bootstrap.isViewAs ? 'Administrator View-As · read-only' : 'Own authenticated session'}</span></div>
      <div><strong>Effective roles</strong><span>{actorRoles.length ? actorRoles.join(', ') : 'No scoped role returned'}</span></div>
      <div><strong>Publishing</strong><span>{canWrite ? 'Available' : 'Unavailable in this session'}</span></div>
    </section>

    {!canWrite ? <div className="rpw-banner"><strong>Read-only review</strong><span>Publishing, role membership, and module catalog changes require an actual Super Administrator assignment in the administrator’s own session.</span></div> : null}

    <nav className="dynamic-rbac-tabs" aria-label="RBAC administration views">
      <button type="button" className={tab === 'permissions' ? 'active' : ''} onClick={() => setTab('permissions')}>Role permissions</button>
      <button type="button" className={tab === 'members' ? 'active' : ''} onClick={() => setTab('members')}>Role members</button>
      <button type="button" className={tab === 'modules' ? 'active' : ''} onClick={() => setTab('modules')}>Module catalog</button>
      <button type="button" className={tab === 'history' ? 'active' : ''} onClick={() => setTab('history')}>Policy history</button>
    </nav>

    {tab === 'permissions' ? <>
      <section className="rpw-role-first">
        <label><span>1. Select role</span><select value={roleCode} onChange={(event) => setRoleCode(event.target.value)}>{roles.map((role) => <option key={role.roleCode} value={role.roleCode}>{role.roleName} · {role.activeUserCount} user(s)</option>)}</select></label>
        <article><p className="eyebrow">Role purpose</p><h2>{roleGuidance.title || selectedRole?.roleName}</h2><p>{roleGuidance.purpose || selectedRole?.description || 'This role inherits the permissions published below.'}</p><strong>Access boundary</strong><span>{roleGuidance.boundary || 'Use the selected data scope to define whose records this role may use.'}</span></article>
        <article className="rpw-role-recommendation"><p className="eyebrow">Recommended starting point</p><h2>{roleGuidance.recommendedLevel || 'View'}</h2><p>{ROLE_SCOPES[roleCode] || 'SELF'} data scope</p></article>
      </section>

      <section className="rpw-module-picker">
        <label><span>2. Find module</span><input value={moduleSearch} onChange={(event) => setModuleSearch(event.target.value)} placeholder="Module number, name, or route" /></label>
        <label><span>3. Select module</span><select value={moduleCode} onChange={(event) => setModuleCode(event.target.value)}>{visibleModules.map((module) => <option key={module.moduleCode} value={module.moduleCode}>{module.moduleName}</option>)}</select></label>
        <article><strong>{selectedModule?.moduleName}</strong><span>{selectedModule?.permissionNotes || 'No module-specific exception note.'}</span><small>{selectedModule?.routeScope} · {selectedModule?.currentState}</small></article>
      </section>

      {superAdmin ? <div className="rpw-super-admin-invariant"><strong>Super Administrator invariant</strong><p>Permanent <b>Full Control</b> with organization-wide scope for every active module. This value cannot be reduced.</p></div> : null}

      <section className="rpw-level-section">
        <header><div><p className="eyebrow">4. Permission template</p><h2>Choose the closest access level</h2><p>The detailed table below shows the exact actions included.</p></div><strong className="rpw-level-badge">{effectiveLevel}</strong></header>
        <div className="rpw-level-grid">{LEVELS.map(([name, description]) => <button type="button" key={name} className={effectiveLevel === name ? 'selected' : ''} disabled={!canWrite || superAdmin || unavailable(moduleCode, roleCode, name)} onClick={() => choosePreset(name)}><strong>{name}</strong><span>{description}</span></button>)}</div>
      </section>

      <section className="rpw-scope-section">
        <div><p className="eyebrow">5. Data scope</p><h2>Whose information can this role use?</h2><p>Permission and data visibility are separate. A role may edit records only within the selected scope.</p></div>
        <label><span>Effective scope</span><select value={effectiveScope} disabled={!canWrite || superAdmin || ['No Access', 'Not Set'].includes(effectiveLevel)} onChange={(event) => { setScope(event.target.value); setValidation(null); }}>{catalog.scopes.map((item) => <option key={item.scopeCode} value={item.scopeCode}>{item.scopeCode} · {item.scopeDescription}</option>)}</select></label>
        <div className="rpw-scope-hint"><strong>Recommended</strong><span>{ROLE_SCOPES[roleCode] || 'SELF'}</span></div>
      </section>

      <section className="rpw-permission-table-section">
        <header><div><p className="eyebrow">6. Detailed permissions</p><h2>{selectedRole?.roleName} · {selectedModule?.moduleName}</h2><p>Use a template for speed, or change an individual permission to switch into Custom mode.</p></div><label><span>Search permissions</span><input value={permissionSearch} onChange={(event) => setPermissionSearch(event.target.value)} placeholder="Permission code or plain-language action" /></label></header>
        <div className="rpw-table-wrap"><table className="rpw-permission-table"><thead><tr><th>Permission</th><th>What it allows</th><th>Decision</th><th>Data scope</th><th>Safeguards</th></tr></thead><tbody>{actionRows.map((action) => {
          const grant = draft.find((item) => item.actionCode === action.actionCode);
          const decision = grant?.effect || 'NOT_SET';
          const ptcProtected = roleCode === 'PROJECT_TEAM_COORDINATOR' && moduleCode === '001' && PTC_DENIED_ACTIONS.has(action.actionCode);
          return <tr key={action.actionCode} className={decision === 'GRANT' ? 'allowed' : decision === 'DENY' ? 'denied' : ''}>
            <td><strong>{actionLabel(action.actionCode)}</strong><code>{action.actionCode}</code></td>
            <td>{actionDescription(action.actionCode, action.actionDescription)}</td>
            <td><select value={decision} disabled={!canWrite || superAdmin || action.isNonBypassable || ptcProtected} onChange={(event) => updateAction(action.actionCode, event.target.value)}><option value="NOT_SET">Not configured</option><option value="GRANT">Allow</option><option value="DENY">Deny</option></select>{ptcProtected ? <small>Protected PTC boundary</small> : null}</td>
            <td><select value={grant?.scopeCode || effectiveScope} disabled={!canWrite || superAdmin || !grant || decision === 'NOT_SET'} onChange={(event) => updateActionScope(action.actionCode, event.target.value)}>{catalog.scopes.map((item) => <option key={item.scopeCode} value={item.scopeCode}>{item.scopeCode}</option>)}</select></td>
            <td><div className="rpw-safeguards">{action.isNonBypassable ? <span>Non-bypassable</span> : null}{grant?.delegatedAuthority ? <span>Delegated</span> : null}{grant?.reasonRequired ? <span>Reason required</span> : null}{grant?.auditRequired ? <span>Audited</span> : null}</div></td>
          </tr>;
        })}</tbody></table></div>
      </section>

      <section className="rpw-publish">
        <div><p className="eyebrow">7. Review and publish</p><h2>{pending ? 'Pending role permission change' : 'Matches the published policy'}</h2><p>Module 037 reads this same published policy and updates after refresh.</p></div>
        <label><span>Change notes</span><textarea value={notes} disabled={!canWrite || superAdmin} onChange={(event) => setNotes(event.target.value)} placeholder="What changed?" /></label>
        <label><span>Required reason</span><textarea value={reason} disabled={!canWrite || superAdmin} onChange={(event) => setReason(event.target.value)} placeholder="Why is this permission change needed?" /></label>
        <div className="rpw-publish-actions"><button type="button" disabled={!canWrite || superAdmin || state.busy || !pending} onClick={validate}>Validate changes</button><button type="button" className="primary" disabled={!canWrite || superAdmin || state.busy || !pending || !validation?.valid} onClick={publish}>Publish new policy version</button><button type="button" disabled={!pending} onClick={loadDetail}>Discard</button></div>
        {validation ? <div className={validation.valid ? 'rpw-validation valid' : 'rpw-validation invalid'}><strong>{validation.valid ? 'Validation passed' : 'Validation blocked'}</strong>{validation.errors.map((item) => <span key={item}>{item}</span>)}{validation.warnings.map((item) => <span key={item}>Warning: {item}</span>)}</div> : null}
      </section>
    </> : null}

    {tab === 'members' ? <section className="dynamic-rbac-members">
      <header><div><p className="eyebrow">Role membership</p><h2>{selectedRole?.roleName}</h2><p>Assign this role to an active user. The user immediately receives the role’s published permissions on the next authorized request.</p></div><label><span>Select role</span><select value={roleCode} onChange={(event) => setRoleCode(event.target.value)}>{roles.map((role) => <option key={role.roleCode} value={role.roleCode}>{role.roleName}</option>)}</select></label></header>
      <div className="dynamic-rbac-membership-controls">
        <label><span>Find user</span><input type="search" value={userSearch} onChange={(event) => setUserSearch(event.target.value)} placeholder="Name or email" /></label>
        <label><span>User without this role</span><select value={selectedUserId} onChange={(event) => setSelectedUserId(event.target.value)}><option value="">Select a user</option>{unassignedUsers.map((user) => <option key={user.userId} value={user.userId}>{user.displayName || user.email} · {user.email}</option>)}</select></label>
        <label className="reason"><span>Required reason</span><input value={membershipReason} onChange={(event) => setMembershipReason(event.target.value)} placeholder="Why is this membership changing?" /></label>
        <button type="button" className="primary" disabled={!canManageMemberships || !selectedUserId || !membershipReason.trim() || state.busy} onClick={() => changeMembership('assign', selectedUserId)}>Assign role</button>
      </div>
      <div className="dynamic-rbac-assigned-list"><header><h3>Assigned users</h3><span>{assignedUsers.length}</span></header>{assignedUsers.map((user) => <article key={user.userId || user.UserId || user.email}><div><strong>{user.displayName || user.DisplayName || user.email}</strong><span>{user.email || user.Email}</span></div><button type="button" disabled={!canManageMemberships || !membershipReason.trim() || state.busy} onClick={() => changeMembership('remove', user.userId || user.UserId)}>Remove</button></article>)}{!assignedUsers.length ? <p>No active users are assigned to this role.</p> : null}</div>
    </section> : null}

    {tab === 'modules' ? <section className="dynamic-rbac-modules">
      <header><div><p className="eyebrow">Dynamic module catalog</p><h2>Add or retire RBAC modules without changing a fixed count</h2><p>Register a module after its application route is ready. New modules default to No Access for every ordinary role and permanent Full Control for Super Administrator.</p></div><label><span>Search catalog</span><input type="search" value={catalogSearch} onChange={(event) => setCatalogSearch(event.target.value)} placeholder="Name, code, route, or status" /></label></header>
      <form className="dynamic-rbac-module-form" onSubmit={registerModule}>
        <label><span>Module code</span><input value={moduleForm.moduleCode} onChange={(event) => setModuleForm((current) => ({ ...current, moduleCode: event.target.value.toUpperCase() }))} placeholder="081" required /></label>
        <label><span>Page name</span><input value={moduleForm.moduleName} onChange={(event) => setModuleForm((current) => ({ ...current, moduleName: event.target.value }))} placeholder="Security Analytics" required /></label>
        <label><span>Route or scope</span><input value={moduleForm.routeScope} onChange={(event) => setModuleForm((current) => ({ ...current, routeScope: event.target.value }))} placeholder="security-analytics" required /></label>
        <label><span>State</span><input value={moduleForm.currentState} onChange={(event) => setModuleForm((current) => ({ ...current, currentState: event.target.value }))} /></label>
        <label className="notes"><span>Permission notes</span><input value={moduleForm.permissionNotes} onChange={(event) => setModuleForm((current) => ({ ...current, permissionNotes: event.target.value }))} placeholder="Optional guidance for administrators" /></label>
        <label className="reason"><span>Required reason</span><input value={moduleForm.reason} onChange={(event) => setModuleForm((current) => ({ ...current, reason: event.target.value }))} placeholder="Why is this module entering the RBAC catalog?" required /></label>
        <button type="submit" className="primary" disabled={!canManageModules || state.busy}>Register module</button>
      </form>
      <div className="dynamic-rbac-module-list">{visibleCatalogModules.map((module) => <article key={module.moduleCode} className={module.isActive ? 'active' : 'retired'}><div><strong>{module.moduleName}</strong><span>{module.routeScope}</span><small>{module.currentState}{module.permissionNotes ? ` · ${module.permissionNotes}` : ''}</small></div><div><span className="status">{module.isActive ? 'Active' : 'Retired'}</span>{module.isActive ? <button type="button" disabled={!canManageModules || module.protectedGovernanceModule || state.busy} onClick={() => changeModuleLifecycle(module, 'retire')}>Retire</button> : <button type="button" disabled={!canManageModules || state.busy} onClick={() => changeModuleLifecycle(module, 'restore')}>Restore</button>}</div></article>)}</div>
    </section> : null}

    {tab === 'history' ? <section className="dynamic-rbac-history"><header><p className="eyebrow">Immutable policy history</p><h2>Published and retired versions</h2><p>Restoring a version creates a new immutable version; it never rewrites history.</p></header><div>{versions.map((version) => {
      const status = pick(version, 'policyStatus', 'PolicyStatus', '');
      return <article key={pick(version, 'policyVersionId', 'PolicyVersionId', pick(version, 'versionNumber', 'VersionNumber', 'unknown'))}><div><strong>Version {pick(version, 'versionNumber', 'VersionNumber', '—')} · {status}</strong><span>{pick(version, 'policyName', 'PolicyName', '')}</span><small>{pick(version, 'policyNotes', 'PolicyNotes', '')}</small></div><button type="button" disabled={!canWrite || state.busy || status === 'PUBLISHED'} onClick={() => restore(version)}>Restore as new version</button></article>;
    })}</div></section> : null}

    {state.error ? <p className="role-policy-error">{state.error}</p> : null}
    {state.message ? <p className="role-policy-message">{state.message}</p> : null}
  </section>;
}
