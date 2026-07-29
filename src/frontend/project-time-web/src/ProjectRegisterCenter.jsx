import { useEffect, useMemo, useState } from 'react';
import './project-register-center.css';

const EMPTY_SUMMARY = Object.freeze({ total: 0, active: 0, historical: 0, archived: 0 });

function normalize(value) {
  return String(value ?? '').trim().toLowerCase();
}

function labelize(value) {
  const text = String(value ?? '').trim();
  if (!text) return 'Not set';
  return text
    .replaceAll('_', ' ')
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function money(value) {
  const amount = Number(value ?? 0);
  return Number.isFinite(amount)
    ? new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(amount)
    : '$0.00';
}

function hours(value) {
  const amount = Number(value ?? 0);
  return Number.isFinite(amount) ? `${amount.toFixed(1)} hrs` : '0.0 hrs';
}

function dateOnly(value) {
  if (!value) return 'Not set';
  return String(value).slice(0, 10);
}

function isHistorical(item) {
  const lifecycle = normalize(item?.lifecycle);
  const status = normalize(item?.status);
  return item?.isArchived === true
    || lifecycle === 'closed'
    || ['archived', 'closed', 'completed', 'done', 'cancelled', 'canceled'].includes(status);
}

function unique(items, selector) {
  return [...new Set(items.map(selector).filter(Boolean))]
    .sort((left, right) => String(left).localeCompare(String(right)));
}

async function fetchJson(path) {
  const response = await fetch(path, {
    method: 'GET',
    headers: {
      Accept: 'application/json',
      'Cache-Control': 'no-cache, no-store'
    }
  });

  const text = await response.text();
  let body = null;
  if (text.trim()) {
    try {
      body = JSON.parse(text);
    } catch {
      body = { message: text };
    }
  }

  if (!response.ok) {
    throw new Error(body?.message || body?.detail || `${path} returned HTTP ${response.status}.`);
  }

  return body ?? {};
}

function ArrayList({ values, emptyText }) {
  const items = Array.isArray(values) ? values : [];
  if (!items.length) return <p className="project-register-empty-copy">{emptyText}</p>;
  return (
    <div className="project-register-detail-list">
      {items.slice(0, 50).map((item, index) => (
        <article key={item?.taskId || item?.assignmentId || item?.documentId || item?.auditId || index}>
          <strong>{item?.taskName || item?.displayName || item?.fileName || item?.eventType || item?.action || item?.name || `Record ${index + 1}`}</strong>
          <span>
            {item?.status || item?.role || item?.email || item?.description || item?.details || item?.occurredAt || item?.createdAt || 'Evidence available'}
          </span>
        </article>
      ))}
    </div>
  );
}

export default function ProjectRegisterCenter({ legacyRoute = false }) {
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });
  const [searchTerm, setSearchTerm] = useState('');
  const [lifecycle, setLifecycle] = useState('active');
  const [customer, setCustomer] = useState('all');
  const [status, setStatus] = useState('all');
  const [owner, setOwner] = useState('all');
  const [selectedProject, setSelectedProject] = useState(null);
  const [details, setDetails] = useState({ loading: false, data: null, error: null });

  async function load() {
    setPayload((current) => ({ ...current, loading: true, error: null }));
    try {
      const data = await fetchJson('/api/work-register/overview');
      setPayload({ loading: false, data, error: null });
    } catch (error) {
      setPayload({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load the Project Register.'
      });
    }
  }

  useEffect(() => {
    load();
  }, []);

  const projects = useMemo(
    () => (Array.isArray(payload.data?.workItems) ? payload.data.workItems : [])
      .filter((item) => normalize(item?.sourceTable) === 'projects'),
    [payload.data]
  );

  const summary = useMemo(() => projects.reduce((result, item) => {
    result.total += 1;
    if (isHistorical(item)) {
      result.historical += 1;
      if (item?.isArchived === true || normalize(item?.status) === 'archived') result.archived += 1;
    } else {
      result.active += 1;
    }
    return result;
  }, { ...EMPTY_SUMMARY }), [projects]);

  const customerOptions = useMemo(() => unique(projects, (item) => item.customerName), [projects]);
  const statusOptions = useMemo(() => unique(projects, (item) => item.status), [projects]);
  const ownerOptions = useMemo(() => unique(projects, (item) => item.projectManager), [projects]);

  const filteredProjects = useMemo(() => {
    const search = normalize(searchTerm);
    return projects.filter((item) => {
      const historical = isHistorical(item);
      if (lifecycle === 'active' && historical) return false;
      if (lifecycle === 'historical' && !historical) return false;
      if (customer !== 'all' && normalize(item.customerName) !== normalize(customer)) return false;
      if (status !== 'all' && normalize(item.status) !== normalize(status)) return false;
      if (owner !== 'all' && normalize(item.projectManager) !== normalize(owner)) return false;
      if (!search) return true;

      return [
        item.projectCode,
        item.workName,
        item.projectName,
        item.customerName,
        item.status,
        item.contractType,
        item.workType,
        item.projectManager,
        item.projectCoordinator,
        item.accountExecutive,
        item.solutionArchitect,
        item.insideSales,
        item.sellQuoteNumber,
        item.salesforceIdNumber,
        item.certiniaIdNumber,
        ...(item.assignedEngineers ?? [])
      ].join(' ').toLowerCase().includes(search);
    });
  }, [customer, lifecycle, owner, projects, searchTerm, status]);

  const totals = useMemo(() => filteredProjects.reduce((result, item) => {
    result.allocatedHours += Number(item.allocatedHours ?? 0);
    result.usedHours += Number(item.usedHours ?? 0);
    result.totalCost += Number(item.totalCost ?? 0);
    result.remainingCost += Number(item.remainingCost ?? 0);
    return result;
  }, { allocatedHours: 0, usedHours: 0, totalCost: 0, remainingCost: 0 }), [filteredProjects]);

  async function openProject(item) {
    setSelectedProject(item);
    setDetails({ loading: true, data: null, error: null });

    try {
      const [projectResult, lifecycleResult] = await Promise.allSettled([
        fetchJson(`/api/work-register/projects/${item.workId}/details`),
        fetchJson(`/api/work-lifecycle/projects/${item.workId}`)
      ]);

      if (projectResult.status === 'rejected') throw projectResult.reason;
      setDetails({
        loading: false,
        data: {
          ...projectResult.value,
          workLifecycle: lifecycleResult.status === 'fulfilled' ? lifecycleResult.value : null
        },
        error: null
      });
    } catch (error) {
      setDetails({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load project details.'
      });
    }
  }

  const detailTasks = details.data?.tasks ?? details.data?.projectTasks ?? [];
  const detailAssignments = details.data?.assignments ?? details.data?.projectAssignments ?? [];
  const detailDocuments = details.data?.documents ?? details.data?.projectDocuments ?? [];
  const detailAudit = details.data?.workLifecycle?.audit ?? details.data?.changeHistory ?? details.data?.audit ?? [];

  return (
    <section
      className="project-register-center projectpulse-module-standard"
      data-module="006"
      data-module-name="Project Register"
      data-canonical-route="project-register"
      data-project-register-contract="authoritative-work-register-composition-v1"
    >
      <header className="project-register-hero">
        <div>
          <p className="eyebrow">MODULE 006 · PROJECT OPERATIONS</p>
          <h2>Project Register</h2>
          <p>
            Search the authoritative ProjectPulse project inventory, separate active work from archived history,
            and open the existing project-management workspace without creating a second project system.
          </p>
        </div>
        <div className="project-register-hero-actions">
          <button type="button" className="secondary-action" onClick={load} disabled={payload.loading}>
            {payload.loading ? 'Refreshing…' : 'Refresh register'}
          </button>
          <a className="primary-action project-register-link-button" href="#work-register">
            Manage Existing Projects
          </a>
        </div>
      </header>

      {legacyRoute ? (
        <div className="project-register-banner warning">
          The legacy <code>#psa-modules</code> link is being honored temporarily. The canonical Module 006 route is <code>#project-register</code>.
        </div>
      ) : null}

      {payload.error ? <div className="project-register-banner error">{payload.error}</div> : null}

      <div className="project-register-summary" aria-label="Project Register summary">
        <article><span>Total projects</span><strong>{payload.loading ? '…' : summary.total}</strong><small>{filteredProjects.length} shown</small></article>
        <article><span>Active</span><strong>{payload.loading ? '…' : summary.active}</strong><small>Current delivery records</small></article>
        <article><span>Historical</span><strong>{payload.loading ? '…' : summary.historical}</strong><small>{summary.archived} archived</small></article>
        <article><span>Filtered value</span><strong>{money(totals.totalCost)}</strong><small>{hours(totals.usedHours)} used</small></article>
      </div>

      <div className="project-register-toolbar">
        <label className="wide">
          Search
          <input
            type="search"
            value={searchTerm}
            onChange={(event) => setSearchTerm(event.target.value)}
            placeholder="Project code, project, customer, PM, engineer, SELL quote…"
          />
        </label>
        <label>
          Register view
          <select value={lifecycle} onChange={(event) => setLifecycle(event.target.value)}>
            <option value="active">Active</option>
            <option value="historical">Archived / historical</option>
            <option value="all">All projects</option>
          </select>
        </label>
        <label>
          Customer
          <select value={customer} onChange={(event) => setCustomer(event.target.value)}>
            <option value="all">All customers</option>
            {customerOptions.map((value) => <option value={value} key={value}>{value}</option>)}
          </select>
        </label>
        <label>
          Status
          <select value={status} onChange={(event) => setStatus(event.target.value)}>
            <option value="all">All statuses</option>
            {statusOptions.map((value) => <option value={value} key={value}>{labelize(value)}</option>)}
          </select>
        </label>
        <label>
          Project Manager
          <select value={owner} onChange={(event) => setOwner(event.target.value)}>
            <option value="all">All Project Managers</option>
            {ownerOptions.map((value) => <option value={value} key={value}>{value}</option>)}
          </select>
        </label>
      </div>

      <div className="project-register-totalbar">
        <span><strong>{filteredProjects.length}</strong> projects</span>
        <span><strong>{hours(totals.allocatedHours)}</strong> allocated</span>
        <span><strong>{hours(totals.usedHours)}</strong> used</span>
        <span><strong>{money(totals.remainingCost)}</strong> remaining</span>
      </div>

      <div className="project-register-table-wrap">
        <table className="project-register-table">
          <thead>
            <tr>
              <th>Project</th>
              <th>Customer / Contract</th>
              <th>Lifecycle</th>
              <th>Ownership</th>
              <th>Engineering</th>
              <th>Dates</th>
              <th>Delivery</th>
              <th>Financial</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            {filteredProjects.map((item) => (
              <tr key={item.workId} data-project-register-project={item.workId}>
                <td>
                  <strong>{item.projectCode || item.workName || 'Project'}</strong>
                  <small>{item.workName || item.projectName || 'Unnamed project'}</small>
                  {item.sellQuoteNumber ? <small>SELL: {item.sellQuoteNumber}</small> : null}
                </td>
                <td>
                  <strong>{item.customerName || 'No customer linked'}</strong>
                  <small>{item.contractType ? labelize(item.contractType) : 'Contract not set'}</small>
                  <small>{item.workType ? labelize(item.workType) : 'Work type not set'}</small>
                </td>
                <td>
                  <span className={`project-register-state ${isHistorical(item) ? 'historical' : 'active'}`}>
                    {isHistorical(item) ? 'Historical' : 'Active'}
                  </span>
                  <small>{labelize(item.status)}</small>
                </td>
                <td>
                  <small>PM: {item.projectManager || 'Not assigned'}</small>
                  <small>PTC: {item.projectCoordinator || 'Not assigned'}</small>
                  <small>AE: {item.accountExecutive || 'Not assigned'}</small>
                  <small>SA: {item.solutionArchitect || 'Not assigned'}</small>
                </td>
                <td>
                  {(item.assignedEngineers ?? []).length
                    ? (item.assignedEngineers ?? []).slice(0, 5).map((engineer) => <small key={engineer}>{engineer}</small>)
                    : <small>No engineers assigned</small>}
                </td>
                <td>
                  <small>Start: {dateOnly(item.startDate)}</small>
                  <small>Estimate: {dateOnly(item.estimatedEndDate)}</small>
                  <small>Closed: {dateOnly(item.closedDate)}</small>
                </td>
                <td>
                  <small>{item.taskCount ?? 0} tasks · {item.openTaskCount ?? 0} open</small>
                  <small>{item.documentCount ?? 0} documents</small>
                  <small>{hours(item.usedHours)} used</small>
                </td>
                <td>
                  <small>Total: {money(item.totalCost)}</small>
                  <small>Used: {money(item.costUsed)}</small>
                  <small>Remaining: {money(item.remainingCost)}</small>
                  <small>{labelize(item.burnStatus)} {Number(item.burnPercent ?? 0) ? `${item.burnPercent}%` : ''}</small>
                </td>
                <td>
                  <button type="button" className="project-register-row-action" onClick={() => openProject(item)}>
                    View register detail
                  </button>
                  {item.canEditProject === true && !isHistorical(item) ? (
                    <a href="#work-register" className="project-register-inline-link">Manage in 055C</a>
                  ) : null}
                </td>
              </tr>
            ))}
            {!payload.loading && filteredProjects.length === 0 ? (
              <tr><td colSpan="9" className="project-register-empty-cell">No projects match the current register filters.</td></tr>
            ) : null}
            {payload.loading ? (
              <tr><td colSpan="9" className="project-register-empty-cell">Loading authoritative projects…</td></tr>
            ) : null}
          </tbody>
        </table>
      </div>

      <div className="project-register-governance-grid">
        <article>
          <span>Workbook import</span>
          <strong>Review-gated</strong>
          <p>Beck workbook rows will require mapping, duplicate detection, actor attribution, and approval before persistence.</p>
          <button type="button" disabled>Import controls locked</button>
        </article>
        <article>
          <span>Branded exports</span>
          <strong>Evidence-gated</strong>
          <p>Excel and PDF exports will preserve filters, user scope, an as-of timestamp, and immutable export audit evidence.</p>
          <button type="button" disabled>Export controls locked</button>
        </article>
      </div>

      {selectedProject ? (
        <div className="project-register-drawer-backdrop" role="presentation">
          <aside className="project-register-drawer" role="dialog" aria-modal="true" aria-label="Project Register project detail">
            <header>
              <div>
                <p className="eyebrow">PROJECT REGISTER DETAIL</p>
                <h3>{selectedProject.workName || selectedProject.projectName}</h3>
                <p>{selectedProject.customerName || 'No customer linked'} · {selectedProject.projectCode || selectedProject.workId}</p>
              </div>
              <button type="button" className="secondary-action" onClick={() => setSelectedProject(null)}>Close</button>
            </header>

            <div className={`project-register-readonly-notice ${selectedProject.canEditProject === true && !isHistorical(selectedProject) ? 'managed' : ''}`}>
              {isHistorical(selectedProject)
                ? 'Historical project. Project state remains read-only while task, document, assignment, financial, and audit evidence stays available.'
                : selectedProject.canEditProject === true
                  ? 'You have management authority for this project. Mutations remain in Module 055C so Module 006 cannot create a competing project record.'
                  : 'Read-only project scope. The backend remains the authorization authority.'}
            </div>

            {details.loading ? <div className="project-register-banner">Loading project detail…</div> : null}
            {details.error ? <div className="project-register-banner error">{details.error}</div> : null}

            <section className="project-register-detail-summary">
              <article><span>Status</span><strong>{labelize(selectedProject.status)}</strong></article>
              <article><span>Project Manager</span><strong>{selectedProject.projectManager || 'Not assigned'}</strong></article>
              <article><span>Tasks</span><strong>{selectedProject.taskCount ?? 0}</strong></article>
              <article><span>Documents</span><strong>{selectedProject.documentCount ?? 0}</strong></article>
            </section>

            <section className="project-register-detail-section">
              <h4>Assignments</h4>
              <ArrayList values={detailAssignments} emptyText="No assignment rows were returned by the current project-detail contract." />
            </section>
            <section className="project-register-detail-section">
              <h4>Tasks</h4>
              <ArrayList values={detailTasks} emptyText="No task rows were returned by the current project-detail contract." />
            </section>
            <section className="project-register-detail-section">
              <h4>Documents</h4>
              <ArrayList values={detailDocuments} emptyText="No document rows were returned by the current project-detail contract." />
            </section>
            <section className="project-register-detail-section">
              <h4>Immutable lifecycle and audit evidence</h4>
              <ArrayList values={detailAudit} emptyText="No lifecycle audit rows were returned for this project." />
            </section>

            <footer>
              {selectedProject.canEditProject === true && !isHistorical(selectedProject) ? (
                <a className="primary-action project-register-link-button" href="#work-register">Open management workspace</a>
              ) : null}
              <button type="button" className="secondary-action" onClick={() => setSelectedProject(null)}>Close detail</button>
            </footer>
          </aside>
        </div>
      ) : null}
    </section>
  );
}
