import { useEffect, useMemo, useState } from 'react';

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
  const response = await fetch(path, {
    headers: getProjectPulseAuthHeaders()
  });

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, path));
  }

  return response.json();
}

async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...getProjectPulseAuthHeaders() },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, path));
  }

  return response.json();
}

export default function ProjectAllocationInfoPanel() {
  const [data, setData] = useState({ loading: true, projects: [], canManage: false, canPurge: false, error: null });
  const [engineers, setEngineers] = useState([]);
  const [sourceProjects, setSourceProjects] = useState([]);
  const [status, setStatus] = useState('Ready');
  const [projectDraft, setProjectDraft] = useState({
    projectCode: '',
    projectName: '',
    customerName: '',
    serviceRequestNumber: '',
    projectStatus: 'intake',
    sourceProjectId: '',
    sourceTaskId: '',
    sourceMappingNotes: '',
    allocations: []
  });
  const [uploadDraft, setUploadDraft] = useState({ projectId: '', documentType: 'SOW', file: null });
  const [purgeDraft, setPurgeDraft] = useState({ olderThanDays: 120, includeActiveProjects: false });

  async function loadProjectAllocationInfo() {
    setData((current) => ({ ...current, loading: true, error: null }));

    try {
      const projectsResult = await fetchJson('/api/project-allocation-info/projects');

      setData({
        loading: false,
        projects: projectsResult.projects ?? [],
        canManage: Boolean(projectsResult.canManage),
        canPurge: Boolean(projectsResult.canPurge),
        error: null
      });

      if (projectsResult.canManage) {
        try {
          const [engineersResult, sourceProjectsResult] = await Promise.all([
            fetchJson('/api/project-allocation-info/engineers'),
            fetchJson('/api/project-allocation-info/source-projects')
          ]);

          setEngineers(engineersResult.engineers ?? []);
          setSourceProjects(sourceProjectsResult.sourceProjects ?? []);
        } catch {
          setEngineers([]);
        }
      }
    } catch (error) {
      setData((current) => ({
        ...current,
        loading: false,
        error: error instanceof Error ? error.message : 'Unable to load project allocation information.'
      }));
    }
  }

  useEffect(() => {
    loadProjectAllocationInfo();
  }, []);

  const projectOptions = useMemo(() => data.projects.map((project) => ({
    projectId: project.projectId,
    label: `${project.projectCode} - ${project.projectName}`
  })), [data.projects]);

  function addAllocationRow() {
    setProjectDraft((current) => ({
      ...current,
      allocations: [
        ...current.allocations,
        { userId: '', allocatedHours: 0, notes: '' }
      ]
    }));
  }

  function updateAllocationRow(index, patch) {
    setProjectDraft((current) => ({
      ...current,
      allocations: current.allocations.map((allocation, allocationIndex) => (
        allocationIndex === index ? { ...allocation, ...patch } : allocation
      ))
    }));
  }

  function removeAllocationRow(index) {
    setProjectDraft((current) => ({
      ...current,
      allocations: current.allocations.filter((_, allocationIndex) => allocationIndex !== index)
    }));
  }

  async function saveProjectAllocation() {
    setStatus('Saving project allocation...');

    try {
      const payload = {
        ...projectDraft,
        sourceProjectId: projectDraft.sourceProjectId || null,
        sourceTaskId: projectDraft.sourceTaskId || null,
        sourceMappingNotes: projectDraft.sourceMappingNotes || '',
        allocations: projectDraft.allocations
          .filter((allocation) => allocation.userId)
          .map((allocation) => ({
            userId: allocation.userId,
            allocatedHours: Number(allocation.allocatedHours || 0),
            notes: allocation.notes ?? ''
          }))
      };

      const result = await postJson('/api/project-allocation-info/projects', payload);
      setStatus(result.message ?? 'Project allocation saved.');

      setProjectDraft({
        projectCode: '',
        projectName: '',
        customerName: '',
        serviceRequestNumber: '',
        projectStatus: 'intake',
        sourceProjectId: '',
        sourceTaskId: '',
        sourceMappingNotes: '',
        allocations: []
      });

      await loadProjectAllocationInfo();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to save project allocation.');
    }
  }

  async function uploadDocument() {
    if (!uploadDraft.projectId || !uploadDraft.file) {
      setStatus('Select a project and file before uploading.');
      return;
    }

    setStatus(`Uploading ${uploadDraft.documentType}...`);

    try {
      const formData = new FormData();
      formData.append('projectId', uploadDraft.projectId);
      formData.append('documentType', uploadDraft.documentType);
      formData.append('file', uploadDraft.file);

      const response = await fetch('/api/project-allocation-info/documents/upload', {
        method: 'POST',
        headers: getProjectPulseAuthHeaders(),
        body: formData
      });

      if (!response.ok) {
        throw new Error(await readApiErrorMessage(response, '/api/project-allocation-info/documents/upload'));
      }

      const result = await response.json();
      setStatus(result.message ?? 'Document uploaded.');
      setUploadDraft({ projectId: uploadDraft.projectId, documentType: 'SOW', file: null });

      await loadProjectAllocationInfo();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to upload document.');
    }
  }

  async function purgeOldDocuments() {
    setStatus('Purging old SOW/GSD documents...');

    try {
      const result = await postJson('/api/project-allocation-info/documents/purge', {
        olderThanDays: Number(purgeDraft.olderThanDays || 120),
        includeActiveProjects: Boolean(purgeDraft.includeActiveProjects),
        purgeReason: 'Manual purge from Project Allocation and Info page.'
      });

      setStatus(result.message ?? 'Document purge completed.');
      await loadProjectAllocationInfo();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to purge documents.');
    }
  }

  return (
    <div className="project-allocation-shell">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Project Allocation and Info</p>
          <h1>Project hours, SOW/GSD, and engineer allocation</h1>
          <p className="section-copy">
            PMs and Project/Team Coordinators can upload SOW/GSD files and allocate engineer hours during intake. Engineers can view assigned projects and download project documents.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={loadProjectAllocationInfo}>
          Refresh
        </button>
      </div>

      {data.error && <div className="error-text">{data.error}</div>}

      <div className="manager-status-row">
        <span>Projects: <strong>{data.loading ? 'Loading...' : data.projects.length}</strong></span>
        <span>Manage access: <strong>{data.canManage ? 'Yes' : 'No'}</strong></span>
        <span>Action: <strong>{status}</strong></span>
      </div>

      {data.canManage && (
        <div className="project-allocation-admin-grid">
          <div className="project-allocation-card">
            <p className="eyebrow">Project intake</p>
            <h2>Create or update project allocation</h2>

            <label>Project code</label>
            <input value={projectDraft.projectCode} onChange={(event) => setProjectDraft((current) => ({ ...current, projectCode: event.target.value }))} />

            <label>Project name</label>
            <input value={projectDraft.projectName} onChange={(event) => setProjectDraft((current) => ({ ...current, projectName: event.target.value }))} />

            <label>Customer name</label>
            <input value={projectDraft.customerName} onChange={(event) => setProjectDraft((current) => ({ ...current, customerName: event.target.value }))} />

            <label>Service request number</label>
            <input value={projectDraft.serviceRequestNumber} onChange={(event) => setProjectDraft((current) => ({ ...current, serviceRequestNumber: event.target.value }))} />

            <label>Project status</label>
            <select value={projectDraft.projectStatus} onChange={(event) => setProjectDraft((current) => ({ ...current, projectStatus: event.target.value }))}>
              <option value="intake">Intake</option>
              <option value="active">Active</option>
              <option value="in_progress">In progress</option>
              <option value="closed">Closed</option>
              <option value="cancelled">Cancelled</option>
            </select>

            <label>Source project mapping</label>
            <select
              value={`${projectDraft.sourceProjectId || ''}|${projectDraft.sourceTaskId || ''}`}
              onChange={(event) => {
                const [sourceProjectId, sourceTaskId] = event.target.value.split('|');
                setProjectDraft((current) => ({
                  ...current,
                  sourceProjectId,
                  sourceTaskId
                }));
              }}
            >
              <option value="|">Select source project/time-entry mapping</option>
              {sourceProjects.map((source) => (
                <option
                  value={`${source.projectId}|${source.taskId ?? ''}`}
                  key={`${source.projectId}-${source.taskId ?? 'all'}`}
                >
                  Project {source.projectId}{source.taskId ? ` / Task ${source.taskId}` : ' / All tasks'} • {Number(source.billableHours ?? 0).toFixed(2)} billable hrs
                </option>
              ))}
            </select>

            <label>Source mapping notes</label>
            <input
              value={projectDraft.sourceMappingNotes}
              onChange={(event) => setProjectDraft((current) => ({ ...current, sourceMappingNotes: event.target.value }))}
              placeholder="Optional notes about project/service request mapping"
            />

            <div className="section-heading compact-heading">
              <div>
                <p className="eyebrow">Engineer allocation</p>
                <h3>Allocate hours</h3>
              </div>
              <button type="button" className="secondary-action" onClick={addAllocationRow}>
                Add engineer
              </button>
            </div>

            <div className="allocation-edit-list">
              {projectDraft.allocations.map((allocation, index) => (
                <div className="allocation-edit-row" key={`${allocation.userId}-${index}`}>
                  <select value={allocation.userId} onChange={(event) => updateAllocationRow(index, { userId: event.target.value })}>
                    <option value="">Select engineer</option>
                    {engineers.map((engineer) => (
                      <option value={engineer.userId} key={engineer.userId}>
                        {engineer.displayName} - {engineer.teamName ?? engineer.departmentName ?? engineer.email}
                      </option>
                    ))}
                  </select>
                  <input
                    type="number"
                    min="0"
                    step="0.25"
                    value={allocation.allocatedHours}
                    onChange={(event) => updateAllocationRow(index, { allocatedHours: event.target.value })}
                    placeholder="Allocated hours"
                  />
                  <input
                    value={allocation.notes}
                    onChange={(event) => updateAllocationRow(index, { notes: event.target.value })}
                    placeholder="Notes"
                  />
                  <button type="button" className="secondary-action" onClick={() => removeAllocationRow(index)}>
                    Remove
                  </button>
                </div>
              ))}
            </div>

            <button type="button" className="primary-action" onClick={saveProjectAllocation}>
              Save project allocation
            </button>
          </div>

          <div className="project-allocation-card">
            <p className="eyebrow">Documents</p>
            <h2>Upload SOW or GSD</h2>

            <label>Project</label>
            <select value={uploadDraft.projectId} onChange={(event) => setUploadDraft((current) => ({ ...current, projectId: event.target.value }))}>
              <option value="">Select project</option>
              {projectOptions.map((project) => (
                <option value={project.projectId} key={project.projectId}>{project.label}</option>
              ))}
            </select>

            <label>Document type</label>
            <select value={uploadDraft.documentType} onChange={(event) => setUploadDraft((current) => ({ ...current, documentType: event.target.value }))}>
              <option value="SOW">SOW</option>
              <option value="GSD">GSD</option>
            </select>

            <label>File</label>
            <input type="file" onChange={(event) => setUploadDraft((current) => ({ ...current, file: event.target.files?.[0] ?? null }))} />

            <button type="button" className="primary-action" onClick={uploadDocument}>
              Upload document
            </button>

            {data.canPurge && (
              <div className="document-purge-box">
                <p className="eyebrow">Storage cleanup</p>
                <h3>Purge old SOW/GSD files</h3>

                <label>Older than days</label>
                <input
                  type="number"
                  min="1"
                  value={purgeDraft.olderThanDays}
                  onChange={(event) => setPurgeDraft((current) => ({ ...current, olderThanDays: event.target.value }))}
                />

                <label className="checkbox-row">
                  <input
                    type="checkbox"
                    checked={purgeDraft.includeActiveProjects}
                    onChange={(event) => setPurgeDraft((current) => ({ ...current, includeActiveProjects: event.target.checked }))}
                  />
                  Include active projects
                </label>

                <button type="button" className="danger-action" onClick={purgeOldDocuments}>
                  Purge documents
                </button>
              </div>
            )}
          </div>
        </div>
      )}

      <div className="project-allocation-grid">
        {data.projects.map((project) => (
          <article className="project-allocation-card project-allocation-project-card" key={project.projectId}>
            <div className="project-allocation-card-header">
              <div>
                <p className="eyebrow">{project.projectCode}</p>
                <h2>{project.projectName}</h2>
                <p className="section-copy">
                  {project.customerName ?? 'No customer listed'} {project.serviceRequestNumber ? `• SR ${project.serviceRequestNumber}` : ''}
                </p>
                <p className="source-mapping-copy">
                  Source project: <strong>{project.sourceProjectId ?? 'Not mapped'}</strong>
                  {project.sourceTaskId ? <> • Task: <strong>{project.sourceTaskId}</strong></> : null}
                </p>
              </div>
              <span className="badge">{project.projectStatus}</span>
            </div>

            <div className="project-hours-summary">
              <span>Allocated <strong>{Number(project.totalAllocatedHours ?? 0).toFixed(2)} hrs</strong></span>
              <span>Used <strong>{Number(project.totalUsedHours ?? 0).toFixed(2)} hrs</strong></span>
              <span>Remaining <strong>{Number(project.totalRemainingHours ?? 0).toFixed(2)} hrs</strong></span>
            </div>

            <h3>Engineer allocations</h3>
            <div className="allocation-list">
              {project.allocations?.length ? project.allocations.map((allocation) => (
                <div className="allocation-row" key={allocation.allocationId}>
                  <span>
                    <strong>{allocation.displayName}</strong>
                    <small>{allocation.teamName ?? allocation.departmentName ?? allocation.email}</small>
                  </span>
                  <span>{Number(allocation.allocatedHours).toFixed(2)} allocated</span>
                  <span>{Number(allocation.usedHours).toFixed(2)} used</span>
                  <span>{Number(allocation.remainingHours).toFixed(2)} remaining</span>
                </div>
              )) : (
                <div className="manager-empty-state">No engineer allocations have been added yet.</div>
              )}
            </div>

            <h3>SOW/GSD documents</h3>
            <div className="document-link-list">
              {project.documents?.length ? project.documents.map((document) => (
                <div className="document-link-row" key={document.documentId}>
                  <span>
                    <strong>{document.documentType}</strong>
                    <small>{document.originalFileName}</small>
                  </span>
                  {document.isPurged ? (
                    <span className="purged-label">Purged</span>
                  ) : (
                    <a className="secondary-action" href={document.downloadUrl}>
                      Download
                    </a>
                  )}
                </div>
              )) : (
                <div className="manager-empty-state">No SOW or GSD documents uploaded yet.</div>
              )}
            </div>
          </article>
        ))}
      </div>
    </div>
  );
}
