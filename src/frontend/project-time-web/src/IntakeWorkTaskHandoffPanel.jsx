import { useEffect, useMemo, useState } from 'react';
import './intake-work-task-handoff-panel.css';

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

  if (response.status === 403) return { canViewIntakeWorkTaskHandoff: false };

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

export default function IntakeWorkTaskHandoffPanel() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });
  const [stageFilter, setStageFilter] = useState('all');

  async function loadHandoff() {
    setPayload((current) => ({ ...current, loading: true, error: null }));

    try {
      const data = await fetchJson('/api/project-intake/work-task-handoff');
      setPayload({ loading: false, data, error: null });
    } catch (error) {
      setPayload({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load intake handoff readiness.'
      });
    }
  }

  useEffect(() => {
    loadHandoff();
  }, []);

  const data = payload.data;
  const intakes = data?.intakes ?? [];
  const projects = data?.projects ?? [];

  const stages = useMemo(() => {
    return Array.from(new Set(intakes.map((item) => item.readinessStage).filter(Boolean)));
  }, [intakes]);

  const filteredIntakes = useMemo(() => {
    if (stageFilter === 'all') return intakes;
    return intakes.filter((item) => item.readinessStage === stageFilter);
  }, [intakes, stageFilter]);

  if (payload.loading) return null;

  if (!payload.error && !data?.canViewIntakeWorkTaskHandoff) {
    return null;
  }

  return (
    <section id="intake-work-task-handoff" className="panel intake-work-task-handoff-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">019M-AR</p>
          <h2>Project Intake → Work Task Builder Handoff</h2>
          <p className="section-copy">
            This readiness view explains how an intake should move into project work, work tasks, engineer assignments, timesheets, and utilization. It is visibility first; automatic conversion will come later.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={loadHandoff}>Refresh</button>
      </div>

      {payload.error ? <div className="error-text">{payload.error}</div> : null}

      <div className="handoff-lifecycle-grid">
        {(data?.lifecycle ?? []).map((step) => (
          <article key={step.step}>
            <span>{step.step}</span>
            <strong>{step.title}</strong>
            <p>{step.description}</p>
          </article>
        ))}
      </div>

      <div className="handoff-summary-grid">
        <article><span>Intakes</span><strong>{data?.summary?.intakeCount ?? 0}</strong><small>Total in scope</small></article>
        <article><span>Signed intakes</span><strong>{data?.summary?.signedIntakeCount ?? 0}</strong><small>Signed date recorded</small></article>
        <article><span>Direct project links</span><strong>{data?.summary?.directProjectLinkedIntakeCount ?? 0}</strong><small>Confirmed intake-to-project links</small></article>
        <article><span>Candidate matches</span><strong>{data?.summary?.candidateProjectMatchedIntakeCount ?? 0}</strong><small>Possible matches to confirm</small></article>
        <article><span>Task-ready</span><strong>{data?.summary?.taskReadyIntakeCount ?? 0}</strong><small>Project has work tasks</small></article>
        <article><span>Assignment-ready</span><strong>{data?.summary?.assignmentReadyIntakeCount ?? 0}</strong><small>Engineers assigned</small></article>
      </div>

      <div className="handoff-toolbar">
        <label>
          Readiness filter
          <select value={stageFilter} onChange={(event) => setStageFilter(event.target.value)}>
            <option value="all">All readiness stages</option>
            {stages.map((stage) => (
              <option value={stage} key={stage}>{stageLabel(stage)}</option>
            ))}
          </select>
        </label>
        <span>{filteredIntakes.length} intake record{filteredIntakes.length === 1 ? '' : 's'} shown</span>
      </div>

      <div className="handoff-card-list">
        {filteredIntakes.map((item) => (
          <article key={item.intakeId} className={`handoff-card stage-${item.readinessStage}`}>
            <div className="handoff-card-header">
              <div>
                <p className="eyebrow">{item.requestNumber}</p>
                <h3>{item.requestTitle}</h3>
                <small>{item.clientName} · PM: {item.assignedPmName || 'Unassigned'} · Status: {item.intakeStatus}</small>
              </div>
              <span>{stageLabel(item.readinessStage)}</span>
            </div>

            <p className="handoff-message">{item.readinessMessage}</p>

            <div className="handoff-readiness-grid">
              <div><span>Signed date</span><strong>{item.projectSignedDate || 'Missing'}</strong></div>
              <div><span>Direct projects</span><strong>{formatNumber(item.directlyLinkedProjectCount)}</strong></div>
              <div><span>Candidate projects</span><strong>{formatNumber(item.candidateProjectCount)}</strong></div>
              <div><span>Work tasks</span><strong>{formatNumber(item.taskCount)}</strong></div>
              <div><span>Assignments</span><strong>{formatNumber(item.assignmentCount)}</strong></div>
              <div><span>Engineers</span><strong>{formatNumber(item.assignedEngineerCount)}</strong></div>
              <div><span>Assigned hours</span><strong>{formatNumber(item.assignedHours)}</strong></div>
              <div><span>Timesheet entries</span><strong>{formatNumber(item.timeEntryCount)}</strong></div>
            </div>

            <div className="handoff-project-label">
              <strong>Project link/match:</strong>
              <span>{item.projectLabels || 'No project linked or matched yet.'}</span>
            </div>
          </article>
        ))}
      </div>

      <div className="handoff-project-readiness">
        <div className="section-heading compact">
          <div>
            <h3>Project Work Readiness</h3>
            <p className="section-copy">Projects in scope are shown with their task, assignment, and timesheet activity status.</p>
          </div>
        </div>

        <div className="handoff-project-grid">
          {projects.map((project) => (
            <article key={project.projectId}>
              <strong>{project.projectCode} · {project.projectName}</strong>
              <small>{project.clientName || 'No client'} · PM: {project.projectManagerName || 'Unassigned'}</small>
              <div>
                <span>{formatNumber(project.taskCount)} tasks</span>
                <span>{formatNumber(project.assignmentCount)} assignments</span>
                <span>{formatNumber(project.assignedEngineerCount)} engineers</span>
                <span>{formatNumber(project.usedHours)} used hrs</span>
              </div>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}
