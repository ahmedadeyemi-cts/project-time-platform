import { useEffect, useMemo, useState } from 'react';
import './role-admin-directory-panel.css';
import './role-permission-workbench.css';
import {
  ACTION_GUIDANCE,
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

const REQUIRED_ROLE_COUNT = 12;
const REQUIRED_MODULE_COUNT = 70;
const PTC_DENIED_ACTIONS = new Set(['TIME_SUBMIT', 'TIME_DELETE_PERMANENT', 'SYSTEM_CONFIGURE', 'MODULE_CONFIGURE', 'POLICY_PUBLISH', 'POLICY_RESTORE']);

function normalizeSummary(payload) {
  const roles = arr(pick(payload, 'roles', 'Roles', [])).map((role) => ({
    roleCode: pick(role, 'roleCode', 'RoleCode', ''),
    roleName: pick(role, 'roleName', 'RoleName', ''),
    description: pick(role, 'description', 'Description', ''),
    activeUserCount: Number(pick(role, 'activeUserCount', 'ActiveUserCount', 0) || 0)
  }));
  const modules = arr(pick(payload, 'modules', 'Modules', [])).map((module) => ({
    moduleCode: String(pick(module, 'moduleCode', 'ModuleCode', '')),
    moduleName: pick(module, 'moduleName', 'ModuleName', ''),
    routeScope: pick(module, 'routeScope', 'RouteScope', ''),
    currentState: pick(module, 'currentState', 'CurrentState', ''),
    permissionNotes: pick(module, 'permissionNotes', 'PermissionNotes', '')
  }));

  if (roles.length < REQUIRED_ROLE_COUNT || modules.length < REQUIRED_MODULE_COUNT) {
    throw new Error(`Role-policy foundation is incomplete. Expected ${REQUIRED_ROLE_COUNT} roles and ${REQUIRED_MODULE_COUNT} modules, but received ${roles.length} roles and ${modules.length} modules. Refresh your session and try again.`);
  }

  return {
    ...payload,
    roles,
    modules,
    actor: pick(payload, 'actor', 'Actor', null),
    canWritePolicy: Boolean(pick(payload, 'canWritePolicy', 'CanWritePolicy', false)),
    isViewAs: Boolean(pick(payload, 'isViewAs', 'IsViewAs', false)),
    policyVersion: pick(payload, 'policyVersion', 'PolicyVersion', null)
  };
}

function normalizeCatalog(payload) {
  return {
    actions: arr(pick(payload, 'actions', 'Actions', [])).map((action) => ({
      actionCode: pick(action, 'actionCode', 'ActionCode', ''),
      actionDescription: pick(action, 'actionDescription', 'ActionDescription', ''),
      isNonBypassable: Boolean(pick(action, 'isNonBypassable', 'IsNonBypassable', false))
    })).filter((action) => action.actionCode),
    scopes: arr(pick(payload, 'scopes', 'Scopes', [])).map((scope) => ({
      scopeCode: pick(scope, 'scopeCode', 'ScopeCode', ''),
      scopeDescription: pick(scope, 'scopeDescription', 'ScopeDescription', '')
    })).filter((scope) => scope.scopeCode),
    effects: arr(pick(payload, 'effects', 'Effects', ['GRANT', 'DENY']))
  };
}

function relevantAction(actionCode, moduleCode, baseline, draft) {
  if (baseline.some((grant) => grant.actionCode === actionCode) || draft.some((grant) => grant.actionCode === actionCode)) return true;
  if (['MODULE_ACCESS', 'MODULE_VIEW', 'AUDIT_VIEW', 'AUDIT_RECORD'].includes(actionCode)) return true;
  if (moduleCode === '001') return actionCode.startsWith('TIME_');
  if (moduleCode === '002') return actionCode.startsWith('APPROVAL_');
  if (moduleCode === '003') return actionCode.startsWith('UTILIZATION_');
  if (moduleCode === '012') return actionCode.startsWith('POLICY_');
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

export default function RoleAdminDirectoryPanel() {
  const [summary, setSummary] = useState(null);
  const [catalog, setCatalog] = useState({ actions: [], scopes: [], effects: ['GRANT', 'DENY'] });
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
  const [state, setState] = useState({ loading: true, busy: false, error: '', message: '' });

  const roles = arr(summary?.roles);
  const modules = arr(summary?.modules);
  const canWrite = Boolean(summary?.canWritePolicy) && !summary?.isViewAs;
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

  async function loadFoundation() {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const [summaryPayload, catalogPayload, versionPayload] = await Promise.all([
        api('/api/role-policy/summary'),
        api('/api/role-policy/catalog'),
        api('/api/role-policy/versions')
      ]);
      const nextSummary = normalizeSummary(summaryPayload);
      setSummary(nextSummary);
      setCatalog(normalizeCatalog(catalogPayload));
      setVersions(arr(pick(versionPayload, 'versions', 'Versions', [])));
      if (!nextSummary.roles.some((role) => role.roleCode === roleCode)) setRoleCode(nextSummary.roles[0].roleCode);
      if (!nextSummary.modules.some((module) => module.moduleCode === moduleCode)) setModuleCode(nextSummary.modules[0].moduleCode);
      setState({ loading: false, busy: false, error: '', message: '' });
    } catch (error) {
      setSummary(null);
      setState({ loading: false, busy: false, error: error.message || 'Unable to load role permissions.', message: '' });
    }
  }

  async function loadDetail() {
    if (!summary || !roleCode || !moduleCode) return;
    setState((current) => ({ ...current, busy: true, error: '' }));
    try {
      const payload = await api(`/api/role-policy/roles/${encodeURIComponent(roleCode)}?moduleCode=${encodeURIComponent(moduleCode)}`);
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

  useEffect(() => { void loadFoundation(); }, []);
  useEffect(() => { void loadDetail(); }, [summary, roleCode, moduleCode]);

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
      baseVersionNumber: summary?.policyVersion?.versionNumber || summary?.policyVersion?.VersionNumber || 0,
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
      const result = await api('/api/role-policy/validate', { method: 'POST', body: JSON.stringify(requestBody()) });
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
    if (!window.confirm(`Publish the ${selectedRole?.roleName || roleCode} permissions for Module ${moduleCode} as a new immutable policy version?`)) return;
    setState((current) => ({ ...current, busy: true, error: '', message: 'Publishing role permissions…' }));
    try {
      const result = await api('/api/role-policy/publish', { method: 'POST', body: JSON.stringify(requestBody()) });
      setState((current) => ({ ...current, busy: false, message: `Published policy version ${pick(result, 'versionNumber', 'VersionNumber', '—')}. Module 037 will show the same decision after refresh.` }));
      window.dispatchEvent(new CustomEvent('projectpulse:permissions-changed'));
      await loadFoundation();
    } catch (error) {
      setState((current) => ({ ...current, busy: false, error: error.message || 'Publishing failed.' }));
    }
  }

  async function restore(version) {
    const versionNumber = pick(version, 'versionNumber', 'VersionNumber', '—');
    const policyVersionId = pick(version, 'policyVersionId', 'PolicyVersionId', '');
    if (!policyVersionId) return;
    const restoreReason = window.prompt(`Restore version ${versionNumber} as a new immutable policy version. Enter the required reason:`);
    if (!restoreReason?.trim()) return;
    try {
      await api(`/api/role-policy/versions/${encodeURIComponent(policyVersionId)}/restore`, { method: 'POST', body: JSON.stringify({ reason: restoreReason.trim() }) });
      window.dispatchEvent(new CustomEvent('projectpulse:permissions-changed'));
      await loadFoundation();
    } catch (error) {
      setState((current) => ({ ...current, error: error.message || 'Policy restore failed.' }));
    }
  }

  if (state.loading) return <section className="role-permission-workbench"><div className="rpw-loading">Loading the role and permission catalog…</div></section>;
  if (!summary) return <section className="role-permission-workbench"><div className="rpw-foundation-error"><p className="eyebrow">Module 012</p><h2>Role-policy data did not load</h2><p>{state.error}</p><button type="button" onClick={loadFoundation}>Try again</button></div></section>;

  const actorRoles = arr(summary?.actor?.roleCodes || summary?.actor?.RoleCodes);

  return <section className="role-permission-workbench" data-projectpulse-module="012">
    <header className="rpw-hero">
      <div><p className="eyebrow">Module 012</p><h1>Role Administration</h1><p>Select a role first, review what that role is responsible for, then configure its module permissions in plain language.</p></div>
      <div className="rpw-kpis"><article><span>Policy</span><strong>v{summary.policyVersion?.versionNumber || summary.policyVersion?.VersionNumber || '—'}</strong></article><article><span>Roles</span><strong>{roles.length}</strong></article><article><span>Database modules</span><strong>{modules.length}</strong></article></div>
    </header>

    <section className="rpw-session-status">
      <div><strong>Current policy session</strong><span>{summary.isViewAs ? 'Administrator View-As · read-only' : 'Own authenticated session'}</span></div>
      <div><strong>Effective roles</strong><span>{actorRoles.length ? actorRoles.join(', ') : 'No scoped role returned'}</span></div>
      <div><strong>Publishing</strong><span>{canWrite ? 'Available' : 'Unavailable in this session'}</span></div>
    </section>

    {!canWrite ? <div className="rpw-banner"><strong>Read-only review</strong><span>Publishing requires an actual Super Administrator assignment in the administrator’s own session. You can still review every role and permission.</span></div> : null}

    <section className="rpw-role-first">
      <label><span>1. Select role</span><select value={roleCode} onChange={(event) => setRoleCode(event.target.value)}>{roles.map((role) => <option key={role.roleCode} value={role.roleCode}>{role.roleName} · {role.activeUserCount} user(s)</option>)}</select></label>
      <article><p className="eyebrow">Role purpose</p><h2>{roleGuidance.title || selectedRole?.roleName}</h2><p>{roleGuidance.purpose || selectedRole?.description || 'No role description is stored.'}</p><strong>Access boundary</strong><span>{roleGuidance.boundary || 'Use the selected data scope to define whose records this role may use.'}</span></article>
      <article className="rpw-role-recommendation"><p className="eyebrow">Recommended starting point</p><h2>{roleGuidance.recommendedLevel || 'View'}</h2><p>{ROLE_SCOPES[roleCode] || 'SELF'} data scope</p></article>
    </section>

    {roleCode === 'PROJECT_TEAM_COORDINATOR' ? <section className="rpw-ptc-steward">
      <div><p className="eyebrow">Project Team Coordinator · Time Steward</p><h2>Manage other users’ time without submitting for them</h2><p>The recommended Module 001 Manage preset lets the PTC select a user, return submitted time to draft, correct or remove an entry, move it to another task, and create or assign a replacement task. Every change requires a reason and immutable audit evidence.</p></div>
      <ul><li>Can reopen and unsubmit time for correction</li><li>Can change hours, description, billing flag, project, and task</li><li>Can create and assign the correct task before moving time</li><li>Cannot submit a timesheet on another user’s behalf</li><li>Cannot permanently delete audit evidence or configure the platform</li></ul>
    </section> : null}

    <section className="rpw-module-picker">
      <label><span>2. Find module</span><input value={moduleSearch} onChange={(event) => setModuleSearch(event.target.value)} placeholder="Module number, name, or route" /></label>
      <label><span>3. Select module</span><select value={moduleCode} onChange={(event) => setModuleCode(event.target.value)}>{visibleModules.map((module) => <option key={module.moduleCode} value={module.moduleCode}>Module {module.moduleCode} · {module.moduleName}</option>)}</select></label>
      <article><strong>Module {moduleCode} · {selectedModule?.moduleName}</strong><span>{selectedModule?.permissionNotes || 'No module-specific exception note.'}</span><small>{selectedModule?.routeScope} · {selectedModule?.currentState}</small></article>
    </section>

    {superAdmin ? <div className="rpw-super-admin-invariant"><strong>Super Administrator invariant</strong><p>Permanent <b>Full Control</b> with organization-wide scope for every module. This value cannot be reduced.</p></div> : null}

    <section className="rpw-level-section">
      <header><div><p className="eyebrow">4. Quick permission template</p><h2>Choose the closest access level</h2><p>The detailed permission table below shows exactly what the selected template grants.</p></div><strong className="rpw-level-badge">{effectiveLevel}</strong></header>
      <div className="rpw-level-grid">{LEVELS.map(([name, description]) => <button type="button" key={name} className={effectiveLevel === name ? 'selected' : ''} disabled={!canWrite || superAdmin || unavailable(moduleCode, roleCode, name)} onClick={() => choosePreset(name)}><strong>{name}</strong><span>{description}</span></button>)}</div>
    </section>

    <section className="rpw-scope-section">
      <div><p className="eyebrow">5. Data scope</p><h2>Whose information can this role use?</h2><p>Permission and data visibility are separate. A role may be allowed to edit, but only within this selected scope.</p></div>
      <label><span>Effective scope</span><select value={effectiveScope} disabled={!canWrite || superAdmin || ['No Access', 'Not Set'].includes(effectiveLevel)} onChange={(event) => { setScope(event.target.value); setValidation(null); }}>{catalog.scopes.map((item) => <option key={item.scopeCode} value={item.scopeCode}>{item.scopeCode} · {item.scopeDescription}</option>)}</select></label>
      <div className="rpw-scope-hint"><strong>Recommended</strong><span>{ROLE_SCOPES[roleCode] || 'SELF'}</span></div>
    </section>

    <section className="rpw-permission-table-section">
      <header><div><p className="eyebrow">6. Detailed permissions</p><h2>{selectedRole?.roleName} · Module {moduleCode}</h2><p>Use the template for speed, or change an individual row to switch into Custom mode.</p></div><label><span>Search permissions</span><input value={permissionSearch} onChange={(event) => setPermissionSearch(event.target.value)} placeholder="Permission code or plain-language action" /></label></header>
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

    <section className="rpw-users"><header><h2>Assigned users</h2><span>{arr(detail?.assignedUsers).length}</span></header><div>{arr(detail?.assignedUsers).slice(0, 20).map((user) => <article key={user.userId || user.UserId || user.email}><strong>{user.displayName || user.DisplayName || user.email}</strong><span>{user.email || user.Email}</span></article>)}{!arr(detail?.assignedUsers).length ? <p>No active users are assigned to this role.</p> : null}</div></section>

    <section className="rpw-publish">
      <div><p className="eyebrow">7. Review and publish</p><h2>{pending ? 'Pending role permission change' : 'Matches the published policy'}</h2><p>Module 037 reads the same published policy and displays the role-by-permission matrix after refresh.</p></div>
      <label><span>Change notes</span><textarea value={notes} disabled={!canWrite || superAdmin} onChange={(event) => setNotes(event.target.value)} placeholder="What changed?" /></label>
      <label><span>Required reason</span><textarea value={reason} disabled={!canWrite || superAdmin} onChange={(event) => setReason(event.target.value)} placeholder="Why is this permission change needed?" /></label>
      <div className="rpw-publish-actions"><button type="button" disabled={!canWrite || superAdmin || state.busy || !pending} onClick={validate}>Validate changes</button><button type="button" className="primary" disabled={!canWrite || superAdmin || state.busy || !pending || !validation?.valid} onClick={publish}>Publish new policy version</button><button type="button" disabled={!pending} onClick={loadDetail}>Discard</button></div>
      {validation ? <div className={validation.valid ? 'rpw-validation valid' : 'rpw-validation invalid'}><strong>{validation.valid ? 'Validation passed' : 'Validation blocked'}</strong>{validation.errors.map((item) => <span key={item}>{item}</span>)}{validation.warnings.map((item) => <span key={item}>Warning: {item}</span>)}</div> : null}
    </section>

    <details className="rpw-history"><summary>Policy version history</summary><div>{versions.map((version) => {
      const status = pick(version, 'policyStatus', 'PolicyStatus', '');
      return <article key={pick(version, 'policyVersionId', 'PolicyVersionId', pick(version, 'versionNumber', 'VersionNumber', 'unknown'))}><strong>Version {pick(version, 'versionNumber', 'VersionNumber', '—')} · {status}</strong><span>{pick(version, 'policyName', 'PolicyName', '')}</span><button type="button" disabled={!canWrite || state.busy || status === 'PUBLISHED'} onClick={() => restore(version)}>Restore as new version</button></article>;
    })}</div></details>

    {state.error ? <p className="role-policy-error">{state.error}</p> : null}
    {state.message ? <p className="role-policy-message">{state.message}</p> : null}
  </section>;
}
