import { useEffect, useMemo, useState } from 'react';
import './project-flowhive-center.css';

const views = [
  { id: 'portfolio', label: 'Portfolio' },
  { id: 'tasks', label: 'Task grid' },
  { id: 'capabilities', label: 'Capability plan' }
];

function storedSession() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return null;

    const session = JSON.parse(raw);
    if (!session?.sessionToken) return null;
    if (session.expiresAt && Date.now() >= Date.parse(session.expiresAt)) return null;

    return session;
  } catch {
    return null;
  }
}

function authenticationHeaders() {
  const session = storedSession();
  return session?.sessionToken
    ? { 'X-ProjectPulse-Session': session.sessionToken }
    : {};
}

async function getJson(path) {
  const response = await fetch(path, {
    headers: authenticationHeaders()
  });

  const text = await response.text();
  let body = {};

  if (text) {
    try {
      body = JSON.parse(text);
    } catch {
      body = { message: text };
    }
  }

  if (!response.ok) {
    throw new Error(body.message || body.detail || `${path} returned HTTP ${response.status}`);
  }

  return body;
}

function formatDate(value) {
  if (!value) return 'Not scheduled';

  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) return value;

  return date.toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric'
  });
}

function formatHours(value) {
  return Number(value ?? 0).toLocaleString(undefined, {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2
  });
}

function labelFrom(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function statusTone(status) {
  const normalized = String(status ?? '').toLowerCase();
  if (['active', 'foundation', 'available'].includes(normalized)) return 'ready';
  if (['blocked', 'error'].includes(normalized)) return 'blocked';
  return 'planned';
}

function EmptyState({ children }) {
  return <div className="flowhive-empty-state">{children}</div>;
}

export default function ProjectFlowHiveCenter() {
  const [activeView, setActiveView] = useState('portfolio');
  const [capabilityResponse, setCapabilityResponse] = useState(null);
  const [portfolio, setPortfolio] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [customer, setCustomer] = useState('all');
  const [projectStatus, setProjectStatus] = useState('all');

  async function loadFoundation() {
    setLoading(true);
    setError('');

    try {
      const [capabilities, portfolioResult] = await Promise.all([
        getJson('/api/project-flowhive/capabilities'),
        getJson('/api/project-flowhive/portfolio')
      ]);

      setCapabilityResponse(capabilities);
      setPortfolio(portfolioResult);
    } catch (loadError) {
      setError(loadError.message || 'Project FlowHive could not be loaded.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    loadFoundation();
  }, []);

  const projects = portfolio?.projects ?? [];
  const tasks = portfolio?.tasks ?? [];
  const assignments = portfolio?.assignments ?? [];
  const capabilities = capabilityResponse?.capabilities ?? [];

  const customerOptions = useMemo(() => {
    return [...new Set(projects.map((project) => project.customerName).filter(Boolean))]
      .sort((left, right) => left.localeCompare(right));
  }, [projects]);

  const statusOptions = useMemo(() => {
    return [...new Set(projects.map((project) => project.status).filter(Boolean))]
      .sort((left, right) => left.localeCompare(right));
  }, [projects]);

  const filteredProjects = useMemo(() => {
    const query = search.trim().toLowerCase();

    return projects.filter((project) => {
      if (customer !== 'all' && project.customerName !== customer) return false;
      if (projectStatus !== 'all' && project.status !== projectStatus) return false;
      if (!query) return true;

      return [
        project.projectCode,
        project.projectName,
        project.customerName,
        project.projectManagerName,
        project.status
      ].some((value) => String(value ?? '').toLowerCase().includes(query));
    });
  }, [customer, projectStatus, projects, search]);

  const visibleProjectIds = useMemo(() => {
    return new Set(filteredProjects.map((project) => project.projectId));
  }, [filteredProjects]);

  const filteredTasks = useMemo(() => {
    const query = search.trim().toLowerCase();

    return tasks.filter((task) => {
      if (!visibleProjectIds.has(task.projectId)) return false;
      if (!query) return true;

      return [
        task.projectCode,
        task.projectName,
        task.taskCode,
        task.taskName,
        task.taskDescription
      ].some((value) => String(value ?? '').toLowerCase().includes(query));
    });
  }, [search, tasks, visibleProjectIds]);

  const assignmentsByTask = useMemo(() => {
    const grouped = new Map();

    assignments.forEach((assignment) => {
      if (!assignment.taskId) return;
      const current = grouped.get(assignment.taskId) ?? [];
      current.push(assignment.resourceName);
      grouped.set(assignment.taskId, current);
    });

    return grouped;
  }, [assignments]);

  return (
    <section
      className="project-flowhive-center"
      data-module="066"
      data-phase="066A"
      data-mode="read-only"
    >
      <header className="flowhive-hero">
        <div>
          <p className="flowhive-eyebrow">Module 066 · Foundation 066A</p>
          <h2>Project FlowHive</h2>
          <p>
            A governed, multi-customer planning workspace foundation built from
            canonical ProjectPulse projects, tasks, and assignments.
          </p>
        </div>
        <div className="flowhive-hero-actions">
          <span className="flowhive-phase-badge">Read-only foundation</span>
          <button type="button" onClick={loadFoundation} disabled={loading}>
            {loading ? 'Refreshing…' : 'Refresh'}
          </button>
        </div>
      </header>

      <aside className="flowhive-foundation-notice" aria-label="Foundation boundary">
        <strong>Controlled planning is intentionally locked.</strong>
        <span>
          WBS hierarchy, dependencies, Gantt scheduling, baselines, collaboration,
          AI generation, and customer exports require later authorized phases.
        </span>
      </aside>

      {portfolio?.access ? (
        <div className="flowhive-access-banner">
          <div>
            <span>Effective user</span>
            <strong>{portfolio.access.displayName || portfolio.access.email}</strong>
          </div>
          <div>
            <span>Backend scope</span>
            <strong>{labelFrom(portfolio.access.scope)}</strong>
          </div>
          <div>
            <span>View-As</span>
            <strong>{portfolio.access.isViewAs ? 'Read-only preview' : 'Not active'}</strong>
          </div>
          <div>
            <span>Authorization</span>
            <strong>{portfolio.access.serverAuthorized ? 'Server enforced' : 'Unavailable'}</strong>
          </div>
        </div>
      ) : null}

      {error ? (
        <div className="flowhive-error" role="alert">
          <strong>Project FlowHive is unavailable.</strong>
          <span>{error}</span>
        </div>
      ) : null}

      <nav className="flowhive-view-tabs" aria-label="Project FlowHive views">
        {views.map((view) => (
          <button
            type="button"
            key={view.id}
            aria-pressed={activeView === view.id}
            className={activeView === view.id ? 'active' : ''}
            onClick={() => setActiveView(view.id)}
          >
            {view.label}
          </button>
        ))}
      </nav>

      {activeView !== 'capabilities' ? (
        <div className="flowhive-filter-bar">
          <label>
            Search
            <input
              type="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Project, customer, manager, or task"
            />
          </label>
          <label>
            Customer
            <select value={customer} onChange={(event) => setCustomer(event.target.value)}>
              <option value="all">All authorized customers</option>
              {customerOptions.map((value) => (
                <option key={value} value={value}>{value}</option>
              ))}
            </select>
          </label>
          <label>
            Project status
            <select value={projectStatus} onChange={(event) => setProjectStatus(event.target.value)}>
              <option value="all">All statuses</option>
              {statusOptions.map((value) => (
                <option key={value} value={value}>{labelFrom(value)}</option>
              ))}
            </select>
          </label>
        </div>
      ) : null}

      {activeView === 'portfolio' ? (
        <div className="flowhive-view-panel">
          <div className="flowhive-summary-grid">
            <article>
              <span>Authorized projects</span>
              <strong>{portfolio?.summary?.projectCount ?? 0}</strong>
              <small>{filteredProjects.length} match current filters</small>
            </article>
            <article>
              <span>Visible tasks</span>
              <strong>{portfolio?.summary?.taskCount ?? 0}</strong>
              <small>Canonical task records</small>
            </article>
            <article>
              <span>Assignments</span>
              <strong>{portfolio?.summary?.assignmentCount ?? 0}</strong>
              <small>{formatHours(portfolio?.summary?.assignedHours)} assigned hours</small>
            </article>
            <article>
              <span>Controlled baselines</span>
              <strong>{portfolio?.summary?.controlledBaselineCount ?? 0}</strong>
              <small>Planned for a later phase</small>
            </article>
          </div>

          {loading ? <EmptyState>Loading authorized portfolio…</EmptyState> : null}

          {!loading && !error && filteredProjects.length === 0 ? (
            <EmptyState>No authorized projects match the current filters.</EmptyState>
          ) : null}

          <div className="flowhive-project-grid">
            {filteredProjects.map((project) => (
              <article className="flowhive-project-card" key={project.projectId}>
                <div className="flowhive-project-card-heading">
                  <div>
                    <span>{project.customerName}</span>
                    <h3>{project.projectCode} · {project.projectName}</h3>
                  </div>
                  <span className={`flowhive-status ${statusTone(project.status)}`}>
                    {labelFrom(project.status)}
                  </span>
                </div>
                <dl>
                  <div><dt>Project Manager</dt><dd>{project.projectManagerName}</dd></div>
                  <div><dt>Current dates</dt><dd>{formatDate(project.startDate)} – {formatDate(project.endDate)}</dd></div>
                  <div><dt>Tasks</dt><dd>{project.taskCount}</dd></div>
                  <div><dt>Assignments</dt><dd>{project.assignmentCount}</dd></div>
                </dl>
                <footer>
                  <span>Source: canonical project record</span>
                  <span>Baseline: not established</span>
                </footer>
              </article>
            ))}
          </div>
        </div>
      ) : null}

      {activeView === 'tasks' ? (
        <div className="flowhive-view-panel">
          <div className="flowhive-table-heading">
            <div>
              <h3>Canonical task grid</h3>
              <p>
                Task codes are source references only. They are not yet governed
                WBS numbers and do not imply schedule dependencies.
              </p>
            </div>
            <span>{filteredTasks.length} visible tasks</span>
          </div>

          {loading ? <EmptyState>Loading authorized tasks…</EmptyState> : null}

          {!loading && !error && filteredTasks.length === 0 ? (
            <EmptyState>No authorized tasks match the current filters.</EmptyState>
          ) : null}

          {filteredTasks.length > 0 ? (
            <div className="flowhive-table-wrap">
              <table className="flowhive-task-table">
                <thead>
                  <tr>
                    <th>Task reference</th>
                    <th>Project</th>
                    <th>Task</th>
                    <th>Assigned resources</th>
                    <th>Hours</th>
                    <th>Planning state</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredTasks.map((task) => {
                    const resources = assignmentsByTask.get(task.taskId) ?? [];

                    return (
                      <tr key={task.taskId}>
                        <td>
                          <strong>{task.taskCode}</strong>
                          <span>Canonical code</span>
                        </td>
                        <td>
                          <strong>{task.projectCode}</strong>
                          <span>{task.projectName}</span>
                        </td>
                        <td>
                          <strong>{task.taskName}</strong>
                          <span>{task.taskDescription || 'No description recorded'}</span>
                        </td>
                        <td>
                          <strong>{resources.length ? resources.join(', ') : 'Unassigned'}</strong>
                          <span>{task.assigneeCount} assignment record(s)</span>
                        </td>
                        <td>
                          <strong>{formatHours(task.usedHours)} used</strong>
                          <span>{formatHours(task.assignedHours)} assigned · {formatHours(task.remainingHours)} remaining</span>
                        </td>
                        <td>
                          <span className="flowhive-status planned">WBS planned</span>
                          <span>Dependencies and baseline unavailable</span>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          ) : null}
        </div>
      ) : null}

      {activeView === 'capabilities' ? (
        <div className="flowhive-view-panel">
          <div className="flowhive-table-heading">
            <div>
              <h3>Phased capability plan</h3>
              <p>
                Status is evidence-based. Planned capabilities remain unavailable
                until their dependencies and acceptance evidence exist.
              </p>
            </div>
            <span>{capabilities.length} tracked capabilities</span>
          </div>

          <div className="flowhive-capability-grid">
            {capabilities.map((capability) => (
              <article key={capability.code}>
                <div>
                  <span>{capability.priority}</span>
                  <span className={`flowhive-status ${statusTone(capability.status)}`}>
                    {labelFrom(capability.status)}
                  </span>
                </div>
                <h3>{labelFrom(capability.code)}</h3>
                <p>{capability.evidence}</p>
              </article>
            ))}
          </div>
        </div>
      ) : null}
    </section>
  );
}
