import { useEffect, useMemo, useState } from 'react';
import './work-task-builder-panel.css';

function getProjectPulseAuthHeaders(extra = {}) {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return extra;
    const session = JSON.parse(rawSession);
    return session?.sessionToken ? { ...extra, 'X-ProjectPulse-Session': session.sessionToken } : extra;
  } catch {
    return extra;
  }
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

async function fetchJson(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: getProjectPulseAuthHeaders(options.headers ?? {})
  });

  if (response.status === 403) {
    return { canViewWorkTaskBuilder: false };
  }

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

function formatNumber(value) {
  return Number(value ?? 0).toLocaleString(undefined, { maximumFractionDigits: 2 });
}

function labelFrom(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

const defaultTemplateForm = {
  templateName: '',
  templateDescription: '',
  taskCategory: 'project_task',
  billingClassification: 'billable',
  utilizationClassification: 'billable_utilization'
};

const defaultProjectTaskForm = {
  projectId: '',
  taskName: '',
  taskDescription: '',
  taskCategory: 'project_task',
  billingClassification: 'billable',
  utilizationClassification: 'billable_utilization',
  serviceRequestNumber: ''
};

const defaultAssignmentForm = {
  projectId: '',
  taskId: '',
  engineerUserId: '',
  assignedHours: '0',
  allocationPercent: '0',
  effectiveStartDate: new Date().toISOString().slice(0, 10),
  effectiveEndDate: '',
  assignmentNotes: ''
};

export default function WorkTaskBuilderPanel() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });
  const [templateForm, setTemplateForm] = useState(defaultTemplateForm);
  const [projectTaskForm, setProjectTaskForm] = useState(defaultProjectTaskForm);
  const [assignmentForm, setAssignmentForm] = useState(defaultAssignmentForm);
  const [actionMessage, setActionMessage] = useState('');

  async function loadSummary() {
    setPayload((current) => ({ ...current, loading: true, error: null }));

    try {
      const data = await fetchJson('/api/work-tasks/summary');
      setPayload({ loading: false, data, error: null });
    } catch (error) {
      setPayload({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load Work Task Builder.'
      });
    }
  }

  useEffect(() => {
    loadSummary();
  }, []);

  const data = payload.data;
  const access = data?.access ?? {};
  const projects = data?.projects ?? [];
  const engineers = data?.engineers ?? [];
  const templates = data?.templates ?? [];
  const classifications = data?.classifications ?? {};
  const selectedProjectForAssignment = projects.find((project) => project.projectId === assignmentForm.projectId);
  const selectedProjectTasks = selectedProjectForAssignment?.tasks ?? [];

  const projectOptions = useMemo(() => projects.filter((project) => (project.tasks ?? []).length >= 0), [projects]);

  if (payload.loading) return null;

  if (!payload.error && !data?.canViewWorkTaskBuilder) {
    return null;
  }

  async function postJson(path, body) {
    setActionMessage('');
    const result = await fetchJson(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });

    setActionMessage(result.message || result.status || 'Saved.');
    await loadSummary();
    return result;
  }

  async function handleTemplateSubmit(event) {
    event.preventDefault();
    try {
      await postJson('/api/work-tasks/templates', templateForm);
      setTemplateForm(defaultTemplateForm);
    } catch (error) {
      setActionMessage(error instanceof Error ? error.message : 'Unable to save template.');
    }
  }

  async function handleProjectTaskSubmit(event) {
    event.preventDefault();
    try {
      const result = await postJson('/api/work-tasks/project-tasks', projectTaskForm);
      setProjectTaskForm(defaultProjectTaskForm);
      setAssignmentForm((current) => ({
        ...current,
        projectId: result.projectId ?? current.projectId,
        taskId: result.taskId ?? current.taskId
      }));
    } catch (error) {
      setActionMessage(error instanceof Error ? error.message : 'Unable to create project task.');
    }
  }

  async function handleAssignmentSubmit(event) {
    event.preventDefault();
    try {
      await postJson('/api/work-tasks/assignments', {
        ...assignmentForm,
        assignedHours: Number(assignmentForm.assignedHours || 0),
        allocationPercent: Number(assignmentForm.allocationPercent || 0),
        effectiveEndDate: assignmentForm.effectiveEndDate || null
      });
      setAssignmentForm(defaultAssignmentForm);
    } catch (error) {
      setActionMessage(error instanceof Error ? error.message : 'Unable to assign work task.');
    }
  }

  function syncUtilizationFromBilling(nextBillingClassification, setter) {
    setter((current) => ({
      ...current,
      billingClassification: nextBillingClassification,
      utilizationClassification: nextBillingClassification === 'billable' ? 'billable_utilization' : 'non_billable_utilization'
    }));
  }

  return (
    <section id="work-task-builder" className="panel work-task-builder-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">019M-AP</p>
          <h2>Work Task Builder</h2>
          <p className="section-copy">
            Build and classify work tasks across project work, service request work, open tasks, and non-project work. Classifications control billing and utilization treatment.
          </p>
        </div>
        <span className="badge">Task Classification Foundation</span>
      </div>

      {payload.error ? <div className="error-text">{payload.error}</div> : null}
      {actionMessage ? <div className="success-text">{actionMessage}</div> : null}

      <div className="work-task-summary-grid">
        <article>
          <span>Templates</span>
          <strong>{data?.summary?.templateCount ?? 0}</strong>
          <small>Global work task patterns</small>
        </article>
        <article>
          <span>Projects in scope</span>
          <strong>{data?.summary?.projectCount ?? 0}</strong>
          <small>{access.canViewAll ? 'Organization scope' : 'Assigned PM scope'}</small>
        </article>
        <article>
          <span>Project tasks</span>
          <strong>{data?.summary?.projectTaskCount ?? 0}</strong>
          <small>Classified delivery tasks</small>
        </article>
        <article>
          <span>Engineers</span>
          <strong>{data?.summary?.engineerCount ?? 0}</strong>
          <small>Available for assignment</small>
        </article>
      </div>

      <div className="work-task-classification-grid">
        {(classifications.taskCategories ?? []).map((item) => (
          <article key={item.value}>
            <strong>{item.label}</strong>
            <p>{item.description}</p>
          </article>
        ))}
      </div>

      <div className="work-task-builder-layout">
        <div className="work-task-card">
          <div className="section-heading compact">
            <div>
              <h3>Global templates</h3>
              <p className="section-copy">PTC/Admin managed starting points for project, service request, open, and non-project work.</p>
            </div>
          </div>

          <div className="work-task-template-list">
            {templates.map((template) => (
              <article key={template.templateId}>
                <div>
                  <strong>{template.templateName}</strong>
                  <small>{template.templateCode}</small>
                  <p>{template.templateDescription || 'No description provided.'}</p>
                </div>
                <span>{labelFrom(template.taskCategory)}</span>
                <span>{labelFrom(template.billingClassification)}</span>
                <span>{labelFrom(template.utilizationClassification)}</span>
              </article>
            ))}
          </div>

          {access.canManageTemplates ? (
            <form className="work-task-form" onSubmit={handleTemplateSubmit}>
              <h4>Add or update template</h4>
              <label>
                Template name
                <input value={templateForm.templateName} onChange={(event) => setTemplateForm({ ...templateForm, templateName: event.target.value })} required />
              </label>
              <label>
                Description
                <textarea value={templateForm.templateDescription} onChange={(event) => setTemplateForm({ ...templateForm, templateDescription: event.target.value })} rows={3} />
              </label>
              <label>
                Task category
                <select value={templateForm.taskCategory} onChange={(event) => setTemplateForm({ ...templateForm, taskCategory: event.target.value })}>
                  {(classifications.taskCategories ?? []).map((item) => <option value={item.value} key={item.value}>{item.label}</option>)}
                </select>
              </label>
              <label>
                Billing classification
                <select value={templateForm.billingClassification} onChange={(event) => syncUtilizationFromBilling(event.target.value, setTemplateForm)}>
                  {(classifications.billingClassifications ?? []).map((item) => <option value={item.value} key={item.value}>{item.label}</option>)}
                </select>
              </label>
              <label>
                Utilization classification
                <select value={templateForm.utilizationClassification} onChange={(event) => setTemplateForm({ ...templateForm, utilizationClassification: event.target.value })}>
                  {(classifications.utilizationClassifications ?? []).map((item) => <option value={item.value} key={item.value}>{item.label}</option>)}
                </select>
              </label>
              <button type="submit" className="primary-action">Save template</button>
            </form>
          ) : null}
        </div>

        <div className="work-task-card">
          <div className="section-heading compact">
            <div>
              <h3>Project work tasks</h3>
              <p className="section-copy">Project Managers can create and assign tasks only inside their project scope. PTC/Admin can work across all projects.</p>
            </div>
          </div>

          {access.canAssignTasks ? (
            <>
              <form className="work-task-form" onSubmit={handleProjectTaskSubmit}>
                <h4>Create project work task</h4>
                <label>
                  Project
                  <select value={projectTaskForm.projectId} onChange={(event) => setProjectTaskForm({ ...projectTaskForm, projectId: event.target.value })} required>
                    <option value="">Select project</option>
                    {projectOptions.map((project) => (
                      <option value={project.projectId} key={project.projectId}>{project.projectCode} · {project.projectName}</option>
                    ))}
                  </select>
                </label>
                <label>
                  Task name
                  <input value={projectTaskForm.taskName} onChange={(event) => setProjectTaskForm({ ...projectTaskForm, taskName: event.target.value })} required />
                </label>
                <label>
                  Description
                  <textarea value={projectTaskForm.taskDescription} onChange={(event) => setProjectTaskForm({ ...projectTaskForm, taskDescription: event.target.value })} rows={3} />
                </label>
                <label>
                  Task category
                  <select value={projectTaskForm.taskCategory} onChange={(event) => setProjectTaskForm({ ...projectTaskForm, taskCategory: event.target.value })}>
                    {(classifications.taskCategories ?? []).map((item) => <option value={item.value} key={item.value}>{item.label}</option>)}
                  </select>
                </label>
                <label>
                  Billing classification
                  <select value={projectTaskForm.billingClassification} onChange={(event) => syncUtilizationFromBilling(event.target.value, setProjectTaskForm)}>
                    {(classifications.billingClassifications ?? []).map((item) => <option value={item.value} key={item.value}>{item.label}</option>)}
                  </select>
                </label>
                <label>
                  Utilization classification
                  <select value={projectTaskForm.utilizationClassification} onChange={(event) => setProjectTaskForm({ ...projectTaskForm, utilizationClassification: event.target.value })}>
                    {(classifications.utilizationClassifications ?? []).map((item) => <option value={item.value} key={item.value}>{item.label}</option>)}
                  </select>
                </label>
                {projectTaskForm.taskCategory === 'service_request_task' ? (
                  <label>
                    Service request number
                    <input value={projectTaskForm.serviceRequestNumber} onChange={(event) => setProjectTaskForm({ ...projectTaskForm, serviceRequestNumber: event.target.value })} />
                  </label>
                ) : null}
                <button type="submit" className="primary-action">Create task</button>
              </form>

              <form className="work-task-form" onSubmit={handleAssignmentSubmit}>
                <h4>Assign work task</h4>
                <label>
                  Project
                  <select value={assignmentForm.projectId} onChange={(event) => setAssignmentForm({ ...assignmentForm, projectId: event.target.value, taskId: '' })} required>
                    <option value="">Select project</option>
                    {projectOptions.map((project) => (
                      <option value={project.projectId} key={project.projectId}>{project.projectCode} · {project.projectName}</option>
                    ))}
                  </select>
                </label>
                <label>
                  Task
                  <select value={assignmentForm.taskId} onChange={(event) => setAssignmentForm({ ...assignmentForm, taskId: event.target.value })} required>
                    <option value="">Select task</option>
                    {selectedProjectTasks.map((task) => (
                      <option value={task.taskId} key={task.taskId}>{task.taskCode} · {task.taskName}</option>
                    ))}
                  </select>
                </label>
                <label>
                  Engineer
                  <select value={assignmentForm.engineerUserId} onChange={(event) => setAssignmentForm({ ...assignmentForm, engineerUserId: event.target.value })} required>
                    <option value="">Select engineer</option>
                    {engineers.map((engineer) => (
                      <option value={engineer.userId} key={engineer.userId}>{engineer.displayName} · {engineer.teamName}</option>
                    ))}
                  </select>
                </label>
                <label>
                  Assigned hours
                  <input type="number" min="0" step="0.25" value={assignmentForm.assignedHours} onChange={(event) => setAssignmentForm({ ...assignmentForm, assignedHours: event.target.value })} />
                </label>
                <label>
                  Allocation percent
                  <input type="number" min="0" max="100" step="1" value={assignmentForm.allocationPercent} onChange={(event) => setAssignmentForm({ ...assignmentForm, allocationPercent: event.target.value })} />
                </label>
                <label>
                  Start date
                  <input type="date" value={assignmentForm.effectiveStartDate} onChange={(event) => setAssignmentForm({ ...assignmentForm, effectiveStartDate: event.target.value })} required />
                </label>
                <label>
                  End date
                  <input type="date" value={assignmentForm.effectiveEndDate} onChange={(event) => setAssignmentForm({ ...assignmentForm, effectiveEndDate: event.target.value })} />
                </label>
                <label>
                  Assignment notes
                  <textarea value={assignmentForm.assignmentNotes} onChange={(event) => setAssignmentForm({ ...assignmentForm, assignmentNotes: event.target.value })} rows={3} />
                </label>
                <button type="submit" className="primary-action">Assign task</button>
              </form>
            </>
          ) : (
            <p className="section-copy">This role can review classifications and readiness but cannot create or assign work tasks.</p>
          )}
        </div>
      </div>

      <div className="work-task-card full-width">
        <div className="section-heading compact">
          <div>
            <h3>Project task readiness</h3>
            <p className="section-copy">Assigned project tasks flow into the Engineer timesheet task selector and retain billable/utilization classification.</p>
          </div>
        </div>

        <div className="work-task-project-list">
          {projects.map((project) => (
            <article key={project.projectId}>
              <div className="work-task-project-header">
                <div>
                  <strong>{project.projectCode} · {project.projectName}</strong>
                  <small>{project.clientName || 'No client'} · PM: {project.projectManagerName || 'Unassigned'}</small>
                </div>
                <span>{project.projectBillable ? 'Billable project' : 'Non-billable project'}</span>
              </div>

              <div className="work-task-table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Task</th>
                      <th>Category</th>
                      <th>Billing</th>
                      <th>Utilization</th>
                      <th>Assigned</th>
                      <th>Used</th>
                      <th>Remaining</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(project.tasks ?? []).map((task) => (
                      <tr key={task.taskId}>
                        <td><strong>{task.taskCode}</strong><span>{task.taskName}</span></td>
                        <td>{labelFrom(task.workTaskCategory)}</td>
                        <td>{labelFrom(task.billingClassification)}</td>
                        <td>{labelFrom(task.utilizationClassification)}</td>
                        <td>{formatNumber(task.assignedHours)} hrs · {task.assignedEngineerCount} engineer{task.assignedEngineerCount === 1 ? '' : 's'}</td>
                        <td>{formatNumber(task.usedHours)} hrs</td>
                        <td>{formatNumber(task.remainingHours)} hrs</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {(project.tasks ?? []).length === 0 ? (
                <p className="section-copy">No tasks have been created for this project yet.</p>
              ) : null}
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}
