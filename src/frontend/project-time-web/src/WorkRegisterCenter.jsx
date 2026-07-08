import { useEffect, useLayoutEffect, useMemo, useState } from 'react';
import './work-register-center.css';

function readSession() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (!parsed?.sessionToken) return null;
    return parsed;
  } catch {
    return null;
  }
}

function authHeaders(extra = {}) {
  const session = readSession();
  const headers = { ...extra };

  if (session?.sessionToken) {
    headers['X-ProjectPulse-Session'] = session.sessionToken;
  }

  try {
    const rawViewAs = window.localStorage.getItem('projectPulseViewAsUser');
    const viewAs = rawViewAs ? JSON.parse(rawViewAs) : null;
    if (viewAs?.userId) {
      headers['X-ProjectPulse-View-As-User'] = viewAs.userId;
    }
  } catch {
    // Ignore malformed view-as cache.
  }

  return headers;
}

async function fetchJson(url) {
  const response = await fetch(url, {
    headers: authHeaders()
  });

  if (!response.ok) {
    let message = `HTTP ${response.status}`;
    try {
      const error = await response.json();
      message = error.message || error.status || message;
    } catch {
      // Ignore non-JSON responses.
    }
    throw new Error(message);
  }

  return response.json();
}


async function postJson(url, payload) {
  const response = await fetch(url, {
    method: 'POST',
    headers: authHeaders({ 'Content-Type': 'application/json' }),
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    let message = `HTTP ${response.status}`;
    try {
      const error = await response.json();
      message = error.message || error.status || message;
    } catch {
      // Ignore non-JSON responses.
    }
    throw new Error(message);
  }

  return response.json();
}

function money(value) {
  const numberValue = Number(value ?? 0);
  return numberValue.toLocaleString(undefined, {
    style: 'currency',
    currency: 'USD'
  });
}

function hours(value) {
  return Number(value ?? 0).toLocaleString(undefined, {
    maximumFractionDigits: 2
  });
}

function normalize(value) {
  return String(value ?? '').trim().toLowerCase();
}

function labelize(value) {
  return String(value || 'Unknown')
    .replaceAll('_', ' ')
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function uniqueValues(items, getter) {
  return [...new Set(items.map(getter).filter(Boolean))]
    .sort((a, b) => String(a).localeCompare(String(b)));
}


function userHasAnyRole(user, roleCodes) {
  const assigned = new Set((user?.roleCodes ?? []).map((roleCode) => String(roleCode).toUpperCase()));
  return roleCodes.some((roleCode) => assigned.has(String(roleCode).toUpperCase()));
}

function activeUsersByRole(users, roleCodes) {
  const activeUsers = users.filter((user) => user.isActive !== false);
  const filtered = activeUsers.filter((user) => userHasAnyRole(user, roleCodes));
  return filtered.length ? filtered : activeUsers;
}

function dateOnly(value) {
  if (!value) return '';
  return String(value).slice(0, 10);
}

export default function WorkRegisterCenter() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });
  const [searchTerm, setSearchTerm] = useState('');
  const [lifecycleFilter, setLifecycleFilter] = useState('active');
  const [workTypeFilter, setWorkTypeFilter] = useState('all');
  const [customerFilter, setCustomerFilter] = useState('all');
  const [statusFilter, setStatusFilter] = useState('all');
  const [personFilter, setPersonFilter] = useState('all');
  const [burnFilter, setBurnFilter] = useState('all');

  const [editFoundation, setEditFoundation] = useState({ loading: true, data: null, error: null });
  const [selectedWorkItem, setSelectedWorkItem] = useState(null);
  const [editForm, setEditForm] = useState({});
  const [editStatus, setEditStatus] = useState('');
  // 055C_2_WORK_REGISTER_EDIT_DRAWER


  useLayoutEffect(() => {
    const focusWorkRegisterRoute = () => {
      const panel = document.getElementById('work-register') || document.querySelector('.work-register-route-panel');
      if (!panel) return;
      const top = Math.max(0, panel.getBoundingClientRect().top + window.scrollY - 12);
      window.scrollTo({ top, behavior: 'auto' });
    };

    focusWorkRegisterRoute();
    const timers = [
      window.setTimeout(focusWorkRegisterRoute, 50),
      window.setTimeout(focusWorkRegisterRoute, 200),
      window.setTimeout(focusWorkRegisterRoute, 600)
    ];

    return () => timers.forEach((timer) => window.clearTimeout(timer));
  }, []);


  async function loadEditFoundation() {
    setEditFoundation((current) => ({ ...current, loading: true, error: null }));

    try {
      const data = await fetchJson('/api/work-register/edit-foundation');
      setEditFoundation({ loading: false, data, error: null });
    } catch (error) {
      setEditFoundation({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load edit foundation.'
      });
    }
  }

  async function load() {
    setPayload((current) => ({ ...current, loading: true, error: null }));

    try {
      const data = await fetchJson('/api/work-register/overview');
      setPayload({ loading: false, data, error: null });
    } catch (error) {
      setPayload({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load Work Register.'
      });
    }
  }

  useEffect(() => {
    load();
    loadEditFoundation();
  }, []);

  const workItems = payload.data?.workItems ?? [];
  const summary = payload.data?.summary ?? {
    total: 0,
    active: 0,
    closed: 0,
    projects: 0,
    intakes: 0
  };

  const peopleOptions = useMemo(() => uniqueValues(workItems, (item) => [
    item.projectManager,
    item.projectCoordinator,
    item.accountExecutive,
    item.solutionArchitect,
    item.insideSales,
    ...(item.assignedEngineers ?? [])
  ].filter(Boolean).join('|')).flatMap((group) => group.split('|')).filter(Boolean).sort(), [workItems]);

  const filteredItems = useMemo(() => {
    const search = normalize(searchTerm);

    return workItems.filter((item) => {
      if (lifecycleFilter !== 'all' && normalize(item.lifecycle) !== lifecycleFilter) return false;
      if (workTypeFilter !== 'all' && normalize(item.workType) !== normalize(workTypeFilter)) return false;
      if (customerFilter !== 'all' && normalize(item.customerName) !== normalize(customerFilter)) return false;
      if (statusFilter !== 'all' && normalize(item.status) !== normalize(statusFilter)) return false;
      if (burnFilter !== 'all' && normalize(item.burnStatus) !== normalize(burnFilter)) return false;

      if (personFilter !== 'all') {
        const people = [
          item.projectManager,
          item.projectCoordinator,
          item.accountExecutive,
          item.solutionArchitect,
          item.insideSales,
          ...(item.assignedEngineers ?? [])
        ].map(normalize);

        if (!people.includes(normalize(personFilter))) return false;
      }

      if (!search) return true;

      const haystack = [
        item.customerName,
        item.workName,
        item.workType,
        item.status,
        item.contractType,
        item.projectManager,
        item.projectCoordinator,
        item.accountExecutive,
        item.solutionArchitect,
        item.insideSales,
        item.sourceTable,
        ...(item.assignedEngineers ?? [])
      ].join(' ').toLowerCase();

      return haystack.includes(search);
    });
  }, [burnFilter, customerFilter, lifecycleFilter, personFilter, searchTerm, statusFilter, workItems, workTypeFilter]);

  const totals = useMemo(() => filteredItems.reduce((accumulator, item) => {
    accumulator.allocatedHours += Number(item.allocatedHours ?? 0);
    accumulator.usedHours += Number(item.usedHours ?? 0);
    accumulator.totalCost += Number(item.totalCost ?? 0);
    accumulator.costUsed += Number(item.costUsed ?? 0);
    accumulator.remainingCost += Number(item.remainingCost ?? 0);
    return accumulator;
  }, {
    allocatedHours: 0,
    usedHours: 0,
    totalCost: 0,
    costUsed: 0,
    remainingCost: 0
  }), [filteredItems]);

  const workTypeOptions = uniqueValues(workItems, (item) => item.workType);
  const customerOptions = uniqueValues(workItems, (item) => item.customerName);
  const statusOptions = uniqueValues(workItems, (item) => item.status);
  const burnOptions = uniqueValues(workItems, (item) => item.burnStatus);


  const canEditWorkRegister = editFoundation.data?.canEditWorkRegister === true;
  const editCustomerOptions = editFoundation.data?.customers ?? [];
  const userOptions = editFoundation.data?.users ?? [];
  const contractOptions = editFoundation.data?.contractTypes ?? [];
  const statusEditOptions = editFoundation.data?.statuses ?? [];

  const pmOptions = activeUsersByRole(userOptions, ['PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGEMENT_LEAD', 'PM_TEAM_LEAD']);
  const pcOptions = activeUsersByRole(userOptions, ['PROJECT_TEAM_COORDINATOR', 'PROJECT_COORDINATOR', 'PROJECT_MANAGEMENT']);
  const aeOptions = activeUsersByRole(userOptions, ['ACCOUNT_EXECUTIVE', 'SALES', 'SALES_EXECUTIVE', 'AE']);
  const saOptions = activeUsersByRole(userOptions, ['SOLUTION_ARCHITECT', 'SA', 'ARCHITECT', 'ANALYST_DEV_ARCHITECT']);
  const saaOptions = activeUsersByRole(userOptions, ['SAA', 'INSIDE_SALES', 'SALES_SUPPORT', 'SALES']);

  function openEditDrawer(item) {
    setSelectedWorkItem(item);
    setEditStatus('');
    setEditForm({
      workId: item.workId,
      sourceTable: item.sourceTable,
      clientId: item.customerId || '',
      contractType: item.contractType || '',
      projectManagerUserId: '',
      projectCoordinatorUserId: '',
      accountExecutiveUserId: '',
      solutionArchitectUserId: '',
      insideSalesUserId: '',
      projectStartDate: dateOnly(item.startDate),
      estimatedEndDate: dateOnly(item.estimatedEndDate),
      sowSignedDate: dateOnly(item.sowSignedDate),
      status: item.status || '',
      editReason: ''
    });
  }

  function closeEditDrawer() {
    setSelectedWorkItem(null);
    setEditForm({});
    setEditStatus('');
  }

  function updateEditField(field, value) {
    setEditForm((current) => ({
      ...current,
      [field]: value
    }));
  }

  async function saveProjectSetup(event) {
    event.preventDefault();

    if (!selectedWorkItem) return;

    if (!canEditWorkRegister) {
      setEditStatus('This page is view-only for your role. Only Project Team Coordinators, Administrators, and Super Administrators can save changes.');
      return;
    }

    if (!editForm.editReason?.trim()) {
      setEditStatus('Edit reason is required.');
      return;
    }

    const payload = {
      workId: selectedWorkItem.workId,
      sourceTable: selectedWorkItem.sourceTable,
      editReason: editForm.editReason.trim()
    };

    // 055C_3_WORK_REGISTER_CHANGED_FIELD_PAYLOAD_START
    /* 055C_4_CASE_INSENSITIVE_CHANGED_FIELD_START */
    const addIfChanged = (field, originalValue = '') => {
      const nextValue = String(editForm[field] ?? '').trim();
      const priorValue = String(originalValue ?? '').trim();

      if (nextValue && nextValue.toLowerCase() !== priorValue.toLowerCase()) {
        payload[field] = nextValue;
      }
    };
    /* 055C_4_CASE_INSENSITIVE_CHANGED_FIELD_END */

    const addIfSelected = (field) => {
      const nextValue = String(editForm[field] ?? '').trim();

      if (nextValue) {
        payload[field] = nextValue;
      }
    };

    addIfChanged('clientId', selectedWorkItem.customerId || '');
    addIfChanged('contractType', selectedWorkItem.contractType || '');
    addIfChanged('projectStartDate', dateOnly(selectedWorkItem.startDate));
    addIfChanged('estimatedEndDate', dateOnly(selectedWorkItem.estimatedEndDate));
    addIfChanged('sowSignedDate', dateOnly(selectedWorkItem.sowSignedDate));
    addIfChanged('status', selectedWorkItem.status || '');

    // User dropdowns start as blank, meaning "keep current".
    // Send only if PTC/Admin intentionally selects a replacement.
    addIfSelected('projectManagerUserId');
    addIfSelected('projectCoordinatorUserId');
    addIfSelected('accountExecutiveUserId');
    addIfSelected('solutionArchitectUserId');
    addIfSelected('insideSalesUserId');

    if (Object.keys(payload).length <= 3) {
      setEditStatus('No setup changes were selected. Choose at least one field to update.');
      return;
    }
    // 055C_3_WORK_REGISTER_CHANGED_FIELD_PAYLOAD_END

    setEditStatus('Saving project setup...');

    try {
      const result = await postJson('/api/work-register/projects/update', payload);
      setEditStatus(result.message || 'Project setup saved.');
      await load();
      window.setTimeout(() => {
        closeEditDrawer();
      }, 800);
    } catch (error) {
      setEditStatus(error instanceof Error ? error.message : 'Unable to save project setup.');
    }
  }


  return (
    <section className="work-register-center">
      <div className="work-register-header">
        <div>
          <p className="eyebrow">Work Register</p>
          <h2>Active, closed, and historical work</h2>
          <p className="muted">
            Search and filter projects, intakes, tasks, customers, stakeholders, documents, hours, and cost indicators without removing any existing modules.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={load}>
          Refresh
        </button>
      </div>

      {payload.error ? <div className="work-register-banner error">{payload.error}</div> : null}

      <div className="work-register-summary">
        <article>
          <span>Total work</span>
          <strong>{payload.loading ? '...' : summary.total}</strong>
          <small>{filteredItems.length} shown</small>
        </article>
        <article>
          <span>Active</span>
          <strong>{payload.loading ? '...' : summary.active}</strong>
          <small>Open or in-progress</small>
        </article>
        <article>
          <span>Closed / historical</span>
          <strong>{payload.loading ? '...' : summary.closed}</strong>
          <small>Closed, completed, archived, or done</small>
        </article>
        <article>
          <span>Filtered cost</span>
          <strong>{money(totals.totalCost)}</strong>
          <small>{hours(totals.usedHours)} used hours</small>
        </article>
      </div>

      <div className="work-register-toolbar">
        <label className="wide">
          Search
          <input
            type="search"
            value={searchTerm}
            onChange={(event) => setSearchTerm(event.target.value)}
            placeholder="Search customer, project, PM, engineer, AE, SA, SAA, task..."
          />
        </label>
        <label>
          Lifecycle
          <select value={lifecycleFilter} onChange={(event) => setLifecycleFilter(event.target.value)}>
            <option value="active">Active</option>
            <option value="closed">Closed / historical</option>
            <option value="all">All</option>
          </select>
        </label>
        <label>
          Work type
          <select value={workTypeFilter} onChange={(event) => setWorkTypeFilter(event.target.value)}>
            <option value="all">All work types</option>
            {workTypeOptions.map((value) => <option value={value} key={value}>{labelize(value)}</option>)}
          </select>
        </label>
        <label>
          Customer
          <select value={customerFilter} onChange={(event) => setCustomerFilter(event.target.value)}>
            <option value="all">All customers</option>
            {customerOptions.map((value) => <option value={value} key={value}>{value}</option>)}
          </select>
        </label>
        <label>
          Status
          <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
            <option value="all">All statuses</option>
            {statusOptions.map((value) => <option value={value} key={value}>{labelize(value)}</option>)}
          </select>
        </label>
        <label>
          Person
          <select value={personFilter} onChange={(event) => setPersonFilter(event.target.value)}>
            <option value="all">All people</option>
            {peopleOptions.map((value) => <option value={value} key={value}>{value}</option>)}
          </select>
        </label>
        <label>
          Burn
          <select value={burnFilter} onChange={(event) => setBurnFilter(event.target.value)}>
            <option value="all">All burn states</option>
            {burnOptions.map((value) => <option value={value} key={value}>{labelize(value)}</option>)}
          </select>
        </label>
      </div>

      <div className="work-register-totalbar">
        <span><strong>{filteredItems.length}</strong> records</span>
        <span><strong>{hours(totals.allocatedHours)}</strong> allocated hours</span>
        <span><strong>{hours(totals.usedHours)}</strong> used hours</span>
        <span><strong>{money(totals.costUsed)}</strong> cost used</span>
        <span><strong>{money(totals.remainingCost)}</strong> remaining cost</span>
      </div>

      <div className="work-register-table-wrap">
        <table className="work-register-table">
          <thead>
            <tr>
              <th>Customer / Work</th>
              <th>Type / Status</th>
              <th>Stakeholders</th>
              <th>Engineers</th>
              <th>Dates</th>
              <th>Tasks / Docs</th>
              <th>Hours</th>
              <th>Cost / Burn</th>
            </tr>
          </thead>
          <tbody>
            {filteredItems.map((item) => (
              <tr key={`${item.sourceTable}-${item.workId}`}>
                <td>
                  <strong>{item.customerName || 'No customer linked'}</strong>
                  <small>{item.workName}</small>
                  <small>{item.contractType ? `Contract: ${labelize(item.contractType)}` : 'Contract: not set'}</small>

                  <button type="button" className="work-register-row-action" onClick={() => openEditDrawer(item)}>
                    {canEditWorkRegister ? 'Edit work' : 'View details'}
                  </button>
                </td>
                <td>
                  <span className={`work-register-pill lifecycle-${normalize(item.lifecycle)}`}>
                    {labelize(item.lifecycle)}
                  </span>
                  <span className="work-register-pill">{labelize(item.workType)}</span>
                  <small>Status: {labelize(item.status)}</small>
                  <small>Source: {item.sourceTable}</small>
                </td>
                <td>
                  <small>PM: {item.projectManager || 'Not assigned'}</small>
                  <small>PC: {item.projectCoordinator || 'Not assigned'}</small>
                  <small>AE: {item.accountExecutive || 'Not assigned'}</small>
                  <small>SA: {item.solutionArchitect || 'Not assigned'}</small>
                  <small>SAA: {item.insideSales || 'Not assigned'}</small>
                </td>
                <td>
                  {(item.assignedEngineers ?? []).length ? (
                    <div className="engineer-list">
                      {(item.assignedEngineers ?? []).map((engineer) => <span key={engineer}>{engineer}</span>)}
                    </div>
                  ) : (
                    <small>No engineers assigned</small>
                  )}
                </td>
                <td>
                  <small>Start: {item.startDate || 'Not set'}</small>
                  <small>End: {item.estimatedEndDate || 'Not set'}</small>
                  <small>Closed: {item.closedDate || 'Not closed'}</small>
                  <small>SOW signed: {item.sowSignedDate || 'Not set'}</small>
                </td>
                <td>
                  <small>Total tasks: {item.taskCount ?? 0}</small>
                  <small>Open: {item.openTaskCount ?? 0}</small>
                  <small>Closed: {item.closedTaskCount ?? 0}</small>
                  <small>Docs: {item.documentCount ?? 0}</small>
                </td>
                <td>
                  <small>Allocated: {hours(item.allocatedHours)}</small>
                  <small>Used: {hours(item.usedHours)}</small>
                  <small>Eng allocated: {hours(item.engineeringHoursAllocated)}</small>
                  <small>PM allocated: {hours(item.pmHoursAllocated)}</small>
                </td>
                <td>
                  <small>Total: {money(item.totalCost)}</small>
                  <small>Used: {money(item.costUsed)}</small>
                  <small>Remaining: {money(item.remainingCost)}</small>
                  <span className={`burn-pill burn-${normalize(item.burnStatus)}`}>
                    {labelize(item.burnStatus)} {Number(item.burnPercent ?? 0) > 0 ? `${item.burnPercent}%` : ''}
                  </span>
                </td>
              </tr>
            ))}
            {!payload.loading && filteredItems.length === 0 ? (
              <tr>
                <td colSpan="8">No work items match the current filters.</td>
              </tr>
            ) : null}
            {payload.loading ? (
              <tr>
                <td colSpan="8">Loading Work Register...</td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>

      {selectedWorkItem ? (
        <div className="work-register-drawer-backdrop" role="presentation">
          <aside className="work-register-drawer" aria-label="Work Register project setup editor">
            <div className="work-register-drawer-header">
              <div>
                <p className="eyebrow">{canEditWorkRegister ? 'Edit Project Setup' : 'View Project Setup'}</p>
                <h3>{selectedWorkItem.workName}</h3>
                <p className="muted">
                  {selectedWorkItem.customerName || 'No customer linked'} · {labelize(selectedWorkItem.sourceTable)}
                </p>
              </div>
              <button type="button" className="secondary-action" onClick={closeEditDrawer}>Close</button>
            </div>

            <div className={canEditWorkRegister ? 'work-register-edit-notice allowed' : 'work-register-edit-notice'}>
              {canEditWorkRegister
                ? 'Project Team Coordinator/Admin edit mode. All saves require a reason and are audited.'
                : 'View-only mode. Solution Architects, PMs, Engineers, Sales, and SAA cannot edit Work Register setup fields.'}
            </div>

            {editFoundation.error ? (
              <div className="work-register-banner error">{editFoundation.error}</div>
            ) : null}

            {editStatus ? (
              <div className="work-register-banner">{editStatus}</div>
            ) : null}

            <form className="work-register-edit-form" onSubmit={saveProjectSetup}>
              <label>
                Customer
                <select
                  value={editForm.clientId || ''}
                  onChange={(event) => updateEditField('clientId', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current / not linked</option>
                  {editCustomerOptions.map((customer) => (
                    <option value={customer.clientId} key={customer.clientId}>
                      {customer.clientName} {customer.clientCode ? `(${customer.clientCode})` : ''}{customer.isActive === false ? ' - inactive' : ''}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                Contract type
                <select
                  value={editForm.contractType || ''}
                  onChange={(event) => updateEditField('contractType', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current / not set</option>
                  {contractOptions.map((value) => <option value={value} key={value}>{value}</option>)}
                </select>
              </label>

              <label>
                Project Manager
                <select
                  value={editForm.projectManagerUserId || ''}
                  onChange={(event) => updateEditField('projectManagerUserId', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current: {selectedWorkItem.projectManager || 'Not assigned'}</option>
                  {pmOptions.map((user) => (
                    <option value={user.userId} key={user.userId}>{user.displayName} {user.isActive === false ? '- inactive' : ''}</option>
                  ))}
                </select>
              </label>

              <label>
                Project Coordinator
                <select
                  value={editForm.projectCoordinatorUserId || ''}
                  onChange={(event) => updateEditField('projectCoordinatorUserId', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current: {selectedWorkItem.projectCoordinator || 'Not assigned'}</option>
                  {pcOptions.map((user) => (
                    <option value={user.userId} key={user.userId}>{user.displayName} {user.isActive === false ? '- inactive' : ''}</option>
                  ))}
                </select>
              </label>

              <label>
                Account Executive / AE
                <select
                  value={editForm.accountExecutiveUserId || ''}
                  onChange={(event) => updateEditField('accountExecutiveUserId', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current: {selectedWorkItem.accountExecutive || 'Not assigned'}</option>
                  {aeOptions.map((user) => (
                    <option value={user.userId} key={user.userId}>{user.displayName} {user.isActive === false ? '- inactive' : ''}</option>
                  ))}
                </select>
              </label>

              <label>
                Solution Architect / SA
                <select
                  value={editForm.solutionArchitectUserId || ''}
                  onChange={(event) => updateEditField('solutionArchitectUserId', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current: {selectedWorkItem.solutionArchitect || 'Not assigned'}</option>
                  {saOptions.map((user) => (
                    <option value={user.userId} key={user.userId}>{user.displayName} {user.isActive === false ? '- inactive' : ''}</option>
                  ))}
                </select>
              </label>

              <label>
                Inside Sales / SAA
                <select
                  value={editForm.insideSalesUserId || ''}
                  onChange={(event) => updateEditField('insideSalesUserId', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current: {selectedWorkItem.insideSales || 'Not assigned'}</option>
                  {saaOptions.map((user) => (
                    <option value={user.userId} key={user.userId}>{user.displayName} {user.isActive === false ? '- inactive' : ''}</option>
                  ))}
                </select>
              </label>

              <label>
                Status
                <select
                  value={editForm.status || ''}
                  onChange={(event) => updateEditField('status', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current / not set</option>
                  {statusEditOptions.map((value) => <option value={value} key={value}>{labelize(value)}</option>)}
                </select>
              </label>

              <label>
                Project start date
                <input
                  type="date"
                  value={editForm.projectStartDate || ''}
                  onChange={(event) => updateEditField('projectStartDate', event.target.value)}
                  disabled={!canEditWorkRegister}
                />
              </label>

              <label>
                Estimated end date
                <input
                  type="date"
                  value={editForm.estimatedEndDate || ''}
                  onChange={(event) => updateEditField('estimatedEndDate', event.target.value)}
                  disabled={!canEditWorkRegister}
                />
              </label>

              <label>
                SOW signed date
                <input
                  type="date"
                  value={editForm.sowSignedDate || ''}
                  onChange={(event) => updateEditField('sowSignedDate', event.target.value)}
                  disabled={!canEditWorkRegister}
                />
              </label>

              <label className="full-width">
                Edit reason
                <textarea
                  rows={3}
                  value={editForm.editReason || ''}
                  onChange={(event) => updateEditField('editReason', event.target.value)}
                  placeholder="Required. Example: Reassigned PM because prior PM left organization."
                  disabled={!canEditWorkRegister}
                />
              </label>

              <div className="work-register-drawer-actions">
                {canEditWorkRegister ? (
                  <button type="submit" className="primary-action">Save changes</button>
                ) : null}
                <button type="button" className="secondary-action" onClick={closeEditDrawer}>Cancel</button>
              </div>
            </form>
          </aside>
        </div>
      ) : null}

    </section>
  );
}
