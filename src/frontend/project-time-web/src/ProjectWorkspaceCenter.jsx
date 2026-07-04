import { useEffect, useMemo, useState } from 'react';
import './project-workspace-center.css';

function getStoredProjectPulseAuthSession() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return null;

    const parsed = JSON.parse(rawSession);
    if (!parsed?.sessionToken) return null;

    if (parsed?.expiresAt && Date.now() >= Date.parse(parsed.expiresAt)) {
      return null;
    }

    return parsed;
  } catch {
    return null;
  }
}

function getProjectPulseAuthHeaders(viewAsUserId = '') {
  const session = getStoredProjectPulseAuthSession();
  const headers = session?.sessionToken ? { 'X-Project Health Dashboard-Session': session.sessionToken } : {};

  if (viewAsUserId) {
    headers['X-Project Health Dashboard-View-As-User'] = viewAsUserId;
  }

  return headers;
}

async function fetchJson(path, viewAsUserId = '') {
  const response = await fetch(path, {
    headers: getProjectPulseAuthHeaders(viewAsUserId)
  });

  const body = await response.text();

  if (!response.ok) {
    throw new Error(`${path} returned HTTP ${response.status}${body ? `: ${body.slice(0, 160)}` : ''}`);
  }

  if (!body.trim()) {
    throw new Error(`${path} returned HTTP ${response.status} with an empty response body.`);
  }

  try {
    return JSON.parse(body);
  } catch {
    throw new Error(`${path} returned invalid JSON.`);
  }
}

function fmt(value) {
  return value ?? 'Not set';
}

function StatusBadge({ children, tone = 'neutral' }) {
  return <span className={`workspace-badge ${tone}`}>{children}</span>;
}

export default function ProjectWorkspaceCenter() {
  const [overview, setOverview] = useState({ loading: true, data: null, error: null });
  const [documentFilter, setDocumentFilter] = useState('engineering');
  const [viewAsUsers, setViewAsUsers] = useState([]);
  const [selectedViewAsUserId, setSelectedViewAsUserId] = useState('');

  async function loadViewAsUsers() {
    try {
      const result = await fetchJson('/api/project-workspace/view-as/users');
      setViewAsUsers(result.users ?? []);
    } catch {
      setViewAsUsers([]);
    }
  }

  async function loadOverview(viewAsUserId = selectedViewAsUserId) {
    setOverview((current) => ({ ...current, loading: true, error: null }));

    try {
      const result = await fetchJson('/api/project-workspace/overview', viewAsUserId);
      setOverview({ loading: false, data: result, error: null });
    } catch (error) {
      setOverview({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load project workspace overview.'
      });
    }
  }

  useEffect(() => {
    loadViewAsUsers();
  }, []);

  useEffect(() => {
    loadOverview(selectedViewAsUserId);
  }, [selectedViewAsUserId]);

  const projects = overview.data?.projects ?? [];
  const documents = overview.data?.documents ?? [];
  const assignments = overview.data?.assignments ?? [];
  const resourceRequests = overview.data?.resourceRequests ?? [];
  const access = overview.data?.access;
  const selectedViewAsUser = viewAsUsers.find((user) => user.userId === selectedViewAsUserId);

  const filteredDocuments = useMemo(() => {
    if (documentFilter === 'ai') {
      return documents.filter((document) => document.aiTimesheetContextEnabled);
    }

    if (documentFilter === 'engineering') {
      return documents.filter((document) => document.engineeringVisible);
    }

    return documents;
  }, [documents, documentFilter]);

  return (
    <section className="project-workspace-center">
      {viewAsUsers.length > 0 ? (
        <div className="admin-view-as-panel">
          <div>
            <strong>Administrator User Experience Preview</strong>
            <p>View this workspace as another user. Preview mode is read-only and audited.</p>
          </div>
          <label>
            View as
            <select value={selectedViewAsUserId} onChange={(event) => setSelectedViewAsUserId(event.target.value)}>
              <option value="">My Administrator view</option>
              {viewAsUsers.map((user) => (
                <option value={user.userId} key={user.userId}>
                  {user.displayName} — {user.roleCodes || 'No role'} — {user.teamOrDepartment || 'No team'}
                </option>
              ))}
            </select>
          </label>
        </div>
      ) : null}

      {selectedViewAsUser ? (
        <div className="admin-view-as-banner">
          Viewing as <strong>{selectedViewAsUser.displayName}</strong> ({selectedViewAsUser.email}). Actions are restricted to preview behavior.
          <button type="button" onClick={() => setSelectedViewAsUserId('')}>Exit preview</button>
        </div>
      ) : null}

      <div className="project-workspace-header">
        <div>
          <p className="eyebrow">019M-U</p>
          <h2>Project Workspace & Engineering Documents</h2>
          <p className="muted">
            Project-centered view of assignments, engineering-visible documents, resource requests, and role-scoped workspace access.
          </p>
          {access ? (
            <p className="workspace-access-line">
              Current scope: <strong>{access.scope}</strong>
              {access.isViewAs ? <> · Effective user: <strong>{access.email}</strong></> : null}
            </p>
          ) : null}
        </div>
        <StatusBadge tone="safe">Role scoped</StatusBadge>
      </div>

      {overview.error && <div className="workspace-error">{overview.error}</div>}

      <div className="workspace-summary-grid">
        <article>
          <span>Projects</span>
          <strong>{overview.loading ? '...' : overview.data?.summary?.projectCount ?? 0}</strong>
          <small>Visible in current scope</small>
        </article>
        <article>
          <span>Documents</span>
          <strong>{overview.loading ? '...' : overview.data?.summary?.documentCount ?? 0}</strong>
          <small>{overview.data?.summary?.engineeringVisibleDocumentCount ?? 0} engineering-visible</small>
        </article>
        <article>
          <span>Timesheet context</span>
          <strong>{overview.loading ? '...' : overview.data?.summary?.aiContextReadyDocumentCount ?? 0}</strong>
          <small>SOW/GSD ready for future assistant</small>
        </article>
        <article>
          <span>Assignments</span>
          <strong>{overview.loading ? '...' : overview.data?.summary?.assignmentCount ?? 0}</strong>
          <small>Visible engineer assignments</small>
        </article>
      </div>

      <article className="workspace-panel">
        <h3>Project Workspace Readiness</h3>
        <div className="workspace-card-grid">
          {projects.map((project) => (
            <div className="workspace-project-card" key={project.id}>
              <strong>{project.projectCode}</strong>
              <h4>{project.projectName}</h4>
              <p>{project.clientName}</p>
              <dl>
                <div><dt>PM</dt><dd>{fmt(project.projectManagerName)}</dd></div>
                <div><dt>Tasks</dt><dd>{project.taskCount}</dd></div>
                <div><dt>Assignments</dt><dd>{project.assignmentCount}</dd></div>
                <div><dt>Documents</dt><dd>{project.documentCount}</dd></div>
              </dl>
              <StatusBadge tone={project.status === 'active' ? 'safe' : 'neutral'}>{project.status}</StatusBadge>
            </div>
          ))}
        </div>
      </article>

      <article className="workspace-panel">
        <div className="workspace-panel-header">
          <div>
            <h3>Engineering Documents</h3>
            <p className="muted">SOW, GSD, and supporting documents uploaded from intake remain available based on role scope.</p>
          </div>
          <div className="workspace-filter-row">
            <button type="button" className={documentFilter === 'engineering' ? 'active' : ''} onClick={() => setDocumentFilter('engineering')}>Engineering visible</button>
            <button type="button" className={documentFilter === 'ai' ? 'active' : ''} onClick={() => setDocumentFilter('ai')}>Timesheet context</button>
            <button type="button" className={documentFilter === 'all' ? 'active' : ''} onClick={() => setDocumentFilter('all')}>All</button>
          </div>
        </div>

        <div className="workspace-doc-grid">
          {filteredDocuments.length === 0 ? (
            <div className="workspace-empty">No documents match this filter in the current user scope.</div>
          ) : filteredDocuments.map((document) => (
            <div className="workspace-document-card" key={document.id}>
              <div>
                <strong>{document.originalFileName}</strong>
                <StatusBadge tone={document.documentCategory === 'sow' || document.documentCategory === 'gsd' ? 'safe' : 'neutral'}>
                  {document.documentCategory}
                </StatusBadge>
              </div>
              <p>{document.projectOrIntakeName}</p>
              <small>{document.projectCode} · {document.requestNumber || 'No intake number'} · {Math.round((document.sizeBytes || 0) / 1024)} KB</small>
              <div className="workspace-document-flags">
                <span>{document.engineeringVisible ? 'Engineering visible' : 'Not engineering visible'}</span>
                <span>{document.aiTimesheetContextEnabled ? 'Timesheet assistant context ready' : 'Not used for timesheet context'}</span>
                <span>Extraction: {document.extractionStatus}</span>
              </div>
              <a
                className="workspace-download-link"
                href={selectedViewAsUserId ? `${document.downloadUrl}?viewAsUserId=${selectedViewAsUserId}` : document.downloadUrl}
                target="_blank"
                rel="noreferrer"
              >
                Download document
              </a>
            </div>
          ))}
        </div>
      </article>

      <article className="workspace-panel">
        <h3>Engineering Assignments</h3>
        <div className="workspace-table-wrap">
          <table className="workspace-table">
            <thead>
              <tr>
                <th>Project</th>
                <th>Task</th>
                <th>Engineer</th>
                <th>Dates</th>
                <th>Assigned</th><th>Used</th><th>Remaining</th><th>Allocation</th>
              </tr>
            </thead>
            <tbody>
              {assignments.map((assignment) => (
                <tr key={assignment.id}>
                  <td><strong>{assignment.projectCode}</strong><span>{assignment.projectName}</span></td>
                  <td>{assignment.taskCode}<span>{assignment.taskName}</span></td>
                  <td>{assignment.engineerName}<span>{assignment.engineerEmail}</span></td>
                  <td>{assignment.effectiveStartDate} → {fmt(assignment.effectiveEndDate)}</td>
                  <td>{Number(assignment.assignedHours ?? 0).toFixed(2)} hrs</td>
                  <td className={assignment.isOverAllocated ? "workspace-hours-overrun" : ""}>{Number(assignment.usedHours ?? 0).toFixed(2)} hrs</td>
                  <td>{Number(assignment.remainingHours ?? 0).toFixed(2)} hrs</td>
                  <td>{assignment.allocationPercent ?? 0}%</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>

      <article className="workspace-panel">
        <h3>Resource Request Assignment Status</h3>
        <div className="workspace-card-grid">
          {resourceRequests.map((request) => (
            <div className="workspace-resource-card" key={request.requestNumber}>
              <div>
                <strong>{request.requestNumber}</strong>
                <StatusBadge tone={request.status === 'assigned' ? 'safe' : 'attention'}>{request.status}</StatusBadge>
              </div>
              <h4>{request.requestedFunction}</h4>
              <p>{request.sourceName}</p>
              <small>{request.projectCode} · {request.requestedHours} hrs · {request.priority}</small>
              <p>Assigned engineers: {request.assignedEngineers || 'None yet'} ({request.assignedEngineerCount}/15)</p>
            </div>
          ))}
        </div>
      </article>
    </section>
  );
}
