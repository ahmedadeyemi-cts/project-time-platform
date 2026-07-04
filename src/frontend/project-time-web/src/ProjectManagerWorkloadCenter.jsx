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
  return session?.sessionToken ? { 'X-Project Health Dashboard-Session': session.sessionToken } : {};
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
  return Number(value ?? 0).toLocaleString(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
}

function formatStatus(value) {
  return String(value ?? 'unknown').replaceAll('_', ' ').replace(/\b\w/g, (match) => match.toUpperCase());
}

function formatDate(value) {
  if (!value) return 'Not scheduled';
  return new Date(`${value}T00:00:00`).toLocaleDateString();
}

function getScopeLabel(scope) {
  switch (scope) {
    case 'all_projects':
      return 'All project managers';
    case 'selected_project_manager_scope':
      return 'Selected project manager';
    case 'pm_team_scope':
      return 'PM team scope';
    case 'selected_team_project_manager_scope':
      return 'Selected team project manager';
    case 'own_project_manager_scope':
      return 'My project workload';
    default:
      return formatStatus(scope);
  }
}

export default function ProjectManagerWorkloadCenter() {
  const [selectedProjectManagerUserId, setSelectedProjectManagerUserId] = useState('');
  const [workload, setWorkload] = useState({ loading: true, data: null, error: null });

  async function loadWorkload(projectManagerUserId = selectedProjectManagerUserId) {
    setWorkload((current) => ({ ...current, loading: true, error: null }));

    const query = projectManagerUserId ? `?projectManagerUserId=${encodeURIComponent(projectManagerUserId)}` : '';

    try {
      const result = await fetchJson(`/api/project-management/workload${query}`);
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
    loadWorkload('');
  }, []);

  const summary = workload.data?.summary ?? {};
  const projects = workload.data?.projects ?? [];
  const risks = workload.data?.riskHighlights ?? [];
  const statusBreakdown = workload.data?.statusBreakdown ?? [];
  const selectableProjectManagers = workload.data?.selectableProjectManagers ?? [];
  const access = workload.data?.access ?? {};
  const canSelectProjectManager = Boolean(access.canSelectProjectManager) && selectableProjectManagers.length > 1;

  const activeProjects = useMemo(
    () => projects.filter((project) => ['active', 'open', 'in_progress', 'planning'].includes(String(project.status ?? '').toLowerCase())),
    [projects]
  );

  function handleProjectManagerChange(value) {
    setSelectedProjectManagerUserId(value);
    loadWorkload(value);
  }

  return (
    <section className="pm-workload-center">
      <div className="pm-workload-header">
        <div>
          <p className="eyebrow">019M-AM</p>
          <h2>Project Workload</h2>
          <p className="muted">
            Project Managers see only their own workload. PM Team Leads can select project managers on their team. Administrators and PTC can review broader workload.
          </p>
        </div>
        <span className="pm-workload-scope">{getScopeLabel(workload.data?.scope)}</span>
      </div>

      {workload.error ? <div className="pm-workload-banner error">{workload.error}</div> : null}

      {canSelectProjectManager ? (
        <article className="pm-workload-panel pm-workload-selector-panel">
          <div>
            <h3>Project Manager Selector</h3>
            <p className="muted">
              The dropdown is role-scoped. PM Team Leads only see project managers on their team.
            </p>
          </div>
          <div className="pm-workload-selector-row">
            <label>
              Workload view
              <select value={selectedProjectManagerUserId} onChange={(event) => handleProjectManagerChange(event.target.value)}>
                <option value="">{access.canViewAll ? 'All project managers' : 'All PMs on my team'}</option>
                {selectableProjectManagers.map((pm) => (
                  <option value={pm.userId} key={pm.userId}>
                    {pm.displayName} ({pm.email})
                  </option>
                ))}
              </select>
            </label>
            <button type="button" className="secondary-action" onClick={() => loadWorkload(selectedProjectManagerUserId)}>Refresh</button>
          </div>
        </article>
      ) : null}

      <div className="pm-workload-summary-grid">
        <article>
          <span>Active this quarter</span>
          <strong>{workload.loading ? '...' : formatNumber(summary.activeProjectsThisQuarter)}</strong>
          <small>{workload.data?.quarter ?? 'Current quarter'}</small>
        </article>
        <article>
          <span>Closed this quarter</span>
          <strong>{workload.loading ? '...' : formatNumber(summary.closedProjectsThisQuarter)}</strong>
          <small>Completed project count</small>
        </article>
        <article>
          <span>Total projects</span>
          <strong>{workload.loading ? '...' : formatNumber(summary.totalProjects)}</strong>
          <small>{getScopeLabel(workload.data?.scope)}</small>
        </article>
        <article>
          <span>Workload risks</span>
          <strong>{workload.loading ? '...' : formatNumber(risks.length)}</strong>
          <small>Cost/schedule/status review</small>
        </article>
      </div>

      <div className="pm-workload-layout">
        <article className="pm-workload-panel">
          <div className="pm-workload-panel-heading">
            <div>
              <h3>Project Status</h3>
              <p className="muted">Status breakdown for the selected workload scope.</p>
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
            {!workload.loading && statusBreakdown.length === 0 ? <p className="muted">No project statuses found.</p> : null}
          </div>
        </article>

        <article className="pm-workload-panel">
          <div className="pm-workload-panel-heading">
            <div>
              <h3>Workload Risk Highlights</h3>
              <p className="muted">Projects with cost alerts, schedule review, or status review needs.</p>
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
            <p className="muted">Project delivery view for the selected PM workload scope.</p>
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
                <small>{project.clientName} • PM: {project.projectManagerName}{project.projectManagerEmail ? ` (${project.projectManagerEmail})` : ''}</small>
                <small>{formatDate(project.startDate)} → {formatDate(project.endDate)}</small>
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
