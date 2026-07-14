import { useEffect, useMemo, useState } from 'react';

const columns = [
  ['projectCode', 'Project code', 'Essential', true],
  ['customer', 'Customer', 'Essential', true],
  ['project', 'Project', 'Essential', true],
  ['workType', 'Work type', 'Essential', true],
  ['billingModel', 'Contract type', 'Essential', true],
  ['status', 'Work status', 'Essential', true],
  ['projectManager', 'Project Manager', 'Ownership', true],
  ['coordinator', 'Project Team Coordinator', 'Ownership', false],
  ['assignedEngineers', 'Assigned engineers', 'Ownership', false],
  ['certiniaId', 'Certinia ID', 'External IDs', true],
  ['sellQuoteId', 'SELL Quote', 'External IDs', false],
  ['salesforceId', 'Salesforce ID', 'External IDs', false],
  ['purchaseOrder', 'Purchase order', 'External IDs', false],
  ['approvedLines', 'Approved billing lines', 'Billing data', true],
  ['approvedHours', 'Approved billing hours', 'Billing data', true],
  ['effectiveRate', 'Effective rate', 'Billing data', true],
  ['candidateAmount', 'Candidate amount', 'Billing data', true]
].map(([key, label, group, defaultVisible]) => ({ key, label, group, defaultVisible }));

const defaultColumns = columns.filter((column) => column.defaultVisible).map((column) => column.key);
const missingValue = 'Not configured';
const pendingBillingValue = 'Awaiting billing API';

function text(value, fallback = '') {
  const result = String(value ?? '').trim();
  return result || fallback;
}

function firstValue(source, keys, fallback = '') {
  for (const key of keys) {
    const value = source?.[key];
    if (value !== undefined && value !== null && String(value).trim() !== '') return value;
  }
  return fallback;
}

function normalizePeople(value) {
  if (!Array.isArray(value)) return [];
  return value
    .map((person) => {
      if (typeof person === 'string') return person.trim();
      return text(firstValue(person, ['displayName', 'name', 'engineerName', 'userName', 'email']));
    })
    .filter(Boolean);
}

function normalizeWorkItem(item, index) {
  const projectCode = text(firstValue(item, ['workCode', 'projectCode', 'code', 'project_code']), missingValue);
  const project = text(firstValue(item, ['workName', 'projectName', 'name', 'project_name']), 'Unnamed work item');
  const customer = text(firstValue(item, ['customerName', 'clientName', 'customer', 'customer_name']), missingValue);
  const workType = text(firstValue(item, ['workType', 'requestedWorkType', 'type', 'work_type']), missingValue);
  const billingModel = text(firstValue(item, ['contractType', 'billingModel', 'contract_type']), missingValue);
  const status = text(firstValue(item, ['status', 'lifecycleStatus', 'lifecycleState', 'projectStatus']), missingValue);
  const projectManager = text(firstValue(item, ['projectManager', 'projectManagerName', 'pmName']), 'Not assigned');
  const coordinator = text(firstValue(item, ['projectCoordinator', 'projectCoordinatorName', 'coordinatorName']), 'Not assigned');
  const assignedEngineers = normalizePeople(firstValue(item, ['assignedEngineers', 'engineers', 'assignedResources'], []));
  const certiniaId = text(firstValue(item, ['certiniaIdNumber', 'certinia_id_number', 'certiniaId']), missingValue);
  const sellQuoteId = text(firstValue(item, ['sellQuoteNumber', 'sell_quote_number', 'sellQuoteId']), missingValue);
  const salesforceId = text(firstValue(item, ['salesforceIdNumber', 'salesforce_id_number', 'salesforceId']), missingValue);
  const rawId = firstValue(item, ['workId', 'projectId', 'id', 'project_id'], `${projectCode}-${index}`);
  const normalizedStatus = status.toLowerCase();
  const closed = ['closed', 'completed', 'complete', 'archived', 'cancelled', 'canceled'].some((value) => normalizedStatus.includes(value));

  const blockers = [];
  if (customer === missingValue) blockers.push('Customer is not configured in the Work Register.');
  if (projectCode === missingValue) blockers.push('Project code is not configured in the Work Register.');
  if (billingModel === missingValue) blockers.push('Contract type is not configured in the Work Register.');
  if (projectManager === 'Not assigned') blockers.push('Project Manager is not assigned.');
  blockers.push('Approved time-entry billing lines are not connected yet.');
  blockers.push('Effective rate-card mapping is not connected yet.');
  blockers.push('Purchase-order records are not available until the PO model is installed.');

  return {
    id: String(rawId),
    customer,
    project,
    projectCode,
    workType,
    billingModel,
    status,
    projectManager,
    coordinator,
    assignedEngineers,
    certiniaId,
    sellQuoteId,
    salesforceId,
    purchaseOrder: missingValue,
    approvedLines: pendingBillingValue,
    approvedHours: pendingBillingValue,
    effectiveRate: pendingBillingValue,
    candidateAmount: pendingBillingValue,
    closed,
    blockers,
    source: item
  };
}

async function fetchJson(path) {
  const response = await fetch(path, { headers: { Accept: 'application/json' } });
  const raw = await response.text();
  let parsed = null;

  try {
    parsed = raw ? JSON.parse(raw) : null;
  } catch {
    parsed = null;
  }

  if (!response.ok) {
    const detail = parsed?.message || parsed?.detail || parsed?.status || raw || `HTTP ${response.status}`;
    throw new Error(`${path} returned HTTP ${response.status}: ${detail}`);
  }

  return parsed;
}

function workItemsFromPayload(payload) {
  const candidates = [payload?.data?.workItems, payload?.workItems, payload?.data, payload];
  return candidates.find(Array.isArray) || [];
}

function readColumns(userKey) {
  try {
    const stored = JSON.parse(window.localStorage.getItem(`projectPulseModule042Columns:${userKey || 'current-user'}`) || 'null');
    const valid = new Set(columns.map((column) => column.key));
    const filtered = Array.isArray(stored) ? stored.filter((key) => valid.has(key)) : [];
    return filtered.length ? filtered : defaultColumns;
  } catch {
    return defaultColumns;
  }
}

function Cell({ candidate, columnKey }) {
  if (columnKey === 'projectCode') return <span className="m042-stack"><strong>{candidate.projectCode}</strong><small>Invoice not created</small></span>;
  if (columnKey === 'billingModel') return <span className="m042-pill blue">{candidate.billingModel}</span>;
  if (columnKey === 'status') return <span className="m042-pill amber">{candidate.status}</span>;
  if (columnKey === 'assignedEngineers') return candidate.assignedEngineers.length ? candidate.assignedEngineers.join(', ') : 'Not assigned';
  if (['approvedLines', 'approvedHours', 'effectiveRate', 'candidateAmount'].includes(columnKey)) {
    return <span className="m042-status-note">{pendingBillingValue}</span>;
  }
  return candidate[columnKey] ?? missingValue;
}

export default function InvoiceBillingCenter({ usSignalLogoUrl, userKey }) {
  const [view, setView] = useState('queue');
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('All');
  const [model, setModel] = useState('All');
  const [visibleColumns, setVisibleColumns] = useState(() => readColumns(userKey));
  const [selectedId, setSelectedId] = useState('');
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [payload, setPayload] = useState({ loading: true, error: '', candidates: [] });

  async function loadLiveData() {
    setPayload((current) => ({ ...current, loading: true, error: '' }));

    try {
      const result = await fetchJson('/api/work-register/overview');
      const candidates = workItemsFromPayload(result).map(normalizeWorkItem);
      setPayload({ loading: false, error: '', candidates });
      setSelectedId((current) => current || candidates[0]?.id || '');
    } catch (error) {
      setPayload({ loading: false, error: error instanceof Error ? error.message : 'Unable to load Work Register data.', candidates: [] });
    }
  }

  useEffect(() => {
    void loadLiveData();
  }, []);

  useEffect(() => {
    try {
      window.localStorage.setItem(`projectPulseModule042Columns:${userKey || 'current-user'}`, JSON.stringify(visibleColumns));
    } catch {
      // The selected columns remain active for this browser session.
    }
  }, [userKey, visibleColumns]);

  const candidates = payload.candidates;
  const selected = candidates.find((candidate) => candidate.id === selectedId) || candidates[0] || null;
  const visibleDefinitions = columns.filter((column) => visibleColumns.includes(column.key));
  const groups = [...new Set(columns.map((column) => column.group))];
  const statusOptions = [...new Set(candidates.map((candidate) => candidate.status).filter(Boolean))].sort();
  const modelOptions = [...new Set(candidates.map((candidate) => candidate.billingModel).filter(Boolean))].sort();

  const filtered = useMemo(() => {
    const needle = search.trim().toLowerCase();
    return candidates.filter((candidate) => {
      if (view === 'closed' && !candidate.closed) return false;
      if (view === 'queue' && candidate.closed) return false;
      if (status !== 'All' && candidate.status !== status) return false;
      if (model !== 'All' && candidate.billingModel !== model) return false;
      if (!needle) return true;
      return [
        candidate.customer,
        candidate.project,
        candidate.projectCode,
        candidate.workType,
        candidate.billingModel,
        candidate.projectManager,
        candidate.coordinator,
        candidate.certiniaId,
        candidate.sellQuoteId,
        candidate.salesforceId,
        ...candidate.assignedEngineers
      ].join(' ').toLowerCase().includes(needle);
    });
  }, [candidates, model, search, status, view]);

  const configuredExternalIds = candidates.filter((candidate) => (
    candidate.certiniaId !== missingValue || candidate.sellQuoteId !== missingValue || candidate.salesforceId !== missingValue
  )).length;
  const coreDataExceptions = candidates.filter((candidate) => (
    candidate.customer === missingValue || candidate.projectCode === missingValue || candidate.billingModel === missingValue
  )).length;

  const toggleColumn = (key) => setVisibleColumns((current) => {
    if (current.includes(key)) return current.length === 1 ? current : current.filter((item) => item !== key);
    return columns.map((column) => column.key).filter((item) => current.includes(item) || item === key);
  });

  return (
    <div className="m042-center" data-module-number="042">
      <header className="m042-hero">
        <div>
          <p className="eyebrow">MODULE 042 • Invoice operations</p>
          <h1>Invoice &amp; Billing Center</h1>
          <p>Review real Work Register projects and their system-owned customer, project, people, contract, and external-identifier data. Invoice amounts remain blocked until approved time, effective rates, purchase orders, and invoice snapshots are connected.</p>
        </div>
        <div className="m042-actions">
          <button type="button" className="secondary-action" onClick={() => setDrawerOpen(true)}>Customize columns</button>
          <button type="button" className="secondary-action" onClick={() => void loadLiveData()}>Reload system data</button>
          <button type="button" className="secondary-action" disabled title="Requires the live invoice ledger and approved billing lines.">Export Excel</button>
          <button type="button" className="primary-action" disabled title="Requires the live invoice ledger and approved billing lines.">Export PDF</button>
        </div>
      </header>

      <section className="m042-preview-mode m042-live-mode" aria-label="Module 042 live data status">
        <strong>Live system data</strong>
        <span>
          Customers, projects, Project Managers, Project Team Coordinators, assigned engineers, contract types, and external IDs are loaded from the Work Register. No sample customers, people, projects, rates, amounts, purchase orders, or time entries are generated.
        </span>
      </section>

      {payload.error ? <div className="m042-notice m042-error" role="alert">{payload.error}</div> : null}

      <section className="m042-workflow-explainer" aria-label="Billing workflow">
        <article><span>1</span><div><strong>Module 039</strong><small>Check real approved-time readiness and resolve blockers.</small></div></article>
        <b aria-hidden="true">→</b>
        <article><span>2</span><div><strong>Module 042</strong><small>Prepare an immutable invoice from eligible system records.</small></div></article>
        <b aria-hidden="true">→</b>
        <article><span>3</span><div><strong>Export / Accounting</strong><small>Deliver the approved PDF or Excel package and preserve history.</small></div></article>
      </section>

      <section className="m042-metrics" aria-label="Live system summary">
        <article><span>Work Register projects</span><strong>{candidates.length}</strong><small>Loaded from the system</small></article>
        <article><span>External IDs configured</span><strong>{configuredExternalIds}</strong><small>Certinia, SELL, or Salesforce</small></article>
        <article><span>Recently closed</span><strong>{candidates.filter((candidate) => candidate.closed).length}</strong><small>Based on Work Register status</small></article>
        <article><span>Core data exceptions</span><strong>{coreDataExceptions}</strong><small>Missing customer, code, or contract type</small></article>
      </section>

      <nav className="m042-tabs" aria-label="Invoice Center views">
        <button type="button" className={view === 'queue' ? 'active' : ''} onClick={() => setView('queue')}>Active billing candidates</button>
        <button type="button" className={view === 'closed' ? 'active' : ''} onClick={() => setView('closed')}>Recently closed</button>
        <button type="button" className={view === 'reports' ? 'active' : ''} onClick={() => setView('reports')}>Reports</button>
      </nav>

      {view === 'reports' ? (
        <section className="m042-reports">
          <article><span>Fixed Price</span><h2>Over / Under Report</h2><p>This report will use planned final hours, actual approved system time, and the immutable utilization-adjustment ledger. No values are displayed until those records are connected.</p><dl><div><dt>Data status</dt><dd>Awaiting billing API</dd></div></dl></article>
          <article><span>T&amp;M</span><h2>Customer Balance</h2><p>This report will use customer billing balances, approved uninvoiced time, prior invoice snapshots, and effective rate-card lines.</p><dl><div><dt>Data status</dt><dd>Awaiting billing API</dd></div></dl></article>
          <article><span>Invoice history</span><h2>Partial and Final Invoices</h2><p>Invoice history will come only from the immutable invoice ledger. No draft counts or dollar totals are estimated.</p><dl><div><dt>Data status</dt><dd>Invoice ledger not installed</dd></div></dl></article>
        </section>
      ) : (
        <>
          <section className="m042-toolbar">
            <label><span>Search system projects</span><input type="search" value={search} placeholder="Customer, project, code, PM, PTC, engineer, Certinia, SELL, Salesforce..." onChange={(event) => setSearch(event.target.value)} /></label>
            <label><span>Status</span><select value={status} onChange={(event) => setStatus(event.target.value)}><option>All</option>{statusOptions.map((value) => <option value={value} key={value}>{value}</option>)}</select></label>
            <label><span>Contract type</span><select value={model} onChange={(event) => setModel(event.target.value)}><option>All</option>{modelOptions.map((value) => <option value={value} key={value}>{value}</option>)}</select></label>
            <div className="m042-column-count"><strong>{visibleColumns.length}</strong><small>columns shown</small></div>
          </section>

          <section className="m042-workspace">
            <div className="m042-card">
              <header className="m042-card-head"><div><h2>{view === 'closed' ? 'Recently closed Work Register projects' : 'Active project billing candidates'}</h2><p>Every row below is loaded from the system. Selecting a row does not create an invoice.</p></div><span>{filtered.length} shown</span></header>
              <div className="m042-table-wrap">
                <table>
                  <thead><tr>{visibleDefinitions.map((column) => <th key={column.key}>{column.label}</th>)}</tr></thead>
                  <tbody>
                    {filtered.map((candidate) => (
                      <tr key={candidate.id} className={selected?.id === candidate.id ? 'selected' : ''} onClick={() => setSelectedId(candidate.id)}>
                        {visibleDefinitions.map((column) => <td key={`${candidate.id}-${column.key}`}><Cell candidate={candidate} columnKey={column.key} /></td>)}
                      </tr>
                    ))}
                    {!payload.loading && filtered.length === 0 ? <tr><td colSpan={Math.max(1, visibleDefinitions.length)}><div className="m042-empty-state"><strong>No matching system projects</strong><span>Adjust the filters or confirm that the Work Register contains accessible projects.</span></div></td></tr> : null}
                  </tbody>
                </table>
              </div>
            </div>

            <aside className="m042-card m042-preview">
              {payload.loading ? (
                <div className="m042-empty-state"><strong>Loading Work Register data…</strong><span>No placeholder records will be displayed.</span></div>
              ) : selected ? (
                <div className="m042-invoice">
                  <header className="m042-invoice-head"><div className="m042-brand">{usSignalLogoUrl ? <img src={usSignalLogoUrl} alt="US Signal" /> : <strong>US Signal</strong>}<span>Invoice candidate review</span></div><div><span>Invoice not created</span><strong>{selected.projectCode}</strong><small>{selected.status}</small></div></header>
                  <section className="m042-identities"><div><span>Customer</span><strong>{selected.customer}</strong><small>Loaded from the Work Register</small></div><div><span>Project</span><strong>{selected.project}</strong><small>{selected.workType} · {selected.billingModel}</small></div><div><span>Ownership</span><strong>{selected.projectManager}</strong><small>PTC: {selected.coordinator}</small></div></section>
                  <section className="m042-refs"><div><span>Certinia ID</span><strong>{selected.certiniaId}</strong></div><div><span>SELL Quote</span><strong>{selected.sellQuoteId}</strong></div><div><span>Salesforce ID</span><strong>{selected.salesforceId}</strong></div><div><span>Purchase order</span><strong>{selected.purchaseOrder}</strong><small>PO model pending</small></div></section>
                  <section className="m042-resource-list"><span>Assigned engineers</span><div>{selected.assignedEngineers.length ? selected.assignedEngineers.map((engineer) => <strong key={engineer}>{engineer}</strong>) : <em>Not assigned</em>}</div></section>
                  <div className="m042-lines"><table><thead><tr><th>Date</th><th>Engineer / PM and work detail</th><th>Hours</th><th>Rate</th><th>Amount</th></tr></thead><tbody><tr><td colSpan="5"><div className="m042-empty-state"><strong>No approved invoice lines loaded</strong><span>The permanent billing API will create one traceable line for each eligible engineer or PM time entry. Until then, hours, rates, and amounts remain unavailable rather than estimated.</span></div></td></tr></tbody></table></div>
                  <section className="m042-data-quality"><h3>Billing blockers</h3><ul>{selected.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}</ul></section>
                  <section className="m042-totals"><div><span>Previously invoiced</span><strong>Not available</strong></div><div><span>Current invoice</span><strong>Not calculated</strong></div><div><span>Remaining balance</span><strong>Not available</strong></div></section>
                  <footer className="m042-invoice-foot">No invoice, invoice number, rate, amount, purchase order, or time-entry line is generated on this page.</footer>
                </div>
              ) : (
                <div className="m042-empty-state"><strong>No Work Register projects are available</strong><span>Module 042 will remain empty until accessible system projects exist.</span></div>
              )}
            </aside>
          </section>
        </>
      )}

      {drawerOpen ? <div className="m042-backdrop" onMouseDown={(event) => event.target === event.currentTarget && setDrawerOpen(false)}><section className="m042-drawer" role="dialog" aria-modal="true" aria-label="Customize invoice headers"><header><div><p className="eyebrow">Personal table settings</p><h2>Customize columns</h2><p>Add or remove headers from your Invoice Center. The selection is saved for this user on this browser.</p></div><button type="button" aria-label="Close" onClick={() => setDrawerOpen(false)}>×</button></header><div className="m042-shortcuts"><button type="button" onClick={() => setVisibleColumns(columns.map((column) => column.key))}>Show all</button><button type="button" onClick={() => setVisibleColumns(columns.filter((column) => column.group === 'Essential').map((column) => column.key))}>Essential only</button><button type="button" onClick={() => setVisibleColumns(defaultColumns)}>Restore defaults</button></div><div className="m042-groups">{groups.map((group) => <fieldset key={group}><legend>{group}</legend>{columns.filter((column) => column.group === group).map((column) => <label key={column.key}><input type="checkbox" checked={visibleColumns.includes(column.key)} onChange={() => toggleColumn(column.key)} /><span>{column.label}</span></label>)}</fieldset>)}</div><footer><span>{visibleColumns.length} columns selected</span><button type="button" className="primary-action" onClick={() => setDrawerOpen(false)}>Apply columns</button></footer></section></div> : null}
    </div>
  );
}
