import { useEffect, useMemo, useState } from 'react';
import './roles-permissions-matrix.css';
import './role-permission-matrix-v2.css';
import { actionDescription, actionLabel } from './role-permission-model.js';
import { PERMISSION_LEVELS, ROLE_REFERENCE, api, downloadCsv, formatDate, inferLevel, inferScope, levelClass } from './role-permission-matrix-model.js';

function arrayOf(payload, camel, pascal) {
  const value = payload?.[camel] ?? payload?.[pascal];
  return Array.isArray(value) ? value : [];
}

function valueOf(payload, camel, pascal, fallback = '') {
  return Object.prototype.hasOwnProperty.call(payload || {}, camel)
    ? payload[camel]
    : Object.prototype.hasOwnProperty.call(payload || {}, pascal)
      ? payload[pascal]
      : fallback;
}

function uniqueBy(items, key) {
  const seen = new Set();
  return items.filter((item) => {
    const value = String(item?.[key] || '').trim().toUpperCase();
    if (!value || seen.has(value)) return false;
    seen.add(value);
    return true;
  });
}

function normalizeRole(role) {
  return {
    roleCode: String(valueOf(role, 'roleCode', 'RoleCode')).toUpperCase(),
    roleName: valueOf(role, 'roleName', 'RoleName'),
    description: valueOf(role, 'description', 'Description'),
    activeUserCount: Number(valueOf(role, 'activeUserCount', 'ActiveUserCount', 0) || 0)
  };
}

function normalizeModule(module) {
  return {
    moduleCode: String(valueOf(module, 'moduleCode', 'ModuleCode')).toUpperCase(),
    moduleName: valueOf(module, 'moduleName', 'ModuleName'),
    routeScope: valueOf(module, 'routeScope', 'RouteScope'),
    currentState: valueOf(module, 'currentState', 'CurrentState'),
    permissionNotes: valueOf(module, 'permissionNotes', 'PermissionNotes')
  };
}

function normalizeGrant(grant) {
  return {
    roleCode: String(valueOf(grant, 'roleCode', 'RoleCode')).toUpperCase(),
    moduleCode: String(valueOf(grant, 'moduleCode', 'ModuleCode')).toUpperCase(),
    moduleName: valueOf(grant, 'moduleName', 'ModuleName'),
    actionCode: valueOf(grant, 'actionCode', 'ActionCode'),
    scopeCode: valueOf(grant, 'scopeCode', 'ScopeCode'),
    grantEffect: valueOf(grant, 'grantEffect', 'GrantEffect', valueOf(grant, 'effect', 'Effect')),
    conditions: valueOf(grant, 'conditions', 'Conditions', {}) || {},
    delegatedAuthority: Boolean(valueOf(grant, 'delegatedAuthority', 'DelegatedAuthority', false)),
    reasonRequired: Boolean(valueOf(grant, 'reasonRequired', 'ReasonRequired', false)),
    auditRequired: valueOf(grant, 'auditRequired', 'AuditRequired', true) !== false,
    sourceDesignation: valueOf(grant, 'sourceDesignation', 'SourceDesignation'),
    sourceNotes: valueOf(grant, 'sourceNotes', 'SourceNotes'),
    versionNumber: valueOf(grant, 'versionNumber', 'VersionNumber'),
    lastModifiedBy: valueOf(grant, 'lastModifiedBy', 'LastModifiedBy'),
    lastModifiedAt: valueOf(grant, 'lastModifiedAt', 'LastModifiedAt'),
    inherited: Boolean(valueOf(grant, 'inherited', 'Inherited', false)),
    explicitDeny: Boolean(valueOf(grant, 'explicitDeny', 'ExplicitDeny', false)),
    granted: Boolean(valueOf(grant, 'granted', 'Granted', false))
  };
}

function normalizeAction(action) {
  return {
    actionCode: valueOf(action, 'actionCode', 'ActionCode'),
    actionDescription: valueOf(action, 'actionDescription', 'ActionDescription'),
    isNonBypassable: Boolean(valueOf(action, 'isNonBypassable', 'IsNonBypassable', false))
  };
}

function decisionFor(grants, roleCode, actionCode) {
  if (roleCode === 'SUPER_ADMINISTRATOR') return { state: 'ALLOW', scope: 'ORGANIZATION', grants };
  const matches = grants.filter((grant) => grant.roleCode === roleCode && grant.actionCode === actionCode);
  if (matches.some((grant) => grant.grantEffect === 'DENY' || grant.explicitDeny)) {
    return { state: 'DENY', scope: matches.find((grant) => grant.grantEffect === 'DENY')?.scopeCode || 'ORGANIZATION', grants: matches };
  }
  if (matches.some((grant) => grant.grantEffect === 'GRANT' || grant.granted)) {
    return { state: 'ALLOW', scope: inferScope(matches, roleCode), grants: matches };
  }
  if (matches.some((grant) => grant.inherited || grant.actionCode === 'LEGACY_FALLBACK')) {
    return { state: 'NOT_SET', scope: 'LEGACY', grants: matches };
  }
  return { state: 'NOT_SET', scope: '—', grants: [] };
}

export default function RolesPermissionsMatrix() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: '' });
  const [tab, setTab] = useState('matrix');
  const [moduleCode, setModuleCode] = useState('001');
  const [roleCode, setRoleCode] = useState('all');
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState(null);
  const [message, setMessage] = useState('');

  async function loadMatrix() {
    setPayload((current) => ({ ...current, loading: true, error: '' }));
    try {
      const matrixPayload = await api('/api/rbac/v1/matrix');
      const roles = uniqueBy(arrayOf(matrixPayload, 'roles', 'Roles').map(normalizeRole), 'roleCode');
      const modules = uniqueBy(arrayOf(matrixPayload, 'modules', 'Modules').map(normalizeModule), 'moduleCode');
      const actions = uniqueBy(arrayOf(matrixPayload, 'actions', 'Actions').map(normalizeAction).filter((action) => action.actionCode), 'actionCode');
      if (!roles.length || !modules.length || !actions.length) {
        throw new Error('The RBAC matrix service did not return active roles, active modules, and permission actions.');
      }
      if (!roles.some((role) => role.roleCode === 'SUPER_ADMINISTRATOR')) {
        throw new Error('The Super Administrator role is missing from the permission matrix.');
      }
      const data = {
        contractVersion: valueOf(matrixPayload, 'contractVersion', 'ContractVersion', ''),
        fixedModuleCountRequired: Boolean(valueOf(matrixPayload, 'fixedModuleCountRequired', 'FixedModuleCountRequired', false)),
        roles,
        modules,
        grants: arrayOf(matrixPayload, 'grants', 'Grants').map(normalizeGrant),
        legacyFallback: arrayOf(matrixPayload, 'legacyFallback', 'LegacyFallback').map(normalizeGrant),
        policyVersion: valueOf(matrixPayload, 'policyVersion', 'PolicyVersion', null),
        summary: valueOf(matrixPayload, 'summary', 'Summary', {}) || {},
        actions
      };
      setPayload({ loading: false, data, error: '' });
      if (moduleCode !== 'all' && !modules.some((module) => module.moduleCode === moduleCode)) {
        setModuleCode(modules[0].moduleCode);
      }
    } catch (error) {
      setPayload({ loading: false, data: null, error: error.message || 'Unable to load the role and permission matrix.' });
    }
  }

  useEffect(() => { void loadMatrix(); }, []);

  const roles = payload.data?.roles || [];
  const modules = payload.data?.modules || [];
  const grants = useMemo(() => [...(payload.data?.grants || []), ...(payload.data?.legacyFallback || [])], [payload.data]);
  const actionCatalog = useMemo(() => new Map((payload.data?.actions || []).map((action) => [action.actionCode, action])), [payload.data]);
  const visibleRoles = useMemo(() => roleCode === 'all' ? roles : roles.filter((role) => role.roleCode === roleCode), [roleCode, roles]);
  const visibleModules = useMemo(() => moduleCode === 'all' ? modules : modules.filter((module) => module.moduleCode === moduleCode), [moduleCode, modules]);

  const rows = useMemo(() => {
    const result = [];
    const term = search.trim().toLowerCase();
    for (const module of visibleModules) {
      const codes = new Set(grants.filter((grant) => grant.moduleCode === module.moduleCode).map((grant) => grant.actionCode));
      if (moduleCode !== 'all') {
        for (const action of payload.data?.actions || []) {
          const code = action.actionCode;
          if (['MODULE_ACCESS', 'MODULE_VIEW', 'AUDIT_VIEW', 'AUDIT_RECORD'].includes(code)) codes.add(code);
          if (module.moduleCode === '001' && code.startsWith('TIME_')) codes.add(code);
          if (module.moduleCode === '002' && code.startsWith('APPROVAL_')) codes.add(code);
          if (module.moduleCode === '003' && code.startsWith('UTILIZATION_')) codes.add(code);
          if (module.moduleCode === '012' && (code.startsWith('POLICY_') || code === 'ROLE_ASSIGN')) codes.add(code);
          if (module.moduleCode === '037' && (code.startsWith('MATRIX_') || code === 'ACCESS_EXPLAIN')) codes.add(code);
        }
      }
      for (const actionCode of [...codes].filter(Boolean).sort()) {
        const action = actionCatalog.get(actionCode) || { actionCode, actionDescription: '' };
        const haystack = `${module.moduleName} ${module.routeScope} ${actionCode} ${actionLabel(actionCode)} ${actionDescription(actionCode, action.actionDescription)}`.toLowerCase();
        if (term && !haystack.includes(term)) continue;
        result.push({ module, action });
      }
    }
    return result;
  }, [actionCatalog, grants, moduleCode, payload.data?.actions, search, visibleModules]);

  const groupedByPair = useMemo(() => {
    const map = new Map();
    for (const grant of grants) {
      const key = `${grant.moduleCode}|${grant.roleCode}`;
      const list = map.get(key) || [];
      list.push(grant);
      map.set(key, list);
    }
    return map;
  }, [grants]);

  const totals = useMemo(() => {
    const values = Object.fromEntries(PERMISSION_LEVELS.map((level) => [level.code, 0]));
    for (const module of modules) {
      for (const role of roles) {
        const level = inferLevel(groupedByPair.get(`${module.moduleCode}|${role.roleCode}`) || [], role.roleCode);
        values[level] = (values[level] || 0) + 1;
      }
    }
    return values;
  }, [groupedByPair, modules, roles]);

  function exportCsv() {
    const header = ['Module', 'Page Name', 'Permission', 'Permission Code', 'Description', ...visibleRoles.map((role) => `${role.roleName} (${role.roleCode})`)];
    const body = rows.map(({ module, action }) => {
      const moduleGrants = grants.filter((grant) => grant.moduleCode === module.moduleCode);
      return [module.moduleCode, module.moduleName, actionLabel(action.actionCode), action.actionCode, actionDescription(action.actionCode, action.actionDescription), ...visibleRoles.map((role) => {
        const decision = decisionFor(moduleGrants, role.roleCode, action.actionCode);
        return `${decision.state} · ${decision.scope}`;
      })];
    });
    downloadCsv('projectpulse-dynamic-rbac-matrix.csv', [header, ...body]);
    setMessage('The current filtered RBAC matrix was exported.');
  }

  if (payload.loading) return <section className="role-permission-matrix-v2"><div className="rpm-loading">Loading the dynamic role and permission matrix…</div></section>;
  if (payload.error || !payload.data) return <section className="role-permission-matrix-v2"><div className="rpm-foundation-error"><p className="eyebrow">Module 037</p><h2>Permission matrix did not load</h2><p>{payload.error}</p><button type="button" onClick={loadMatrix}>Try again</button></div></section>;

  const version = payload.data.policyVersion || {};
  const summary = payload.data.summary || {};

  return <section className="role-permission-matrix-v2" data-projectpulse-module="037" data-rbac-contract="projectpulse-rbac-v1" data-read-only="true">
    <header className="rpm-hero"><div><p className="eyebrow">Module 037</p><h1>Roles and Permissions Matrix</h1><p>Read-only confirmation of the permissions published in Module 012. Roles and modules are loaded dynamically from the active RBAC catalog; no fixed module count is required.</p></div><div className="rpm-actions"><button type="button" onClick={loadMatrix}>Refresh</button><button type="button" onClick={exportCsv}>Export matrix</button></div></header>
    <div className="rpm-readonly-banner"><strong>Confirmation view only</strong><span>Change permissions, role memberships, and module lifecycle in Module 012. Super Administrator is always Full Control across every active module.</span></div>
    <nav className="rpm-tabs" aria-label="Module 037 views"><button type="button" className={tab === 'matrix' ? 'active' : ''} onClick={() => setTab('matrix')}>Permission Matrix</button><button type="button" className={tab === 'roles' ? 'active' : ''} onClick={() => setTab('roles')}>Role Reference</button><button type="button" className={tab === 'levels' ? 'active' : ''} onClick={() => setTab('levels')}>Permission Levels</button></nav>

    {tab === 'matrix' ? <>
      <section className="rpm-summary-grid"><article><span>Policy version</span><strong>v{valueOf(version, 'versionNumber', 'VersionNumber', '—')}</strong><small>{valueOf(version, 'policyStatus', 'PolicyStatus', 'Unknown')}</small></article><article><span>Active modules</span><strong>{modules.length}</strong><small>Dynamic database catalog</small></article><article><span>Active roles</span><strong>{roles.length}</strong><small>Role columns from the role directory</small></article><article><span>No Access</span><strong>{totals['No Access'] || 0}</strong><small>Module hidden for those role/module pairs</small></article></section>
      <section className="rpm-summary-grid"><article><span>Configured pairs</span><strong>{Number(valueOf(summary, 'configuredPairCount', 'ConfiguredPairCount', 0) || 0)}</strong><small>Explicit published decisions</small></article><article><span>Unconfigured pairs</span><strong>{Number(valueOf(summary, 'unconfiguredPairCount', 'UnconfiguredPairCount', 0) || 0)}</strong><small>Existing endpoint authorization remains until configured</small></article><article><span>Catalog mode</span><strong>Dynamic</strong><small>No 70-module requirement</small></article><article><span>Super Admin</span><strong>Full Control</strong><small>Permanent organization-wide invariant</small></article></section>
      <section className="rpm-toolbar"><label><span>Module</span><select value={moduleCode} onChange={(event) => setModuleCode(event.target.value)}><option value="all">All modules</option>{modules.map((module) => <option value={module.moduleCode} key={module.moduleCode}>{module.moduleName}</option>)}</select></label><label><span>Role</span><select value={roleCode} onChange={(event) => setRoleCode(event.target.value)}><option value="all">All roles</option>{roles.map((role) => <option value={role.roleCode} key={role.roleCode}>{role.roleName}</option>)}</select></label><label><span>Search</span><input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Permission, role, page, TIME_REASSIGN…" /></label></section>
      <div className="rpm-scroll-note"><strong>Matrix viewing tip</strong><span>The Page, Permission, and Description columns stay pinned. Scroll horizontally inside the table to compare roles without losing context.</span></div>
      {message ? <p className="roles-matrix-alert">{message}</p> : null}
      <p className="rpm-count">{rows.length} permission row(s) · {visibleRoles.length} role column(s)</p>
      <div className="rpm-permission-table-wrap"><table className="rpm-permission-table"><thead><tr><th>Page</th><th>Permission</th><th>Description</th>{visibleRoles.map((role) => <th key={role.roleCode} title={`${role.roleName} · ${role.roleCode}`}><span className="rpm-role-heading"><strong>{role.roleName}</strong><small>{role.roleCode}</small></span></th>)}</tr></thead><tbody>{rows.map(({ module, action }) => {
        const moduleGrants = grants.filter((grant) => grant.moduleCode === module.moduleCode);
        return <tr key={`${module.moduleCode}-${action.actionCode}`}><td><strong>{module.moduleName}</strong><small>{module.routeScope}</small></td><td><strong>{actionLabel(action.actionCode)}</strong><code>{action.actionCode}</code></td><td>{actionDescription(action.actionCode, action.actionDescription)}</td>{visibleRoles.map((role) => {
          const decision = decisionFor(moduleGrants, role.roleCode, action.actionCode);
          return <td key={role.roleCode}><button type="button" className={decision.state === 'ALLOW' ? 'rpm-decision allow' : decision.state === 'DENY' ? 'rpm-decision deny' : 'rpm-decision not-set'} onClick={() => setSelected({ module, action, role, decision })}><strong>{decision.state === 'ALLOW' ? 'Allow' : decision.state === 'DENY' ? 'No Access' : 'Not Set'}</strong><small>{decision.scope}</small></button></td>;
        })}</tr>;
      })}</tbody></table></div>
    </> : null}

    {tab === 'roles' ? <section className="rpm-reference-grid">{roles.map((role) => {
      const reference = ROLE_REFERENCE[role.roleCode] || { purpose: role.description || 'Uses the permissions assigned in Module 012.', visibility: 'Controlled by the published data scope.', defaultScope: 'CONFIGURED' };
      return <article key={role.roleCode}><p className="eyebrow">{role.roleCode}</p><h2>{role.roleName}</h2><p>{reference.purpose}</p><strong>Visibility</strong><span>{reference.visibility}</span><strong>Default scope</strong><span>{reference.defaultScope}</span><strong>Assigned users</strong><span>{role.activeUserCount}</span></article>;
    })}</section> : null}

    {tab === 'levels' ? <section className="rpm-level-reference">{PERMISSION_LEVELS.map((level) => <article key={level.code}><strong className={levelClass(level.code)}>{level.code}</strong><p>{level.meaning}</p></article>)}</section> : null}

    {selected ? <div className="rpm-drawer-backdrop" role="presentation" onClick={() => setSelected(null)}><aside className="rpm-drawer" role="dialog" aria-modal="true" aria-label="Permission explanation" onClick={(event) => event.stopPropagation()}><header><div><p className="eyebrow">Permission explanation</p><h2>{selected.role.roleName}</h2><p>{selected.module.moduleName} · {actionLabel(selected.action.actionCode)}</p></div><button type="button" onClick={() => setSelected(null)}>Close</button></header><dl><div><dt>Decision</dt><dd>{selected.decision.state}</dd></div><div><dt>Scope</dt><dd>{selected.decision.scope}</dd></div><div><dt>Permission code</dt><dd>{selected.action.actionCode}</dd></div><div><dt>Last modified</dt><dd>{formatDate(selected.decision.grants?.[0]?.lastModifiedAt)}</dd></div></dl><p>{selected.decision.grants?.[0]?.sourceNotes || selected.decision.grants?.[0]?.explanation || 'No additional source note was stored.'}</p></aside></div> : null}
  </section>;
}
