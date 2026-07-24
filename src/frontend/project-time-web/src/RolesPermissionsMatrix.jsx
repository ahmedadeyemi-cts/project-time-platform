import { useEffect, useMemo, useState } from 'react';
import './roles-permissions-matrix.css';

function headers() {
  try {
    const session = JSON.parse(localStorage.getItem('projectPulseAuthSession') || 'null');
    return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
  } catch {
    return {};
  }
}

async function api(path) {
  const response = await fetch(path, {
    method: 'GET',
    cache: 'no-store',
    headers: {
      ...headers(),
      'Cache-Control': 'no-cache',
      Pragma: 'no-cache'
    }
  });
  const raw = await response.text();
  let payload = {};
  try { payload = raw ? JSON.parse(raw) : {}; } catch { payload = { message: raw }; }
  if (!response.ok) {
    throw new Error(payload.message || payload.detail || `${path} returned HTTP ${response.status}`);
  }
  return payload;
}

function csvEscape(value) {
  const text = String(value ?? '');
  return /[",\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

function downloadCsv(filename, rows) {
  const content = rows.map((row) => row.map(csvEscape).join(',')).join('\n');
  const blob = new Blob([content], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function formatDate(value) {
  if (!value) return 'Not recorded';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
}

function roleCell(entry) {
  if (!entry) {
    return {
      granted: false,
      explicitDeny: false,
      inherited: true,
      delegatedAuthority: false,
      reasonRequired: false,
      auditRequired: true,
      conditions: { legacyAuthorizationPreserved: true },
      explanation: 'No scoped decision exists. Existing ProjectPulse authorization remains in effect.'
    };
  }
  return entry;
}

function cellLabel(cell) {
  if (cell.explicitDeny || cell.grantEffect === 'DENY') return 'Denied';
  if (cell.granted || cell.grantEffect === 'GRANT') return 'Granted';
  if (cell.inherited) return 'Legacy';
  return 'Not granted';
}

function cellClass(cell) {
  if (cell.explicitDeny || cell.grantEffect === 'DENY') return 'not-granted';
  if (cell.granted || cell.grantEffect === 'GRANT') return 'granted';
  return 'not-granted';
}

export default function RolesPermissionsMatrix() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: '' });
  const [moduleCode, setModuleCode] = useState('all');
  const [roleCode, setRoleCode] = useState('all');
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState(null);
  const [explanation, setExplanation] = useState(null);
  const [message, setMessage] = useState('');

  async function loadMatrix() {
    setPayload((current) => ({ ...current, loading: true, error: '' }));
    try {
      const data = await api('/api/role-policy/matrix');
      setPayload({ loading: false, data, error: '' });
    } catch (error) {
      setPayload({ loading: false, data: null, error: error.message || 'Unable to load the effective matrix.' });
    }
  }

  useEffect(() => { void loadMatrix(); }, []);

  const roles = payload.data?.roles || [];
  const modules = payload.data?.modules || [];
  const effectiveEntries = useMemo(
    () => [...(payload.data?.grants || []), ...(payload.data?.legacyFallback || [])],
    [payload.data]
  );

  const rows = useMemo(() => {
    const byKey = new Map();
    effectiveEntries.forEach((entry) => {
      const key = `${entry.moduleCode}|${entry.actionCode}|${entry.scopeCode}`;
      const row = byKey.get(key) || {
        key,
        moduleCode: entry.moduleCode,
        moduleName: entry.moduleName || modules.find((item) => item.moduleCode === entry.moduleCode)?.moduleName || '',
        routeScope: entry.routeScope || modules.find((item) => item.moduleCode === entry.moduleCode)?.routeScope || '',
        actionCode: entry.actionCode,
        scopeCode: entry.scopeCode,
        cells: new Map()
      };
      row.cells.set(entry.roleCode, entry);
      byKey.set(key, row);
    });
    return [...byKey.values()].sort((left, right) => (
      String(left.moduleCode).localeCompare(String(right.moduleCode), undefined, { numeric: true })
      || String(left.actionCode).localeCompare(String(right.actionCode))
      || String(left.scopeCode).localeCompare(String(right.scopeCode))
    ));
  }, [effectiveEntries, modules]);

  const visibleRoles = useMemo(
    () => roleCode === 'all' ? roles : roles.filter((role) => role.roleCode === roleCode),
    [roleCode, roles]
  );

  const visibleRows = useMemo(() => {
    const term = search.trim().toLowerCase();
    return rows.filter((row) => {
      if (moduleCode !== 'all' && row.moduleCode !== moduleCode) return false;
      if (!term) return true;
      return [row.moduleCode, row.moduleName, row.routeScope, row.actionCode, row.scopeCode]
        .join(' ')
        .toLowerCase()
        .includes(term);
    });
  }, [moduleCode, rows, search]);

  const totals = useMemo(() => {
    let grants = 0;
    let denials = 0;
    let legacy = 0;
    let delegated = 0;
    effectiveEntries.forEach((entry) => {
      if (entry.grantEffect === 'GRANT' || entry.granted) grants += 1;
      if (entry.grantEffect === 'DENY' || entry.explicitDeny) denials += 1;
      if (entry.inherited) legacy += 1;
      if (entry.delegatedAuthority) delegated += 1;
    });
    return { grants, denials, legacy, delegated };
  }, [effectiveEntries]);

  async function selectCell(row, role, cell) {
    const next = { row, role, cell: roleCell(cell) };
    setSelected(next);
    setExplanation({ loading: true });
    try {
      const params = new URLSearchParams({
        roleCode: role.roleCode,
        moduleCode: row.moduleCode,
        actionCode: row.actionCode,
        scopeCode: row.scopeCode
      });
      setExplanation(await api(`/api/role-policy/explain?${params.toString()}`));
    } catch (error) {
      setExplanation({ error: error.message || 'Unable to explain this effective access decision.' });
    }
  }

  function exportCsv() {
    const header = [
      'Module', 'Module Name', 'Route / Scope', 'Permission Action', 'Scope',
      'Role', 'Status', 'Inherited', 'Explicit Denial', 'Delegated Authority',
      'Reason Required', 'Audit Required', 'Conditions', 'Policy Version',
      'Last Modified By', 'Last Modified Date'
    ];
    const data = [];
    visibleRows.forEach((row) => {
      visibleRoles.forEach((role) => {
        const cell = roleCell(row.cells.get(role.roleCode));
        data.push([
          row.moduleCode,
          row.moduleName,
          row.routeScope,
          row.actionCode,
          row.scopeCode,
          role.roleName || role.roleCode,
          cellLabel(cell),
          Boolean(cell.inherited),
          Boolean(cell.explicitDeny || cell.grantEffect === 'DENY'),
          Boolean(cell.delegatedAuthority),
          Boolean(cell.reasonRequired),
          cell.auditRequired !== false,
          JSON.stringify(cell.conditions || {}),
          cell.versionNumber || payload.data?.policyVersion?.versionNumber || '',
          cell.lastModifiedBy || '',
          cell.lastModifiedAt || ''
        ]);
      });
    });
    downloadCsv('projectpulse-effective-scoped-role-matrix.csv', [header, ...data]);
    setMessage('Effective scoped permissions exported as CSV.');
  }

  if (payload.loading) {
    return <section className="roles-permissions-matrix">Loading effective scoped permissions…</section>;
  }

  if (payload.error || !payload.data) {
    return (
      <section className="roles-permissions-matrix">
        <p className="eyebrow">Module 037</p>
        <h2>Roles and Permissions Matrix</h2>
        <p className="roles-matrix-error">{payload.error || 'The effective matrix is unavailable.'}</p>
        <button type="button" onClick={loadMatrix}>Retry</button>
      </section>
    );
  }

  return (
    <section className="roles-permissions-matrix" data-projectpulse-module="037" data-read-only="true">
      <header className="roles-matrix-header">
        <div>
          <p className="eyebrow">Module 037</p>
          <h1>Roles and Permissions Matrix</h1>
          <p className="muted">
            Strictly read-only representation of the effective, versioned permissions configured through Module 012.
          </p>
        </div>
        <div className="roles-matrix-actions">
          <button type="button" onClick={loadMatrix}>Refresh</button>
          <button type="button" onClick={exportCsv}>Export CSV</button>
        </div>
      </header>

      <div className="roles-matrix-alert">
        <strong>Read-only policy view.</strong> This module has no permission-editing controls or write endpoint.
        Policy changes and controlled restoration are available only through Module 012.
      </div>

      <div className="roles-matrix-summary-grid">
        <article><span>Policy version</span><strong>v{payload.data.policyVersion?.versionNumber || '—'}</strong><small>{payload.data.policyVersion?.policyStatus || 'Unknown'}</small></article>
        <article><span>Granular grants</span><strong>{totals.grants}</strong><small>Effective scoped grants</small></article>
        <article><span>Explicit denials</span><strong>{totals.denials}</strong><small>Override grants for matching actions</small></article>
        <article><span>Legacy fallbacks</span><strong>{totals.legacy}</strong><small>Workbook Not Set preserves current behavior</small></article>
        <article><span>Delegated grants</span><strong>{totals.delegated}</strong><small>Delegation remains reasoned and audited</small></article>
      </div>

      <div className="roles-matrix-toolbar">
        <label>
          <span>Module</span>
          <select value={moduleCode} onChange={(event) => setModuleCode(event.target.value)}>
            <option value="all">All modules</option>
            {modules.map((module) => (
              <option value={module.moduleCode} key={module.moduleCode}>
                Module {module.moduleCode} · {module.moduleName}
              </option>
            ))}
          </select>
        </label>
        <label>
          <span>Role</span>
          <select value={roleCode} onChange={(event) => setRoleCode(event.target.value)}>
            <option value="all">All canonical roles</option>
            {roles.map((role) => <option value={role.roleCode} key={role.roleCode}>{role.roleName}</option>)}
          </select>
        </label>
        <label>
          <span>Search</span>
          <input
            type="search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Search module, action, scope, or route"
          />
        </label>
      </div>

      {message ? <p className="roles-matrix-alert">{message}</p> : null}
      <p className="roles-matrix-count">{visibleRows.length} action/scope row(s)</p>

      <div className="roles-matrix-table-wrap">
        <table className="roles-matrix-table">
          <thead>
            <tr>
              <th>Module</th>
              <th>Permission / action</th>
              <th>Scope</th>
              {visibleRoles.map((role) => <th key={role.roleCode}>{role.roleName}</th>)}
            </tr>
          </thead>
          <tbody>
            {visibleRows.map((row) => (
              <tr key={row.key}>
                <td>
                  <strong>Module {row.moduleCode}</strong>
                  <small>{row.moduleName}</small>
                  <small>{row.routeScope}</small>
                </td>
                <td><strong>{row.actionCode}</strong></td>
                <td><strong>{row.scopeCode}</strong></td>
                {visibleRoles.map((role) => {
                  const cell = roleCell(row.cells.get(role.roleCode));
                  return (
                    <td className={cellClass(cell)} key={role.roleCode}>
                      <button
                        type="button"
                        className="roles-matrix-cell-button"
                        onClick={() => selectCell(row, role, cell)}
                        title="Open effective access explanation"
                      >
                        <strong>{cellLabel(cell)}</strong>
                        <small>{cell.inherited ? 'Inherited legacy behavior' : `Policy v${cell.versionNumber || payload.data.policyVersion?.versionNumber || '—'}`}</small>
                        {cell.delegatedAuthority ? <small>Delegated</small> : null}
                        {cell.reasonRequired ? <small>Reason required</small> : null}
                      </button>
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {!visibleRows.length ? <p className="roles-matrix-empty">No effective permissions match the current filters.</p> : null}

      {selected ? (
        <section className="roles-matrix-panel" aria-live="polite">
          <p className="eyebrow">Effective access explanation</p>
          <h2>{selected.role.roleName} · Module {selected.row.moduleCode} · {selected.row.actionCode}</h2>
          {explanation?.loading ? <p>Loading explanation…</p> : null}
          {explanation?.error ? <p className="roles-matrix-error">{explanation.error}</p> : null}
          {explanation && !explanation.loading && !explanation.error ? (
            <div className="governance-signal-list">
              <article><span>Decision</span><strong>{explanation.explicitDeny ? 'Explicitly denied' : explanation.granted ? 'Granted' : 'Legacy fallback'}</strong><p>{explanation.explanation}</p></article>
              <article><span>Scope</span><strong>{explanation.scopeCode}</strong><p>Inherited: {String(Boolean(explanation.inherited))}</p></article>
              <article><span>Controls</span><strong>{explanation.delegatedAuthority ? 'Delegation allowed' : 'No delegated authority'}</strong><p>Reason required: {String(Boolean(explanation.reasonRequired))} · Audit required: {String(explanation.auditRequired !== false)}</p></article>
              <article><span>Policy evidence</span><strong>Version {explanation.policyVersion || payload.data.policyVersion?.versionNumber || '—'}</strong><p>Last modified by {explanation.lastModifiedBy || 'System'} · {formatDate(explanation.lastModifiedAt)}</p></article>
            </div>
          ) : null}
        </section>
      ) : null}
    </section>
  );
}
