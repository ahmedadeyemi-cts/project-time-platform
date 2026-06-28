import { useEffect, useMemo, useState } from 'react';
import './project-manager-workload-center.css';

function getStoredAuthSession() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    return rawSession ? JSON.parse(rawSession) : null;
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
  const response = await fetch(path, { headers: getProjectPulseAuthHeaders() });
  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

function formatNumber(value) {
  return Number(value ?? 0).toLocaleString(undefined, { maximumFractionDigits: 2 });
}

function formatMoney(value) {
  return Number(value ?? 0).toLocaleString(undefined, {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0
  });
}

function formatStatus(status) {
  return String(status ?? 'unknown').replaceAll('_', ' ').replace(/\b\w/g, (match) => match.toUpperCase());
}

export default function ProjectManagerWorkloadCenter() {
  const [workload, setWorkload] = useState({ loading: true, data: null, error: null });

  async function loadWorkload() {
    setWorkload((current) => ({ ...current, loading: true, error: null }));

    try {
      const result = await fetchJson('/api/project-management/workload');
      setWorkload({ loading: false, data: result, error: null });
    } catch (error) {
      setWorkload({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load project workload.'
      });
    }
  }

  useEffect(() => {
    loadWorkload();
  }, []);

  const summary = workload.data?.summary ?? {};
  const projects = workload.data?.projects ?? [];
  const risks = workload.data?.risks ?? [];
  const statusBreakdown = workload.data?.statusBreakdown ?? [];

  const activeProjects = useMemo(
    () => projects.filter((project) => ['active', 'open', 'in_progress', 'planning'].includes(String(project.status ?? '').toLowerCase())),
    [projects]
  );

  return (
    <section className="pm-workload-center">
      <div className="pm-workload-header">
        <div>
          <p className="eyebrow">Project workload</p>
          <h2>Project Manager Dashboard</h2>
          <p className="muted">
            Review active projects, closed projects, current status mix, PM-owned project list, and workload risks for the quarter.
          </p>
        </div>
        <div className="pm-workload-actions">
          <span className="pm-workload-pill">{workload.data?.quarter ?? 'Current Quarter'}</span>
          <button type="button" className="secondary-action" onClick={loadWorkload}>Refresh</button>
        </div>
      </div>

      {workload.error ? <div className="pm-workload-banner error">{workload.error}</div> : null}

      <div className="pm-workload-summary-grid">
        <article>
          <span>Active this quarter</span>
          <strong>{workload.loading ? '...' : formatNumber(summary.activeProjectsThisQuarter)}</strong>
          <small>Projects currently moving through delivery</small>
        </article>
        <article>
          <span>Closed this quarter</span>
          <strong>{workload.loading ? '...' : formatNumber(summary.closedProjectsThisQuarter)}</strong>
          <small>Projects completed during the quarter</small>
        </article>
        <article>
          <span>Total PM projects</span>
          <strong>{workload.loading ? '...' : formatNumber(summary.totalProjects)}</strong>
          <small>{workload.data?.scope === 'all_projects' ? 'All projects visible in admin scope' : 'Projects assigned to this PM'}</small>
        </article>
        <article>
          <span>Overdue active</span>
          <strong>{workload.loading ? '...' : formatNumber(summary.overdueActiveProjects)}</strong>
          <small>Active projects past target end date</small>
        </article>
      </div>

      <div className="pm-workload-layout">
        <article className="pm-workload-panel">
          <div className="pm-workload-panel-heading">
            <div>
              <h3>Project Status</h3>
              <p className="muted">Overall status distribution for the current PM scope.</p>
            </div>
            <span>{statusBreakdown.length} statuses</span>
          </div>
          <div className="pm-status-list">
            {statusBreakdown.map((item) => (
              <div className="pm-status-row" key={item.status}>
                <span>{formatStatus(item.status)}</span>
                <strong>{formatNumber(item.count)}</strong>
              </div>
            ))}
            {!workload.loading && statusBreakdown.length === 0 ? <p className="muted">No project status data found.</p> : null}
          </div>
        </article>

        <article className="pm-workload-panel">
          <div className="pm-workload-panel-heading">
            <div>
              <h3>Workload Risks</h3>
              <p className="muted">Items that may need PM review or follow-up.</p>
            </div>
            <span>{risks.length} risks</span>
          </div>
          <div className="pm-risk-list">
            {risks.map((risk) => (
              <div className="pm-risk-row" key={`${risk.projectCode}-${risk.riskSummary}`}>
                <strong>{risk.projectCode}</strong>
                <span>{risk.riskSummary}</span>
                <small>{risk.projectName} • {formatStatus(risk.status)}</small>
              </div>
            ))}
            {!workload.loading && risks.length === 0 ? <p className="muted">No workload risks found.</p> : null}
          </div>
        </article>
      </div>

      <article className="pm-workload-panel">
        <div className="pm-workload-panel-heading">
          <div>
            <h3>Assigned Project List</h3>
            <p className="muted">Project delivery view for this PM workload.</p>
          </div>
          <span>{activeProjects.length} active / {projects.length} total</span>
        </div>

        <div className="pm-project-list">
          {projects.map((project) => (
            <div className="pm-project-row" key={project.projectId}>
              <div>
                <span className={`pm-status-badge status-${String(project.status ?? '').toLowerCase().replaceAll(' ', '-')}`}>
                  {formatStatus(project.status)}
                </span>
                <strong>{project.projectCode} · {project.projectName}</strong>
                <small>{project.clientName} • PM: {project.projectManagerName}</small>
              </div>
              <div className="pm-project-metrics">
                <span>{formatNumber(project.assignedResourceCount)} resources</span>
                <span>{formatNumber(project.taskCount)} tasks</span>
                <span>{formatNumber(project.assignedHours)} assigned hrs</span>
                <span>{formatMoney(project.plannedTotalProjectCost)}</span>
                {Number(project.openCostAlertCount ?? 0) > 0 ? <em>{project.openCostAlertCount} cost alert(s)</em> : null}
              </div>
            </div>
          ))}
          {!workload.loading && projects.length === 0 ? <p className="muted">No projects are currently assigned to this PM scope.</p> : null}
        </div>
      </article>
    </section>
  );
}
