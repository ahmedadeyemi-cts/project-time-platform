import { useEffect, useMemo, useState } from 'react';
import './roles-permissions-matrix.css';
import './role-permission-matrix-v2.css';
import { actionDescription, actionLabel } from './role-permission-model.js';
import { PERMISSION_LEVELS, ROLE_REFERENCE, api, downloadCsv, formatDate, inferLevel, inferScope, levelClass } from './role-permission-matrix-model.js';

const REQUIRED_ROLE_COUNT = 12;
const REQUIRED_MODULE_COUNT = 70;

function arrayOf(payload, camel, pascal) {
  const value = payload?.[camel] ?? payload?.[pascal];
  return Array.isArray(value) ? value : [];
}

function valueOf(payload, camel, pascal, fallback = '') {
  return Object.prototype.hasOwnProperty.call(payload || {}, camel) ? payload[camel] : Object.prototype.hasOwnProperty.call(payload || {}, pascal) ? payload[pascal] : fallback;
}

function normalizeRole(role) {
  return {
    roleCode: valueOf(role, 'roleCode', 'RoleCode'),
    roleName: valueOf(role, 'roleName', 'RoleName'),
    description: valueOf(role, 'description', 'Description'),
    activeUserCount: Number(valueOf(role, 'activeUserCount', 'ActiveUserCount', 0) || 0)
  };
}

function normalizeModule(module) {
  return {
    moduleCode: String(valueOf(module, 'moduleCode', 'ModuleCode')),
    moduleName: valueOf(module, 'moduleName', 'ModuleName'),
    routeScope: valueOf(module, 'routeScope', 'RouteScope'),
    currentState: valueOf(module, 'currentState', 'CurrentState'),
    permissionNotes: valueOf(module, 'permissionNotes', 'PermissionNotes')
  };
}

function normalizeGrant(grant) {
  return {
    roleCode: valueOf(grant, 'roleCode', 'RoleCode'),
    moduleCode: String(valueOf(grant, 'moduleCode', 'ModuleCode')),
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
  if (matches.some((grant) => grant.grantEffect === 'DENY' || grant.explicitDeny)) return { state: 'DENY', scope: matches.find((grant) => grant.grantEffect === 'DENY')?.scopeCode || 'ORGANIZATION', grants: matches };
  if (matches.some((grant) => grant.grantEffect === 'GRANT' || grant.granted)) return { state: 'ALLOW', scope: inferScope(matches, roleCode), grants: matches };
  if (matches.some((grant) => grant.inherited || grant.actionCode === 'LEGACY_FALLBACK')) return { state: 'NOT_SET', scope: 'LEGACY', grants: matches };
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
      const [matrixPayload, catalogPayload] = await Promise.all([
        api('/api/role-policy/matrix'),
        api('/api/role-policy/catalog')
      ]);
      const roles = arrayOf(matrixPayload, 'roles', 'Roles').map(normalizeRole);
      const modules = arrayOf(matrixPayload, 'modules', 'Modules').map(normalizeModule);
      if (roles.length < REQUIRED_ROLE_COUNT || modules.length < REQUIRED_MODULE_COUNT) {
        throw new Error(`The published permission matrix is incomplete. Expected ${REQUIRED_ROLE_COUNT} roles and ${REQUIRED_MODULE_COUNT} modules, but received ${roles.length} roles and ${modules.length} modules.`);
      }
      const data = {
        roles,
        modules,
        grants: arrayOf(matrixPayload, 'grants', 'Grants').map(normalizeGrant),
        legacyFallback: arrayOf(matrixPayload, 'legacyFallback', 'LegacyFallback').map(normalizeGrant),
        policyVersion: valueOf(matrixPayload, 'policyVersion', 'PolicyVersion', null),
        actions: arrayOf(catalogPayload, 'actions', 'Actions').map(normalizeAction).filter((action) => action.actionCode)
      };
      setPayload({ loading: false, data, error: '' });
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
          if (module.moduleCode === '012' && code.startsWith('POLICY_')) codes.add(code);
          if (module.moduleCode === '037' && (code.startsWith('MATRIX_') || code === 'ACCESS_EXPLAIN')) codes.add(code);
        }
      }
      for (const actionCode of [...codes].filter(Boolean).sort()) {
        const action = actionCatalog.get(actionCode) || { actionCode, actionDescription: '' };
        const haystack = `${module.moduleCode} ${module.moduleName} ${actionCode} ${actionLabel(actionCode)} ${actionDescription(actionCode, action.actionDescription)}`.toLowerCase();
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
    for (const module of modules) for (const role of roles) values[inferLevel(groupedByPair.get(`${module.moduleCode}|${role.roleCode}`) || [], role.roleCode)] += 1;
    return values;
  }, [groupedByPair, modules, roles]);

  function exportCsv() {
    const header = ['Module', 'Module Name', 'Permission', 'Permission Code', 'Description', ...visibleRoles.map((role) => `${role.roleName} (${role.roleCode})`)];
    const body = rows.map(({ module, action }) => {
      const moduleGrants = grants.filter((grant) => grant.moduleCode === module.moduleCode);
      return [module.moduleCode, module.moduleName, actionLabel(action.actionCode), action.actionCode, actionDescription(action.actionCode, action.actionDescription), ...visibleRoles.map((role) => {
        const decision = decisionFor(moduleGrants, role.roleCode, action.actionCode);
        return `${decision.state} · ${decision.scope}`;
      })];
    });
    downloadCsv('projectpulse-role-permission-matrix.csv', [header, ...body]);
    setMessage('The current filtered role and permission matrix was exported.');
  }

  if (payload.loading) return <section className="role-permission-matrix-v2"><div className="rpm-loading">Loading the published role and permission matrix…</div></section>;
  if (payload.error || !payload.data) return <section className="role-permission-matrix-v2"><div className="rpm-foundation-error"><p className="eyebrow">Module 037</p><h2>Permission matrix did not load</h2><p>{payload.error}</p><button type="button" onClick={loadMatrix}>Try again</button></div></section>;

  const version = payload.data.policyVersion || {};

  return <section className="role-permission-matrix-v2" data-projectpulse-module="037" data-read-only="true">
    <header className="rpm-hero"><div><p className="eyebrow">Module 037</p><h1>Roles and Permissions Matrix</h1><p>Read-only confirmation of the permissions published in Module 012. The primary view follows the familiar spreadsheet layout: module, permission, and description remain pinned while role columns scroll horizontally.</p></div><div className="rpm-actions"><button type="button" onClick={loadMatrix}>Refresh</button><button type="button" onClick={exportCsv}>Export matrix</button></div></header>
    <div className="rpm-readonly-banner"><strong>Confirmation view only</strong><span>Change permissions in Module 012, publish a new immutable policy version, and refresh this page to verify the result.</span></div>
    <nav className="rpm-tabs" aria-label="Module 037 views"><button type="button" className={tab === 'matrix' ? 'active' : ''} onClick={() => setTab('matrix')}>Permission Matrix</button><button type="button" className={tab === 'roles' ? 'active' : ''} onClick={() => setTab('roles')}>Role Reference</button><button type="button" className={tab === 'levels' ? 'active' : ''} onClick={() => setTab('levels')}>Permission Levels</button></nav>

    {tab === 'matrix' ? <>
      <section className="rpm-summary-grid"><article><span>Policy version</span><strong>v{valueOf(version, 'versionNumber', 'VersionNumber', '—')}</strong><small>{valueOf(version, 'policyStatus', 'PolicyStatus', 'Unknown')}</small></article><article><span>Database modules</span><strong>{modules.length}</strong><small>Rows populate from the module table</small></article><article><span>Canonical roles</span><strong>{roles.length}</strong><small>Role columns populate from the role directory</small></article><article><span>No Access</span><strong>{totals['No Access']}</strong><small>Module hidden for those role/module pairs</small></article></section>
      <section className="rpm-toolbar"><label><span>Module</span><select value={moduleCode} onChange={(event) => setModuleCode(event.target.value)}><option value="all">All modules</option>{modules.map((module) => <option value={module.moduleCode} key={module.moduleCode}>Module {module.moduleCode} · {module.moduleName}</option>)}</select></label><label><span>Role</span><select value={roleCode} onChange={(event) => setRoleCode(event.target.value)}><option value="all">All roles</option>{roles.map((role) => <option value={role.roleCode} key={role.roleCode}>{role.roleName}</option>)}</select></label><label><span>Search</span><input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Permission, role, module, TIME_REASSIGN…" /></label></section>
      <div className="rpm-scroll-note"><strong>Matrix viewing tip</strong><span>The Module, Permission, and Description columns stay pinned. Scroll horizontally inside the table to compare Engineer, Engineering Lead, Project Team Coordinator, Super Administrator, and other roles without losing context.</span></div>
      {message ? <p className="roles-matrix-alert">{message}</p> : null}
      <p className="rpm-count">{rows.length} permission row(s) · {visibleRoles.length} role column(s)</p>
      <div className="rpm-permission-table-wrap"><table className="rpm-permission-table"><thead><tr><th>Module</th><th>Permission</th><th>Description</th>{visibleRoles.map((role) => <th key={role.roleCode} title={`${role.roleName} · ${role.roleCode}`}><span className="rpm-role-heading"><strong>{role.roleName}</strong><small>{role.roleCode}</small></span></th>)}</tr></thead><tbody>{rows.map(({ module, action }) => {
        const moduleGrants = grants.filter((grant) => grant.moduleCode === module.moduleCode);
        return <tr key={`${module.moduleCode}-${action.actionCode}`}><td><strong>Module {module.moduleCode}</strong><span>{module.moduleName}</span><small>{module.routeScope}</small></td><td><strong>{actionLabel(action.actionCode)}</strong><code>{action.actionCode}</code></td><td>{actionDescription(action.actionCode, action.actionDescription)}</td>{visibleRoles.map((role) => {
          const decision = decisionFor(moduleGrants, role.roleCode, action.actionCode);
          return <td key={role.roleCode} className={`rpm-decision rpm-decision-${decision.state.toLowerCase().replace('_', '-')}`}><button type="button" onClick={() => setSelected({ module, action, role, decision })} title={`${role.roleName}: ${decision.state} · ${decision.scope}`}><strong>{decision.state === 'ALLOW' ? '✓ Allow' : decision.state === 'DENY' ? '× Deny' : '— Not set'}</strong><small>{decision.scope}</small></button></td>;
        })}</tr>;
      })}</tbody></table></div>
    </> : null}

    {tab === 'roles' ? <section className="rpm-reference-grid">{roles.map((role) => {
      const reference = ROLE_REFERENCE[role.roleCode] || {};
      return <article key={role.roleCode} className={role.roleCode === 'PROJECT_TEAM_COORDINATOR' ? 'ptc-reference' : ''}><header><div><h2>{role.roleName}</h2><code>{role.roleCode}</code></div><span>{role.activeUserCount} assigned user(s)</span></header><p>{reference.purpose || role.description}</p><dl><dt>Whose records?</dt><dd>{reference.visibility || 'Defined by the module scope.'}</dd><dt>Default scope</dt><dd>{reference.defaultScope || 'SELF'}</dd></dl>{role.roleCode === 'PROJECT_TEAM_COORDINATOR' ? <div className="rpm-role-boundary"><strong>Time-steward boundary</strong><span>May manage other users’ time with reason and audit evidence, but does not submit their timesheets.</span></div> : null}</article>;
    })}</section> : null}

    {tab === 'levels' ? <section className="rpm-level-reference">{PERMISSION_LEVELS.map((level) => <article key={level.code}><span className={levelClass(level.code)}>{level.code}</span><div><h2>{level.code}</h2><p>{level.meaning}</p></div></article>)}</section> : null}

    {selected ? <div className="rpm-detail-overlay" role="dialog" aria-modal="true" aria-label="Permission details"><article><button type="button" className="rpm-close" onClick={() => setSelected(null)}>×</button><p className="eyebrow">Policy evidence</p><h2>{selected.role.roleName} · {actionLabel(selected.action.actionCode)}</h2><p>{actionDescription(selected.action.actionCode, selected.action.actionDescription)}</p><dl><dt>Module</dt><dd>Module {selected.module.moduleCode} · {selected.module.moduleName}</dd><dt>Permission code</dt><dd><code>{selected.action.actionCode}</code></dd><dt>Decision</dt><dd>{selected.decision.state}</dd><dt>Scope</dt><dd>{selected.decision.scope}</dd><dt>Policy evidence</dt><dd>{selected.decision.grants.length ? selected.decision.grants.map((grant) => `${grant.grantEffect || (grant.granted ? 'GRANT' : 'INHERITED')} · ${grant.scopeCode} · v${grant.versionNumber || '—'}`).join('; ') : 'No scoped grant is configured.'}</dd><dt>Reason required</dt><dd>{selected.decision.grants.some((grant) => grant.reasonRequired) ? 'Yes' : 'No'}</dd><dt>Audit required</dt><dd>{selected.decision.grants.some((grant) => grant.auditRequired) ? 'Yes' : 'No'}</dd><dt>Last modified</dt><dd>{formatDate(selected.decision.grants[0]?.lastModifiedAt)}</dd></dl></article></div> : null}
  </section>;
}
