import { useEffect, useMemo, useState } from 'react';
import './customer-directory-center.css';

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
    headers: getProjectPulseAuthHeaders()
  });

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

async function sendJson(path, method, payload) {
  const response = await fetch(path, {
    method,
    headers: { 'Content-Type': 'application/json', ...getProjectPulseAuthHeaders() },
    body: JSON.stringify(payload)
  });

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

function fmtMoney(value) {
  const amount = Number(value ?? 0);
  return amount.toLocaleString(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
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
  isActive: true
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
  displayOrder: 0
};

export default function CustomerDirectoryCenter({ canManageCustomers = false }) {
  const [directory, setDirectory] = useState({ loading: true, data: null, error: null });
  const [actionStatus, setActionStatus] = useState('');
  const [selectedClientId, setSelectedClientId] = useState('');
  const [customerForm, setCustomerForm] = useState(emptyCustomer);
  const [editingCustomerId, setEditingCustomerId] = useState('');
  const [contactForm, setContactForm] = useState(emptyContact);
  const [editingContactId, setEditingContactId] = useState('');
  const [searchTerm, setSearchTerm] = useState('');

  async function loadDirectory() {
    setDirectory((current) => ({ ...current, loading: true, error: null }));

    try {
      const result = await fetchJson('/api/customers/overview');
      setDirectory({ loading: false, data: result, error: null });

      if (!selectedClientId && result.customers?.length) {
        setSelectedClientId(result.customers[0].clientId);
      }
    } catch (error) {
      setDirectory({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load customer directory.'
      });
    }
  }

  useEffect(() => {
    loadDirectory();
  }, []);

  const customers = directory.data?.customers ?? [];
  const contacts = directory.data?.contacts ?? [];

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
      Number(customer.plannedProjectTotalCost ?? 0) > 0 ||
      Number(customer.plannedIntakeTotalCost ?? 0) > 0
    )).length;

    return {
      activeCustomers,
      inactiveCustomers,
      customersWithoutContacts,
      customersWithCostContext
    };
  }, [customers]);

  const selectedReadinessItems = selectedCustomer ? [
    {
      label: 'Customer record',
      ready: selectedCustomer.isActive !== false,
      detail: selectedCustomer.isActive === false ? 'Customer is inactive.' : 'Customer is active and available for intake/project workflows.'
    },
    {
      label: 'Contact coverage',
      ready: selectedContacts.length > 0,
      detail: selectedContacts.length > 0 ? `${selectedContacts.length} active contact(s) loaded.` : 'No active contact is loaded for this customer.'
    },
    {
      label: 'Primary contact',
      ready: Boolean(selectedPrimaryContact),
      detail: selectedPrimaryContact ? `${selectedPrimaryContact.contactName} is marked primary.` : 'No primary contact is selected.'
    },
    {
      label: 'Cost context',
      ready: Number(selectedCustomer.plannedProjectTotalCost ?? 0) > 0 || Number(selectedCustomer.plannedIntakeTotalCost ?? 0) > 0,
      detail: 'Project and intake planned cost values are shown for downstream cost review.'
    },
    {
      label: 'Over-plan risk',
      ready: Number(selectedCustomer.projectsOverPlanCount ?? 0) === 0,
      detail: Number(selectedCustomer.projectsOverPlanCount ?? 0) === 0
        ? 'No over-plan project count is currently reported.'
        : `${selectedCustomer.projectsOverPlanCount} project(s) are reporting over-plan risk.`
    }
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
      isActive: customer.isActive ?? true
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
      displayOrder: contact.displayOrder ?? 0
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
      clientCode: customerForm.clientCode || makeClientCode(customerForm.clientName)
    };

    try {
      setActionStatus(editingCustomerId ? 'Updating customer...' : 'Saving customer...');

      const result = editingCustomerId
        ? await sendJson(`/api/customers/${editingCustomerId}`, 'PUT', payload)
        : await sendJson('/api/customers', 'POST', payload);

      setActionStatus(result.message ?? 'Customer saved.');
      setCustomerForm(emptyCustomer);
      setEditingCustomerId('');
      await loadDirectory();
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
      await loadDirectory();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to save contact.');
    }
  }

  return (
    <section className="customer-directory-center">
      <div className="customer-directory-header">
        <div>
          <p className="eyebrow">019M-AH</p>
          <h2>Customer Directory</h2>
          <p className="muted">
            Manage customer records, up to 10 active contacts per customer, and intake/project cost readiness.
          </p>
        </div>
        <span className="customer-directory-status">{canManageCustomers ? 'Management enabled' : 'Read only'}</span>
      </div>

      {directory.error && <div className="customer-directory-alert error">{directory.error}</div>}
      {actionStatus && <div className="customer-directory-alert">{actionStatus}</div>}

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
              <p className="muted">Search and select a customer to view contacts and cost context.</p>
            </div>
          </div>

          <input
            className="customer-search-input"
            value={searchTerm}
            placeholder="Search customer or code..."
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
            <form className="customer-directory-form" onSubmit={saveCustomer}>
              <label>
                Customer name
                <input
                  value={customerForm.clientName}
                  onChange={(event) => setCustomerForm((current) => ({
                    ...current,
                    clientName: event.target.value,
                    clientCode: current.clientCode || makeClientCode(event.target.value)
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
            <p className="muted">Selected customer: {selectedCustomer?.clientName ?? 'None selected'}.</p>
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
