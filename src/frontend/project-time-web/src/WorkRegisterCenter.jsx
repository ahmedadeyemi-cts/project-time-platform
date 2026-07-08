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

export default function WorkRegisterCenter() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });
  const [searchTerm, setSearchTerm] = useState('');
  const [lifecycleFilter, setLifecycleFilter] = useState('active');
  const [workTypeFilter, setWorkTypeFilter] = useState('all');
  const [customerFilter, setCustomerFilter] = useState('all');
  const [statusFilter, setStatusFilter] = useState('all');
  const [personFilter, setPersonFilter] = useState('all');
  const [burnFilter, setBurnFilter] = useState('all');

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
    </section>
  );
}
