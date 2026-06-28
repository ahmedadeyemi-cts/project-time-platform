import { useEffect, useMemo, useState } from 'react';
import './resource-assignment-handoff-panel.css';

function getProjectPulseAuthHeaders() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return {};
    const session = JSON.parse(rawSession);
    return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
  } catch {
    return {};
  }
}

async function fetchJson(path) {
  const response = await fetch(path, { headers: getProjectPulseAuthHeaders() });

  if (response.status === 403) return { canViewResourceAssignmentHandoff: false };

  if (!response.ok) {
    const raw = await response.text();
    throw new Error(raw || `${path} returned HTTP ${response.status}`);
  }

  return response.json();
}

function formatNumber(value) {
  return Number(value ?? 0).toLocaleString(undefined, { maximumFractionDigits: 2 });
}

function stageLabel(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

export default function ResourceAssignmentHandoffPanel() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });
  const [stageFilter, setStageFilter] = useState('all');

  async function loadHandoff() {
    setPayload((current) => ({ ...current, loading: true, error: null }));

    try {
      const data = await fetchJson('/api/project-intake/resource-assignment-handoff');
      setPayload({ loading: false, data, error: null });
    } catch (error) {
      setPayload({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load resource assignment handoff readiness.'
      });
    }
  }

  useEffect(() => {
    loadHandoff();
  }, []);

  const data = payload.data;
  const requests = data?.requests ?? [];

  const stages = useMemo(() => {
    return Array.from(new Set(requests.map((item) => item.readinessStage).filter(Boolean)));
  }, [requests]);

  const filteredRequests = useMemo(() => {
    if (stageFilter === 'all') return requests;
    return requests.filter((item) => item.readinessStage === stageFilter);
  }, [requests, stageFilter]);

  if (payload.loading) return null;

  if (!payload.error && !data?.canViewResourceAssignmentHandoff) {
    return null;
  }

  return (
    <section id="resource-assignment-handoff" className="panel resource-assignment-handoff-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">019M-AT</p>
          <h2>Resource Request → Work Task Assignment Handoff</h2>
          <p className="section-copy">
            This readiness view shows whether engineering resource request assignments have a linked project, work tasks, task-level assignments, and timesheet activity. It is visibility first; automatic promotion remains disabled.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={loadHandoff}>Refresh</button>
      </div>

      {payload.error ? <div className="error-text">{payload.error}</div> : null}

      <div className="resource-handoff-lifecycle">
        {(data?.lifecycle ?? []).map((step) => (
          <article key={step.step}>
            <span>{step.step}</span>
            <strong>{step.title}</strong>
            <p>{step.description}</p>
          </article>
        ))}
      </div>

      <div className="resource-handoff-summary-grid">
        <article><span>Resource requests</span><strong>{data?.summary?.resourceRequestCount ?? 0}</strong><small>Total in scope</small></article>
        <article><span>Project linked</span><strong>{data?.summary?.projectLinkedRequestCount ?? 0}</strong><small>Resource request has project</small></article>
        <article><span>Work-task ready</span><strong>{data?.summary?.workTaskReadyRequestCount ?? 0}</strong><small>Project has tasks</small></article>
        <article><span>Resource assigned</span><strong>{data?.summary?.resourceAssignmentReadyRequestCount ?? 0}</strong><small>Engineers allocated</small></article>
        <article><span>Task assigned</span><strong>{data?.summary?.projectTaskAssignmentReadyRequestCount ?? 0}</strong><small>Engineers assigned to tasks</small></article>
        <article><span>Gaps</span><strong>{data?.summary?.gapRequestCount ?? 0}</strong><small>Need handoff attention</small></article>
      </div>

      <div className="resource-handoff-toolbar">
        <label>
          Readiness filter
          <select value={stageFilter} onChange={(event) => setStageFilter(event.target.value)}>
            <option value="all">All readiness stages</option>
            {stages.map((stage) => (
              <option value={stage} key={stage}>{stageLabel(stage)}</option>
            ))}
          </select>
        </label>
        <span>{filteredRequests.length} resource request{filteredRequests.length === 1 ? '' : 's'} shown</span>
      </div>

      <div className="resource-handoff-card-list">
        {filteredRequests.map((item) => (
          <article key={item.resourceRequestId} className={`resource-handoff-card stage-${item.readinessStage}`}>
            <div className="resource-handoff-card-header">
              <div>
                <p className="eyebrow">{item.requestNumber}</p>
                <h3>{item.requestedFunction}</h3>
                <small>{item.projectCode || 'No project'} · {item.projectName || 'Project link needed'} · PM: {item.assignedPmName || 'Unassigned'}</small>
              </div>
              <span>{stageLabel(item.readinessStage)}</span>
            </div>

            <p className="resource-handoff-message">{item.readinessMessage}</p>

            <div className="resource-handoff-metric-grid">
              <div><span>Requested hours</span><strong>{formatNumber(item.requestedHours)}</strong></div>
              <div><span>Resource assignments</span><strong>{formatNumber(item.resourceAssignmentCount)}</strong></div>
              <div><span>Engineers</span><strong>{formatNumber(item.assignedEngineerCount)}</strong></div>
              <div><span>Allocated hours</span><strong>{formatNumber(item.allocatedHours)}</strong></div>
              <div><span>Work tasks</span><strong>{formatNumber(item.taskCount)}</strong></div>
              <div><span>Task assignments</span><strong>{formatNumber(item.projectAssignmentCount)}</strong></div>
              <div><span>Task assigned hours</span><strong>{formatNumber(item.projectAssignedHours)}</strong></div>
              <div><span>Timesheet entries</span><strong>{formatNumber(item.timeEntryCount)}</strong></div>
            </div>

            <div className="resource-handoff-detail-grid">
              <div>
                <h4>Resource assignment detail</h4>
                {(item.assignments ?? []).length > 0 ? (
                  <div className="resource-handoff-mini-list">
                    {item.assignments.map((assignment) => (
                      <div key={assignment.resourceAssignmentId}>
                        <strong>{assignment.engineerName}</strong>
                        <small>
                          {formatNumber(assignment.allocatedHours)} allocated hrs · {formatNumber(assignment.projectAssignedHours)} task hrs
                        </small>
                        <p>{assignment.taskLabels || 'No project task assignment found for this engineer yet.'}</p>
                      </div>
                    ))}
                  </div>
                ) : (
                  <p className="section-copy">No engineers are assigned to this resource request yet.</p>
                )}
              </div>

              <div>
                <h4>Project task readiness</h4>
                {(item.projectTasks ?? []).length > 0 ? (
                  <div className="resource-handoff-mini-list">
                    {item.projectTasks.slice(0, 6).map((task) => (
                      <div key={task.taskId}>
                        <strong>{task.taskCode} · {task.taskName}</strong>
                        <small>{task.workTaskCategory} · {task.billingClassification} · {task.utilizationClassification}</small>
                        <p>{formatNumber(task.assignmentCount)} assignments · {formatNumber(task.assignedHours)} assigned hrs</p>
                      </div>
                    ))}
                  </div>
                ) : (
                  <p className="section-copy">No project work tasks are available for this request yet.</p>
                )}
              </div>
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}
