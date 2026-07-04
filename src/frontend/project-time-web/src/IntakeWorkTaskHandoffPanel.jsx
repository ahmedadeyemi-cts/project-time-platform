import { useEffect, useMemo, useState } from 'react';
import './intake-work-task-handoff-panel.css';

function getProjectPulseAuthHeaders() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return {};
    const session = JSON.parse(rawSession);
    return session?.sessionToken ? { 'X-Project Health Dashboard-Session': session.sessionToken } : {};
  } catch {
    return {};
  }
}

async function fetchJson(path) {
  const response = await fetch(path, { headers: getProjectPulseAuthHeaders() });

  if (response.status === 403) return { canViewIntakeWorkTaskHandoff: false, canViewProjectLinkOptions: false };

  if (!response.ok) {
    const raw = await response.text();
    throw new Error(raw || `${path} returned HTTP ${response.status}`);
  }

  return response.json();
}

async function postJson(path, body) {
  const response = await fetch(path, {
    method: 'POST',
    headers: {
      ...getProjectPulseAuthHeaders(),
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(body)
  });

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

function optionLabel(project) {
  return `${project.projectCode} — ${project.projectName}${project.clientName ? ` · ${project.clientName}` : ''}`;
}

export default function IntakeWorkTaskHandoffPanel() {
  const [payload, setPayload] = useState({ loading: true, data: null, options: null, error: null });
  const [stageFilter, setStageFilter] = useState('all');
  const [linkForms, setLinkForms] = useState({});
  const [actionStatus, setActionStatus] = useState('');

  async function loadHandoff() {
    setPayload((current) => ({ ...current, loading: true, error: null }));

    try {
      const [data, options] = await Promise.all([
        fetchJson('/api/project-intake/work-task-handoff'),
        fetchJson('/api/project-intake/project-link-options')
      ]);

      setPayload({ loading: false, data, options, error: null });

      const nextForms = {};
      (options?.intakes ?? []).forEach((item) => {
        nextForms[item.intakeId] = {
          projectId: item.confirmedProjectId || '',
          confirmationNote: item.confirmationNote || ''
        };
      });
      setLinkForms(nextForms);
    } catch (error) {
      setPayload({
        loading: false,
        data: null,
        options: null,
        error: error instanceof Error ? error.message : 'Unable to load intake handoff readiness.'
      });
    }
  }

  useEffect(() => {
    loadHandoff();
  }, []);

  const data = payload.data;
  const options = payload.options;
  const intakes = data?.intakes ?? [];
  const projects = options?.projects ?? data?.projects ?? [];

  const optionByIntake = useMemo(() => {
    const map = new Map();
    (options?.intakes ?? []).forEach((item) => map.set(item.intakeId, item));
    return map;
  }, [options]);

  const stages = useMemo(() => {
    return Array.from(new Set(intakes.map((item) => item.readinessStage).filter(Boolean)));
  }, [intakes]);

  const filteredIntakes = useMemo(() => {
    if (stageFilter === 'all') return intakes;
    return intakes.filter((item) => item.readinessStage === stageFilter);
  }, [intakes, stageFilter]);

  const canManageProjectLinks = Boolean(data?.access?.canManageProjectLinks || options?.canManageProjectLinks);

  async function confirmProjectLink(intake) {
    const form = linkForms[intake.intakeId] ?? {};

    if (!form.projectId) {
      setActionStatus('Select a project before confirming the intake project link.');
      return;
    }

    setActionStatus(`Confirming project link for ${intake.requestNumber}...`);

    try {
      const result = await postJson(`/api/project-intake/${intake.intakeId}/project-link`, {
        projectId: form.projectId,
        confirmationNote: form.confirmationNote || `Confirmed from Project Intake handoff readiness for ${intake.requestNumber}.`
      });

      setActionStatus(result.message || 'Project link confirmed.');
      await loadHandoff();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to confirm project link.');
    }
  }

  if (payload.loading) return null;

  if (!payload.error && !data?.canViewIntakeWorkTaskHandoff) {
    return null;
  }

  return (
    <section id="intake-work-task-handoff" className="panel intake-work-task-handoff-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">019M-AR / 019M-AS</p>
          <h2>Project Intake → Work Task Builder Handoff</h2>
          <p className="section-copy">
            This readiness view explains how an intake should move into project work, work tasks, engineer assignments, timesheets, and utilization. Project links are manually confirmed; automatic conversion remains disabled.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={loadHandoff}>Refresh</button>
      </div>

      {payload.error ? <div className="error-text">{payload.error}</div> : null}
      {actionStatus ? <div className="project-intake-alert">{actionStatus}</div> : null}

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
        {filteredIntakes.map((item) => {
          const linkOption = optionByIntake.get(item.intakeId);
          const candidateProjects = linkOption?.candidateProjects ?? [];
          const form = linkForms[item.intakeId] ?? { projectId: '', confirmationNote: '' };
          const candidateIds = new Set(candidateProjects.map((project) => project.projectId));
          const remainingProjects = projects.filter((project) => !candidateIds.has(project.projectId));

          return (
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
                <strong>Current project link/match:</strong>
                <span>{item.projectLabels || 'No project linked or matched yet.'}</span>
              </div>

              {canManageProjectLinks ? (
                <div className="handoff-confirm-panel">
                  <h4>Confirm project link</h4>
                  <p className="section-copy">
                    Select the project that belongs to this intake. This confirms the relationship only; it does not automatically create projects, tasks, or timesheets.
                  </p>

                  {linkOption?.confirmedProjectId ? (
                    <div className="handoff-confirmed-link">
                      Confirmed: {linkOption.confirmedProjectCode} — {linkOption.confirmedProjectName}
                    </div>
                  ) : null}

                  <label>
                    Project
                    <select
                      value={form.projectId}
                      onChange={(event) => setLinkForms({
                        ...linkForms,
                        [item.intakeId]: { ...form, projectId: event.target.value }
                      })}
                    >
                      <option value="">Select project</option>
                      {candidateProjects.length > 0 ? <option disabled>Suggested matches</option> : null}
                      {candidateProjects.map((project) => (
                        <option key={`candidate-${project.projectId}`} value={project.projectId}>{optionLabel(project)}</option>
                      ))}
                      {remainingProjects.length > 0 ? <option disabled>All available projects</option> : null}
                      {remainingProjects.map((project) => (
                        <option key={`all-${project.projectId}`} value={project.projectId}>{optionLabel(project)}</option>
                      ))}
                    </select>
                  </label>

                  <label>
                    Confirmation note
                    <textarea
                      value={form.confirmationNote}
                      onChange={(event) => setLinkForms({
                        ...linkForms,
                        [item.intakeId]: { ...form, confirmationNote: event.target.value }
                      })}
                      placeholder="Why is this the correct project link?"
                    />
                  </label>

                  <button type="button" className="primary-action" onClick={() => confirmProjectLink(item)}>
                    Confirm project link
                  </button>
                </div>
              ) : null}
            </article>
          );
        })}
      </div>
    </section>
  );
}
