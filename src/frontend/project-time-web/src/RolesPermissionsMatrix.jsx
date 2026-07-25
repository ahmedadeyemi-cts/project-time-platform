import { useEffect, useMemo, useState } from 'react';
import './roles-permissions-matrix.css';
import './role-permission-matrix-v2.css';
import { PERMISSION_LEVELS, ROLE_REFERENCE, api, downloadCsv, formatDate, inferLevel, inferScope, levelClass } from './role-permission-matrix-model.js';

export default function RolesPermissionsMatrix() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: '' });
  const [tab, setTab] = useState('matrix');
  const [moduleCode, setModuleCode] = useState('all');
  const [roleCode, setRoleCode] = useState('all');
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState(null);
  const [message, setMessage] = useState('');

  async function loadMatrix() {
    setPayload((current) => ({ ...current, loading: true, error: '' }));
    try {
      const data = await api('/api/role-policy/matrix');
      setPayload({ loading: false, data, error: '' });
    } catch (error) {
      setPayload({ loading: false, data: null, error: error.message || 'Unable to load the effective permission matrix.' });
    }
  }

  useEffect(() => { void loadMatrix(); }, []);

  const roles = payload.data?.roles || [];
  const modules = payload.data?.modules || [];
  const grants = useMemo(() => [...(payload.data?.grants || []), ...(payload.data?.legacyFallback || [])], [payload.data]);

  const grouped = useMemo(() => {
    const map = new Map();
    grants.forEach((grant) => {
      const key = `${grant.moduleCode}|${grant.roleCode}`;
      const list = map.get(key) || [];
      list.push(grant);
      map.set(key, list);
    });
    return map;
  }, [grants]);

  const visibleRoles = useMemo(() => roleCode === 'all' ? roles : roles.filter((role) => role.roleCode === roleCode), [roleCode, roles]);
  const visibleModules = useMemo(() => {
    const term = search.trim().toLowerCase();
    return modules.filter((module) => {
      if (moduleCode !== 'all' && module.moduleCode !== moduleCode) return false;
      if (!term) return true;
      return [module.moduleCode, module.moduleName, module.routeScope, module.permissionNotes].join(' ').toLowerCase().includes(term);
    });
  }, [moduleCode, modules, search]);

  const totals = useMemo(() => {
    const counts = Object.fromEntries(PERMISSION_LEVELS.map((level) => [level.code, 0]));
    modules.forEach((module) => roles.forEach((role) => {
      counts[inferLevel(grouped.get(`${module.moduleCode}|${role.roleCode}`) || [], role.roleCode)] += 1;
    }));
    return counts;
  }, [grouped, modules, roles]);

  function exportCsv() {
    const header = ['Module', 'Module Name', 'Route / Scope', ...roles.map((role) => role.roleName), 'Permission Notes'];
    const rows = modules.map((module) => [
      module.moduleCode,
      module.moduleName,
      module.routeScope,
      ...roles.map((role) => {
        const pair = grouped.get(`${module.moduleCode}|${role.roleCode}`) || [];
        return `${inferLevel(pair, role.roleCode)} (${inferScope(pair, role.roleCode)})`;
      }),
      module.permissionNotes || ''
    ]);
    downloadCsv('projectpulse-module-role-permissions-matrix.csv', [header, ...rows]);
    setMessage('The visual permission matrix was exported as CSV.');
  }

  if (payload.loading) return <section className="roles-permissions-matrix">Loading module permissions…</section>;
  if (payload.error || !payload.data) {
    return <section className="roles-permissions-matrix"><p className="eyebrow">Module 037</p><h2>Roles and Permissions Matrix</h2><p className="roles-matrix-error">{payload.error || 'The permission matrix is unavailable.'}</p><button type="button" onClick={loadMatrix}>Retry</button></section>;
  }

  return (
    <section className="role-permission-matrix-v2" data-projectpulse-module="037" data-read-only="true">
      <header className="rpm-hero">
        <div>
          <p className="eyebrow">Module 037</p>
          <h1>Roles and Permissions Matrix</h1>
          <p>Read-only confirmation of the permissions published in Module 012. Modules and roles are loaded from the database.</p>
        </div>
        <div className="rpm-actions"><button type="button" onClick={loadMatrix}>Refresh</button><button type="button" onClick={exportCsv}>Export matrix</button></div>
      </header>

      <div className="rpm-readonly-banner"><strong>Confirmation view only</strong><span>Change permissions in Module 012. Refresh this page to confirm the newly published value.</span></div>

      <nav className="rpm-tabs" aria-label="Module 037 views">
        <button type="button" className={tab === 'matrix' ? 'active' : ''} onClick={() => setTab('matrix')}>Permission Matrix</button>
        <button type="button" className={tab === 'roles' ? 'active' : ''} onClick={() => setTab('roles')}>Role Reference</button>
        <button type="button" className={tab === 'levels' ? 'active' : ''} onClick={() => setTab('levels')}>Permission Levels</button>
      </nav>

      {tab === 'matrix' ? (
        <>
          <section className="rpm-summary-grid">
            <article><span>Policy version</span><strong>v{payload.data.policyVersion?.versionNumber || '—'}</strong><small>{payload.data.policyVersion?.policyStatus || 'Unknown'}</small></article>
            <article><span>Database modules</span><strong>{modules.length}</strong><small>Rows populate from the module table</small></article>
            <article><span>No Access</span><strong>{totals['No Access']}</strong><small>Hidden from those roles</small></article>
            <article><span>Full Control</span><strong>{totals['Full Control']}</strong><small>Includes the Super Administrator invariant</small></article>
          </section>

          <section className="rpm-toolbar">
            <label><span>Module</span><select value={moduleCode} onChange={(event) => setModuleCode(event.target.value)}><option value="all">All modules</option>{modules.map((module) => <option value={module.moduleCode} key={module.moduleCode}>Module {module.moduleCode} · {module.moduleName}</option>)}</select></label>
            <label><span>Role</span><select value={roleCode} onChange={(event) => setRoleCode(event.target.value)}><option value="all">All canonical roles</option>{roles.map((role) => <option value={role.roleCode} key={role.roleCode}>{role.roleName}</option>)}</select></label>
            <label><span>Search modules</span><input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Number, name, route, or note" /></label>
          </section>

          {message ? <p className="roles-matrix-alert">{message}</p> : null}
          <p className="rpm-count">{visibleModules.length} module row(s) · {visibleRoles.length} role column(s)</p>

          <div className="rpm-table-wrap">
            <table className="rpm-table">
              <thead><tr><th>Module</th>{visibleRoles.map((role) => <th key={role.roleCode}>{role.roleName}</th>)}</tr></thead>
              <tbody>
                {visibleModules.map((module) => (
                  <tr key={module.moduleCode}>
                    <td><strong>Module {module.moduleCode}</strong><span>{module.moduleName}</span><small>{module.routeScope} · {module.currentState}</small></td>
                    {visibleRoles.map((role) => {
                      const pair = grouped.get(`${module.moduleCode}|${role.roleCode}`) || [];
                      const level = inferLevel(pair, role.roleCode);
                      const scope = inferScope(pair, role.roleCode);
                      return (
                        <td key={role.roleCode}>
                          <button type="button" className={levelClass(level)} onClick={() => setSelected({ module, role, level, scope, grants: pair })} title="View permission details">
                            <strong>{level}</strong><small>{scope}</small>
                          </button>
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {!visibleModules.length ? <p className="roles-matrix-empty">No modules match the current filters.</p> : null}

          {selected ? (
            <section className="rpm-detail-panel">
              <header><div><p className="eyebrow">Permission details</p><h2>{selected.role.roleName} · Module {selected.module.moduleCode}</h2></div><button type="button" onClick={() => setSelected(null)}>Close</button></header>
              <div className="rpm-detail-grid">
                <article><span>Permission</span><strong>{selected.level}</strong><p>{PERMISSION_LEVELS.find((item) => item.code === selected.level)?.meaning}</p></article>
                <article><span>Scope</span><strong>{selected.scope}</strong><p>{ROLE_REFERENCE[selected.role.roleCode]?.visibility || 'The published action scopes determine visibility.'}</p></article>
                <article><span>Module note</span><strong>{selected.module.moduleName}</strong><p>{selected.module.permissionNotes || 'No exception note is stored.'}</p></article>
                <article><span>Policy evidence</span><strong>v{payload.data.policyVersion?.versionNumber || '—'}</strong><p>{selected.grants[0]?.lastModifiedBy ? `Published by ${selected.grants[0].lastModifiedBy} · ${formatDate(selected.grants[0].lastModifiedAt)}` : 'Super Administrator invariant or legacy fallback.'}</p></article>
              </div>
              <div className="rpm-action-list">
                {selected.grants.map((grant, index) => <span key={`${grant.actionCode}-${grant.scopeCode}-${index}`}>{grant.grantEffect === 'DENY' || grant.explicitDeny ? 'Deny' : grant.inherited ? 'Legacy' : 'Allow'} {grant.actionCode} · {grant.scopeCode}</span>)}
                {!selected.grants.length && selected.role.roleCode === 'SUPER_ADMINISTRATOR' ? <span>Permanent Full Control · ORGANIZATION</span> : null}
              </div>
            </section>
          ) : null}
        </>
      ) : null}

      {tab === 'roles' ? (
        <section className="rpm-reference-grid">
          {roles.map((role) => {
            const reference = ROLE_REFERENCE[role.roleCode] || { purpose: role.description, visibility: 'Defined by published module permissions.', defaultScope: 'CUSTOM_RULE' };
            return <article key={role.roleCode}><header><div><p className="eyebrow">{role.roleCode}</p><h2>{role.roleName}</h2></div><span>{role.activeUserCount || 0} user(s)</span></header><p>{reference.purpose || role.description}</p><dl><div><dt>Default visibility</dt><dd>{reference.visibility}</dd></div><div><dt>Recommended scope</dt><dd>{reference.defaultScope}</dd></div></dl>{role.roleCode === 'SUPER_ADMINISTRATOR' ? <strong className="rpm-invariant">Permanent Full Control</strong> : null}</article>;
          })}
        </section>
      ) : null}

      {tab === 'levels' ? (
        <section className="rpm-level-reference">
          {PERMISSION_LEVELS.map((level) => <article key={level.code}><span className={levelClass(level.code)}><strong>{level.code}</strong></span><div><h2>{level.code}</h2><p>{level.meaning}</p>{level.code === 'No Access' ? <small>The role should not see the module in navigation, dashboards, search, or direct route access.</small> : null}{level.code === 'View' ? <small>“View” is always limited by the role’s configured scope, such as SELF, MANAGED_TEAM, or ASSIGNED_PROJECTS.</small> : null}</div></article>)}
        </section>
      ) : null}
    </section>
  );
}
