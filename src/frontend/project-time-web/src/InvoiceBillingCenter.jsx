import { useEffect, useMemo, useState } from 'react';

const columns = [
  ['invoiceNumber', 'Invoice', 'Essential', true],
  ['customer', 'Customer', 'Essential', true],
  ['project', 'Project', 'Essential', true],
  ['billingModel', 'Billing model', 'Essential', true],
  ['invoiceType', 'Invoice type', 'Essential', true],
  ['status', 'Status', 'Essential', true],
  ['projectManager', 'Project Manager', 'Ownership', true],
  ['coordinator', 'Project Team Coordinator', 'Ownership', false],
  ['billingPeriod', 'Billing period', 'Dates', true],
  ['certiniaId', 'Certinia ID', 'External IDs', true],
  ['sellQuoteId', 'SELL Quote', 'External IDs', false],
  ['salesforceId', 'Salesforce ID', 'External IDs', false],
  ['purchaseOrder', 'Purchase order', 'External IDs', false],
  ['invoiceHours', 'Invoice hours', 'Financial', true],
  ['hourlyRate', 'Hourly rate', 'Financial', true],
  ['currentAmount', 'Current amount', 'Financial', true],
  ['previouslyInvoiced', 'Previously invoiced', 'Financial', false],
  ['remainingBalance', 'Remaining balance', 'Financial', true]
].map(([key, label, group, defaultVisible]) => ({ key, label, group, defaultVisible }));

const invoices = [
  {
    id: 'inv-001', invoiceNumber: 'DRAFT-2026-0714-001', customer: 'Northwind Health Systems',
    project: 'Contact Center Modernization', projectCode: 'PRJ-260184', billingModel: 'T&M',
    invoiceType: 'Partial', status: 'Ready for PM Review', projectManager: 'Morgan Ellis',
    coordinator: 'Taylor Brooks', billingPeriod: 'July 1–15, 2026', certiniaId: 'CERT-004821',
    sellQuoteId: 'SELL-38192', salesforceId: 'SF-OPP-209384', purchaseOrder: 'PO-882174',
    invoiceHours: 38.5, hourlyRate: 195, currentAmount: 7507.5, previouslyInvoiced: 23400,
    remainingBalance: 19092.5, closed: false,
    lines: [
      ['07/02/2026', 'Alex Johnson', 'Discovery and call-flow review', 'Reviewed current call routing, documented business-hours and after-hours behavior, and validated escalation requirements with the customer project team.', 6, 195],
      ['07/06/2026', 'Alex Johnson', 'Solution design', 'Developed the contact center routing design, queue strategy, voicemail treatment, and implementation sequence.', 8.5, 195],
      ['07/08/2026', 'Priya Shah', 'Configuration and validation', 'Configured test queues and agent profiles, validated call delivery, and documented customer acceptance results.', 12, 195],
      ['07/11/2026', 'Alex Johnson', 'Implementation support', 'Supported production implementation, monitored routing behavior, and corrected two configuration discrepancies during cutover.', 12, 195]
    ]
  },
  {
    id: 'inv-002', invoiceNumber: 'FINAL-2026-0714-002', customer: 'Summit Regional Bank',
    project: 'Identity and Endpoint Security', projectCode: 'PRJ-260127', billingModel: 'Fixed Price',
    invoiceType: 'Final', status: 'Ready for Coordinator', projectManager: 'Jordan Lee',
    coordinator: 'Taylor Brooks', billingPeriod: 'Project completion', certiniaId: 'CERT-004704',
    sellQuoteId: 'SELL-37760', salesforceId: 'SF-OPP-207915', purchaseOrder: 'PO-119047',
    invoiceHours: 286, hourlyRate: null, currentAmount: 18500, previouslyInvoiced: 55500,
    remainingBalance: 0, closed: true,
    lines: [
      ['Final milestone', 'Project Delivery Team', 'Production completion', 'Final fixed-price milestone for production completion, administrative closeout, customer documentation, and acceptance.', 1, 18500]
    ]
  },
  {
    id: 'inv-003', invoiceNumber: 'DRAFT-2026-0714-003', customer: 'Great Lakes Manufacturing',
    project: 'IQS Network Assessment', projectCode: 'IQS-260091', billingModel: 'IQS',
    invoiceType: 'Partial', status: 'Draft', projectManager: 'Cameron Reed', coordinator: 'Sydney Parker',
    billingPeriod: 'July 2026', certiniaId: 'CERT-004889', sellQuoteId: 'SELL-38461',
    salesforceId: 'SF-OPP-210466', purchaseOrder: 'PO-551039', invoiceHours: 18,
    hourlyRate: 215, currentAmount: 3870, previouslyInvoiced: 0, remainingBalance: 4730,
    closed: false,
    lines: [
      ['07/07/2026', 'Riley Anderson', 'IQS technical assessment', 'Reviewed switching, routing, wireless, and network-management configuration and documented key operational and security findings.', 10, 215],
      ['07/09/2026', 'Riley Anderson', 'Findings presentation', 'Prepared technical findings, risk summary, prioritized remediation recommendations, and the customer presentation.', 8, 215]
    ]
  },
  {
    id: 'inv-004', invoiceNumber: 'SR-2026-0714-004', customer: 'River Valley Utilities',
    project: 'Collaboration Support Request', projectCode: 'SR-260478', billingModel: 'Service Request',
    invoiceType: 'Final', status: 'Recently Closed', projectManager: 'Morgan Ellis',
    coordinator: 'Taylor Brooks', billingPeriod: 'July 2026', certiniaId: 'CERT-004913',
    sellQuoteId: 'SELL-38504', salesforceId: 'SF-CASE-770321', purchaseOrder: 'PO-774205',
    invoiceHours: 7.5, hourlyRate: 225, currentAmount: 1687.5, previouslyInvoiced: 0,
    remainingBalance: 0, closed: true,
    lines: [
      ['07/13/2026', 'Jamie Martinez', 'Service Request resolution', 'Investigated intermittent calling failures, corrected the affected trunk configuration, validated inbound and outbound calling, and provided closure notes.', 7.5, 225]
    ]
  }
];

const money = (value) => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(Number(value || 0));
const hours = (value) => Number(value || 0).toFixed(2).replace(/\.00$/, '');
const defaultColumns = columns.filter((column) => column.defaultVisible).map((column) => column.key);

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

function Cell({ invoice, columnKey }) {
  if (columnKey === 'invoiceNumber') return <span className="m042-stack"><strong>{invoice.invoiceNumber}</strong><small>{invoice.projectCode}</small></span>;
  if (columnKey === 'billingModel') return <span className="m042-pill blue">{invoice.billingModel}</span>;
  if (columnKey === 'invoiceType') return <span className={`m042-pill ${invoice.invoiceType === 'Final' ? 'green' : 'blue'}`}>{invoice.invoiceType}</span>;
  if (columnKey === 'status') return <span className="m042-pill amber">{invoice.status}</span>;
  if (columnKey === 'invoiceHours') return hours(invoice.invoiceHours);
  if (columnKey === 'hourlyRate') return invoice.hourlyRate ? money(invoice.hourlyRate) : 'Milestone';
  if (['currentAmount', 'previouslyInvoiced', 'remainingBalance'].includes(columnKey)) return money(invoice[columnKey]);
  return invoice[columnKey] ?? '—';
}

export default function InvoiceBillingCenter({ usSignalLogoUrl, userKey }) {
  const [view, setView] = useState('queue');
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('All');
  const [model, setModel] = useState('All');
  const [visibleColumns, setVisibleColumns] = useState(() => readColumns(userKey));
  const [selectedId, setSelectedId] = useState(invoices[0].id);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [notice, setNotice] = useState('');

  useEffect(() => {
    try {
      window.localStorage.setItem(`projectPulseModule042Columns:${userKey || 'current-user'}`, JSON.stringify(visibleColumns));
    } catch {
      // The selected columns remain active for this browser session.
    }
  }, [userKey, visibleColumns]);

  const selected = invoices.find((invoice) => invoice.id === selectedId) || invoices[0];
  const visibleDefinitions = columns.filter((column) => visibleColumns.includes(column.key));
  const groups = [...new Set(columns.map((column) => column.group))];

  const filtered = useMemo(() => {
    const needle = search.trim().toLowerCase();
    return invoices.filter((invoice) => {
      if (view === 'closed' && !invoice.closed) return false;
      if (status !== 'All' && invoice.status !== status) return false;
      if (model !== 'All' && invoice.billingModel !== model) return false;
      if (!needle) return true;
      return [invoice.invoiceNumber, invoice.customer, invoice.project, invoice.projectCode, invoice.certiniaId, invoice.sellQuoteId, invoice.salesforceId, invoice.projectManager, invoice.coordinator].join(' ').toLowerCase().includes(needle);
    });
  }, [model, search, status, view]);

  const action = (message) => {
    setNotice(message);
    window.setTimeout(() => setNotice(''), 4500);
  };

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
          <p>Prepare partial and final invoices, review recently closed work, preserve customer-facing time detail, and monitor billing reports from one professional workspace.</p>
        </div>
        <div className="m042-actions">
          <button type="button" className="secondary-action" onClick={() => setDrawerOpen(true)}>Customize columns</button>
          <button type="button" className="secondary-action" onClick={() => action('Excel export will use the selected headers when the invoice ledger API is connected.')}>Export Excel</button>
          <button type="button" className="primary-action" onClick={() => action('PDF export will use the US Signal branded invoice layout shown in this preview.')}>Export PDF</button>
        </div>
      </header>

      {notice ? <div className="m042-notice" role="status">{notice}</div> : null}

      <section className="m042-preview-mode" aria-label="Module 042 preview status">
        <strong>Preview mode</strong>
        <span>
          Sample invoice records are displayed for workflow and layout review.
          This page does not create a customer invoice or write to Certinia,
          SELL, Salesforce, or the accounting ledger yet.
        </span>
      </section>

      <section className="m042-workflow-explainer" aria-label="Billing workflow">
        <article>
          <span>1</span>
          <div>
            <strong>Module 039</strong>
            <small>Check billing readiness and resolve blockers.</small>
          </div>
        </article>
        <b aria-hidden="true">→</b>
        <article>
          <span>2</span>
          <div>
            <strong>Module 042</strong>
            <small>Prepare and review the actual invoice.</small>
          </div>
        </article>
        <b aria-hidden="true">→</b>
        <article>
          <span>3</span>
          <div>
            <strong>Export / Accounting</strong>
            <small>Deliver the PDF or Excel package and record the invoice.</small>
          </div>
        </article>
      </section>

      <section className="m042-metrics" aria-label="Billing summary">
        <article><span>Ready to invoice</span><strong>{money(invoices.reduce((sum, invoice) => sum + invoice.currentAmount, 0))}</strong><small>Four invoice packages</small></article>
        <article><span>Approved unbilled</span><strong>{money(invoices.reduce((sum, invoice) => sum + invoice.remainingBalance, 0))}</strong><small>Remaining customer value</small></article>
        <article><span>Recently closed</span><strong>{invoices.filter((invoice) => invoice.closed).length}</strong><small>Ready for final review</small></article>
        <article><span>Billing exceptions</span><strong>0</strong><small>No missing-rate or duplicate issues</small></article>
      </section>

      <nav className="m042-tabs" aria-label="Invoice Center views">
        <button type="button" className={view === 'queue' ? 'active' : ''} onClick={() => setView('queue')}>Invoice queue</button>
        <button type="button" className={view === 'closed' ? 'active' : ''} onClick={() => setView('closed')}>Recently closed</button>
        <button type="button" className={view === 'reports' ? 'active' : ''} onClick={() => setView('reports')}>Reports</button>
      </nav>

      {view === 'reports' ? (
        <section className="m042-reports">
          <article><span>Fixed Price</span><h2>Over / Under Report</h2><p>Planned hours, actual approved hours, remaining or exceeded hours, eligible assigned engineers, allocation percentages, and utilization adjustments.</p><dl><div><dt>Positive adjustment</dt><dd>50 hours</dd></div><div><dt>Awaiting allocation</dt><dd>2 projects</dd></div></dl></article>
          <article><span>T&amp;M</span><h2>Customer Balance</h2><p>Approved amount, invoiced amount, approved unbilled balance, contract or NTE amount, and remaining customer balance.</p><dl><div><dt>Approved unbilled</dt><dd>{money(26730)}</dd></div><div><dt>Remaining contract value</dt><dd>{money(53122.5)}</dd></div></dl></article>
          <article><span>Invoice history</span><h2>Partial and Final Invoices</h2><p>Search invoice history by customer, PM, coordinator, engineer, external identifier, billing period, hours, rate, amount, and export status.</p><dl><div><dt>Partial invoices</dt><dd>2</dd></div><div><dt>Final invoices</dt><dd>2</dd></div></dl></article>
        </section>
      ) : (
        <>
          <section className="m042-toolbar">
            <label><span>Search invoices</span><input type="search" value={search} placeholder="Customer, project, invoice, Certinia, SELL, Salesforce..." onChange={(event) => setSearch(event.target.value)} /></label>
            <label><span>Status</span><select value={status} onChange={(event) => setStatus(event.target.value)}><option>All</option><option>Draft</option><option>Ready for PM Review</option><option>Ready for Coordinator</option><option>Recently Closed</option></select></label>
            <label><span>Billing model</span><select value={model} onChange={(event) => setModel(event.target.value)}><option>All</option><option>T&amp;M</option><option>Fixed Price</option><option>IQS</option><option>Service Request</option></select></label>
            <div className="m042-column-count"><strong>{visibleColumns.length}</strong><small>columns shown</small></div>
          </section>

          <section className="m042-workspace">
            <div className="m042-card">
              <header className="m042-card-head"><div><h2>{view === 'closed' ? 'Recently closed projects' : 'Invoice preparation queue'}</h2><p>Select a row to inspect its detailed customer invoice.</p></div><span>{filtered.length} shown</span></header>
              <div className="m042-table-wrap">
                <table><thead><tr>{visibleDefinitions.map((column) => <th key={column.key}>{column.label}</th>)}</tr></thead><tbody>{filtered.map((invoice) => (
                  <tr key={invoice.id} className={selected.id === invoice.id ? 'selected' : ''} onClick={() => setSelectedId(invoice.id)}>{visibleDefinitions.map((column) => <td key={`${invoice.id}-${column.key}`}><Cell invoice={invoice} columnKey={column.key} /></td>)}</tr>
                ))}</tbody></table>
              </div>
            </div>

            <aside className="m042-card m042-preview">
              <div className="m042-invoice">
                <header className="m042-invoice-head"><div className="m042-brand">{usSignalLogoUrl ? <img src={usSignalLogoUrl} alt="US Signal" /> : <strong>US Signal</strong>}<span>Professional Services Invoice</span></div><div><span>{selected.invoiceType} invoice</span><strong>{selected.invoiceNumber}</strong><small>{selected.status}</small></div></header>
                <section className="m042-identities"><div><span>Bill to</span><strong>{selected.customer}</strong><small>Customer billing address and contact</small></div><div><span>Project</span><strong>{selected.project}</strong><small>{selected.projectCode}</small></div><div><span>Billing period</span><strong>{selected.billingPeriod}</strong><small>{selected.projectManager}</small></div></section>
                <section className="m042-refs"><div><span>Certinia ID</span><strong>{selected.certiniaId}</strong></div><div><span>SELL Quote</span><strong>{selected.sellQuoteId}</strong></div><div><span>Salesforce ID</span><strong>{selected.salesforceId}</strong></div><div><span>Purchase order</span><strong>{selected.purchaseOrder}</strong></div></section>
                <div className="m042-lines"><table><thead><tr><th>Date</th><th>Engineer / Work detail</th><th>Hours</th><th>Rate</th><th>Amount</th></tr></thead><tbody>{selected.lines.map(([date, engineer, task, description, lineHours, rate], index) => <tr key={`${selected.id}-line-${index}`}><td>{date}</td><td><strong>{engineer}</strong><span>{task}</span><p>{description}</p></td><td>{hours(lineHours)}</td><td>{money(rate)}</td><td>{money(lineHours * rate)}</td></tr>)}</tbody></table></div>
                <section className="m042-totals"><div><span>Previously invoiced</span><strong>{money(selected.previouslyInvoiced)}</strong></div><div><span>Current invoice</span><strong>{money(selected.currentAmount)}</strong></div><div><span>Remaining balance</span><strong>{money(selected.remainingBalance)}</strong></div></section>
                <footer className="m042-invoice-foot">Hours × rate calculations and customer-facing work descriptions are preserved with the invoice snapshot.</footer>
              </div>
            </aside>
          </section>
        </>
      )}

      {drawerOpen ? <div className="m042-backdrop" onMouseDown={(event) => event.target === event.currentTarget && setDrawerOpen(false)}><section className="m042-drawer" role="dialog" aria-modal="true" aria-label="Customize invoice headers"><header><div><p className="eyebrow">Personal table settings</p><h2>Customize columns</h2><p>Add or remove headers from your Invoice Center. The selection is saved for this user on this browser.</p></div><button type="button" aria-label="Close" onClick={() => setDrawerOpen(false)}>×</button></header><div className="m042-shortcuts"><button type="button" onClick={() => setVisibleColumns(columns.map((column) => column.key))}>Show all</button><button type="button" onClick={() => setVisibleColumns(columns.filter((column) => column.group === 'Essential').map((column) => column.key))}>Essential only</button><button type="button" onClick={() => setVisibleColumns(defaultColumns)}>Restore defaults</button></div><div className="m042-groups">{groups.map((group) => <fieldset key={group}><legend>{group}</legend>{columns.filter((column) => column.group === group).map((column) => <label key={column.key}><input type="checkbox" checked={visibleColumns.includes(column.key)} onChange={() => toggleColumn(column.key)} /><span>{column.label}</span></label>)}</fieldset>)}</div><footer><span>{visibleColumns.length} columns selected</span><button type="button" className="primary-action" onClick={() => setDrawerOpen(false)}>Apply columns</button></footer></section></div> : null}
    </div>
  );
}
