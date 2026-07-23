import { useEffect, useMemo, useState } from 'react';
import './role-admin-directory-panel.css';

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
    const error = new Error(payload.message || payload.detail || `${path} returned HTTP ${response.status}`);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload;
}

function normalizeGrant(grant = {}) {
  return {
    actionCode: grant.actionCode || 'MODULE_VIEW',
    scopeCode: grant.scopeCode || 'SELF',
    effect: grant.grantEffect || grant.effect || 'GRANT',
    conditionsText: JSON.stringify(grant.conditions || {}, null, 2),
    delegatedAuthority: Boolean(grant.delegatedAuthority),
    reasonRequired: Boolean(grant.reasonRequired),
    auditRequired: grant.auditRequired !== false,
    isActive: grant.isActive !== false
  };
}

function stable(grants) {
  return JSON.stringify([...(grants || [])]
    .map((grant) => ({ ...grant, conditionsText: grant.conditionsText.trim() || '{}' }))
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
  const [catalog, setCatalog] = useState({ actions: [], scopes: [], effects: ['GRANT', 'DENY'] });
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

  const roles = summary?.roles || [];
  const modules = summary?.modules || [];
  const canWrite = Boolean(summary?.canWritePolicy) && !summary?.isViewAs;
  const pending = stable(draft) !== stable(baseline);
  const selectedRole = useMemo(
    () => roles.find((role) => role.roleCode === roleCode),
    [roles, roleCode]
  );
  const selectedModule = useMemo(
    () => modules.find((module) => module.moduleCode === moduleCode),
    [modules, moduleCode]
  );

  async function loadFoundation() {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const [nextSummary, nextCatalog, versionPayload, legacyPayload] = await Promise.all([
        api('/api/role-policy/summary'),
        api('/api/role-policy/catalog'),
        api('/api/role-policy/versions'),
        api('/api/role-admin/summary').catch(() => null)
      ]);
      setSummary(nextSummary);
      setCatalog(nextCatalog);
      setVersions(versionPayload.versions || []);
      setLegacy(legacyPayload);
      if (!nextSummary.roles?.some((role) => role.roleCode === roleCode)) {
        setRoleCode(nextSummary.roles?.[0]?.roleCode || 'SUPER_ADMINISTRATOR');
      }
      if (!nextSummary.modules?.some((module) => module.moduleCode === moduleCode)) {
        setModuleCode(nextSummary.modules?.[0]?.moduleCode || '012');
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
      const payload = await api(
        `/api/role-policy/roles/${encodeURIComponent(roleCode)}?moduleCode=${encodeURIComponent(moduleCode)}`
      );
      const grants = (payload.grants || []).map(normalizeGrant);
      setDetail(payload);
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
    setDraft((current) => current.map((grant, currentIndex) =>
      currentIndex === index ? { ...grant, ...patch } : grant
    ));
    setValidation(null);
  }

  function buildRequest() {
    if (!reason.trim()) throw new Error('A reason is required.');
    return {
      baseVersionNumber: summary?.policyVersion?.versionNumber || 0,
      reason: reason.trim(),
      changes: [{
        roleCode,
        moduleCode,
        notes: notes.trim(),
        grants: draft.map((grant, index) => ({
          actionCode: grant.actionCode.trim().toUpperCase(),
          scopeCode: grant.scopeCode.trim().toUpperCase(),
          effect: grant.effect.trim().toUpperCase(),
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
      const result = await api('/api/role-policy/validate', {
        method: 'POST',
        body: JSON.stringify(buildRequest())
      });
      setValidation(result);
      setState((current) => ({
        ...current,
        busy: false,
        message: result.valid ? 'Policy validation passed.' : 'Policy validation found blocking issues.'
      }));
    } catch (error) {
      setState((current) => ({ ...current, busy: false, error: error.message || 'Validation failed.' }));
    }
  }

  async function publish() {
    if (!window.confirm('Publish these changes as a new immutable policy version?')) return;
    setState((current) => ({ ...current, busy: true, error: '', message: 'Publishing policy version…' }));
    try {
      const result = await api('/api/role-policy/publish', {
        method: 'POST',
        body: JSON.stringify(buildRequest())
      });
      setReason('');
      setValidation(null);
      setState((current) => ({ ...current, busy: false, message: `Published version ${result.versionNumber}.` }));
      await loadFoundation();
    } catch (error) {
      setState((current) => ({ ...current, busy: false, error: error.message || 'Policy publish failed.' }));
    }
  }

  async function restore(version) {
    const restoreReason = window.prompt(
      `Restore version ${version.versionNumber} as a new version. Enter the required reason:`
    );
    if (!restoreReason?.trim()) return;
    setState((current) => ({ ...current, busy: true, error: '', message: 'Restoring policy version…' }));
    try {
      const result = await api(`/api/role-policy/versions/${version.policyVersionId}/restore`, {
        method: 'POST',
        body: JSON.stringify({ reason: restoreReason.trim() })
      });
      setState((current) => ({
        ...current,
        busy: false,
        message: `Restored version ${result.sourceVersionNumber} as version ${result.versionNumber}.`
      }));
      await loadFoundation();
    } catch (error) {
      setState((current) => ({ ...current, busy: false, error: error.message || 'Policy restore failed.' }));
    }
  }

  if (state.loading) {
    return <section className="role-policy-admin">Loading authoritative scoped role policy…</section>;
  }

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
          <div><dt>Policy</dt><dd>v{summary.policyVersion?.versionNumber || '—'}</dd></div>
          <div><dt>Roles</dt><dd>{summary.summary?.roleCount || 0}</dd></div>
          <div><dt>Modules</dt><dd>{summary.summary?.moduleCount || 0}</dd></div>
          <div><dt>Grants</dt><dd>{summary.summary?.grantCount || 0}</dd></div>
        </dl>
      </header>

      {!canWrite ? (
        <div className="role-policy-readonly">
          <strong>Read-only policy review</strong>
          <span>
            Policy writes require an authenticated Super Administrator in their own session.
            View-As and delegated preview sessions cannot publish or restore policy.
          </span>
        </div>
      ) : null}

      <section className="role-policy-selectors">
        <label>
          <span>1. Role</span>
          <select value={roleCode} onChange={(event) => setRoleCode(event.target.value)}>
            {roles.map((role) => (
              <option value={role.roleCode} key={role.roleCode}>
                {role.roleName} · {role.activeUserCount} active user(s)
              </option>
            ))}
          </select>
        </label>
        <label>
          <span>2. Module</span>
          <select value={moduleCode} onChange={(event) => setModuleCode(event.target.value)}>
            {modules.map((module) => (
              <option value={module.moduleCode} key={module.moduleCode}>
                Module {module.moduleCode} · {module.moduleName}
              </option>
            ))}
          </select>
        </label>
      </section>

      <div className="role-policy-context-grid">
        <article>
          <span>Role</span>
          <h2>{selectedRole?.roleName || roleCode}</h2>
          <p>{selectedRole?.description || 'No role description has been configured.'}</p>
        </article>
        <article>
          <span>Module</span>
          <h2>Module {moduleCode} · {selectedModule?.moduleName}</h2>
          <p>{selectedModule?.permissionNotes || 'No workbook exception note.'}</p>
          <small>{selectedModule?.routeScope} · {selectedModule?.currentState}</small>
        </article>
      </div>

      <section className="role-policy-users">
        <header><h2>Assigned users</h2><span>{detail?.assignedUsers?.length || 0}</span></header>
        <div>
          {(detail?.assignedUsers || []).map((user) => (
            <article key={user.userId}>
              <strong>{user.displayName}</strong>
              <span>{user.email}</span>
              <small>{user.isActive ? 'Active' : 'Inactive'}</small>
            </article>
          ))}
          {!detail?.assignedUsers?.length ? <p>No active users are assigned to this role.</p> : null}
        </div>
      </section>

      <section className="role-policy-editor">
        <header>
          <div>
            <p className="eyebrow">3. Granular actions and scopes</p>
            <h2>Effective permission configuration</h2>
          </div>
          <div>
            <span className={pending ? 'pending' : 'current'}>
              {pending ? 'Pending changes' : 'Matches published policy'}
            </span>
            <button
              type="button"
              disabled={!canWrite || state.busy}
              onClick={() => setDraft((current) => [...current, normalizeGrant({
                actionCode: catalog.actions.find((action) => !action.isNonBypassable)?.actionCode,
                scopeCode: catalog.scopes?.[0]?.scopeCode
              })])}
            >
              Add action
            </button>
          </div>
        </header>

        <div className="role-policy-grant-list">
          {draft.map((grant, index) => (
            <article className="role-policy-grant" key={`${grant.actionCode}-${grant.scopeCode}-${index}`}>
              <label><span>Action</span>
                <select value={grant.actionCode} disabled={!canWrite}
                  onChange={(event) => updateGrant(index, { actionCode: event.target.value })}>
                  {catalog.actions.map((action) => (
                    <option value={action.actionCode} key={action.actionCode}>
                      {action.actionCode}{action.isNonBypassable ? ' · non-bypassable' : ''}
                    </option>
                  ))}
                </select>
              </label>
              <label><span>Scope</span>
                <select value={grant.scopeCode} disabled={!canWrite}
                  onChange={(event) => updateGrant(index, { scopeCode: event.target.value })}>
                  {catalog.scopes.map((scope) => (
                    <option value={scope.scopeCode} key={scope.scopeCode}>{scope.scopeCode}</option>
                  ))}
                </select>
              </label>
              <label><span>Effect</span>
                <select value={grant.effect} disabled={!canWrite}
                  onChange={(event) => updateGrant(index, { effect: event.target.value })}>
                  {(catalog.effects || ['GRANT', 'DENY']).map((effect) => (
                    <option value={effect} key={effect}>{effect}</option>
                  ))}
                </select>
              </label>
              <label className="role-policy-conditions"><span>Optional conditions (JSON)</span>
                <textarea value={grant.conditionsText} disabled={!canWrite}
                  onChange={(event) => updateGrant(index, { conditionsText: event.target.value })} />
              </label>
              <div className="role-policy-flags">
                {[
                  ['delegatedAuthority', 'Delegated authority'],
                  ['reasonRequired', 'Reason required'],
                  ['auditRequired', 'Audit required'],
                  ['isActive', 'Active']
                ].map(([field, label]) => (
                  <label key={field}>
                    <input type="checkbox" checked={Boolean(grant[field])} disabled={!canWrite}
                      onChange={(event) => updateGrant(index, { [field]: event.target.checked })} />
                    <span>{label}</span>
                  </label>
                ))}
              </div>
              <button type="button" className="danger" disabled={!canWrite}
                onClick={() => setDraft((current) => current.filter((_, rowIndex) => rowIndex !== index))}>
                Remove action
              </button>
            </article>
          ))}
          {!draft.length ? (
            <p className="role-policy-empty">No scoped rows. Existing authorization remains in effect for this role/module pair.</p>
          ) : null}
        </div>

        <label className="role-policy-notes"><span>Change notes</span>
          <textarea value={notes} disabled={!canWrite} onChange={(event) => setNotes(event.target.value)}
            placeholder="Explain scope, exception, delegation, and safety reasoning." />
        </label>
      </section>

      <section className="role-policy-publish">
        <div>
          <p className="eyebrow">4–6. Review, validate, and publish</p>
          <h2>Versioned policy change</h2>
          <p>The published version is immutable. Changes are cloned, validated, audited, and published as a new version.</p>
        </div>
        <label><span>Required reason</span>
          <textarea value={reason} disabled={!canWrite} onChange={(event) => setReason(event.target.value)} />
        </label>
        <div className="role-policy-publish-actions">
          <button type="button" disabled={!canWrite || state.busy || !pending} onClick={validateDraft}>Validate</button>
          <button type="button" className="primary"
            disabled={!canWrite || state.busy || !pending || !validation?.valid} onClick={publish}>
            Publish new policy version
          </button>
          <button type="button" disabled={!canWrite || state.busy || !pending}
            onClick={() => { setDraft(baseline.map((grant) => ({ ...grant }))); setValidation(null); }}>
            Discard
          </button>
        </div>
        {validation ? (
          <div className={validation.valid ? 'role-policy-validation valid' : 'role-policy-validation invalid'}>
            <strong>{validation.valid ? 'Validation passed' : 'Validation blocked'}</strong>
            {(validation.errors || []).map((item) => <span key={item}>{item}</span>)}
            {(validation.warnings || []).map((item) => <span key={item}>Warning: {item}</span>)}
          </div>
        ) : null}
      </section>

      <section className="role-policy-history">
        <header><div><p className="eyebrow">7–8. Audit and controlled restoration</p><h2>Policy versions</h2></div></header>
        <div>
          {versions.map((version) => (
            <article key={version.policyVersionId}>
              <div>
                <strong>Version {version.versionNumber} · {version.policyStatus}</strong>
                <span>{version.policyName}</span>
                <small>{version.grantCount} grants · {version.auditEventCount} audit event(s)</small>
                <small>Published by {version.publishedBy} · {formatDate(version.publishedAt)}</small>
              </div>
              <button type="button" disabled={!canWrite || state.busy || version.policyStatus === 'PUBLISHED'}
                onClick={() => restore(version)}>Restore as new version</button>
            </article>
          ))}
        </div>
      </section>

      <details className="role-policy-legacy-validation">
        <summary>Legacy role and route validation retained</summary>
        <p>Existing roles, permissions, assignments, and route checks remain authoritative for Not Set decisions.</p>
        <dl>
          <div><dt>Legacy roles</dt><dd>{legacy?.summary?.roleCount ?? legacy?.roles?.length ?? 'Preserved'}</dd></div>
          <div><dt>Not Set</dt><dd>Preserve existing authorization</dd></div>
          <div><dt>Safety gates</dt><dd>Secrets, deployment, database, security, and audit remain separate</dd></div>
        </dl>
      </details>

      {state.error ? <p className="role-policy-error">{state.error}</p> : null}
      {state.message ? <p className="role-policy-message">{state.message}</p> : null}
    </section>
  );
}
