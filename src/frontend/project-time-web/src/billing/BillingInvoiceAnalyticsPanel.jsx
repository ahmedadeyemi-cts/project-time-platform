import { useCallback, useEffect, useMemo, useState } from 'react';

function money(value) {
  const amount = Number(value ?? 0);
  return Number.isFinite(amount)
    ? amount.toLocaleString(undefined, { style: 'currency', currency: 'USD' })
    : '—';
}

function date(value) {
  if (!value) return '—';
  const parsed = new Date(`${String(value).slice(0, 10)}T00:00:00`);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleDateString();
}

function dateTime(value) {
  if (!value) return '—';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

async function readJson(response) {
  const payload = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(payload?.message || payload?.status || `Invoice analytics returned HTTP ${response.status}.`);
  }
  return payload;
}

function csvCell(value) {
  const text = value === null || value === undefined ? '' : String(value);
  return `"${text.replaceAll('"', '""')}"`;
}

function downloadCsv(rows) {
  const columns = [
    ['invoiceNumber', 'Invoice Number'],
    ['invoiceType', 'Invoice Type'],
    ['invoiceStatus', 'Invoice Status'],
    ['customerName', 'Customer'],
    ['projectCode', 'Project Code'],
    ['projectName', 'Project Name'],
    ['projectManagerName', 'Project Manager'],
    ['billingPeriodStart', 'Period Start'],
    ['billingPeriodEnd', 'Period End'],
    ['invoiceDate', 'Invoice Date'],
    ['lineCount', 'Line Count'],
    ['laborAmount', 'Labor Amount'],
    ['expenseAmount', 'Expense Amount'],
    ['milestoneAmount', 'Milestone Amount'],
    ['otherAmount', 'Other Amount'],
    ['subtotalAmount', 'Subtotal'],
    ['adjustmentAmount', 'Adjustment'],
    ['taxAmount', 'Tax'],
    ['totalAmount', 'Total'],
    ['purchaseOrderNumber', 'Purchase Order'],
    ['createdBy', 'Created By'],
    ['createdAt', 'Created At'],
    ['finalizedBy', 'Finalized By'],
    ['finalizedAt', 'Finalized At'],
    ['immutableSnapshotAvailable', 'Immutable Snapshot']
  ];
  const content = [
    columns.map(([, label]) => csvCell(label)).join(','),
    ...rows.map((row) => columns.map(([key]) => csvCell(row?.[key])).join(','))
  ].join('\r\n');
  const blob = new Blob([content], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = `partial-final-invoice-lifecycle-${new Date().toISOString().slice(0, 10)}.csv`;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export default function BillingInvoiceAnalyticsPanel({
  projects = [],
  selectedProjectId = '',
  onProjectChange,
  onOpenProject
}) {
  const [filters, setFilters] = useState({
    projectId: selectedProjectId,
    invoiceType: '',
    status: '',
    dateFrom: '',
    dateTo: '',
    search: ''
  });
  const [state, setState] = useState({ loading: true, error: '', data: null });

  useEffect(() => {
    setFilters((current) => ({ ...current, projectId: selectedProjectId || '' }));
  }, [selectedProjectId]);

  const runReport = useCallback(async (nextFilters = filters) => {
    const query = new URLSearchParams();
    for (const [key, value] of Object.entries(nextFilters)) {
      if (value !== '' && value !== null && value !== undefined) query.set(key, String(value));
    }
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const response = await fetch(`/api/billing-journey/analytics?${query.toString()}`, {
        credentials: 'include',
        cache: 'no-store',
        headers: { Accept: 'application/json' }
      });
      const data = await readJson(response);
      setState({ loading: false, error: '', data });
    } catch (error) {
      setState({
        loading: false,
        error: error instanceof Error ? error.message : 'Unable to load invoice analytics.',
        data: null
      });
    }
  }, [filters]);

  useEffect(() => {
    void runReport({
      projectId: selectedProjectId || '',
      invoiceType: '',
      status: '',
      dateFrom: '',
      dateTo: '',
      search: ''
    });
  }, []); // The first report is deliberately portfolio-wide.

  const rows = state.data?.rows ?? [];
  const summary = state.data?.summary ?? {};
  const statusOptions = useMemo(() => {
    const values = new Set(state.data?.options?.statuses ?? []);
    rows.forEach((row) => row.invoiceStatus && values.add(row.invoiceStatus));
    return [...values].sort((a, b) => String(a).localeCompare(String(b)));
  }, [state.data, rows]);

  function updateFilter(key, value) {
    setFilters((current) => ({ ...current, [key]: value }));
    if (key === 'projectId') onProjectChange?.(value);
  }

  function chooseInvoiceType(invoiceType) {
    const next = { ...filters, invoiceType };
    setFilters(next);
    void runReport(next);
  }

  return (
    <section className="billing-analytics" aria-labelledby="billing-analytics-title">
      <header className="billing-analytics__header">
        <div>
          <p className="billing-journey__eyebrow">Module 030 · governed financial reporting</p>
          <h2 id="billing-analytics-title">Partial &amp; Final Invoice Lifecycle</h2>
          <p>
            Review immutable invoice evidence by customer, project, billing period, type, status, and source amount.
            Partial invoices remain distinct from the final invoice and are never collapsed into a rewritten total.
          </p>
        </div>
        <div className="billing-analytics__header-actions">
          <button type="button" onClick={() => void runReport()} disabled={state.loading}>
            {state.loading ? 'Running…' : 'Run report'}
          </button>
          <button type="button" className="secondary" onClick={() => downloadCsv(rows)} disabled={!rows.length}>
            Download CSV
          </button>
        </div>
      </header>

      <div className="billing-analytics__quick-types" aria-label="Invoice type shortcuts">
        <button type="button" className={!filters.invoiceType ? 'active' : ''} onClick={() => chooseInvoiceType('')}>All invoices</button>
        <button type="button" className={filters.invoiceType === 'partial' ? 'active' : ''} onClick={() => chooseInvoiceType('partial')}>Partial invoices</button>
        <button type="button" className={filters.invoiceType === 'final' ? 'active' : ''} onClick={() => chooseInvoiceType('final')}>Final invoices</button>
      </div>

      <div className="billing-analytics__filters">
        <label>
          <span>Project</span>
          <select value={filters.projectId} onChange={(event) => updateFilter('projectId', event.target.value)}>
            <option value="">All accessible projects</option>
            {projects.map((project) => (
              <option key={project.projectId} value={project.projectId}>
                {project.customerName} — {project.projectCode || project.projectName}
              </option>
            ))}
          </select>
        </label>
        <label>
          <span>Status</span>
          <select value={filters.status} onChange={(event) => updateFilter('status', event.target.value)}>
            <option value="">All statuses</option>
            {statusOptions.map((status) => <option key={status} value={status}>{String(status).replaceAll('_', ' ')}</option>)}
          </select>
        </label>
        <label>
          <span>Date from</span>
          <input type="date" value={filters.dateFrom} onChange={(event) => updateFilter('dateFrom', event.target.value)} />
        </label>
        <label>
          <span>Date to</span>
          <input type="date" value={filters.dateTo} onChange={(event) => updateFilter('dateTo', event.target.value)} />
        </label>
        <label className="billing-analytics__search">
          <span>Search</span>
          <input
            type="search"
            value={filters.search}
            placeholder="Invoice, customer, project, PM, or PO"
            onChange={(event) => updateFilter('search', event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                event.preventDefault();
                void runReport();
              }
            }}
          />
        </label>
      </div>

      {state.error ? <div className="billing-analytics__error" role="alert">{state.error}</div> : null}

      <div className="billing-analytics__metrics" aria-label="Invoice report summary">
        <article><span>Partial invoices</span><strong>{summary.partialInvoiceCount ?? 0}</strong><small>{money(summary.partialInvoiceAmount)}</small></article>
        <article><span>Final invoices</span><strong>{summary.finalInvoiceCount ?? 0}</strong><small>{money(summary.finalInvoiceAmount)}</small></article>
        <article><span>Non-void billed</span><strong>{money(summary.totalNonVoidAmount)}</strong><small>{summary.nonVoidInvoiceCount ?? 0} invoice(s)</small></article>
        <article><span>Labor</span><strong>{money(summary.laborAmount)}</strong><small>Approved immutable source lines</small></article>
        <article><span>Expenses</span><strong>{money(summary.expenseAmount)}</strong><small>Governed pass-through lines</small></article>
        <article><span>Milestones</span><strong>{money(summary.milestoneAmount)}</strong><small>Fixed-price evidence packages</small></article>
      </div>

      <div className="billing-analytics__table-wrap">
        <table>
          <thead>
            <tr>
              <th>Invoice</th>
              <th>Type</th>
              <th>Status</th>
              <th>Customer / project</th>
              <th>Billing period</th>
              <th>Lines</th>
              <th>Labor</th>
              <th>Expenses</th>
              <th>Milestones</th>
              <th>Total</th>
              <th>Finalized</th>
              <th>Evidence</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.billingInvoiceId}>
                <td>
                  <button type="button" className="billing-analytics__invoice-link" onClick={() => onOpenProject?.(row.projectId)}>
                    {row.invoiceNumber}
                  </button>
                  <small>{row.purchaseOrderNumber || 'No PO number'}</small>
                </td>
                <td><span className={`billing-analytics__type ${row.invoiceType}`}>{row.invoiceType}</span></td>
                <td><span className={`billing-analytics__status ${row.invoiceStatus}`}>{String(row.invoiceStatus).replaceAll('_', ' ')}</span></td>
                <td><strong>{row.customerName || 'Customer not recorded'}</strong><small>{row.projectCode} · {row.projectName}</small></td>
                <td>{date(row.billingPeriodStart)} – {date(row.billingPeriodEnd)}</td>
                <td>{row.lineCount}</td>
                <td>{money(row.laborAmount)}</td>
                <td>{money(row.expenseAmount)}</td>
                <td>{money(row.milestoneAmount)}</td>
                <td><strong>{money(row.totalAmount)}</strong></td>
                <td>{dateTime(row.finalizedAt || row.createdAt)}</td>
                <td>{row.immutableSnapshotAvailable ? 'Immutable snapshot' : 'Snapshot unavailable'}</td>
              </tr>
            ))}
            {!state.loading && !rows.length ? (
              <tr><td colSpan="12"><div className="billing-analytics__empty">No partial or final invoices match the current report criteria.</div></td></tr>
            ) : null}
          </tbody>
        </table>
      </div>

      <footer className="billing-analytics__footer">
        <span>Generated {dateTime(state.data?.generatedAt)}</span>
        <span>{rows.length} invoice row(s)</span>
        <span>Invoice snapshots and audit events remain append-only.</span>
      </footer>
    </section>
  );
}
