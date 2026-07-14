#!/usr/bin/env python3
from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

EXPECTED_HEAD = "69d6b61e09963524c61174d057010c5d10a6f0ba"

COMPONENT = r'''import { useEffect, useMemo, useState } from 'react';

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
'''

STYLE_BLOCK = r'''

/* 042C_LIVE_WORK_REGISTER_DATA_INTEGRITY_START */
.m042-actions button:disabled {
  cursor: not-allowed;
  opacity: 0.52;
}

.m042-live-mode {
  border-color: rgba(16, 185, 129, 0.38);
  background: rgba(16, 185, 129, 0.08);
}

.m042-live-mode strong {
  background: rgba(16, 185, 129, 0.16);
  color: #047857;
}

.m042-error {
  border-color: rgba(220, 38, 38, 0.35);
  background: rgba(220, 38, 38, 0.08);
  color: #991b1b;
}

.m042-status-note {
  display: inline-flex;
  max-width: 12rem;
  color: var(--muted, #64748b);
  font-size: 0.8rem;
  line-height: 1.3;
}

.m042-empty-state {
  display: grid;
  gap: 0.35rem;
  justify-items: center;
  padding: 2rem 1rem;
  color: var(--muted, #64748b);
  text-align: center;
}

.m042-empty-state strong {
  color: var(--text, #0f172a);
}

.m042-resource-list,
.m042-data-quality {
  display: grid;
  gap: 0.65rem;
  padding: 0.9rem 0;
  border-top: 1px solid rgba(148, 163, 184, 0.22);
}

.m042-resource-list > span {
  color: var(--muted, #64748b);
  font-size: 0.78rem;
  font-weight: 800;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.m042-resource-list > div {
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
}

.m042-resource-list strong {
  padding: 0.3rem 0.55rem;
  border: 1px solid rgba(14, 165, 233, 0.25);
  border-radius: 999px;
  background: rgba(14, 165, 233, 0.06);
  font-size: 0.8rem;
}

.m042-data-quality h3 {
  margin: 0;
}

.m042-data-quality ul {
  display: grid;
  gap: 0.35rem;
  margin: 0;
  padding-left: 1.25rem;
  color: var(--muted, #64748b);
}
/* 042C_LIVE_WORK_REGISTER_DATA_INTEGRITY_END */
'''


def run(repo: Path, *args: str) -> str:
    result = subprocess.run(
        [*args],
        cwd=repo,
        check=True,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    return result.stdout.strip()


def main() -> int:
    repo = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    if not (repo / ".git").is_dir():
        raise SystemExit("ERROR: Repository root is invalid.")

    head = run(repo, "git", "rev-parse", "HEAD")
    if head != EXPECTED_HEAD:
        raise SystemExit(f"ERROR: Expected source head {EXPECTED_HEAD}; found {head}.")

    if run(repo, "git", "status", "--porcelain"):
        raise SystemExit("ERROR: Repository must be clean before preparing the hotfix.")

    component_path = repo / "src/frontend/project-time-web/src/InvoiceBillingCenter.jsx"
    guide_path = repo / "src/frontend/project-time-web/src/PageContextGuide.jsx"
    styles_path = repo / "src/frontend/project-time-web/src/styles.css"
    obsolete_preparer = repo / "deployment/module-042/prepare-integrated-module.sh"

    for path in (component_path, guide_path, styles_path, obsolete_preparer):
        if not path.exists():
            raise SystemExit(f"ERROR: Required path is missing: {path}")

    component_path.write_text(COMPONENT, encoding="utf-8")

    guide = guide_path.read_text(encoding="utf-8")
    pattern = re.compile(
        r"  'invoice-billing-center': \{.*?^  \},",
        re.MULTILINE | re.DOTALL,
    )
    replacement = """  'invoice-billing-center': {
    page: 'Invoice & Billing Center — Module 042',
    purpose: 'Live Work Register billing-candidate view. It shows only system-owned customer, project, people, contract, status, and external-identifier data. Approved time, effective rates, purchase orders, invoice numbers, amounts, and exports remain blocked until their permanent APIs and ledgers are installed.',
    backend: '/api/work-register/overview for live project data; shared billing-candidate and immutable invoice-ledger APIs pending',
    check: 'Confirm that every visible customer, project, PM, PTC, engineer, contract type, and external ID exists in the Work Register. Missing values must remain visibly unconfigured and must never be replaced with sample data.'
  },"""
    guide, count = pattern.subn(replacement, guide, count=1)
    if count != 1:
        raise SystemExit("ERROR: Module 042 page-context entry was not replaced exactly once.")
    guide_path.write_text(guide, encoding="utf-8")

    styles = styles_path.read_text(encoding="utf-8")
    if "042C_LIVE_WORK_REGISTER_DATA_INTEGRITY_START" not in styles:
        styles = styles.rstrip() + STYLE_BLOCK.rstrip() + "\n"
    styles_path.write_text(styles, encoding="utf-8")

    obsolete_preparer.unlink()

    combined = "\n".join(
        path.read_text(encoding="utf-8", errors="replace")
        for path in (component_path, guide_path, styles_path)
    )
    forbidden = [
        "const invoices = [",
        "Northwind Health Systems",
        "Summit Regional Bank",
        "Great Lakes Manufacturing",
        "River Valley Utilities",
        "Morgan Ellis",
        "Taylor Brooks",
        "Alex Johnson",
        "Priya Shah",
        "PO-882174",
        "26730",
        "53122.5",
    ]
    remaining = [marker for marker in forbidden if marker in combined]
    if remaining:
        raise SystemExit("ERROR: Forbidden sample markers remain: " + ", ".join(remaining))

    required = [
        "/api/work-register/overview",
        "Live system data",
        "No sample customers",
        "Approved time-entry billing lines are not connected yet.",
        "Purchase-order records are not available until the PO model is installed.",
        "Invoice ledger not installed",
        "042C_LIVE_WORK_REGISTER_DATA_INTEGRITY_START",
    ]
    missing = [marker for marker in required if marker not in combined]
    if missing:
        raise SystemExit("ERROR: Required live-data markers are missing: " + ", ".join(missing))

    run(repo, "git", "diff", "--check")

    print("MODULE_042_LIVE_WORK_REGISTER_HOTFIX=PREPARED")
    print("HARDCODED_OPERATIONAL_DATA_REMOVED=YES")
    print("WORK_REGISTER_OVERVIEW_CONNECTED=YES")
    print("MISSING_VALUES_REMAIN_EXPLICIT=YES")
    print("INVOICE_EXPORTS_DISABLED_UNTIL_LEDGER=YES")
    print("OBSOLETE_MOCK_PREPARER_REMOVED=YES")
    print("DATABASE_MODIFIED=NO")
    print("AZURE_MODIFIED=NO")
    print("APPLICATION_DEPLOYED=NO")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
