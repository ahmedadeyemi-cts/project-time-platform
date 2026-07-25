import { useEffect, useMemo, useState } from 'react';
import './role-admin-directory-panel.css';
import './role-permission-workbench.css';
import { LEVELS, ROLE_SCOPES, api, arr, grantsFor, inferLevel, inferScope, normalizeGrant, pick, stable, unavailable } from './role-permission-model.js';

export default function RoleAdminDirectoryPanel() {
  const [summary, setSummary] = useState(null);
  const [catalog, setCatalog] = useState({ actions: [], scopes: [], effects: ['GRANT', 'DENY'] });
  const [versions, setVersions] = useState([]);
  const [roleCode, setRoleCode] = useState('SUPER_ADMINISTRATOR');
  const [moduleCode, setModuleCode] = useState('012');
  const [search, setSearch] = useState('');
  const [detail, setDetail] = useState(null);
  const [baseline, setBaseline] = useState([]);
  const [level, setLevel] = useState('Full Control');
  const [scope, setScope] = useState('ORGANIZATION');
  const [custom, setCustom] = useState([]);
  const [reason, setReason] = useState('');
  const [notes, setNotes] = useState('');
  const [validation, setValidation] = useState(null);
  const [state, setState] = useState({ loading: true, busy: false, error: '', message: '' });
  const roles = arr(summary?.roles);
  const modules = arr(summary?.modules);
  const canWrite = Boolean(summary?.canWritePolicy) && !summary?.isViewAs;
  const superAdmin = roleCode === 'SUPER_ADMINISTRATOR';
  const effectiveLevel = superAdmin ? 'Full Control' : level;
  const effectiveScope = superAdmin ? 'ORGANIZATION' : scope;
  const draft = useMemo(() => effectiveLevel === 'Custom' ? custom : grantsFor(moduleCode, roleCode, effectiveLevel, effectiveScope), [custom, effectiveLevel, effectiveScope, moduleCode, roleCode]);
  const pending = !superAdmin && stable(draft) !== stable(baseline);
  const selectedRole = roles.find((r) => r.roleCode === roleCode);
  const selectedModule = modules.find((m) => m.moduleCode === moduleCode);
  const visibleModules = modules.filter((m) => !search.trim() || `${m.moduleCode} ${m.moduleName} ${m.routeScope}`.toLowerCase().includes(search.trim().toLowerCase()));

  async function loadFoundation() {
    setState((s) => ({ ...s, loading: true, error: '' }));
    try {
      const [summaryData, catalogData, versionData] = await Promise.all([api('/api/role-policy/summary'), api('/api/role-policy/catalog'), api('/api/role-policy/versions')]);
      const next = {
        ...summaryData,
        roles: arr(pick(summaryData, 'roles', 'Roles', [])).map((r) => ({ roleCode: pick(r, 'roleCode', 'RoleCode', ''), roleName: pick(r, 'roleName', 'RoleName', ''), description: pick(r, 'description', 'Description', ''), activeUserCount: Number(pick(r, 'activeUserCount', 'ActiveUserCount', 0) || 0) })),
        modules: arr(pick(summaryData, 'modules', 'Modules', [])).map((m) => ({ moduleCode: String(pick(m, 'moduleCode', 'ModuleCode', '')), moduleName: pick(m, 'moduleName', 'ModuleName', ''), routeScope: pick(m, 'routeScope', 'RouteScope', ''), currentState: pick(m, 'currentState', 'CurrentState', ''), permissionNotes: pick(m, 'permissionNotes', 'PermissionNotes', '') })),
        canWritePolicy: Boolean(pick(summaryData, 'canWritePolicy', 'CanWritePolicy', false)), isViewAs: Boolean(pick(summaryData, 'isViewAs', 'IsViewAs', false)), policyVersion: pick(summaryData, 'policyVersion', 'PolicyVersion', null)
      };
      setSummary(next);
      setCatalog({ actions: arr(pick(catalogData, 'actions', 'Actions', [])).map((a) => ({ actionCode: pick(a, 'actionCode', 'ActionCode', ''), isNonBypassable: Boolean(pick(a, 'isNonBypassable', 'IsNonBypassable', false)) })), scopes: arr(pick(catalogData, 'scopes', 'Scopes', [])).map((s) => ({ scopeCode: pick(s, 'scopeCode', 'ScopeCode', ''), scopeDescription: pick(s, 'scopeDescription', 'ScopeDescription', '') })), effects: arr(pick(catalogData, 'effects', 'Effects', ['GRANT', 'DENY'])) });
      setVersions(arr(pick(versionData, 'versions', 'Versions', [])));
      setState({ loading: false, busy: false, error: '', message: '' });
    } catch (error) { setState({ loading: false, busy: false, error: error.message, message: '' }); }
  }
  async function loadDetail() {
    if (!summary) return;
    try {
      const data = await api(`/api/role-policy/roles/${encodeURIComponent(roleCode)}?moduleCode=${encodeURIComponent(moduleCode)}`);
      const grants = arr(pick(data, 'grants', 'Grants', [])).map(normalizeGrant);
      setDetail({ ...data, assignedUsers: arr(pick(data, 'assignedUsers', 'AssignedUsers', [])) });
      setBaseline(grants); setCustom(grants.map((g) => ({ ...g })));
      setLevel(superAdmin ? 'Full Control' : inferLevel(grants, roleCode));
      setScope(superAdmin ? 'ORGANIZATION' : inferScope(grants, roleCode));
      setReason(''); setNotes(''); setValidation(null);
    } catch (error) { setState((s) => ({ ...s, error: error.message })); }
  }
  useEffect(() => { void loadFoundation(); }, []);
  useEffect(() => { void loadDetail(); }, [summary, roleCode, moduleCode]);

  function requestBody() {
    if (!reason.trim()) throw new Error('A reason is required.');
    return { baseVersionNumber: summary?.policyVersion?.versionNumber || summary?.policyVersion?.VersionNumber || 0, reason: reason.trim(), changes: [{ roleCode, moduleCode, notes: notes.trim() || `${effectiveLevel} within ${effectiveScope}.`, grants: draft.map((g) => ({ actionCode: g.actionCode, scopeCode: g.scopeCode, effect: g.effect, conditions: g.conditions || {}, delegatedAuthority: !!g.delegatedAuthority, reasonRequired: !!g.reasonRequired, auditRequired: g.auditRequired !== false, isActive: g.isActive !== false })) }] };
  }
  async function validate() {
    setState((s) => ({ ...s, busy: true, error: '', message: 'Validating permission…' }));
    try { const result = await api('/api/role-policy/validate', { method: 'POST', body: JSON.stringify(requestBody()) }); const normalized = { valid: Boolean(pick(result, 'valid', 'Valid', false)), errors: arr(pick(result, 'errors', 'Errors', [])), warnings: arr(pick(result, 'warnings', 'Warnings', [])) }; setValidation(normalized); setState((s) => ({ ...s, busy: false, message: normalized.valid ? 'Validation passed.' : 'Validation blocked.' })); } catch (error) { setState((s) => ({ ...s, busy: false, error: error.message })); }
  }
  async function publish() {
    if (!window.confirm('Publish this permission as a new immutable policy version?')) return;
    setState((s) => ({ ...s, busy: true, error: '', message: 'Publishing permission…' }));
    try { const result = await api('/api/role-policy/publish', { method: 'POST', body: JSON.stringify(requestBody()) }); setState((s) => ({ ...s, busy: false, message: `Published policy version ${pick(result, 'versionNumber', 'VersionNumber', '—')}. Refresh Module 037 to confirm.` })); window.dispatchEvent(new CustomEvent('projectpulse:permissions-changed')); await loadFoundation(); } catch (error) { setState((s) => ({ ...s, busy: false, error: error.message })); }
  }

  async function restore(version) {
    const versionNumber = pick(version, 'versionNumber', 'VersionNumber', '—');
    const policyVersionId = pick(version, 'policyVersionId', 'PolicyVersionId', '');
    if (!policyVersionId) {
      setState((s) => ({ ...s, error: 'The selected policy version does not have a restorable identifier.' }));
      return;
    }
    const restoreReason = window.prompt(`Restore version ${versionNumber} as a new immutable version. Enter the required reason:`);
    if (!restoreReason?.trim()) return;
    setState((s) => ({ ...s, busy: true, error: '', message: 'Restoring policy version…' }));
    try {
      const result = await api(`/api/role-policy/versions/${encodeURIComponent(policyVersionId)}/restore`, {
        method: 'POST',
        body: JSON.stringify({ reason: restoreReason.trim() })
      });
      const sourceVersion = pick(result, 'sourceVersionNumber', 'SourceVersionNumber', versionNumber);
      const nextVersion = pick(result, 'versionNumber', 'VersionNumber', '—');
      setState((s) => ({ ...s, busy: false, message: `Restored version ${sourceVersion} as version ${nextVersion}. Refresh Module 037 to confirm.` }));
      window.dispatchEvent(new CustomEvent('projectpulse:permissions-changed'));
      await loadFoundation();
    } catch (error) {
      setState((s) => ({ ...s, busy: false, error: error.message || 'Policy restore failed.' }));
    }
  }

  if (state.loading) return <section className="role-permission-workbench">Loading role permissions…</section>;
  if (!summary) return <section className="role-permission-workbench"><h2>Module 012 · Role Administration</h2><p className="role-policy-error">{state.error}</p><button onClick={loadFoundation}>Retry</button></section>;

  return <section className="role-permission-workbench" data-projectpulse-module="012">
    <header className="rpw-hero"><div><p className="eyebrow">Module 012</p><h1>Role Administration</h1><p>Select a role, choose a database-backed module, assign one permission level, and define whose records are included.</p></div><div className="rpw-kpis"><article><span>Policy</span><strong>v{summary.policyVersion?.versionNumber || '—'}</strong></article><article><span>Roles</span><strong>{roles.length}</strong></article><article><span>Database modules</span><strong>{modules.length}</strong></article></div></header>
    {!canWrite ? <div className="rpw-banner"><strong>Read-only review</strong><span>Publishing requires a Super Administrator in their own session.</span></div> : null}
    <section className="rpw-controls">
      <label><span>Role</span><select value={roleCode} onChange={(e) => setRoleCode(e.target.value)}>{roles.map((r) => <option key={r.roleCode} value={r.roleCode}>{r.roleName} · {r.activeUserCount} user(s)</option>)}</select></label>
      <label><span>Find module</span><input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Number, name, or route" /></label>
      <label><span>Module</span><select value={moduleCode} onChange={(e) => setModuleCode(e.target.value)}>{visibleModules.map((m) => <option key={m.moduleCode} value={m.moduleCode}>Module {m.moduleCode} · {m.moduleName}</option>)}</select></label>
    </section>
    <section className="rpw-context"><article><span>Role</span><h2>{selectedRole?.roleName}</h2><p>{selectedRole?.description || 'No role description is stored.'}</p></article><article><span>Module</span><h2>Module {moduleCode} · {selectedModule?.moduleName}</h2><p>{selectedModule?.permissionNotes || 'No module-specific exception note.'}</p><small>{selectedModule?.routeScope} · {selectedModule?.currentState}</small></article></section>
    {superAdmin ? <div className="rpw-super-admin-invariant"><strong>Super Administrator invariant</strong><p>Permanent <b>Full Control</b> with organization-wide scope for every module. This value cannot be reduced.</p></div> : null}
    <section className="rpw-permission-section"><header><div><p className="eyebrow">Permission level</p><h2>What can this role do?</h2></div><strong className="rpw-level-badge">{effectiveLevel}</strong></header><div className="rpw-level-grid">{LEVELS.map(([name, description]) => <button key={name} className={effectiveLevel === name ? 'selected' : ''} disabled={!canWrite || superAdmin || unavailable(moduleCode, roleCode, name)} title={unavailable(moduleCode, roleCode, name) ? 'This module is read-only or system-controlled for this role.' : ''} onClick={() => { setLevel(name); setValidation(null); }}><strong>{name}</strong><span>{description}</span></button>)}</div></section>
    <section className="rpw-scope-section"><div><p className="eyebrow">Data scope</p><h2>Whose information can this role use?</h2><p>Actions and data visibility are configured separately.</p></div><label><span>Effective scope</span><select value={effectiveScope} disabled={!canWrite || superAdmin || ['No Access', 'Not Set'].includes(effectiveLevel)} onChange={(e) => { setScope(e.target.value); setValidation(null); }}>{catalog.scopes.map((s) => <option key={s.scopeCode} value={s.scopeCode}>{s.scopeCode} · {s.scopeDescription}</option>)}</select></label><div className="rpw-scope-hint"><strong>Recommended scope</strong><span>{ROLE_SCOPES[roleCode] || 'SELF'}</span></div></section>
    <section className="rpw-preview-section"><header><div><p className="eyebrow">Effective preview</p><h2>{effectiveLevel} · {effectiveScope}</h2></div><span>{draft.length} action(s)</span></header><div className="rpw-action-chips">{draft.map((g, i) => <span key={`${g.actionCode}-${i}`} className={g.effect === 'DENY' ? 'deny' : ''}>{g.effect === 'DENY' ? 'Deny' : 'Allow'} {g.actionCode} · {g.scopeCode}</span>)}{!draft.length ? <span>Not Set · preserve existing authorization</span> : null}</div></section>
    {effectiveLevel === 'Custom' ? <details className="rpw-advanced" open><summary>Advanced custom actions</summary><div className="rpw-custom-actions">{custom.map((g, index) => <article key={`${g.actionCode}-${index}`}><label><span>Action</span><select value={g.actionCode} onChange={(e) => setCustom((rows) => rows.map((row, i) => i === index ? { ...row, actionCode: e.target.value } : row))}>{catalog.actions.map((a) => <option key={a.actionCode} value={a.actionCode}>{a.actionCode}{a.isNonBypassable ? ' · protected' : ''}</option>)}</select></label><label><span>Scope</span><select value={g.scopeCode} onChange={(e) => setCustom((rows) => rows.map((row, i) => i === index ? { ...row, scopeCode: e.target.value } : row))}>{catalog.scopes.map((s) => <option key={s.scopeCode}>{s.scopeCode}</option>)}</select></label><label><span>Effect</span><select value={g.effect} onChange={(e) => setCustom((rows) => rows.map((row, i) => i === index ? { ...row, effect: e.target.value } : row))}>{catalog.effects.map((effect) => <option key={effect}>{effect}</option>)}</select></label><button className="danger" onClick={() => setCustom((rows) => rows.filter((_, i) => i !== index))}>Remove</button></article>)}<button onClick={() => setCustom((rows) => [...rows, normalizeGrant({ actionCode: catalog.actions[0]?.actionCode || 'MODULE_VIEW', scopeCode: effectiveScope })])}>Add custom action</button></div></details> : null}
    <section className="rpw-users"><header><h2>Assigned users</h2><span>{arr(detail?.assignedUsers).length}</span></header><div>{arr(detail?.assignedUsers).slice(0, 12).map((u) => <article key={u.userId || u.UserId || u.email}><strong>{u.displayName || u.DisplayName || u.email}</strong><span>{u.email || u.Email}</span></article>)}{!arr(detail?.assignedUsers).length ? <p>No active users are assigned to this role.</p> : null}</div></section>
    <section className="rpw-publish"><div><p className="eyebrow">Review and publish</p><h2>{pending ? 'Pending permission change' : 'Matches the published policy'}</h2><p>Module 037 reads the same database and displays the published value after refresh.</p></div><label><span>Change notes</span><textarea value={notes} disabled={!canWrite || superAdmin} onChange={(e) => setNotes(e.target.value)} /></label><label><span>Required reason</span><textarea value={reason} disabled={!canWrite || superAdmin} onChange={(e) => setReason(e.target.value)} /></label><div className="rpw-publish-actions"><button disabled={!canWrite || superAdmin || state.busy || !pending} onClick={validate}>Validate</button><button className="primary" disabled={!canWrite || superAdmin || state.busy || !pending || !validation?.valid} onClick={publish}>Publish permission</button><button disabled={!pending} onClick={loadDetail}>Discard</button></div>{validation ? <div className={validation.valid ? 'rpw-validation valid' : 'rpw-validation invalid'}><strong>{validation.valid ? 'Validation passed' : 'Validation blocked'}</strong>{validation.errors.map((x) => <span key={x}>{x}</span>)}{validation.warnings.map((x) => <span key={x}>Warning: {x}</span>)}</div> : null}</section>
    <details className="rpw-history"><summary>Policy version history</summary><div>{versions.map((v) => {
      const status = pick(v, 'policyStatus', 'PolicyStatus', '');
      return <article key={pick(v, 'policyVersionId', 'PolicyVersionId', pick(v, 'versionNumber', 'VersionNumber', 'unknown'))}><strong>Version {pick(v, 'versionNumber', 'VersionNumber', '—')} · {status}</strong><span>{pick(v, 'policyName', 'PolicyName', '')}</span><button type="button" disabled={!canWrite || state.busy || status === 'PUBLISHED'} onClick={() => restore(v)}>Restore as new version</button></article>;
    })}</div></details>
    {state.error ? <p className="role-policy-error">{state.error}</p> : null}{state.message ? <p className="role-policy-message">{state.message}</p> : null}
  </section>;
}
