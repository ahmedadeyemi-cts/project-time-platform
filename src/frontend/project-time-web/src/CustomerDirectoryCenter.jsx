import { useEffect, useMemo, useState } from 'react';
import './customer-directory-center.css';
import './customer-directory-sell-sync.css';

function getStoredAuthSession() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return null;
    return JSON.parse(rawSession);
  } catch {
    return null;
  }
}

function getProjectPulseAuthHeaders() {
  const session = getStoredAuthSession();
  return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
}

async function readApiErrorMessage(response, path) {
  const raw = await response.text();
  if (!raw) return `${path} returned HTTP ${response.status}`;

  try {
    const parsed = JSON.parse(raw);
    return `${path} returned HTTP ${response.status}: ${parsed.message || parsed.detail || parsed.status || raw}`;
  } catch {
    return `${path} returned HTTP ${response.status}: ${raw}`;
  }
}

async function fetchJson(path) {
  const response = await fetch(path, {
    credentials: 'include',
    headers: getProjectPulseAuthHeaders(),
  });

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

async function sendJson(path, method, payload) {
  const response = await fetch(path, {
    method,
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...getProjectPulseAuthHeaders() },
    body: JSON.stringify(payload),
  });

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

function fmtMoney(value) {
  const amount = Number(value ?? 0);
  return amount.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
}

function formatDate(value) {
  if (!value) return 'Never';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Never' : parsed.toLocaleString();
}

function words(value) {
  return String(value || 'not configured')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function makeClientCode(name) {
  return String(name ?? '')
    .replace(/[^a-z0-9]/gi, '')
    .slice(0, 8)
    .toUpperCase();
}

const emptyCustomer = {
  clientName: '',
  clientCode: '',
  isActive: true,
};

const emptyContact = {
  contactName: '',
  title: '',
  roleDescription: '',
  email: '',
  phone: '',
  addressLine1: '',
  addressLine2: '',
  city: '',
  stateRegion: '',
  postalCode: '',
  country: 'United States',
  isPrimary: false,
  isActive: true,
  displayOrder: 0,
};

const emptySellState = {
  statusLoading: true,
  status: null,
  statusError: '',
  previewLoading: false,
  preview: null,
  previewError: '',
  importLoading: false,
  runs: [],
};

function providerReady(status) {
  return Boolean(
    status?.provider?.configured
    && status?.provider?.enabled
    && status?.provider?.credentialConfigured
    && status?.provider?.availabilityStatus === 'available',
  );
}

export default function CustomerDirectoryCenter({ canManageCustomers = false }) {
  const [directory, setDirectory] = useState({ loading: true, data: null, error: null });
  const [actionStatus, setActionStatus] = useState('');
  const [selectedClientId, setSelectedClientId] = useState('');
  const [customerForm, setCustomerForm] = useState(emptyCustomer);
  const [editingCustomerId, setEditingCustomerId] = useState('');
  const [contactForm, setContactForm] = useState(emptyContact);
  const [editingContactId, setEditingContactId] = useState('');
  const [searchTerm, setSearchTerm] = useState('');
  const [sellState, setSellState] = useState(emptySellState);
  const [sellFilters, setSellFilters] = useState({ search: '', relationship: 'customer', page: 1, pageSize: 100 });
  const [selectedSellIds, setSelectedSellIds] = useState([]);
  const [showSellHistory, setShowSellHistory] = useState(false);

  async function loadDirectory(preferredClientId = '') {
    setDirectory((current) => ({ ...current, loading: true, error: null }));

    try {
      const result = await fetchJson('/api/customers/overview');
      setDirectory({ loading: false, data: result, error: null });

      if (preferredClientId && result.customers?.some((customer) => customer.clientId === preferredClientId)) {
        setSelectedClientId(preferredClientId);
      } else if (!selectedClientId && result.customers?.length) {
        setSelectedClientId(result.customers[0].clientId);
      }
    } catch (error) {
      setDirectory({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load customer directory.',
      });
    }
  }

  async function loadSellStatus() {
    setSellState((current) => ({ ...current, statusLoading: true, statusError: '' }));
    try {
      const [status, runs] = await Promise.all([
        fetchJson('/api/customers/sell/status'),
        fetchJson('/api/customers/sell/runs').catch(() => ({ runs: [] })),
      ]);
      setSellState((current) => ({
        ...current,
        statusLoading: false,
        status,
        runs: runs.runs ?? [],
        statusError: '',
      }));
    } catch (error) {
      setSellState((current) => ({
        ...current,
        statusLoading: false,
        status: null,
        statusError: error instanceof Error ? error.message : 'SELL synchronization status is unavailable.',
      }));
    }
  }

  useEffect(() => {
    void loadDirectory();
    void loadSellStatus();
  }, []);

  const customers = directory.data?.customers ?? [];
  const contacts = directory.data?.contacts ?? [];
  const sellCustomers = sellState.preview?.customers ?? [];
  const sellIsReady = providerReady(sellState.status);

  const filteredCustomers = useMemo(() => {
    const search = searchTerm.trim().toLowerCase();

    if (!search) return customers;

    return customers.filter((customer) => {
      const customerContacts = contacts
        .filter((contact) => contact.clientId === customer.clientId)
        .map((contact) => `${contact.contactName ?? ''} ${contact.email ?? ''} ${contact.roleDescription ?? ''}`)
        .join(' ');

      const haystack = `${customer.clientName ?? ''} ${customer.clientCode ?? ''} ${customerContacts}`.toLowerCase();
      return haystack.includes(search);
    });
  }, [customers, contacts, searchTerm]);

  const selectedCustomer = customers.find((customer) => customer.clientId === selectedClientId) ?? filteredCustomers[0];
  const selectedContacts = contacts.filter((contact) => contact.clientId === selectedCustomer?.clientId);
  const selectedPrimaryContact = selectedContacts.find((contact) => contact.isPrimary);

  const customerDirectoryMetrics = useMemo(() => {
    const activeCustomers = customers.filter((customer) => customer.isActive !== false).length;
    const inactiveCustomers = customers.length - activeCustomers;
    const customersWithoutContacts = customers.filter((customer) => Number(customer.activeContactCount ?? 0) === 0).length;
    const customersWithCostContext = customers.filter((customer) => (
      Number(customer.plannedProjectTotalCost ?? 0) > 0
      || Number(customer.plannedIntakeTotalCost ?? 0) > 0
    )).length;

    return {
      activeCustomers,
      inactiveCustomers,
      customersWithoutContacts,
      customersWithCostContext,
    };
  }, [customers]);

  const selectedReadinessItems = selectedCustomer ? [
    {
      label: 'Customer record',
      ready: selectedCustomer.isActive !== false,
      detail: selectedCustomer.isActive === false ? 'Customer is inactive.' : 'Customer is active and available for intake/project workflows.',
    },
    {
      label: 'Contact coverage',
      ready: selectedContacts.length > 0,
      detail: selectedContacts.length > 0 ? `${selectedContacts.length} active contact(s) loaded.` : 'No active contact is loaded for this customer.',
    },
    {
      label: 'Primary contact',
      ready: Boolean(selectedPrimaryContact),
      detail: selectedPrimaryContact ? `${selectedPrimaryContact.contactName} is marked primary.` : 'No primary contact is selected.',
    },
    {
      label: 'Cost context',
      ready: Number(selectedCustomer.plannedProjectTotalCost ?? 0) > 0 || Number(selectedCustomer.plannedIntakeTotalCost ?? 0) > 0,
      detail: 'Project and intake planned cost values are shown for downstream cost review.',
    },
    {
      label: 'Over-plan risk',
      ready: Number(selectedCustomer.projectsOverPlanCount ?? 0) === 0,
      detail: Number(selectedCustomer.projectsOverPlanCount ?? 0) === 0
        ? 'No over-plan project count is currently reported.'
        : `${selectedCustomer.projectsOverPlanCount} project(s) are reporting over-plan risk.`,
    },
  ] : [];

  useEffect(() => {
    if (!selectedClientId && filteredCustomers[0]?.clientId) {
      setSelectedClientId(filteredCustomers[0].clientId);
    }
  }, [filteredCustomers, selectedClientId]);

  function startEditCustomer(customer) {
    setSelectedClientId(customer.clientId);
    setEditingCustomerId(customer.clientId);
    setCustomerForm({
      clientName: customer.clientName ?? '',
      clientCode: customer.clientCode ?? '',
      isActive: customer.isActive ?? true,
    });
  }

  function startEditContact(contact) {
    setEditingContactId(contact.contactId);
    setContactForm({
      contactName: contact.contactName ?? '',
      title: contact.title ?? '',
      roleDescription: contact.roleDescription ?? '',
      email: contact.email ?? '',
      phone: contact.phone ?? '',
      addressLine1: contact.addressLine1 ?? '',
      addressLine2: contact.addressLine2 ?? '',
      city: contact.city ?? '',
      stateRegion: contact.stateRegion ?? '',
      postalCode: contact.postalCode ?? '',
      country: contact.country ?? 'United States',
      isPrimary: Boolean(contact.isPrimary),
      isActive: true,
      displayOrder: contact.displayOrder ?? 0,
    });
  }

  async function saveCustomer(event) {
    event.preventDefault();

    if (!canManageCustomers) {
      setActionStatus('Customer Directory management is restricted to administrators and project/team coordinators.');
      return;
    }

    const payload = {
      ...customerForm,
      clientCode: customerForm.clientCode || makeClientCode(customerForm.clientName),
    };

    try {
      setActionStatus(editingCustomerId ? 'Updating customer...' : 'Saving customer...');

      const result = editingCustomerId
        ? await sendJson(`/api/customers/${editingCustomerId}`, 'PUT', payload)
        : await sendJson('/api/customers', 'POST', payload);

      setActionStatus(result.message ?? 'Customer saved.');
      setCustomerForm(emptyCustomer);
      setEditingCustomerId('');
      await loadDirectory(editingCustomerId || result.clientId || '');
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to save customer.');
    }
  }

  async function saveContact(event) {
    event.preventDefault();

    if (!canManageCustomers) {
      setActionStatus('Customer contact management is restricted to administrators and project/team coordinators.');
      return;
    }

    if (!selectedCustomer?.clientId) {
      setActionStatus('Select a customer before saving a contact.');
      return;
    }

    try {
      setActionStatus(editingContactId ? 'Updating contact...' : 'Creating contact...');

      const result = editingContactId
        ? await sendJson(`/api/customers/${selectedCustomer.clientId}/contacts/${editingContactId}`, 'PUT', contactForm)
        : await sendJson(`/api/customers/${selectedCustomer.clientId}/contacts`, 'POST', contactForm);

      setActionStatus(result.message ?? 'Contact saved.');
      setContactForm(emptyContact);
      setEditingContactId('');
      await loadDirectory(selectedCustomer.clientId);
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to save contact.');
    }
  }

  async function previewSellCustomers(nextPage = sellFilters.page) {
    setSellState((current) => ({ ...current, previewLoading: true, previewError: '', preview: null }));
    setSelectedSellIds([]);
    try {
      const result = await sendJson('/api/customers/sell/preview', 'POST', {
        search: sellFilters.search,
        relationship: sellFilters.relationship,
        page: nextPage,
        pageSize: sellFilters.pageSize,
      });
      setSellFilters((current) => ({ ...current, page: result.page ?? nextPage }));
      setSellState((current) => ({ ...current, previewLoading: false, preview: result, previewError: '' }));
      setActionStatus(result.message ?? 'SELL customers loaded for review.');
      await loadSellStatus();
    } catch (error) {
      setSellState((current) => ({
        ...current,
        previewLoading: false,
        preview: null,
        previewError: error instanceof Error ? error.message : 'Unable to preview SELL customers.',
      }));
    }
  }

  function toggleSellCustomer(sourceRecordId) {
    setSelectedSellIds((current) => current.includes(sourceRecordId)
      ? current.filter((value) => value !== sourceRecordId)
      : [...current, sourceRecordId]);
  }

  function toggleAllSellCustomers() {
    const ids = sellCustomers.map((customer) => customer.sourceRecordId);
    const allSelected = ids.length > 0 && ids.every((id) => selectedSellIds.includes(id));
    setSelectedSellIds(allSelected ? [] : ids);
  }

  async function importSellCustomers() {
    if (!canManageCustomers) {
      setActionStatus('SELL customer import is restricted to authorized customer managers.');
      return;
    }
    if (!selectedSellIds.length) {
      setActionStatus('Select at least one SELL organization to import or refresh.');
      return;
    }

    setSellState((current) => ({ ...current, importLoading: true, previewError: '' }));
    try {
      const result = await sendJson('/api/customers/sell/import', 'POST', {
        sourceRecordIds: selectedSellIds,
      });
      setActionStatus(result.message ?? 'SELL customer synchronization completed.');
      const preferredClientId = result.results?.find((item) => item.clientId)?.clientId ?? '';
      setSelectedSellIds([]);
      await Promise.all([loadDirectory(preferredClientId), loadSellStatus()]);
      await previewSellCustomers(sellFilters.page);
    } catch (error) {
      setSellState((current) => ({
        ...current,
        importLoading: false,
        previewError: error instanceof Error ? error.message : 'Unable to import SELL customers.',
      }));
      return;
    }
    setSellState((current) => ({ ...current, importLoading: false }));
  }

  return (
    <section className="customer-directory-center" data-module="021">
      <div className="customer-directory-header">
        <div>
          <p className="eyebrow">MODULE 021</p>
          <h2>Customer Directory</h2>
          <p className="muted">
            Pull authoritative customer organizations from SELL, then enrich each ProjectPulse customer with locally maintained contacts, relationships, addresses, and workflow context.
          </p>
        </div>
        <span className="customer-directory-status">{canManageCustomers ? 'Management enabled' : 'Read only'}</span>
      </div>

      {directory.error && <div className="customer-directory-alert error">{directory.error}</div>}
      {actionStatus && <div className="customer-directory-alert">{actionStatus}</div>}

      <section className="customer-sell-sync" aria-labelledby="customer-sell-sync-title">
        <div className="customer-sell-sync-heading">
          <div>
            <p className="eyebrow">SELL CUSTOMER SOURCE</p>
            <h3 id="customer-sell-sync-title">Pull customers from Module 026</h3>
            <p>
              SELL owns the source organization identity. ProjectPulse stores the source link and keeps local contact enrichment separate, so adding a phone number, relationship, address, or primary contact here does not overwrite SELL.
            </p>
          </div>
          <div className="customer-sell-sync-actions">
            <a className="secondary-action" href="#crm-erp-integrations">Open Module 026</a>
            <button type="button" className="secondary-action" onClick={() => void loadSellStatus()} disabled={sellState.statusLoading}>Refresh connection</button>
          </div>
        </div>

        {sellState.statusError ? <div className="customer-sell-alert error">{sellState.statusError}</div> : null}
        {sellState.previewError ? <div className="customer-sell-alert error">{sellState.previewError}</div> : null}

        <div className="customer-sell-readiness-grid">
          <article>
            <span>Module 026 connection</span>
            <strong>{sellState.statusLoading ? 'Checking…' : sellState.status?.provider?.configured ? 'Configured' : 'Not configured'}</strong>
            <small>{sellState.status?.provider?.name ?? 'SELL (Zendesk Sell)'}</small>
          </article>
          <article>
            <span>Authentication</span>
            <strong>{words(sellState.status?.provider?.authModel)}</strong>
            <small>{sellState.status?.provider?.credentialConfigured ? 'Write-only credential saved' : 'Credential required in Module 026'}</small>
          </article>
          <article>
            <span>Availability</span>
            <strong className={sellIsReady ? 'ready' : 'attention'}>{words(sellState.status?.provider?.availabilityStatus)}</strong>
            <small>{sellIsReady ? 'Ready to pull customers' : 'Enable and successfully test SELL in Module 026'}</small>
          </article>
          <article>
            <span>Linked customers</span>
            <strong>{sellState.status?.linkedCustomers ?? 0}</strong>
            <small>ProjectPulse records linked to SELL source IDs</small>
          </article>
          <article>
            <span>Last synchronization</span>
            <strong>{words(sellState.status?.lastRun?.status)}</strong>
            <small>{formatDate(sellState.status?.lastRun?.completedAt ?? sellState.status?.lastRun?.startedAt)}</small>
          </article>
        </div>

        {!sellIsReady ? (
          <div className="customer-sell-guidance">
            <strong>SELL must be ready before customer preview.</strong>
            <ol>
              <li>Open Module 026 and select SELL.</li>
              <li>Choose OAuth 2.0 or API key/access token and save the non-secret configuration.</li>
              <li>Save the write-only credential, enable the connection, and run Test availability.</li>
              <li>Return here and refresh the connection status.</li>
            </ol>
          </div>
        ) : (
          <>
            <div className="customer-sell-filter-grid">
              <label>
                Search this SELL page
                <input value={sellFilters.search} placeholder="Company, industry, city, email…" onChange={(event) => setSellFilters((current) => ({ ...current, search: event.target.value, page: 1 }))} />
              </label>
              <label>
                Relationship
                <select value={sellFilters.relationship} onChange={(event) => setSellFilters((current) => ({ ...current, relationship: event.target.value, page: 1 }))}>
                  <option value="customer">Current and past customers</option>
                  <option value="current_customer">Current customers</option>
                  <option value="past_customer">Past customers</option>
                  <option value="prospect">Prospects</option>
                  <option value="all">All organizations</option>
                </select>
              </label>
              <label>
                Page size
                <select value={sellFilters.pageSize} onChange={(event) => setSellFilters((current) => ({ ...current, pageSize: Number(event.target.value), page: 1 }))}>
                  <option value={25}>25</option>
                  <option value={50}>50</option>
                  <option value={100}>100</option>
                </select>
              </label>
              <button type="button" className="primary-action" onClick={() => void previewSellCustomers(1)} disabled={sellState.previewLoading}>
                {sellState.previewLoading ? 'Pulling from SELL…' : 'Preview SELL customers'}
              </button>
            </div>

            {sellState.preview ? (
              <div className="customer-sell-preview">
                <div className="customer-sell-preview-toolbar">
                  <div>
                    <strong>{sellCustomers.length} organizations on page {sellState.preview.page}</strong>
                    <span>{sellState.preview.linkedCount ?? 0} linked · {sellState.preview.existingMatchCount ?? 0} name matches · {sellState.preview.newCount ?? 0} new</span>
                  </div>
                  <div className="customer-sell-sync-actions">
                    <button type="button" className="secondary-action" onClick={toggleAllSellCustomers}>{selectedSellIds.length === sellCustomers.length && sellCustomers.length ? 'Clear selection' : 'Select page'}</button>
                    <button type="button" className="primary-action" onClick={() => void importSellCustomers()} disabled={!selectedSellIds.length || sellState.importLoading || !canManageCustomers}>
                      {sellState.importLoading ? 'Synchronizing…' : `Import / refresh selected (${selectedSellIds.length})`}
                    </button>
                  </div>
                </div>

                <div className="customer-sell-table-wrap">
                  <table className="customer-sell-table">
                    <thead>
                      <tr>
                        <th aria-label="Select"></th>
                        <th>SELL organization</th>
                        <th>Relationship</th>
                        <th>Source details</th>
                        <th>ProjectPulse action</th>
                      </tr>
                    </thead>
                    <tbody>
                      {sellCustomers.map((customer) => (
                        <tr key={customer.sourceRecordId}>
                          <td><input type="checkbox" aria-label={`Select ${customer.name}`} checked={selectedSellIds.includes(customer.sourceRecordId)} onChange={() => toggleSellCustomer(customer.sourceRecordId)} /></td>
                          <td>
                            <strong>{customer.name}</strong>
                            <small>SELL ID {customer.sourceRecordId} · Updated {formatDate(customer.updatedAt)}</small>
                          </td>
                          <td>
                            <span className="customer-sell-pill">{customer.customerStatus ? `${words(customer.customerStatus)} customer` : words(customer.prospectStatus || 'organization')}</span>
                          </td>
                          <td>
                            <span>{customer.industry || 'Industry not provided'}</span>
                            <small>{[customer.city, customer.stateRegion, customer.country].filter(Boolean).join(', ') || 'Location not provided'}</small>
                          </td>
                          <td>
                            <strong>{words(customer.importAction)}</strong>
                            <small>{customer.localClientName ? `Matched to ${customer.localClientName}` : 'Creates a new ProjectPulse customer'}</small>
                          </td>
                        </tr>
                      ))}
                      {!sellCustomers.length ? (
                        <tr><td colSpan={5}>No SELL organizations matched this page and filter.</td></tr>
                      ) : null}
                    </tbody>
                  </table>
                </div>

                <div className="customer-sell-pagination">
                  <button type="button" className="secondary-action" disabled={sellFilters.page <= 1 || sellState.previewLoading} onClick={() => void previewSellCustomers(Math.max(1, sellFilters.page - 1))}>Previous page</button>
                  <span>Page {sellFilters.page}</span>
                  <button type="button" className="secondary-action" disabled={sellCustomers.length < sellFilters.pageSize || sellState.previewLoading} onClick={() => void previewSellCustomers(sellFilters.page + 1)}>Next page</button>
                </div>
              </div>
            ) : null}
          </>
        )}

        <div className="customer-sell-history">
          <button type="button" className="customer-sell-history-toggle" onClick={() => setShowSellHistory((current) => !current)}>
            <span>Synchronization history</span>
            <strong>{showSellHistory ? 'Hide' : `Show ${sellState.runs.length}`}</strong>
          </button>
          {showSellHistory ? (
            <div className="customer-sell-history-list">
              {sellState.runs.map((run) => (
                <article key={run.runId}>
                  <div><strong>{words(run.status)}</strong><small>{formatDate(run.completedAt ?? run.startedAt)}</small></div>
                  <span>{run.imported} created · {run.updated} refreshed · {run.linked} linked · {run.failed} failed</span>
                  {run.errorCode ? <small>{words(run.errorCode)}</small> : null}
                </article>
              ))}
              {!sellState.runs.length ? <p>No SELL customer synchronization history is recorded.</p> : null}
            </div>
          ) : null}
        </div>
      </section>

      <div className="customer-directory-summary-grid">
        <article><span>Customers</span><strong>{directory.loading ? '...' : customers.length}</strong><small>{customerDirectoryMetrics.activeCustomers} active · {customerDirectoryMetrics.inactiveCustomers} inactive</small></article>
        <article><span>Contacts</span><strong>{directory.loading ? '...' : contacts.length}</strong><small>10 active contacts maximum per customer</small></article>
        <article><span>Needs contact</span><strong>{directory.loading ? '...' : customerDirectoryMetrics.customersWithoutContacts}</strong><small>Customer records without active contacts</small></article>
        <article><span>Cost-ready customers</span><strong>{directory.loading ? '...' : customerDirectoryMetrics.customersWithCostContext}</strong><small>Customers with project or intake cost context</small></article>
        <article><span>Project planned cost</span><strong>{fmtMoney(customers.reduce((sum, customer) => sum + Number(customer.plannedProjectTotalCost ?? 0), 0))}</strong><small>Loaded project cost plans</small></article>
        <article><span>Intake pipeline cost</span><strong>{fmtMoney(customers.reduce((sum, customer) => sum + Number(customer.plannedIntakeTotalCost ?? 0), 0))}</strong><small>Open intake cost plans</small></article>
      </div>

      <div className="customer-directory-layout">
        <article className="customer-directory-panel customer-list-panel">
          <div className="customer-directory-panel-header">
            <div>
              <h3>Customers</h3>
              <p className="muted">Search and select a customer to view locally enriched contacts and cost context.</p>
            </div>
          </div>

          <input
            className="customer-search-input"
            value={searchTerm}
            placeholder="Search customer, code, or contact…"
            onChange={(event) => setSearchTerm(event.target.value)}
          />

          <div className="customer-list">
            {filteredCustomers.map((customer) => (
              <button
                type="button"
                className={`customer-list-item ${customer.clientId === selectedCustomer?.clientId ? 'selected' : ''}`}
                key={customer.clientId}
                onClick={() => {
                  setSelectedClientId(customer.clientId);
                  setCustomerForm(emptyCustomer);
                  setEditingCustomerId('');
                  setEditingContactId('');
                  setContactForm(emptyContact);
                }}
              >
                <strong>{customer.clientName}</strong>
                <span>{customer.clientCode} · {customer.activeContactCount}/10 contacts</span>
                <small>{customer.activeProjectCount} active projects · {customer.intakeCount} intake records</small>
                {customer.isActive === false && <em>Inactive customer</em>}
              </button>
            ))}

            {!directory.loading && filteredCustomers.length === 0 && (
              <p className="muted">No customers match the current search. Search by customer name, code, contact name, contact email, or relationship.</p>
            )}
          </div>
        </article>

        <article className="customer-directory-panel customer-detail-panel">
          {selectedCustomer ? (
            <>
              <div className="customer-detail-heading">
                <div>
                  <h3>{selectedCustomer.clientName}</h3>
                  <p className="muted">
                    {selectedCustomer.clientCode} · {selectedContacts.length}/10 active contacts
                    <span className={`customer-state-pill ${selectedCustomer.isActive === false ? 'inactive' : 'active'}`}>
                      {selectedCustomer.isActive === false ? 'Inactive' : 'Active'}
                    </span>
                  </p>
                </div>
                {canManageCustomers && (
                  <button type="button" className="secondary-action" onClick={() => startEditCustomer(selectedCustomer)}>
                    Edit customer
                  </button>
                )}
              </div>

              <div className="customer-local-enrichment-banner">
                <strong>Local enrichment</strong>
                <span>Contacts, titles, relationships, phone numbers, and addresses added below remain Pulse-owned and are preserved when the customer is refreshed from SELL.</span>
              </div>

              <div className="customer-cost-grid">
                <article><span>Project planned cost</span><strong>{fmtMoney(selectedCustomer.plannedProjectTotalCost)}</strong></article>
                <article><span>Intake pipeline cost</span><strong>{fmtMoney(selectedCustomer.plannedIntakeTotalCost)}</strong></article>
                <article><span>Projects over plan</span><strong>{selectedCustomer.projectsOverPlanCount ?? 0}</strong></article>
              </div>

              <div className="customer-readiness-panel">
                <div>
                  <h4>Customer workflow readiness</h4>
                  <p className="muted">Checks whether this customer is ready for intake, assignment, cost review, and approval/export workflows.</p>
                </div>
                <div className="customer-readiness-grid">
                  {selectedReadinessItems.map((item) => (
                    <article className={`customer-readiness-item ${item.ready ? 'ready' : 'attention'}`} key={item.label}>
                      <span>{item.ready ? 'Ready' : 'Needs attention'}</span>
                      <strong>{item.label}</strong>
                      <small>{item.detail}</small>
                    </article>
                  ))}
                </div>
              </div>

              <div className="customer-contact-list">
                {selectedContacts.map((contact) => (
                  <div className="customer-contact-row" key={contact.contactId}>
                    <div>
                      <span>{contact.isPrimary ? 'Primary contact' : 'Contact'}</span>
                      <strong>{contact.contactName}</strong>
                      <small>{contact.title || 'No title'} · {contact.roleDescription || 'No role recorded'}</small>
                      <small>{contact.email || 'No email'} · {contact.phone || 'No phone'}</small>
                    </div>
                    {canManageCustomers && (
                      <button type="button" className="secondary-action" onClick={() => startEditContact(contact)}>
                        Edit
                      </button>
                    )}
                  </div>
                ))}

                {selectedContacts.length === 0 && <p className="muted">No active contacts are loaded for this customer.</p>}
              </div>
            </>
          ) : (
            <p className="muted">Select a customer to view details.</p>
          )}
        </article>
      </div>

      {canManageCustomers && (
        <div className="customer-directory-layout management-layout">
          <article className="customer-directory-panel">
            <h3>{editingCustomerId ? 'Edit Customer' : 'Add Customer'}</h3>
            <p className="muted">Manual customers remain supported. When a matching SELL organization is later imported, ProjectPulse links the existing record instead of creating a duplicate.</p>
            <form className="customer-directory-form" onSubmit={saveCustomer}>
              <label>
                Customer name
                <input
                  value={customerForm.clientName}
                  onChange={(event) => setCustomerForm((current) => ({
                    ...current,
                    clientName: event.target.value,
                    clientCode: current.clientCode || makeClientCode(event.target.value),
                  }))}
                  required
                />
              </label>
              <label>
                Customer code
                <input
                  value={customerForm.clientCode}
                  onChange={(event) => setCustomerForm((current) => ({ ...current, clientCode: event.target.value.toUpperCase() }))}
                  required
                />
              </label>
              <label className="checkbox-label">
                <input
                  type="checkbox"
                  checked={customerForm.isActive}
                  onChange={(event) => setCustomerForm((current) => ({ ...current, isActive: event.target.checked }))}
                />
                Active customer
              </label>
              <button className="primary-action" type="submit">{editingCustomerId ? 'Update customer' : 'Save customer'}</button>
              {editingCustomerId && <button type="button" className="secondary-action" onClick={() => { setEditingCustomerId(''); setCustomerForm(emptyCustomer); }}>Cancel edit</button>}
            </form>
          </article>

          <article className="customer-directory-panel">
            <h3>{editingContactId ? 'Edit Contact' : 'Add Contact'}</h3>
            <p className="muted">Selected customer: {selectedCustomer?.clientName ?? 'None selected'}. These details are local ProjectPulse enrichment and are not overwritten by SELL synchronization.</p>
            <form className="customer-directory-form" onSubmit={saveContact}>
              <label>Contact name<input value={contactForm.contactName} onChange={(event) => setContactForm((current) => ({ ...current, contactName: event.target.value }))} required /></label>
              <label>Title<input value={contactForm.title} onChange={(event) => setContactForm((current) => ({ ...current, title: event.target.value }))} /></label>
              <label>Role / relationship<input value={contactForm.roleDescription} onChange={(event) => setContactForm((current) => ({ ...current, roleDescription: event.target.value }))} /></label>
              <label>Email<input type="email" value={contactForm.email} onChange={(event) => setContactForm((current) => ({ ...current, email: event.target.value }))} /></label>
              <label>Phone<input value={contactForm.phone} onChange={(event) => setContactForm((current) => ({ ...current, phone: event.target.value }))} /></label>
              <label>Address line 1<input value={contactForm.addressLine1} onChange={(event) => setContactForm((current) => ({ ...current, addressLine1: event.target.value }))} /></label>
              <label>Address line 2<input value={contactForm.addressLine2} onChange={(event) => setContactForm((current) => ({ ...current, addressLine2: event.target.value }))} /></label>
              <label>City<input value={contactForm.city} onChange={(event) => setContactForm((current) => ({ ...current, city: event.target.value }))} /></label>
              <label>State / region<input value={contactForm.stateRegion} onChange={(event) => setContactForm((current) => ({ ...current, stateRegion: event.target.value }))} /></label>
              <label>Postal code<input value={contactForm.postalCode} onChange={(event) => setContactForm((current) => ({ ...current, postalCode: event.target.value }))} /></label>
              <label>Country<input value={contactForm.country} onChange={(event) => setContactForm((current) => ({ ...current, country: event.target.value }))} /></label>
              <label>Display order<input type="number" min="0" value={contactForm.displayOrder} onChange={(event) => setContactForm((current) => ({ ...current, displayOrder: Number(event.target.value || 0) }))} /></label>
              <label className="checkbox-label"><input type="checkbox" checked={contactForm.isPrimary} onChange={(event) => setContactForm((current) => ({ ...current, isPrimary: event.target.checked }))} />Primary contact</label>
              <label className="checkbox-label"><input type="checkbox" checked={contactForm.isActive} onChange={(event) => setContactForm((current) => ({ ...current, isActive: event.target.checked }))} />Active contact</label>
              <button className="primary-action" type="submit">{editingContactId ? 'Update contact' : 'Add contact'}</button>
              {editingContactId && <button type="button" className="secondary-action" onClick={() => { setEditingContactId(''); setContactForm(emptyContact); }}>Cancel edit</button>}
            </form>
          </article>
        </div>
      )}
    </section>
  );
}
