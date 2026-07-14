#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077

CLONE_DIR="${HOME}/project-time-platform-module-042"
BRANCH="source/invoice-billing-center-preview-20260714"
EXPECTED_HEAD="abba23f62624a984d0a0b51428d41384d3311d75"

APP_FILE="${CLONE_DIR}/src/frontend/project-time-web/src/App.jsx"
STYLE_FILE="${CLONE_DIR}/src/frontend/project-time-web/src/styles.css"
COMPONENT_FILE="${CLONE_DIR}/src/frontend/project-time-web/src/InvoiceBillingCenter.jsx"
FRONTEND_DIR="${CLONE_DIR}/src/frontend/project-time-web"

TIMESTAMP="$(date -u +%Y%m%dT%H%M%SZ)"
BACKUP_DIR="${HOME}/az12d4/module-042-source-backup-${TIMESTAMP}"
BUILD_LOG="${HOME}/az12d4/module-042-frontend-build-${TIMESTAMP}.log"

fail() {
  echo
  echo "============================================================"
  echo "MODULE_042_INTEGRATED_PREVIEW=FAILED"
  echo "ERROR=$*" >&2
  echo "SOURCE_COMMITTED=NO"
  echo "SOURCE_PUSHED=NO"
  echo "IMAGE_BUILT=NO"
  echo "AZURE_MODIFIED=NO"
  echo "DATABASE_MODIFIED=NO"
  echo "APPLICATION_DEPLOYED=NO"
  echo "============================================================"
  exit 1
}

restore_source() {
  if [[ -f "${BACKUP_DIR}/App.jsx" ]]; then
    cp -p "${BACKUP_DIR}/App.jsx" "${APP_FILE}"
  fi

  if [[ -f "${BACKUP_DIR}/styles.css" ]]; then
    cp -p "${BACKUP_DIR}/styles.css" "${STYLE_FILE}"
  fi

  if [[ -f "${BACKUP_DIR}/InvoiceBillingCenter.jsx" ]]; then
    cp -p "${BACKUP_DIR}/InvoiceBillingCenter.jsx" "${COMPONENT_FILE}"
  else
    rm -f "${COMPONENT_FILE}"
  fi
}

on_error() {
  local rc=$?
  local line="${1:-unknown}"

  set +e
  restore_source
  echo "SOURCE_RESTORED_AFTER_FAILURE=YES"
  fail "Unexpected failure at line ${line}; exit ${rc}."
}

trap 'on_error ${LINENO}' ERR

echo "============================================================"
echo "MODULE 042 INTEGRATED REACT PREVIEW"
echo "============================================================"

[[ -d "${CLONE_DIR}/.git" ]] || fail "Module 042 clone is missing."
[[ -s "${APP_FILE}" ]] || fail "App.jsx is missing."
[[ -s "${STYLE_FILE}" ]] || fail "styles.css is missing."

CURRENT_BRANCH="$(git -C "${CLONE_DIR}" branch --show-current)"
CURRENT_HEAD="$(git -C "${CLONE_DIR}" rev-parse HEAD)"
CURRENT_STATUS="$(git -C "${CLONE_DIR}" status --porcelain)"

echo "CURRENT_BRANCH=${CURRENT_BRANCH}"
echo "CURRENT_HEAD=${CURRENT_HEAD}"

[[ "${CURRENT_BRANCH}" == "${BRANCH}" ]] || fail "Repository is on the wrong branch."
[[ "${CURRENT_HEAD}" == "${EXPECTED_HEAD}" ]] || fail "Repository is not at the approved inspection commit."
[[ -z "${CURRENT_STATUS}" ]] || fail "Repository contains uncommitted changes."

mkdir -p "${BACKUP_DIR}"
chmod 700 "${BACKUP_DIR}"
cp -p "${APP_FILE}" "${BACKUP_DIR}/App.jsx"
cp -p "${STYLE_FILE}" "${BACKUP_DIR}/styles.css"
[[ ! -e "${COMPONENT_FILE}" ]] || cp -p "${COMPONENT_FILE}" "${BACKUP_DIR}/InvoiceBillingCenter.jsx"

echo "BACKUP_DIR=${BACKUP_DIR}"

cat > "${COMPONENT_FILE}" <<'JSX'
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
JSX

python3 - "${APP_FILE}" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
text = path.read_text(encoding='utf-8')

import_marker = "import CloseoutEmailAutomationCenter from './CloseoutEmailAutomationCenter.jsx';"
import_line = "import InvoiceBillingCenter from './InvoiceBillingCenter.jsx';"
if import_line not in text:
    if import_marker not in text:
        raise SystemExit('Missing CloseoutEmailAutomationCenter import anchor.')
    text = text.replace(import_marker, import_marker + "\n" + import_line, 1)

module_marker = "  /* 041_CLOSEOUT_EMAIL_AUTOMATION_END */"
module_block = """  /* 042_INVOICE_BILLING_CENTER_START */
  {
    route: 'invoice-billing-center',
    href: '#invoice-billing-center',
    title: 'Invoice & Billing Center',
    navLabel: 'MODULE 042',
    description: 'Prepare partial and final invoices, review recently closed projects, preserve detailed customer-facing time and rate evidence, customize invoice headers, and preview Over / Under and T&M balance reporting.',
    permissions: ['VIEW_ACCOUNT_RECONCILIATION', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  /* 042_INVOICE_BILLING_CENTER_END */
"""
if '042_INVOICE_BILLING_CENTER_START' not in text:
    if module_marker not in text:
        raise SystemExit('Missing MODULE 041 registry boundary.')
    text = text.replace(module_marker, module_marker + "\n" + module_block.rstrip(), 1)

nav_marker = "    case 'closeout-email':\n      return 'Reports & Workflow';"
if "case 'invoice-billing-center':" not in text:
    if nav_marker not in text:
        raise SystemExit('Missing Reports & Workflow navigation anchor.')
    text = text.replace(nav_marker, "    case 'closeout-email':\n    case 'invoice-billing-center':\n      return 'Reports & Workflow';", 1)

route_marker = "    'work-task-builder',\n    'workflow',"
route_block = """    'work-task-builder',
    'billing-readiness',
    'project-closeout',
    'closeout-email',
    'invoice-billing-center',
    'workflow',"""
route_section_start = text.find('  const routeOrder = [')
route_section_end = text.find('  ];', route_section_start)
route_section = text[route_section_start:route_section_end]
if "'invoice-billing-center'" not in route_section:
    if route_marker not in text:
        raise SystemExit('Missing route-order anchor.')
    text = text.replace(route_marker, route_block, 1)

registry_start = text.find('function getInstalledProjectPulseModuleRegistry()')
registry_end = text.find('function getInstalledModuleDescription(module)', registry_start)
if registry_start < 0 or registry_end < 0:
    raise SystemExit('Missing installed-module registry boundaries.')
registry = text[registry_start:registry_end]
if "route: 'invoice-billing-center'" not in registry:
    insertion = registry.rfind('  ];')
    if insertion < 0:
        raise SystemExit('Missing installed-module registry closing boundary.')
    registry_entry = """  {
    route: 'invoice-billing-center',
    title: 'Invoice & Billing Center',
    navLabel: 'MODULE 042',
    group: 'Reports & Workflow',
    permissions: ['VIEW_ACCOUNT_RECONCILIATION', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Prepares detailed partial and final invoice packages with customer identifiers, time-entry evidence, rates, hours, amounts, flexible headers, recently closed work, and billing reports.'
  },
"""
    registry = registry[:insertion] + registry_entry + registry[insertion:]
    text = text[:registry_start] + registry + text[registry_end:]

description_marker = "    'psa-modules': 'Displays PSA workflow modules such as expense, invoice, project, and billing readiness areas as they are connected.'"
if "'invoice-billing-center':" not in text[text.find('function getInstalledModuleDescription'):text.find('function getInstalledModuleDescription') + 8000]:
    if description_marker not in text:
        raise SystemExit('Missing installed-module description anchor.')
    text = text.replace(description_marker, "    'invoice-billing-center': 'Prepares partial and final invoice packages, preserves detailed time and rate evidence, and supports billing and Over / Under reporting.',\n" + description_marker, 1)

render_anchor = """      {(activeRoute === 'closeout-email' && canSeeAny(['VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_ACCOUNT_RECONCILIATION', 'VIEW_EXPENSES', 'EXPORT_TIME_EXCEL', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="closeout-email" className="panel closeout-email-route-panel">
          <CloseoutEmailAutomationCenter />
        </section>
      ) : null}
"""
render_block = """
      {(activeRoute === 'invoice-billing-center' && canSeeAny(['VIEW_ACCOUNT_RECONCILIATION', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="invoice-billing-center" className="panel invoice-billing-center-route-panel">
          <InvoiceBillingCenter
            usSignalLogoUrl={usSignalLogoUrl}
            userKey={authSession?.username ?? currentUser.data?.email ?? 'current-user'}
          />
        </section>
      ) : null}
"""
if 'invoice-billing-center-route-panel' not in text:
    if render_anchor not in text:
        raise SystemExit('Missing MODULE 041 render anchor.')
    text = text.replace(render_anchor, render_anchor + render_block, 1)

path.write_text(text, encoding='utf-8')
PY

cat >> "${STYLE_FILE}" <<'CSS'

/* 042_INVOICE_BILLING_CENTER_STYLES_START */
.invoice-billing-center-route-panel{padding:0;border:0;background:transparent;box-shadow:none}.m042-center{display:grid;gap:1rem;min-width:0}.m042-hero{display:flex;justify-content:space-between;gap:1.25rem;padding:1.5rem;border:1px solid rgba(148,163,184,.24);border-radius:1.2rem;background:radial-gradient(circle at top right,rgba(14,165,233,.13),transparent 34%),var(--surface,#fff);box-shadow:0 18px 45px rgba(15,23,42,.08)}.m042-hero h1{margin:.2rem 0 .45rem;font-size:clamp(1.7rem,2.6vw,2.35rem)}.m042-hero p{max-width:760px;margin:0;color:var(--muted,#64748b);line-height:1.55}.m042-actions{display:flex;flex-wrap:wrap;justify-content:flex-end;align-content:flex-start;gap:.6rem}.m042-notice{padding:.8rem 1rem;border:1px solid rgba(14,165,233,.3);border-radius:.8rem;background:rgba(14,165,233,.08);font-weight:750}.m042-metrics{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:.8rem}.m042-metrics article{display:grid;gap:.25rem;min-height:120px;padding:1rem;border:1px solid rgba(148,163,184,.22);border-radius:1rem;background:var(--surface,#fff);box-shadow:0 10px 25px rgba(15,23,42,.06)}.m042-metrics span{color:var(--muted,#64748b);font-size:.72rem;font-weight:850;letter-spacing:.04em;text-transform:uppercase}.m042-metrics strong{align-self:center;font-size:1.45rem}.m042-metrics small{color:var(--muted,#64748b)}.m042-tabs{display:flex;gap:.3rem;padding:.3rem;border:1px solid rgba(148,163,184,.22);border-radius:.85rem;background:rgba(148,163,184,.08)}.m042-tabs button{flex:0 1 210px;padding:.7rem 1rem;border:0;border-radius:.6rem;background:transparent;color:var(--muted,#64748b);font-weight:850;cursor:pointer}.m042-tabs button.active{background:var(--surface,#fff);color:var(--text,#0f172a);box-shadow:0 7px 18px rgba(15,23,42,.08)}.m042-toolbar{display:grid;grid-template-columns:minmax(280px,1.5fr) minmax(170px,.6fr) minmax(170px,.6fr) auto;gap:.75rem;align-items:end;padding:.9rem;border:1px solid rgba(148,163,184,.22);border-radius:.95rem;background:var(--surface,#fff)}.m042-toolbar label{display:grid;gap:.3rem;color:var(--muted,#64748b);font-size:.75rem;font-weight:800}.m042-toolbar input,.m042-toolbar select{width:100%;min-height:41px;padding:.58rem .7rem;border:1px solid rgba(148,163,184,.34);border-radius:.6rem;background:var(--surface,#fff);color:var(--text,#0f172a)}.m042-column-count{display:grid;min-width:90px;padding:.55rem .75rem;border-radius:.6rem;background:rgba(15,23,42,.06);text-align:center}.m042-column-count small{color:var(--muted,#64748b)}.m042-workspace{display:grid;grid-template-columns:minmax(0,1.45fr) minmax(390px,.8fr);gap:.9rem;align-items:start}.m042-card{min-width:0;border:1px solid rgba(148,163,184,.22);border-radius:1rem;background:var(--surface,#fff);box-shadow:0 12px 32px rgba(15,23,42,.07)}.m042-card-head{display:flex;justify-content:space-between;gap:1rem;padding:1rem;border-bottom:1px solid rgba(148,163,184,.18)}.m042-card-head h2{margin:0 0 .2rem;font-size:1.05rem}.m042-card-head p,.m042-card-head span{margin:0;color:var(--muted,#64748b);font-size:.76rem}.m042-table-wrap{overflow:auto;max-height:690px}.m042-table-wrap table{width:max-content;min-width:100%;border-collapse:separate;border-spacing:0}.m042-table-wrap th{position:sticky;top:0;z-index:1;padding:.7rem;border-bottom:1px solid rgba(148,163,184,.24);background:#f8fafc;color:#475569;font-size:.68rem;letter-spacing:.035em;text-align:left;text-transform:uppercase;white-space:nowrap}.m042-table-wrap td{padding:.75rem .7rem;border-bottom:1px solid rgba(148,163,184,.14);font-size:.8rem;vertical-align:top;white-space:nowrap}.m042-table-wrap tbody tr{cursor:pointer}.m042-table-wrap tbody tr:hover,.m042-table-wrap tbody tr.selected{background:rgba(14,165,233,.075)}.m042-table-wrap tbody tr.selected{box-shadow:inset 3px 0 0 #0284c7}.m042-stack{display:grid;gap:.15rem}.m042-stack small{color:var(--muted,#64748b)}.m042-pill{display:inline-flex;padding:.2rem .5rem;border-radius:999px;font-size:.68rem;font-weight:850}.m042-pill.blue{background:rgba(14,165,233,.11);color:#0369a1}.m042-pill.green{background:rgba(22,163,74,.11);color:#166534}.m042-pill.amber{background:rgba(245,158,11,.13);color:#92400e}.m042-preview{position:sticky;top:1rem;padding:.7rem}.m042-invoice{overflow:hidden;border:1px solid #cbd5e1;border-radius:.75rem;background:#fff;color:#172033}.m042-invoice-head{display:flex;justify-content:space-between;gap:1rem;padding:1rem;border-bottom:4px solid #0284c7}.m042-invoice-head>div:last-child{display:grid;gap:.15rem;text-align:right}.m042-invoice-head span,.m042-invoice-head small{color:#64748b;font-size:.68rem}.m042-brand{display:grid;gap:.2rem}.m042-brand img{width:auto;max-width:145px;height:36px;object-fit:contain;object-position:left center}.m042-identities{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:.65rem;padding:.8rem 1rem;background:#f8fafc}.m042-identities div,.m042-refs div{display:grid;gap:.15rem}.m042-identities span,.m042-refs span{color:#64748b;font-size:.6rem;font-weight:800;letter-spacing:.04em;text-transform:uppercase}.m042-identities strong,.m042-refs strong{font-size:.7rem}.m042-identities small{color:#64748b;font-size:.6rem}.m042-refs{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:.6rem;padding:.8rem 1rem;border-bottom:1px solid #e2e8f0}.m042-lines{overflow:auto;max-height:350px}.m042-lines table{width:100%;border-collapse:collapse}.m042-lines th,.m042-lines td{padding:.55rem;border-bottom:1px solid #e2e8f0;font-size:.62rem;text-align:left;vertical-align:top}.m042-lines th{background:#f8fafc;color:#475569;text-transform:uppercase}.m042-lines td:nth-child(n+3){text-align:right;white-space:nowrap}.m042-lines td:nth-child(2){min-width:210px}.m042-lines td strong,.m042-lines td span{display:block}.m042-lines td p{margin:.3rem 0 0;color:#64748b;line-height:1.4}.m042-totals{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:.5rem;padding:.8rem 1rem;background:#f8fafc}.m042-totals div{display:grid;gap:.15rem}.m042-totals span{color:#64748b;font-size:.58rem;text-transform:uppercase}.m042-totals strong{font-size:.8rem}.m042-invoice-foot{padding:.7rem 1rem;color:#64748b;font-size:.6rem}.m042-reports{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:.9rem}.m042-reports article{display:grid;gap:.7rem;min-height:260px;padding:1.2rem;border:1px solid rgba(148,163,184,.22);border-radius:1rem;background:var(--surface,#fff);box-shadow:0 12px 30px rgba(15,23,42,.07)}.m042-reports article>span{color:#0284c7;font-size:.68rem;font-weight:850;text-transform:uppercase}.m042-reports h2,.m042-reports p{margin:0}.m042-reports p{color:var(--muted,#64748b);line-height:1.5}.m042-reports dl{display:grid;gap:.45rem;margin:auto 0 0}.m042-reports dl div{display:flex;justify-content:space-between;gap:1rem;padding-top:.45rem;border-top:1px solid rgba(148,163,184,.18)}.m042-reports dd{margin:0;font-weight:850}.m042-backdrop{position:fixed;inset:0;z-index:12000;display:flex;justify-content:flex-end;background:rgba(15,23,42,.55);backdrop-filter:blur(4px)}.m042-drawer{display:grid;grid-template-rows:auto auto 1fr auto;width:min(520px,94vw);height:100%;background:var(--surface,#fff);box-shadow:-22px 0 55px rgba(15,23,42,.25)}.m042-drawer>header{display:flex;justify-content:space-between;gap:1rem;padding:1.2rem;border-bottom:1px solid rgba(148,163,184,.22)}.m042-drawer>header h2,.m042-drawer>header p{margin:.2rem 0 0}.m042-drawer>header p{color:var(--muted,#64748b);line-height:1.45}.m042-drawer>header button{align-self:flex-start;width:36px;height:36px;border:0;border-radius:999px;background:rgba(148,163,184,.16);color:inherit;font-size:1.35rem;cursor:pointer}.m042-shortcuts{display:flex;gap:.45rem;padding:.8rem 1.2rem;border-bottom:1px solid rgba(148,163,184,.18)}.m042-shortcuts button{padding:.48rem .65rem;border:1px solid rgba(148,163,184,.3);border-radius:.5rem;background:transparent;color:inherit;font-weight:750;cursor:pointer}.m042-groups{overflow:auto;padding:.9rem 1.2rem}.m042-groups fieldset{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:.5rem;margin:0 0 .9rem;padding:.8rem;border:1px solid rgba(148,163,184,.22);border-radius:.7rem}.m042-groups legend{padding:0 .3rem;font-weight:850}.m042-groups label{display:flex;align-items:center;gap:.5rem;min-height:36px;padding:.4rem .5rem;border-radius:.5rem;cursor:pointer}.m042-groups label:hover{background:rgba(14,165,233,.07)}.m042-drawer>footer{display:flex;align-items:center;justify-content:space-between;gap:1rem;padding:.9rem 1.2rem;border-top:1px solid rgba(148,163,184,.22)}[data-theme='dark'] .m042-table-wrap th{background:#1e293b;color:#cbd5e1}@media(max-width:1350px){.m042-workspace{grid-template-columns:1fr}.m042-preview{position:static}}@media(max-width:1050px){.m042-hero{flex-direction:column}.m042-actions{justify-content:flex-start}.m042-metrics,.m042-reports{grid-template-columns:repeat(2,minmax(0,1fr))}.m042-toolbar{grid-template-columns:1fr 1fr}}@media(max-width:680px){.m042-metrics,.m042-reports,.m042-toolbar,.m042-identities,.m042-totals{grid-template-columns:1fr}.m042-tabs{overflow-x:auto}.m042-tabs button{flex:0 0 auto}.m042-groups fieldset{grid-template-columns:1fr}}
/* 042_INVOICE_BILLING_CENTER_STYLES_END */
CSS

python3 - "${APP_FILE}" "${COMPONENT_FILE}" "${STYLE_FILE}" <<'PY'
from pathlib import Path
import sys

app = Path(sys.argv[1]).read_text(encoding='utf-8')
component = Path(sys.argv[2]).read_text(encoding='utf-8')
styles = Path(sys.argv[3]).read_text(encoding='utf-8')

required_app = [
    "import InvoiceBillingCenter from './InvoiceBillingCenter.jsx';",
    '042_INVOICE_BILLING_CENTER_START',
    "route: 'invoice-billing-center'",
    "case 'invoice-billing-center':",
    "'invoice-billing-center',",
    "'invoice-billing-center': 'Prepares partial and final invoice packages",
    'invoice-billing-center-route-panel',
    '<InvoiceBillingCenter'
]
required_component = [
    'MODULE 042', 'Invoice & Billing Center', 'Customize columns', 'Certinia ID',
    'SELL Quote', 'Salesforce ID', 'Hourly rate', 'Export Excel', 'Export PDF',
    'Over / Under Report', 'Customer Balance', 'lineHours * rate', 'usSignalLogoUrl'
]
required_styles = ['042_INVOICE_BILLING_CENTER_STYLES_START', '.m042-center', '.m042-drawer', '.m042-invoice']

missing = []
for marker in required_app:
    if marker not in app:
        missing.append(f'App:{marker}')
for marker in required_component:
    if marker not in component:
        missing.append(f'Component:{marker}')
for marker in required_styles:
    if marker not in styles:
        missing.append(f'Styles:{marker}')

if missing:
    raise SystemExit('Missing Module 042 markers: ' + ', '.join(missing))

if app.count('042_INVOICE_BILLING_CENTER_START') != 1:
    raise SystemExit('MODULE 042 registry marker count is not one.')
if app.count('invoice-billing-center-route-panel') != 1:
    raise SystemExit('MODULE 042 route renderer count is not one.')
if styles.count('042_INVOICE_BILLING_CENTER_STYLES_START') != 1:
    raise SystemExit('MODULE 042 style marker count is not one.')

print('MODULE_042_SOURCE_VALIDATION=PASSED')
PY

git -C "${CLONE_DIR}" diff --check

echo "GIT_DIFF_CHECK=PASSED"

EXPECTED_FILES="$(printf '%s\n' \
  'src/frontend/project-time-web/src/App.jsx' \
  'src/frontend/project-time-web/src/InvoiceBillingCenter.jsx' \
  'src/frontend/project-time-web/src/styles.css' | sort)"
ACTUAL_FILES="$(git -C "${CLONE_DIR}" status --short | awk '{print $2}' | sort)"

if [[ "${ACTUAL_FILES}" != "${EXPECTED_FILES}" ]]; then
  echo "EXPECTED_FILES:"
  echo "${EXPECTED_FILES}"
  echo "ACTUAL_FILES:"
  echo "${ACTUAL_FILES}"
  restore_source
  fail "Unexpected files changed while preparing Module 042."
fi

echo
echo "CHANGED_FILES"
git -C "${CLONE_DIR}" status --short

echo
echo "FRONTEND_BUILD_START=YES"

cd "${FRONTEND_DIR}"
npm ci --no-audit --no-fund > "${BUILD_LOG}" 2>&1
npm run build >> "${BUILD_LOG}" 2>&1

echo "FRONTEND_BUILD=PASSED"

cd "${CLONE_DIR}"

git diff --stat

trap - ERR

cat > "${HOME}/az12d4/module-042-prepared-${TIMESTAMP}.env" <<EOF
MODULE_NUMBER=042
MODULE_NAME=Invoice & Billing Center
MODULE_ROUTE=invoice-billing-center
SOURCE_BASE=${EXPECTED_HEAD}
CLONE_DIR=${CLONE_DIR}
BACKUP_DIR=${BACKUP_DIR}
BUILD_LOG=${BUILD_LOG}
EOF

chmod 600 "${HOME}/az12d4/module-042-prepared-${TIMESTAMP}.env"

echo
echo "============================================================"
echo "MODULE_042_INTEGRATED_PREVIEW=PREPARED"
echo "MODULE_NUMBER=042"
echo "MODULE_NAME=Invoice & Billing Center"
echo "MODULE_ROUTE=invoice-billing-center"
echo "MODULE_GROUP=Reports & Workflow"
echo "SOURCE_BASE=${EXPECTED_HEAD}"
echo "MODULE_042_SOURCE_VALIDATION=PASSED"
echo "GIT_DIFF_CHECK=PASSED"
echo "FRONTEND_BUILD=PASSED"
echo "CUSTOMIZABLE_HEADERS=INCLUDED"
echo "US_SIGNAL_BRANDING=INCLUDED"
echo "PARTIAL_AND_FINAL_INVOICES=PREVIEWED"
echo "OVER_UNDER_REPORT=PREVIEWED"
echo "TM_BALANCE_REPORT=PREVIEWED"
echo "SOURCE_COMMITTED=NO"
echo "SOURCE_PUSHED=NO"
echo "IMAGE_BUILT=NO"
echo "AZURE_MODIFIED=NO"
echo "DATABASE_MODIFIED=NO"
echo "APPLICATION_DEPLOYED=NO"
echo "BACKUP_DIR=${BACKUP_DIR}"
echo "BUILD_LOG=${BUILD_LOG}"
echo "============================================================"
