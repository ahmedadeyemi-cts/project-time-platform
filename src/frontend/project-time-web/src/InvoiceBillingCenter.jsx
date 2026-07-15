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

function text(value, fallback = '') {
  const result = String(value ?? '').trim();
  return result || fallback;
}

function formatMoney(value) {
  const number = Number(value);
  if (!Number.isFinite(number)) return 'Not calculated';
  return number.toLocaleString(undefined, { style: 'currency', currency: 'USD' });
}

function formatHours(value) {
  const number = Number(value);
  if (!Number.isFinite(number)) return '0.00';
  return number.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function formatDate(value) {
  if (!value) return 'Not configured';
  const date = new Date(`${String(value).slice(0, 10)}T00:00:00`);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleDateString();
}

function formatDateTime(value) {
  if (!value) return 'Not available';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
}

async function fetchJson(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: {
      Accept: 'application/json',
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...(options.headers || {})
    }
  });

  const raw = await response.text();
  let parsed = null;

  try {
    parsed = raw ? JSON.parse(raw) : null;
  } catch {
    parsed = null;
  }

  if (!response.ok) {
    const detail = parsed?.message || parsed?.detail || parsed?.status || raw || `HTTP ${response.status}`;
    const error = new Error(`${path} returned HTTP ${response.status}: ${detail}`);
    error.payload = parsed;
    throw error;
  }

  return parsed;
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

function lineSelectionKey(projectId, timeEntryId) {
  return `${projectId}:${timeEntryId}`;
}

function initializeSelections(candidates, current) {
  const next = { ...current };

  candidates.forEach((candidate) => {
    (candidate.lines || []).forEach((line) => {
      const key = lineSelectionKey(candidate.projectId, line.timeEntryId);
      if (next[key]) return;

      const suggested = text(line.suggestedRateLineId);
      next[key] = {
        selected: Boolean(suggested),
        rateLineId: suggested
      };
    });
  });

  return next;
}

function selectedLineDetails(candidate, selections) {
  return (candidate?.lines || []).map((line) => {
    const key = lineSelectionKey(candidate.projectId, line.timeEntryId);
    const selection = selections[key] || { selected: false, rateLineId: '' };
    const rate = (line.rateOptions || []).find((option) => option.rateLineId === selection.rateLineId) || null;
    const amount = rate ? Number(line.approvedHours || 0) * Number(rate.unitRate || 0) : null;

    return {
      line,
      key,
      selected: selection.selected,
      rateLineId: selection.rateLineId,
      rate,
      amount
    };
  });
}

function candidateSelectedAmount(candidate, selections) {
  const rows = selectedLineDetails(candidate, selections).filter((item) => item.selected && item.rate);
  if (!rows.length) return null;
  return rows.reduce((total, item) => total + Number(item.amount || 0), 0);
}

function candidateCellValue(candidate, columnKey, selections) {
  if (columnKey === 'projectCode') {
    return (
      <span className="m042-stack">
        <strong>{text(candidate.projectCode, missingValue)}</strong>
        <small>{candidate.invoiceHistory?.length ? `${candidate.invoiceHistory.length} invoice(s)` : 'Invoice not created'}</small>
      </span>
    );
  }

  if (columnKey === 'customer') return text(candidate.customerName, missingValue);
  if (columnKey === 'project') return text(candidate.projectName, 'Unnamed project');
  if (columnKey === 'workType') return text(candidate.workType, missingValue);
  if (columnKey === 'billingModel') return <span className="m042-pill blue">{text(candidate.contractType, missingValue)}</span>;
  if (columnKey === 'status') return <span className="m042-pill amber">{text(candidate.status, missingValue)}</span>;
  if (columnKey === 'projectManager') return text(candidate.projectManagerName, 'Not assigned');
  if (columnKey === 'coordinator') return text(candidate.projectCoordinatorName, 'Not assigned');
  if (columnKey === 'assignedEngineers') return candidate.assignedEngineers?.length ? candidate.assignedEngineers.join(', ') : 'Not assigned';
  if (columnKey === 'certiniaId') return text(candidate.certiniaId, missingValue);
  if (columnKey === 'sellQuoteId') return text(candidate.sellQuoteNumber, missingValue);
  if (columnKey === 'salesforceId') return text(candidate.salesforceId, missingValue);

  if (columnKey === 'purchaseOrder') {
    if (candidate.purchaseOrder?.poNumber) return candidate.purchaseOrder.poNumber;
    return candidate.purchaseOrderRequired ? 'Missing required PO' : 'Not required';
  }

  if (columnKey === 'approvedLines') return String(candidate.approvedLineCount ?? 0);
  if (columnKey === 'approvedHours') return formatHours(candidate.approvedHours);

  if (columnKey === 'effectiveRate') {
    const status = candidate.rateResolutionStatus;
    if (status === 'resolved') return 'Stored rate resolved';
    if (status === 'selection_required') return 'Selection required';
    if (status === 'missing_rate') return 'Missing stored rate';
    return 'No eligible time';
  }

  if (columnKey === 'candidateAmount') {
    const selectedAmount = candidateSelectedAmount(candidate, selections);
    if (selectedAmount !== null) return formatMoney(selectedAmount);
    return candidate.autoCalculatedAmount === null || candidate.autoCalculatedAmount === undefined
      ? 'Select invoice lines'
      : formatMoney(candidate.autoCalculatedAmount);
  }

  return missingValue;
}

export default function InvoiceBillingCenter({ usSignalLogoUrl, userKey }) {
  const [view, setView] = useState('queue');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('All');
  const [model, setModel] = useState('All');
  const [visibleColumns, setVisibleColumns] = useState(() => readColumns(userKey));
  const [selectedId, setSelectedId] = useState('');
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [selections, setSelections] = useState({});
  const [payload, setPayload] = useState({
    loading: true,
    error: '',
    candidates: [],
    connectorStatuses: [],
    canCreateInvoices: false,
    scope: ''
  });
  const [action, setAction] = useState({ running: false, error: '', success: '' });
  const [invoiceDetail, setInvoiceDetail] = useState(null);
  const [invoiceDetailLoading, setInvoiceDetailLoading] = useState(false);
  const [invoiceDetailError, setInvoiceDetailError] = useState('');
  const [showCustomerResourceNames, setShowCustomerResourceNames] = useState(false);
  const [certiniaPreview, setCertiniaPreview] = useState('');

  async function loadLiveData(preferredProjectId = '') {
    setPayload((current) => ({ ...current, loading: true, error: '' }));

    try {
      const result = await fetchJson('/api/billing/candidates');
      const candidates = Array.isArray(result?.candidates) ? result.candidates : [];

      setPayload({
        loading: false,
        error: '',
        candidates,
        connectorStatuses: Array.isArray(result?.connectorStatuses) ? result.connectorStatuses : [],
        canCreateInvoices: result?.canCreateInvoices === true,
        scope: text(result?.scope)
      });

      setSelections((current) => initializeSelections(candidates, current));
      setSelectedId((current) => (
        candidates.some((candidate) => candidate.projectId === preferredProjectId)
          ? preferredProjectId
          : candidates.some((candidate) => candidate.projectId === current)
            ? current
            : candidates[0]?.projectId || ''
      ));
    } catch (error) {
      setPayload((current) => ({
        ...current,
        loading: false,
        error: error instanceof Error ? error.message : 'Unable to load billing candidates.',
        candidates: []
      }));
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
  const selected = candidates.find((candidate) => candidate.projectId === selectedId) || candidates[0] || null;
  const visibleDefinitions = columns.filter((column) => visibleColumns.includes(column.key));
  const groups = [...new Set(columns.map((column) => column.group))];
  const statusOptions = [...new Set(candidates.map((candidate) => candidate.status).filter(Boolean))].sort();
  const modelOptions = [...new Set(candidates.map((candidate) => candidate.contractType).filter(Boolean))].sort();

  const filtered = useMemo(() => {
    const needle = search.trim().toLowerCase();

    return candidates.filter((candidate) => {
      const normalizedStatus = text(candidate.status).toLowerCase();
      const closed = ['closed', 'completed', 'complete', 'archived', 'cancelled', 'canceled']
        .some((value) => normalizedStatus.includes(value));

      if (view === 'closed' && !closed) return false;
      if (view === 'queue' && closed) return false;
      if (statusFilter !== 'All' && candidate.status !== statusFilter) return false;
      if (model !== 'All' && candidate.contractType !== model) return false;
      if (!needle) return true;

      return [
        candidate.customerName,
        candidate.projectName,
        candidate.projectCode,
        candidate.workType,
        candidate.contractType,
        candidate.projectManagerName,
        candidate.projectCoordinatorName,
        candidate.certiniaId,
        candidate.sellQuoteNumber,
        candidate.salesforceId,
        ...(candidate.assignedEngineers || [])
      ].join(' ').toLowerCase().includes(needle);
    });
  }, [candidates, model, search, statusFilter, view]);

  const selectedRows = selectedLineDetails(selected, selections);
  const selectedReadyRows = selectedRows.filter((item) => item.selected && item.rate);
  const selectedIncompleteRows = selectedRows.filter((item) => item.selected && !item.rate);
  const currentAmount = selectedReadyRows.reduce((total, item) => total + Number(item.amount || 0), 0);
  const allEligibleLinesSelected = selectedRows.length > 0
    && selectedRows.every((item) => item.selected && item.rate);
  const projectAllowsInvoice = selected?.canCreateInvoice === true;
  const userAllowsInvoice = payload.canCreateInvoices === true && selected?.currentUserCanCreateInvoices === true;
  const partialReady = projectAllowsInvoice && userAllowsInvoice && selectedReadyRows.length > 0 && selectedIncompleteRows.length === 0;
  const finalReady = partialReady && allEligibleLinesSelected;

  const invoiceCount = candidates.reduce((total, candidate) => total + (candidate.invoiceHistory?.length || 0), 0);
  const approvedLineCount = candidates.reduce((total, candidate) => total + Number(candidate.approvedLineCount || 0), 0);
  const approvedHours = candidates.reduce((total, candidate) => total + Number(candidate.approvedHours || 0), 0);
  const configuredConnectors = payload.connectorStatuses.filter((connector) => connector.connectionStatus === 'connected').length;

  function updateSelection(line, patch) {
    if (!selected) return;
    const key = lineSelectionKey(selected.projectId, line.timeEntryId);

    setSelections((current) => ({
      ...current,
      [key]: {
        selected: current[key]?.selected || false,
        rateLineId: current[key]?.rateLineId || '',
        ...patch
      }
    }));
  }

  async function createInvoice(invoiceType) {
    if (!selected) return;

    const rows = selectedLineDetails(selected, selections)
      .filter((item) => item.selected && item.rate);

    setAction({ running: true, error: '', success: '' });

    try {
      const result = await fetchJson(`/api/billing/projects/${selected.projectId}/invoices`, {
        method: 'POST',
        body: JSON.stringify({
          invoiceType,
          lines: rows.map((item) => ({
            timeEntryId: item.line.timeEntryId,
            rateLineId: item.rate.rateLineId
          })),
          notes: ''
        })
      });

      const invoiceNumber = result?.invoice?.header?.invoiceNumber || 'Invoice';
      setAction({
        running: false,
        error: '',
        success: `${invoiceNumber} was created from ${rows.length} verified approved time entr${rows.length === 1 ? 'y' : 'ies'}.`
      });

      await loadLiveData(selected.projectId);
    } catch (error) {
      const blockers = Array.isArray(error?.payload?.blockers)
        ? ` ${error.payload.blockers.join(' ')}`
        : '';

      setAction({
        running: false,
        error: `${error instanceof Error ? error.message : 'Unable to create invoice.'}${blockers}`,
        success: ''
      });
    }
  }

  async function loadInvoiceDetail(invoice) {
    if (!invoice?.billingInvoiceId) return;

    setInvoiceDetailLoading(true);
    setInvoiceDetailError('');
    setCertiniaPreview('');

    try {
      const result = await fetchJson(`/api/billing/invoices/${invoice.billingInvoiceId}`);
      setInvoiceDetail(result?.invoice || null);
    } catch (error) {
      setInvoiceDetail(null);
      setInvoiceDetailError(error instanceof Error ? error.message : 'Unable to load invoice details.');
    } finally {
      setInvoiceDetailLoading(false);
    }
  }

  function customerResourceLabel(line) {
    if (showCustomerResourceNames) return text(line?.resourceName, 'Professional Services Engineer');

    const labor = text(line?.laborCategory).toLowerCase();
    const task = `${text(line?.taskCode)} ${text(line?.taskName)}`.toLowerCase();

    if (labor.includes('project') || task.includes('project management') || task.includes('coordination')) {
      return 'Project Management';
    }

    return 'Professional Services Engineer';
  }

  function csvEscape(value) {
    const normalized = String(value ?? '');
    return `"${normalized.replaceAll('"', '""')}"`;
  }

  function downloadInvoiceCsv() {
    if (!invoiceDetail?.header || !invoiceDetail?.lines?.length) return;

    const header = invoiceDetail.header;
    const rows = [
      ['Invoice Number','Customer','Project Code','Project','PO Number','Work Date','Resource','Task Code','Task','Time Entry Description','Hours','Rate','Amount'],
      ...invoiceDetail.lines.map((line) => [
        header.invoiceNumber,
        header.customerName,
        header.projectCode,
        header.projectName,
        header.purchaseOrderNumber,
        line.workDate,
        customerResourceLabel(line),
        line.taskCode,
        line.taskName,
        line.description,
        line.approvedHours,
        line.unitRate,
        line.lineAmount
      ])
    ];

    const csv = rows.map((row) => row.map(csvEscape).join(',')).join('\r\n');
    const blob = new Blob([`\ufeff${csv}`], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `${header.invoiceNumber || 'invoice'}-detail.csv`;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  }

  function printInvoicePdf() {
    if (!invoiceDetail?.header) return;

    const header = invoiceDetail.header;
    const rows = (invoiceDetail.lines || []).map((line) => `
      <tr>
        <td>${formatDate(line.workDate)}</td>
        <td>${customerResourceLabel(line)}</td>
        <td><strong>${text(line.taskCode)}</strong> ${text(line.taskName)}<br><small>${text(line.description, 'No submitted description')}</small></td>
        <td class="number">${formatHours(line.approvedHours)}</td>
        <td class="number">${formatMoney(line.unitRate)}</td>
        <td class="number">${formatMoney(line.lineAmount)}</td>
      </tr>
    `).join('');

    const popup = window.open('', '_blank', 'noopener,noreferrer,width=1200,height=900');
    if (!popup) {
      setInvoiceDetailError('The browser blocked the print window. Allow pop-ups and try again.');
      return;
    }

    popup.document.write(`<!doctype html>
      <html>
        <head>
          <meta charset="utf-8">
          <title>${header.invoiceNumber}</title>
          <style>
            body { font-family: Arial, sans-serif; color: #142033; margin: 32px; }
            header { display:flex; justify-content:space-between; gap:24px; border-bottom:3px solid #0067a8; padding-bottom:18px; }
            h1 { margin:0; font-size:28px; }
            .meta { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:10px 28px; margin:24px 0; }
            .meta div { border-bottom:1px solid #d7deea; padding-bottom:8px; }
            .meta span { display:block; color:#5e7190; font-size:11px; text-transform:uppercase; font-weight:700; }
            table { width:100%; border-collapse:collapse; font-size:12px; }
            th { background:#eef6fb; text-align:left; padding:9px; border:1px solid #d7deea; }
            td { vertical-align:top; padding:9px; border:1px solid #d7deea; }
            td.number { text-align:right; white-space:nowrap; }
            small { color:#536b8f; line-height:1.4; }
            .total { margin-top:18px; text-align:right; font-size:18px; font-weight:700; }
            .notice { margin-top:18px; color:#536b8f; font-size:11px; }
            @page { size: landscape; margin: 0.45in; }
          </style>
        </head>
        <body>
          <header>
            <div><h1>Invoice ${header.invoiceNumber}</h1><p>${text(header.customerName)}</p></div>
            <div><strong>${text(header.invoiceType).toUpperCase()} INVOICE</strong><br>${formatDate(header.invoiceDate)}</div>
          </header>
          <section class="meta">
            <div><span>Project</span><strong>${text(header.projectCode)} — ${text(header.projectName)}</strong></div>
            <div><span>Purchase Order</span><strong>${text(header.purchaseOrderNumber, 'Not configured')}</strong></div>
            <div><span>Billing Period</span><strong>${formatDate(header.billingPeriodStart)} – ${formatDate(header.billingPeriodEnd)}</strong></div>
            <div><span>Certinia ID</span><strong>${text(header.certiniaId, 'Not configured')}</strong></div>
            <div><span>SELL Quote</span><strong>${text(header.sellQuote, 'Not configured')}</strong></div>
            <div><span>Salesforce ID</span><strong>${text(header.salesforceId, 'Not configured')}</strong></div>
          </section>
          <table>
            <thead><tr><th>Date</th><th>Resource</th><th>Task and time-entry detail</th><th>Hours</th><th>Rate</th><th>Amount</th></tr></thead>
            <tbody>${rows}</tbody>
          </table>
          <div class="total">Invoice total: ${formatMoney(header.totalAmount)}</div>
          <div class="notice">Generated from the immutable ProjectPulse invoice snapshot. Use the browser Print dialog and choose Save as PDF.</div>
          <script>window.addEventListener('load', () => window.print());<\/script>
        </body>
      </html>`);
    popup.document.close();
  }

  function previewCertiniaPayload() {
    if (!invoiceDetail?.header) return;

    const header = invoiceDetail.header;
    const preview = {
      transmissionMode: 'preview_only',
      connectorStatus: 'not_configured',
      invoice: {
        invoiceNumber: header.invoiceNumber,
        invoiceType: header.invoiceType,
        invoiceDate: header.invoiceDate,
        customerName: header.customerName,
        projectCode: header.projectCode,
        projectName: header.projectName,
        purchaseOrderNumber: header.purchaseOrderNumber,
        certiniaId: header.certiniaId,
        salesforceId: header.salesforceId,
        sellQuote: header.sellQuote,
        subtotalAmount: header.subtotalAmount,
        totalAmount: header.totalAmount,
        lines: (invoiceDetail.lines || []).map((line) => ({
          lineNumber: line.lineNumber,
          workDate: line.workDate,
          resource: customerResourceLabel(line),
          taskCode: line.taskCode,
          taskName: line.taskName,
          description: line.description,
          hours: line.approvedHours,
          rateCode: line.rateCode,
          unitRate: line.unitRate,
          amount: line.lineAmount
        }))
      }
    };

    setCertiniaPreview(JSON.stringify(preview, null, 2));
  }

  const certiniaConnector = payload.connectorStatuses.find((connector) =>
    text(connector.systemCode).toLowerCase().includes('certinia')
    || text(connector.displayName).toLowerCase().includes('certinia'));

  const certiniaConnected = certiniaConnector?.connectionStatus === 'connected'
    && certiniaConnector?.outboundEnabled === true;

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
          <p>
            Review approved uninvoiced time, explicitly select effective stored rates, and create immutable partial or final invoices.
            No customer, project, rate, hour, purchase order, amount, or invoice number is fabricated.
          </p>
        </div>
        <div className="m042-actions">
          <button type="button" className="secondary-action" onClick={() => setDrawerOpen(true)}>Customize columns</button>
          <button type="button" className="secondary-action" onClick={() => void loadLiveData(selected?.projectId)}>Reload billing data</button>
          <button type="button" className="secondary-action" disabled title="Excel export remains outside the demo slice.">Export Excel</button>
          <button type="button" className="primary-action" disabled title="PDF export remains outside the demo slice.">Export PDF</button>
        </div>
      </header>

      <section className="m042-preview-mode m042-live-mode" aria-label="Module 042 live data status">
        <strong>Live billing contract</strong>
        <span>
          Approved time comes from the time-entry workflow, rates come from active effective rate cards, purchase orders come from the billing ledger,
          and invoice numbers are allocated by PostgreSQL. Current scope: {payload.scope || 'Loading'}.
        </span>
      </section>

      {payload.error ? <div className="m042-notice m042-error" role="alert">{payload.error}</div> : null}
      {action.error ? <div className="m042-notice m042-error" role="alert">{action.error}</div> : null}
      {action.success ? <div className="m042-notice" role="status">{action.success}</div> : null}

      <section className="m042-workflow-explainer" aria-label="Billing workflow">
        <article><span>1</span><div><strong>Approved system time</strong><small>Only invoice-eligible, uninvoiced entries are shown.</small></div></article>
        <b aria-hidden="true">→</b>
        <article><span>2</span><div><strong>Stored rate selection</strong><small>Ambiguous rates require an explicit user choice.</small></div></article>
        <b aria-hidden="true">→</b>
        <article><span>3</span><div><strong>Immutable invoice</strong><small>Creates PHD-XXXXXX-N history without rewriting source time.</small></div></article>
      </section>

      <section className="m042-metrics" aria-label="Live billing summary">
        <article><span>Accessible projects</span><strong>{candidates.length}</strong><small>Current role and project scope</small></article>
        <article><span>Approved lines</span><strong>{approvedLineCount}</strong><small>Uninvoiced and billable</small></article>
        <article><span>Approved hours</span><strong>{formatHours(approvedHours)}</strong><small>System time only</small></article>
        <article><span>Invoice history</span><strong>{invoiceCount}</strong><small>Immutable ledger records</small></article>
      </section>

      <nav className="m042-tabs" aria-label="Invoice Center views">
        <button type="button" className={view === 'queue' ? 'active' : ''} onClick={() => setView('queue')}>Active billing candidates</button>
        <button type="button" className={view === 'closed' ? 'active' : ''} onClick={() => setView('closed')}>Recently closed</button>
        <button type="button" className={view === 'reports' ? 'active' : ''} onClick={() => setView('reports')}>History &amp; integrations</button>
      </nav>

      {view === 'reports' ? (
        <section className="m042-reports">
          <article>
            <span>Invoice history</span>
            <h2>Partial and Final Invoices</h2>
            <p>Counts and totals below come only from the immutable Module 042 invoice ledger.</p>
            <dl>
              <div><dt>Invoices</dt><dd>{invoiceCount}</dd></div>
              <div><dt>Projects represented</dt><dd>{candidates.filter((candidate) => candidate.invoiceHistory?.length).length}</dd></div>
            </dl>
          </article>
          <article>
            <span>Stored integrations</span>
            <h2>Connector Readiness</h2>
            <p>Salesforce, Certinia, and SELL remain independent connector registrations. No connector action runs from this page.</p>
            <dl>
              <div><dt>Connected</dt><dd>{configuredConnectors}</dd></div>
              <div><dt>Registered</dt><dd>{payload.connectorStatuses.length}</dd></div>
            </dl>
          </article>
          <article>
            <span>Connector detail</span>
            <h2>Current Status</h2>
            <dl>
              {payload.connectorStatuses.map((connector) => (
                <div key={connector.systemCode}>
                  <dt>{connector.displayName}</dt>
                  <dd>{connector.connectionStatus}</dd>
                </div>
              ))}
              {!payload.connectorStatuses.length ? <div><dt>Connectors</dt><dd>Not available</dd></div> : null}
            </dl>
          </article>
        </section>
      ) : (
        <>
          <section className="m042-toolbar">
            <label>
              <span>Search system projects</span>
              <input
                type="search"
                value={search}
                placeholder="Customer, project, code, PM, PTC, engineer, Certinia, SELL, Salesforce..."
                onChange={(event) => setSearch(event.target.value)}
              />
            </label>
            <label>
              <span>Status</span>
              <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
                <option>All</option>
                {statusOptions.map((value) => <option value={value} key={value}>{value}</option>)}
              </select>
            </label>
            <label>
              <span>Contract type</span>
              <select value={model} onChange={(event) => setModel(event.target.value)}>
                <option>All</option>
                {modelOptions.map((value) => <option value={value} key={value}>{value}</option>)}
              </select>
            </label>
            <div className="m042-column-count"><strong>{visibleColumns.length}</strong><small>columns shown</small></div>
          </section>

          <section className="m042-workspace">
            <div className="m042-card">
              <header className="m042-card-head">
                <div>
                  <h2>{view === 'closed' ? 'Recently closed project billing candidates' : 'Active project billing candidates'}</h2>
                  <p>Every row is loaded from the shared server-side billing contract.</p>
                </div>
                <span>{filtered.length} shown</span>
              </header>
              <div className="m042-table-wrap">
                <table>
                  <thead>
                    <tr>{visibleDefinitions.map((column) => <th key={column.key}>{column.label}</th>)}</tr>
                  </thead>
                  <tbody>
                    {filtered.map((candidate) => (
                      <tr
                        key={candidate.projectId}
                        className={selected?.projectId === candidate.projectId ? 'selected' : ''}
                        onClick={() => {
                          setSelectedId(candidate.projectId);
                          setAction({ running: false, error: '', success: '' });
                        }}
                      >
                        {visibleDefinitions.map((column) => (
                          <td key={`${candidate.projectId}-${column.key}`}>
                            {candidateCellValue(candidate, column.key, selections)}
                          </td>
                        ))}
                      </tr>
                    ))}
                    {!payload.loading && filtered.length === 0 ? (
                      <tr>
                        <td colSpan={Math.max(1, visibleDefinitions.length)}>
                          <div className="m042-empty-state">
                            <strong>No matching billing candidates</strong>
                            <span>Adjust the filters or complete approved billable time in the source workflow.</span>
                          </div>
                        </td>
                      </tr>
                    ) : null}
                  </tbody>
                </table>
              </div>
            </div>

            <aside className="m042-card m042-preview">
              {payload.loading ? (
                <div className="m042-empty-state">
                  <strong>Loading billing candidates…</strong>
                  <span>No placeholder records will be displayed.</span>
                </div>
              ) : selected ? (
                <div className="m042-invoice">
                  <header className="m042-invoice-head">
                    <div className="m042-brand">
                      {usSignalLogoUrl ? <img src={usSignalLogoUrl} alt="US Signal" /> : <strong>US Signal</strong>}
                      <span>Invoice candidate review</span>
                    </div>
                    <div>
                      <span>{selected.invoiceHistory?.[0]?.invoiceNumber || 'Invoice not created'}</span>
                      <strong>{text(selected.projectCode, missingValue)}</strong>
                      <small>{text(selected.status, missingValue)}</small>
                    </div>
                  </header>

                  <section className="m042-identities">
                    <div><span>Customer</span><strong>{text(selected.customerName, missingValue)}</strong><small>Work Register</small></div>
                    <div><span>Project</span><strong>{text(selected.projectName, 'Unnamed project')}</strong><small>{text(selected.workType, missingValue)} · {text(selected.contractType, missingValue)}</small></div>
                    <div><span>Ownership</span><strong>{text(selected.projectManagerName, 'Not assigned')}</strong><small>PTC: {text(selected.projectCoordinatorName, 'Not assigned')}</small></div>
                  </section>

                  <section className="m042-refs">
                    <div><span>Certinia ID</span><strong>{text(selected.certiniaId, missingValue)}</strong></div>
                    <div><span>SELL Quote</span><strong>{text(selected.sellQuoteNumber, missingValue)}</strong></div>
                    <div><span>Salesforce ID</span><strong>{text(selected.salesforceId, missingValue)}</strong></div>
                    <div>
                      <span>Purchase order</span>
                      <strong>{selected.purchaseOrder?.poNumber || (selected.purchaseOrderRequired ? 'Missing required PO' : 'Not required')}</strong>
                      <small>{selected.purchaseOrder?.authorizedAmount == null ? '' : formatMoney(selected.purchaseOrder.authorizedAmount)}</small>
                    </div>
                  </section>

                  <section className="m042-resource-list">
                    <span>Assigned engineers</span>
                    <div>
                      {selected.assignedEngineers?.length
                        ? selected.assignedEngineers.map((engineer) => <strong key={engineer}>{engineer}</strong>)
                        : <em>Not assigned</em>}
                    </div>
                  </section>

                  <div className="m042-lines">
                    <table>
                      <thead>
                        <tr>
                          <th aria-label="Select line">Use</th>
                          <th>Date</th>
                          <th>Engineer / PM and work detail</th>
                          <th>Hours</th>
                          <th>Stored rate</th>
                          <th>Amount</th>
                        </tr>
                      </thead>
                      <tbody>
                        {selectedRows.map((item) => (
                          <tr key={item.line.timeEntryId}>
                            <td>
                              <input
                                type="checkbox"
                                checked={item.selected}
                                disabled={!item.line.rateOptions?.length}
                                onChange={(event) => updateSelection(item.line, { selected: event.target.checked })}
                                aria-label={`Select ${item.line.resourceName} on ${formatDate(item.line.workDate)}`}
                              />
                            </td>
                            <td>{formatDate(item.line.workDate)}</td>
                            <td>
                              <span className="m042-stack">
                                <strong>{text(item.line.resourceName, 'Unknown resource')}</strong>
                                <small>{text(item.line.taskCode)} {text(item.line.taskName)}</small>
                                <small>{text(item.line.description, 'No submitted description')}</small>
                                <small>{item.line.approvalStatus} · {item.line.timeType}</small>
                                {item.line.rateBlocker ? <small>{item.line.rateBlocker}</small> : null}
                              </span>
                            </td>
                            <td>{formatHours(item.line.approvedHours)}</td>
                            <td>
                              <select
                                value={item.rateLineId}
                                disabled={!item.line.rateOptions?.length}
                                onChange={(event) => updateSelection(item.line, {
                                  selected: Boolean(event.target.value),
                                  rateLineId: event.target.value
                                })}
                                aria-label={`Stored rate for ${item.line.resourceName}`}
                                style={{ maxWidth: '18rem' }}
                              >
                                <option value="">Select stored rate</option>
                                {(item.line.rateOptions || []).map((rate) => (
                                  <option value={rate.rateLineId} key={rate.rateLineId}>
                                    {rate.rateCardName} · {rate.displayName} · {formatMoney(rate.unitRate)}
                                  </option>
                                ))}
                              </select>
                            </td>
                            <td>{item.amount === null ? 'Not calculated' : formatMoney(item.amount)}</td>
                          </tr>
                        ))}
                        {!selectedRows.length ? (
                          <tr>
                            <td colSpan="6">
                              <div className="m042-empty-state">
                                <strong>No approved uninvoiced lines</strong>
                                <span>Complete the approval workflow or select another project.</span>
                              </div>
                            </td>
                          </tr>
                        ) : null}
                      </tbody>
                    </table>
                  </div>

                  <section className="m042-data-quality">
                    <h3>Billing blockers</h3>
                    {selected.blockers?.length ? (
                      <ul>{selected.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}</ul>
                    ) : (
                      <p>No project-level blockers. Select eligible lines and stored rates.</p>
                    )}
                    {!userAllowsInvoice ? <p>The current role has read-only billing access.</p> : null}
                    {selectedIncompleteRows.length ? <p>Every selected line must have a stored rate.</p> : null}
                  </section>

                  <section className="m042-totals">
                    <div>
                      <span>Previously invoiced</span>
                      <strong>{formatMoney((selected.invoiceHistory || []).reduce((sum, invoice) => sum + Number(invoice.totalAmount || 0), 0))}</strong>
                    </div>
                    <div>
                      <span>Current selection</span>
                      <strong>{selectedReadyRows.length ? formatMoney(currentAmount) : 'Not calculated'}</strong>
                    </div>
                    <div>
                      <span>Selected lines</span>
                      <strong>{selectedReadyRows.length} of {selectedRows.length}</strong>
                    </div>
                  </section>

                  <footer className="m042-invoice-foot">
                    <div className="m042-actions">
                      <button
                        type="button"
                        className="secondary-action"
                        disabled={!partialReady || action.running}
                        onClick={() => void createInvoice('partial')}
                      >
                        {action.running ? 'Creating…' : 'Generate Partial Invoice'}
                      </button>
                      <button
                        type="button"
                        className="primary-action"
                        disabled={!finalReady || action.running}
                        onClick={() => void createInvoice('final')}
                        title={finalReady ? 'Create final invoice' : 'Final invoice requires every eligible line and rate.'}
                      >
                        {action.running ? 'Creating…' : 'Generate Final Invoice'}
                      </button>
                    </div>
                    <span>
                      The server revalidates approval, rate effectiveness, PO readiness, project access, and duplicate billing before committing an invoice number.
                    </span>
                  </footer>

                  <section className="m042-data-quality">
                    <h3>Invoice history</h3>
                    {selected.invoiceHistory?.length ? (
                      <div className="m042-table-wrap">
                        <table>
                          <thead><tr><th>Invoice</th><th>Type</th><th>Lines</th><th>Total</th><th>Finalized</th></tr></thead>
                          <tbody>
                            {selected.invoiceHistory.map((invoice) => (
                              <tr
                                key={invoice.billingInvoiceId}
                                className="m042-history-row"
                                onClick={() => void loadInvoiceDetail(invoice)}
                                title="Open immutable invoice details"
                              >
                                <td><button type="button" className="m042-link-button">{invoice.invoiceNumber}</button></td>
                                <td>{invoice.invoiceType}</td>
                                <td>{invoice.lineCount}</td>
                                <td>{formatMoney(invoice.totalAmount)}</td>
                                <td>{formatDateTime(invoice.finalizedAt || invoice.createdAt)}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    ) : <p>No invoice has been created for this project.</p>}
                  </section>

                  <section className="m042-data-quality m042-invoice-detail-panel" data-projectpulse-invoice-detail-tools="true">
                    <div className="m042-detail-heading">
                      <div>
                        <h3>Invoice detail, PDF, Excel, and Certinia preview</h3>
                        <p>Click an invoice number above to load its immutable billed lines.</p>
                      </div>
                      {invoiceDetail ? <strong>{invoiceDetail.header?.invoiceNumber}</strong> : null}
                    </div>

                    {invoiceDetailLoading ? <p>Loading immutable invoice details…</p> : null}
                    {invoiceDetailError ? <div className="m042-notice m042-error">{invoiceDetailError}</div> : null}

                    {invoiceDetail ? (
                      <>
                        <label className="m042-privacy-toggle">
                          <input
                            type="checkbox"
                            checked={showCustomerResourceNames}
                            onChange={(event) => setShowCustomerResourceNames(event.target.checked)}
                          />
                          Show engineer and PM/PC names on customer output
                        </label>
                        <p className="m042-muted">
                          Default is hidden. Internal audit records retain the original resource identity.
                        </p>

                        <div className="m042-actions m042-detail-actions">
                          <button type="button" className="primary-action" onClick={printInvoicePdf}>Print / Save PDF</button>
                          <button type="button" className="secondary-action" onClick={downloadInvoiceCsv}>Download Excel-compatible CSV</button>
                          <button type="button" className="secondary-action" onClick={previewCertiniaPayload}>Preview Certinia payload</button>
                          <button
                            type="button"
                            className="secondary-action"
                            disabled={!certiniaConnected}
                            title={certiniaConnected ? 'Certinia send requires the production transmission endpoint.' : 'Certinia connector is not configured for outbound transmission.'}
                          >
                            Send to Certinia
                          </button>
                        </div>

                        <div className="m042-certinia-status">
                          <strong>Certinia:</strong>{' '}
                          {certiniaConnected
                            ? 'Connector is marked connected. Production transmission remains disabled in this demo-safe web slice.'
                            : 'Connector not configured — transmission not performed.'}
                        </div>

                        <div className="m042-table-wrap">
                          <table>
                            <thead>
                              <tr><th>Date</th><th>Customer resource</th><th>Task and submitted time detail</th><th>Hours</th><th>Rate</th><th>Amount</th></tr>
                            </thead>
                            <tbody>
                              {(invoiceDetail.lines || []).map((line) => (
                                <tr key={line.billingInvoiceLineId}>
                                  <td>{formatDate(line.workDate)}</td>
                                  <td>{customerResourceLabel(line)}</td>
                                  <td>
                                    <span className="m042-stack">
                                      <strong>{text(line.taskCode)} {text(line.taskName)}</strong>
                                      <small>{text(line.description, 'No submitted description')}</small>
                                      <small>{text(line.timeType)} · {text(line.managerApprovalSnapshot)}</small>
                                    </span>
                                  </td>
                                  <td>{formatHours(line.approvedHours)}</td>
                                  <td>{formatMoney(line.unitRate)}</td>
                                  <td>{formatMoney(line.lineAmount)}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>

                        {certiniaPreview ? (
                          <div className="m042-certinia-preview">
                            <div className="m042-detail-heading">
                              <h3>Certinia payload preview</h3>
                              <button type="button" className="secondary-action" onClick={() => navigator.clipboard?.writeText(certiniaPreview)}>Copy JSON</button>
                            </div>
                            <pre>{certiniaPreview}</pre>
                          </div>
                        ) : null}
                      </>
                    ) : null}
                  </section>
                </div>
              ) : (
                <div className="m042-empty-state">
                  <strong>No accessible projects are available</strong>
                  <span>Module 042 remains empty until the current user can access Work Register projects.</span>
                </div>
              )}
            </aside>
          </section>
        </>
      )}

      {drawerOpen ? (
        <div className="m042-backdrop" onMouseDown={(event) => event.target === event.currentTarget && setDrawerOpen(false)}>
          <section className="m042-drawer" role="dialog" aria-modal="true" aria-label="Customize invoice headers">
            <header>
              <div>
                <p className="eyebrow">Personal table settings</p>
                <h2>Customize columns</h2>
                <p>Add or remove headers from your Invoice Center. The selection is saved for this user on this browser.</p>
              </div>
              <button type="button" aria-label="Close" onClick={() => setDrawerOpen(false)}>×</button>
            </header>
            <div className="m042-shortcuts">
              <button type="button" onClick={() => setVisibleColumns(columns.map((column) => column.key))}>Show all</button>
              <button type="button" onClick={() => setVisibleColumns(columns.filter((column) => column.group === 'Essential').map((column) => column.key))}>Essential only</button>
              <button type="button" onClick={() => setVisibleColumns(defaultColumns)}>Restore defaults</button>
            </div>
            <div className="m042-groups">
              {groups.map((group) => (
                <fieldset key={group}>
                  <legend>{group}</legend>
                  {columns.filter((column) => column.group === group).map((column) => (
                    <label key={column.key}>
                      <input
                        type="checkbox"
                        checked={visibleColumns.includes(column.key)}
                        onChange={() => toggleColumn(column.key)}
                      />
                      <span>{column.label}</span>
                    </label>
                  ))}
                </fieldset>
              ))}
            </div>
            <footer>
              <span>{visibleColumns.length} columns selected</span>
              <button type="button" className="primary-action" onClick={() => setDrawerOpen(false)}>Apply columns</button>
            </footer>
          </section>
        </div>
      ) : null}
    </div>
  );
}
