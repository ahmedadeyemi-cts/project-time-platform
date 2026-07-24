import { useEffect, useMemo, useState } from 'react';
import './role-admin-directory-panel.css';

const DEFAULT_EFFECTS = Object.freeze(['GRANT', 'DENY']);

function asArray(value) {
  return Array.isArray(value) ? value : [];
}

function firstDefined(source, camelName, pascalName, fallback = undefined) {
  if (source && Object.prototype.hasOwnProperty.call(source, camelName)) return source[camelName];
  if (source && Object.prototype.hasOwnProperty.call(source, pascalName)) return source[pascalName];
  return fallback;
}

function headers(json = false) {
  try {
    const session = JSON.parse(localStorage.getItem('projectPulseAuthSession') || 'null');
    return {
      ...(json ? { 'Content-Type': 'application/json' } : {}),
      ...(session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {})
    };
  } catch {
    return json ? { 'Content-Type': 'application/json' } : {};
  }
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    cache: 'no-store',
    headers: {
      ...headers(Boolean(options.body)),
      ...(options.headers || {}),
      'Cache-Control': 'no-cache',
      Pragma: 'no-cache'
    }
  });
  const raw = await response.text();
  let payload = {};
  try { payload = raw ? JSON.parse(raw) : {}; } catch { payload = { message: raw }; }
  if (!response.ok) {
    const error = new Error(payload.message || payload.Message || payload.detail || payload.Detail || `${path} returned HTTP ${response.status}`);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload && typeof payload === 'object' ? payload : {};
}

function normalizeRole(role = {}) {
  return {
    ...role,
    roleCode: firstDefined(role, 'roleCode', 'RoleCode', ''),
    roleName: firstDefined(role, 'roleName', 'RoleName', ''),
    description: firstDefined(role, 'description', 'Description', ''),
    activeUserCount: Number(firstDefined(role, 'activeUserCount', 'ActiveUserCount', 0) || 0)
  };
}

function normalizeModule(module = {}) {
  return {
    ...module,
    moduleCode: String(firstDefined(module, 'moduleCode', 'ModuleCode', '') || ''),
    moduleName: firstDefined(module, 'moduleName', 'ModuleName', ''),
    permissionNotes: firstDefined(module, 'permissionNotes', 'PermissionNotes', ''),
    routeScope: firstDefined(module, 'routeScope', 'RouteScope', ''),
    currentState: firstDefined(module, 'currentState', 'CurrentState', '')
  };
}

function normalizeSummary(payload = {}) {
  const summary = firstDefined(payload, 'summary', 'Summary', {}) || {};
  const policyVersion = firstDefined(payload, 'policyVersion', 'PolicyVersion', null);
  return {
    ...payload,
    roles: asArray(firstDefined(payload, 'roles', 'Roles', [])).map(normalizeRole),
    modules: asArray(firstDefined(payload, 'modules', 'Modules', [])).map(normalizeModule),
    canWritePolicy: Boolean(firstDefined(payload, 'canWritePolicy', 'CanWritePolicy', false)),
    isViewAs: Boolean(firstDefined(payload, 'isViewAs', 'IsViewAs', false)),
    policyVersion,
    summary
  };
}

function normalizeCatalog(payload = {}) {
  const actions = asArray(firstDefined(payload, 'actions', 'Actions', []));
  const scopes = asArray(firstDefined(payload, 'scopes', 'Scopes', []));
  const effects = asArray(firstDefined(payload, 'effects', 'Effects', []));
  return {
    ...payload,
    actions: actions.map((action) => ({
      ...action,
      actionCode: firstDefined(action, 'actionCode', 'ActionCode', ''),
      actionDescription: firstDefined(action, 'actionDescription', 'ActionDescription', ''),
      isNonBypassable: Boolean(firstDefined(action, 'isNonBypassable', 'IsNonBypassable', false))
    })).filter((action) => action.actionCode),
    scopes: scopes.map((scope) => ({
      ...scope,
      scopeCode: firstDefined(scope, 'scopeCode', 'ScopeCode', ''),
      scopeDescription: firstDefined(scope, 'scopeDescription', 'ScopeDescription', '')
    })).filter((scope) => scope.scopeCode),
    effects: effects.length ? effects.map(String) : [...DEFAULT_EFFECTS],
    policyStatuses: asArray(firstDefined(payload, 'policyStatuses', 'PolicyStatuses', []))
  };
}

function normalizeGrant(grant = {}) {
  return {
    actionCode: firstDefined(grant, 'actionCode', 'ActionCode', 'MODULE_VIEW') || 'MODULE_VIEW',
    scopeCode: firstDefined(grant, 'scopeCode', 'ScopeCode', 'SELF') || 'SELF',
    effect: firstDefined(grant, 'grantEffect', 'GrantEffect', firstDefined(grant, 'effect', 'Effect', 'GRANT')) || 'GRANT',
    conditionsText: JSON.stringify(firstDefined(grant, 'conditions', 'Conditions', {}) || {}, null, 2),
    delegatedAuthority: Boolean(firstDefined(grant, 'delegatedAuthority', 'DelegatedAuthority', false)),
    reasonRequired: Boolean(firstDefined(grant, 'reasonRequired', 'ReasonRequired', false)),
    auditRequired: firstDefined(grant, 'auditRequired', 'AuditRequired', true) !== false,
    isActive: firstDefined(grant, 'isActive', 'IsActive', true) !== false
  };
}

function normalizeVersion(version = {}) {
  return {
    ...version,
    policyVersionId: firstDefined(version, 'policyVersionId', 'PolicyVersionId', ''),
    versionNumber: firstDefined(version, 'versionNumber', 'VersionNumber', 0),
    policyStatus: firstDefined(version, 'policyStatus', 'PolicyStatus', ''),
    policyName: firstDefined(version, 'policyName', 'PolicyName', ''),
    grantCount: firstDefined(version, 'grantCount', 'GrantCount', 0),
    auditEventCount: firstDefined(version, 'auditEventCount', 'AuditEventCount', 0),
    publishedBy: firstDefined(version, 'publishedBy', 'PublishedBy', ''),
    publishedAt: firstDefined(version, 'publishedAt', 'PublishedAt', null)
  };
}

function stable(grants) {
  return JSON.stringify(asArray(grants)
    .map((grant) => ({ ...grant, conditionsText: String(grant.conditionsText || '').trim() || '{}' }))
    .sort((left, right) => `${left.actionCode}|${left.scopeCode}|${left.effect}`
      .localeCompare(`${right.actionCode}|${right.scopeCode}|${right.effect}`)));
}

function parseConditions(grant, rowNumber) {
  try {
    return JSON.parse(grant.conditionsText || '{}');
  } catch {
    throw new Error(`Grant row ${rowNumber} contains invalid conditions JSON.`);
  }
}

function formatDate(value) {
  if (!value) return 'Not published';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

export default function RoleAdminDirectoryPanel() {
  const [summary, setSummary] = useState(null);
  const [catalog, setCatalog] = useState(() => normalizeCatalog({}));
  const [versions, setVersions] = useState([]);
  const [legacy, setLegacy] = useState(null);
  const [roleCode, setRoleCode] = useState('SUPER_ADMINISTRATOR');
  const [moduleCode, setModuleCode] = useState('012');
  const [detail, setDetail] = useState(null);
  const [baseline, setBaseline] = useState([]);
  const [draft, setDraft] = useState([]);
  const [notes, setNotes] = useState('');
  const [reason, setReason] = useState('');
  const [validation, setValidation] = useState(null);
  const [state, setState] = useState({ loading: true, busy: false, error: '', message: '' });

  const roles = asArray(summary?.roles);
  const modules = asArray(summary?.modules);
  const catalogActions = asArray(catalog?.actions);
  const catalogScopes = asArray(catalog?.scopes);
  const catalogEffects = asArray(catalog?.effects).length ? asArray(catalog?.effects) : [...DEFAULT_EFFECTS];
  const canWrite = Boolean(summary?.canWritePolicy) && !summary?.isViewAs;
  const pending = stable(draft) !== stable(baseline);
  const selectedRole = useMemo(() => roles.find((role) => role.roleCode === roleCode), [roles, roleCode]);
  const selectedModule = useMemo(() => modules.find((module) => module.moduleCode === moduleCode), [modules, moduleCode]);

  async function loadFoundation() {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const [summaryPayload, catalogPayload, versionPayload, legacyPayload] = await Promise.all([
        api('/api/role-policy/summary'),
        api('/api/role-policy/catalog'),
        api('/api/role-policy/versions'),
        api('/api/role-admin/summary').catch(() => null)
      ]);
      const nextSummary = normalizeSummary(summaryPayload);
      const nextCatalog = normalizeCatalog(catalogPayload);
      const nextVersions = asArray(firstDefined(versionPayload, 'versions', 'Versions', [])).map(normalizeVersion);
      setSummary(nextSummary);
      setCatalog(nextCatalog);
      setVersions(nextVersions);
      setLegacy(legacyPayload);
      if (!nextSummary.roles.some((role) => role.roleCode === roleCode)) {
        setRoleCode(nextSummary.roles[0]?.roleCode || 'SUPER_ADMINISTRATOR');
      }
      if (!nextSummary.modules.some((module) => module.moduleCode === moduleCode)) {
        setModuleCode(nextSummary.modules[0]?.moduleCode || '012');
      }
      setState({ loading: false, busy: false, error: '', message: '' });
    } catch (error) {
      setState({
        loading: false,
        busy: false,
        error: error instanceof Error ? error.message : 'Unable to load scoped role policy.',
        message: ''
      });
    }
  }

  async function loadDetail() {
    if (!summary || !roleCode || !moduleCode) return;
    try {
      const payload = await api(`/api/role-policy/roles/${encodeURIComponent(roleCode)}?moduleCode=${encodeURIComponent(moduleCode)}`);
      const grants = asArray(firstDefined(payload, 'grants', 'Grants', [])).map(normalizeGrant);
      setDetail({
        ...payload,
        assignedUsers: asArray(firstDefined(payload, 'assignedUsers', 'AssignedUsers', [])).map((user) => ({
          ...user,
          userId: firstDefined(user, 'userId', 'UserId', ''),
          displayName: firstDefined(user, 'displayName', 'DisplayName', ''),
          email: firstDefined(user, 'email', 'Email', ''),
          isActive: firstDefined(user, 'isActive', 'IsActive', true) !== false
        }))
      });
      setBaseline(grants);
      setDraft(grants);
      setNotes('');
      setValidation(null);
    } catch (error) {
      setState((current) => ({
        ...current,
        error: error instanceof Error ? error.message : 'Unable to load role policy detail.'
      }));
    }
  }

  useEffect(() => { void loadFoundation(); }, []);
  useEffect(() => { void loadDetail(); }, [summary, roleCode, moduleCode]);

  function updateGrant(index, patch) {
    setDraft((current) => asArray(current).map((grant, currentIndex) =>
      currentIndex === index ? { ...grant, ...patch } : grant
    ));
    setValidation(null);
  }

  function buildRequest() {
    if (!reason.trim()) throw new Error('A reason is required.');
    return {
      baseVersionNumber: summary?.policyVersion?.versionNumber || summary?.policyVersion?.VersionNumber || 0,
      reason: reason.trim(),
      changes: [{
        roleCode,
        moduleCode,
        notes: notes.trim(),
        grants: asArray(draft).map((grant, index) => ({
          actionCode: String(grant.actionCode || 'MODULE_VIEW').trim().toUpperCase(),
          scopeCode: String(grant.scopeCode || 'SELF').trim().toUpperCase(),
          effect: String(grant.effect || 'GRANT').trim().toUpperCase(),
          conditions: parseConditions(grant, index + 1),
          delegatedAuthority: Boolean(grant.delegatedAuthority),
          reasonRequired: Boolean(grant.reasonRequired),
          auditRequired: Boolean(grant.auditRequired),
          isActive: Boolean(grant.isActive)
        }))
      }]
    };
  }

  async function validateDraft() {
    setState((current) => ({ ...current, busy: true, error: '', message: 'Validating pending changes…' }));
    try {
      const result = await api('/api/role-policy/validate', { method: 'POST', body: JSON.stringify(buildRequest()) });
      const normalized = {
        ...result,
        valid: Boolean(firstDefined(result, 'valid', 'Valid', false)),
        errors: asArray(firstDefined(result, 'errors', 'Errors', [])),
        warnings: asArray(firstDefined(result, 'warnings', 'Warnings', []))
      };
      setValidation(normalized);
      setState((current) => ({
        ...current,
        busy: false,
        message: normalized.valid ? 'Policy validation passed.' : 'Policy validation found blocking issues.'
      }));
    } catch (error) {
      setState((current) => ({ ...current, busy: false, error: error.message || 'Validation failed.' }));
    }
  }

  async function publish() {
    if (!window.confirm('Publish these changes as a new immutable policy version?')) return;
    setState((current) => ({ ...current, busy: true, error: '', message: 'Publishing policy version…' }));
    try {
      const result = await api('/api/role-policy/publish', { method: 'POST', body: JSON.stringify(buildRequest()) });
      const versionNumber = firstDefined(result, 'versionNumber', 'VersionNumber', '—');
      setReason('');
      setValidation(null);
      setState((current) => ({ ...current, busy: false, message: `Published version ${versionNumber}.` }));
      await loadFoundation();
    } catch (error) {
      setState((current) => ({ ...current, busy: false, error: error.message || 'Policy publish failed.' }));
    }
  }

  async function restore(version) {
    const restoreReason = window.prompt(`Restore version ${version.versionNumber} as a new version. Enter the required reason:`);
    if (!restoreReason?.trim()) return;
    setState((current) => ({ ...current, busy: true, error: '', message: 'Restoring policy version…' }));
    try {
      const result = await api(`/api/role-policy/versions/${version.policyVersionId}/restore`, {
        method: 'POST',
        body: JSON.stringify({ reason: restoreReason.trim() })
      });
      const sourceVersion = firstDefined(result, 'sourceVersionNumber', 'SourceVersionNumber', version.versionNumber);
      const nextVersion = firstDefined(result, 'versionNumber', 'VersionNumber', '—');
      setState((current) => ({ ...current, busy: false, message: `Restored version ${sourceVersion} as version ${nextVersion}.` }));
      await loadFoundation();
    } catch (error) {
      setState((current) => ({ ...current, busy: false, error: error.message || 'Policy restore failed.' }));
    }
  }

  if (state.loading) return <section className="role-policy-admin">Loading authoritative scoped role policy…</section>;

  if (!summary) {
    return (
      <section className="role-policy-admin">
        <h2>Module 012 · Role Administration</h2>
        <p className="role-policy-error">{state.error}</p>
        <button type="button" onClick={loadFoundation}>Retry</button>
      </section>
    );
  }

  return (
    <section className="role-policy-admin" data-projectpulse-module="012">
      <header className="role-policy-hero">
        <div>
          <p className="eyebrow">Module 012</p>
          <h1>Role Administration</h1>
          <p>Authoritative, versioned administration of granular module actions and scoped access.</p>
        </div>
        <dl>
          <div><dt>Policy</dt><dd>v{summary.policyVersion?.versionNumber || summary.policyVersion?.VersionNumber || '—'}</dd></div>
          <div><dt>Roles</dt><dd>{summary.summary?.roleCount || summary.summary?.RoleCount || roles.length}</dd></div>
          <div><dt>Modules</dt><dd>{summary.summary?.moduleCount || summary.summary?.ModuleCount || modules.length}</dd></div>
          <div><dt>Grants</dt><dd>{summary.summary?.grantCount || summary.summary?.GrantCount || 0}</dd></div>
        </dl>
      </header>

      {!canWrite ? (
        <div className="role-policy-readonly">
          <strong>Read-only policy review</strong>
          <span>Policy writes require an authenticated Super Administrator in their own session. View-As and delegated preview sessions cannot publish or restore policy.</span>
        </div>
      ) : null}

      <section className="role-policy-selectors">
        <label>
          <span>1. Role</span>
          <select value={roleCode} onChange={(event) => setRoleCode(event.target.value)}>
            {roles.map((role) => <option value={role.roleCode} key={role.roleCode}>{role.roleName} · {role.activeUserCount} active user(s)</option>)}
          </select>
        </label>
        <label>
          <span>2. Module</span>
          <select value={moduleCode} onChange={(event) => setModuleCode(event.target.value)}>
            {modules.map((module) => <option value={module.moduleCode} key={module.moduleCode}>Module {module.moduleCode} · {module.moduleName}</option>)}
          </select>
        </label>
      </section>

      <div className="role-policy-context-grid">
        <article><span>Role</span><h2>{selectedRole?.roleName || roleCode}</h2><p>{selectedRole?.description || 'No role description has been configured.'}</p></article>
        <article><span>Module</span><h2>Module {moduleCode} · {selectedModule?.moduleName}</h2><p>{selectedModule?.permissionNotes || 'No workbook exception note.'}</p><small>{selectedModule?.routeScope} · {selectedModule?.currentState}</small></article>
      </div>

      <section className="role-policy-users">
        <header><h2>Assigned users</h2><span>{asArray(detail?.assignedUsers).length}</span></header>
        <div>
          {asArray(detail?.assignedUsers).map((user) => <article key={user.userId || user.email}><strong>{user.displayName}</strong><span>{user.email}</span><small>{user.isActive ? 'Active' : 'Inactive'}</small></article>)}
          {!asArray(detail?.assignedUsers).length ? <p>No active users are assigned to this role.</p> : null}
        </div>
      </section>

      <section className="role-policy-editor">
        <header>
          <div><p className="eyebrow">3. Granular actions and scopes</p><h2>Effective permission configuration</h2></div>
          <div>
            <span className={pending ? 'pending' : 'current'}>{pending ? 'Pending changes' : 'Matches published policy'}</span>
            <button type="button" disabled={!canWrite || state.busy || !catalogActions.length || !catalogScopes.length}
              onClick={() => setDraft((current) => [...asArray(current), normalizeGrant({
                actionCode: catalogActions.find((action) => !action.isNonBypassable)?.actionCode || catalogActions[0]?.actionCode || 'MODULE_VIEW',
                scopeCode: catalogScopes[0]?.scopeCode || 'SELF'
              })])}>Add action</button>
          </div>
        </header>

        <div className="role-policy-grant-list">
          {asArray(draft).map((grant, index) => (
            <article className="role-policy-grant" key={`${grant.actionCode}-${grant.scopeCode}-${index}`}>
              <label><span>Action</span><select value={grant.actionCode} disabled={!canWrite} onChange={(event) => updateGrant(index, { actionCode: event.target.value })}>
                {catalogActions.map((action) => <option value={action.actionCode} key={action.actionCode}>{action.actionCode}{action.isNonBypassable ? ' · non-bypassable' : ''}</option>)}
              </select></label>
              <label><span>Scope</span><select value={grant.scopeCode} disabled={!canWrite} onChange={(event) => updateGrant(index, { scopeCode: event.target.value })}>
                {catalogScopes.map((scope) => <option value={scope.scopeCode} key={scope.scopeCode}>{scope.scopeCode}</option>)}
              </select></label>
              <label><span>Effect</span><select value={grant.effect} disabled={!canWrite} onChange={(event) => updateGrant(index, { effect: event.target.value })}>
                {catalogEffects.map((effect) => <option value={effect} key={effect}>{effect}</option>)}
              </select></label>
              <label className="role-policy-conditions"><span>Optional conditions (JSON)</span><textarea value={grant.conditionsText} disabled={!canWrite} onChange={(event) => updateGrant(index, { conditionsText: event.target.value })} /></label>
              <div className="role-policy-flags">
                {[
                  ['delegatedAuthority', 'Delegated authority'],
                  ['reasonRequired', 'Reason required'],
                  ['auditRequired', 'Audit required'],
                  ['isActive', 'Active']
                ].map(([field, label]) => <label key={field}><input type="checkbox" checked={Boolean(grant[field])} disabled={!canWrite} onChange={(event) => updateGrant(index, { [field]: event.target.checked })} /><span>{label}</span></label>)}
              </div>
              <button type="button" className="danger" disabled={!canWrite} onClick={() => setDraft((current) => asArray(current).filter((_, rowIndex) => rowIndex !== index))}>Remove action</button>
            </article>
          ))}
          {!asArray(draft).length ? <p className="role-policy-empty">No scoped rows. Existing authorization remains in effect for this role/module pair.</p> : null}
        </div>

        <label className="role-policy-notes"><span>Change notes</span><textarea value={notes} disabled={!canWrite} onChange={(event) => setNotes(event.target.value)} placeholder="Explain scope, exception, delegation, and safety reasoning." /></label>
      </section>

      <section className="role-policy-publish">
        <div><p className="eyebrow">4–6. Review, validate, and publish</p><h2>Versioned policy change</h2><p>The published version is immutable. Changes are cloned, validated, audited, and published as a new version.</p></div>
        <label><span>Required reason</span><textarea value={reason} disabled={!canWrite} onChange={(event) => setReason(event.target.value)} /></label>
        <div className="role-policy-publish-actions">
          <button type="button" disabled={!canWrite || state.busy || !pending} onClick={validateDraft}>Validate</button>
          <button type="button" className="primary" disabled={!canWrite || state.busy || !pending || !validation?.valid} onClick={publish}>Publish new policy version</button>
          <button type="button" disabled={!canWrite || state.busy || !pending} onClick={() => { setDraft(asArray(baseline).map((grant) => ({ ...grant }))); setValidation(null); }}>Discard</button>
        </div>
        {validation ? <div className={validation.valid ? 'role-policy-validation valid' : 'role-policy-validation invalid'}><strong>{validation.valid ? 'Validation passed' : 'Validation blocked'}</strong>{asArray(validation.errors).map((item) => <span key={item}>{item}</span>)}{asArray(validation.warnings).map((item) => <span key={item}>Warning: {item}</span>)}</div> : null}
      </section>

      <section className="role-policy-history">
        <header><div><p className="eyebrow">7–8. Audit and controlled restoration</p><h2>Policy versions</h2></div></header>
        <div>{asArray(versions).map((version) => <article key={version.policyVersionId || version.versionNumber}><div><strong>Version {version.versionNumber} · {version.policyStatus}</strong><span>{version.policyName}</span><small>{version.grantCount} grants · {version.auditEventCount} audit event(s)</small><small>Published by {version.publishedBy} · {formatDate(version.publishedAt)}</small></div><button type="button" disabled={!canWrite || state.busy || version.policyStatus === 'PUBLISHED'} onClick={() => restore(version)}>Restore as new version</button></article>)}</div>
      </section>

      <details className="role-policy-legacy-validation">
        <summary>Legacy role and route validation retained</summary>
        <p>Existing roles, permissions, assignments, and route checks remain authoritative for Not Set decisions.</p>
        <dl><div><dt>Legacy roles</dt><dd>{legacy?.summary?.roleCount ?? legacy?.roles?.length ?? 'Preserved'}</dd></div><div><dt>Not Set</dt><dd>Preserve existing authorization</dd></div><div><dt>Safety gates</dt><dd>Secrets, deployment, database, security, and audit remain separate</dd></div></dl>
      </details>

      {state.error ? <p className="role-policy-error">{state.error}</p> : null}
      {state.message ? <p className="role-policy-message">{state.message}</p> : null}
    </section>
  );
}
