import { useEffect, useMemo, useState } from 'react';
import './project-workspace-center.css';
import UnifiedProjectFinancialWorkspace from './UnifiedProjectFinancialWorkspace.jsx';

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
  const headers = session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};

  if (viewAsUserId) {
    headers['X-ProjectPulse-View-As-User'] = viewAsUserId;
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

function readDownloadFileName(response, fallbackName) {
  const disposition = response.headers.get('Content-Disposition') || '';
  const utf8Match = disposition.match(/filename\*=UTF-8''([^;]+)/i);

  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1].replace(/^["']|["']$/g, ''));
    } catch {
      // Continue to the ordinary filename or API-provided fallback.
    }
  }

  const filenameMatch = disposition.match(/filename="?([^";]+)"?/i);
  return filenameMatch?.[1]?.trim() || fallbackName || 'project-document';
}

async function readDownloadError(response) {
  const body = await response.text();

  if (!body.trim()) {
    return `Document download returned HTTP ${response.status}.`;
  }

  try {
    const result = JSON.parse(body);
    return result?.message || result?.status || `Document download returned HTTP ${response.status}.`;
  } catch {
    return body.slice(0, 240);
  }
}

function StatusBadge({ children, tone = 'neutral' }) {
  return <span className={`workspace-badge ${tone}`}>{children}</span>;
}

export default function ProjectWorkspaceCenter() {
  const [overview, setOverview] = useState({ loading: true, data: null, error: null });
  const [documentFilter, setDocumentFilter] = useState('engineering');
  const [documentSearch, setDocumentSearch] = useState('');
  const [projectFilter, setProjectFilter] = useState('all');
  const [viewAsUsers, setViewAsUsers] = useState([]);
  const [selectedViewAsUserId, setSelectedViewAsUserId] = useState('');
  const [documentDownload, setDocumentDownload] = useState({
    documentId: '',
    message: '',
    error: false
  });
  const [documentPreview, setDocumentPreview] = useState({ loading: false, item: null, url: '', error: '' });

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

  async function downloadDocument(workspaceDocument) {
    if (!workspaceDocument?.downloadUrl) {
      setDocumentDownload({
        documentId: workspaceDocument?.id || '',
        message: 'This project document does not have a download URL.',
        error: true
      });
      return;
    }

    setDocumentDownload({
      documentId: workspaceDocument.id,
      message: `Downloading ${workspaceDocument.originalFileName || 'project document'}...`,
      error: false
    });

    try {
      const response = await fetch(workspaceDocument.downloadUrl, {
        method: 'GET',
        headers: getProjectPulseAuthHeaders(selectedViewAsUserId)
      });

      if (!response.ok) {
        throw new Error(await readDownloadError(response));
      }

      const blob = await response.blob();
      const blobUrl = URL.createObjectURL(blob);
      const anchor = window.document.createElement('a');
      anchor.href = blobUrl;
      anchor.download = readDownloadFileName(response, workspaceDocument.originalFileName);
      window.document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      window.setTimeout(() => URL.revokeObjectURL(blobUrl), 60000);

      setDocumentDownload({
        documentId: workspaceDocument.id,
        message: `${workspaceDocument.originalFileName || 'Project document'} downloaded.`,
        error: false
      });
    } catch (error) {
      setDocumentDownload({
        documentId: workspaceDocument.id,
        message: error instanceof Error ? error.message : 'Unable to download this project document.',
        error: true
      });
    }
  }

  async function previewDocument(workspaceDocument) {
    setDocumentPreview({ loading: true, item: workspaceDocument, url: '', error: '' });
    try {
      const response = await fetch(workspaceDocument.downloadUrl, { headers: getProjectPulseAuthHeaders(selectedViewAsUserId) });
      if (!response.ok) throw new Error(await readDownloadError(response));
      const blob = await response.blob();
      const previewable = String(blob.type || workspaceDocument.contentType || '').match(/^(application\/pdf|image\/|text\/)/i);
      if (!previewable) throw new Error('This file type opens through a secure download. Use Download to view it in its native application.');
      setDocumentPreview({ loading: false, item: workspaceDocument, url: URL.createObjectURL(blob), error: '' });
    } catch (error) {
      setDocumentPreview({ loading: false, item: workspaceDocument, url: '', error: error instanceof Error ? error.message : 'Preview is unavailable.' });
    }
  }

  function closePreview() {
    if (documentPreview.url) URL.revokeObjectURL(documentPreview.url);
    setDocumentPreview({ loading: false, item: null, url: '', error: '' });
  }

  const projects = overview.data?.projects ?? [];
  const documents = overview.data?.documents ?? [];
  const assignments = overview.data?.assignments ?? [];
  const resourceRequests = overview.data?.resourceRequests ?? [];
  const access = overview.data?.access;
  const selectedViewAsUser = viewAsUsers.find((user) => user.userId === selectedViewAsUserId);

  const filteredDocuments = useMemo(() => documents.filter((document) => {
    if (documentFilter === 'ai' && !document.aiTimesheetContextEnabled) return false;
    if (documentFilter === 'engineering' && !document.engineeringVisible) return false;
    if (projectFilter !== 'all' && document.projectCode !== projectFilter) return false;
    const query = documentSearch.trim().toLowerCase();
    return !query || [document.originalFileName, document.projectOrIntakeName, document.projectCode, document.documentCategory, document.documentType]
      .some((value) => String(value ?? '').toLowerCase().includes(query));
  }), [documents, documentFilter, documentSearch, projectFilter]);

  return (
    <section className="project-workspace-center">
      {/* GROUP_3_UNIFIED_PROJECT_FINANCIAL_WORKSPACES_START */}
      <UnifiedProjectFinancialWorkspace workspace="engineering" />
      {/* GROUP_3_UNIFIED_PROJECT_FINANCIAL_WORKSPACES_END */}
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
          <p className="eyebrow">Module 019</p>
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
                {/* 053I_WORKSPACE_AE_SA_UI_START */}
                <div><dt>Account Executive</dt><dd>{fmt(project.accountExecutiveName || project.salesExecutiveName)}</dd></div>
                <div><dt>Solution Architect</dt><dd>{fmt(project.solutionArchitectName)}</dd></div>
                {/* 053I_WORKSPACE_AE_SA_UI_END */}
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
            <input type="search" value={documentSearch} onChange={(event) => setDocumentSearch(event.target.value)} placeholder="Search documents" aria-label="Search engineering documents" />
            <select value={projectFilter} onChange={(event) => setProjectFilter(event.target.value)} aria-label="Filter documents by project"><option value="all">All active projects</option>{[...new Set(documents.map((item) => item.projectCode).filter(Boolean))].sort().map((code) => <option value={code} key={code}>{code}</option>)}</select>
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
              <div className="workspace-document-actions">
                <button type="button" onClick={() => previewDocument(document)}>View</button>
                <button type="button" className="workspace-download-link" onClick={() => downloadDocument(document)} disabled={documentDownload.documentId === document.id && !documentDownload.error && documentDownload.message.startsWith('Downloading ')}>{documentDownload.documentId === document.id && !documentDownload.error && documentDownload.message.startsWith('Downloading ') ? 'Downloading...' : 'Download'}</button>
              </div>
              {documentDownload.documentId === document.id && documentDownload.message ? (
                <small className={`workspace-download-status ${documentDownload.error ? 'error' : ''}`}>
                  {documentDownload.message}
                </small>
              ) : null}
            </div>
          ))}
        </div>
      </article>

      {documentPreview.item ? <div className="workspace-preview-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) closePreview(); }}><section className="workspace-preview-dialog" role="dialog" aria-modal="true" aria-labelledby="workspace-preview-title"><header><div><p className="eyebrow">Secure document preview</p><h3 id="workspace-preview-title">{documentPreview.item.originalFileName}</h3><span>{documentPreview.item.projectCode} · {documentPreview.item.projectOrIntakeName}</span></div><button type="button" onClick={closePreview} aria-label="Close document preview">Close</button></header>{documentPreview.loading ? <div className="workspace-preview-state">Preparing authorized preview…</div> : null}{documentPreview.error ? <div className="workspace-preview-state error">{documentPreview.error}<button type="button" onClick={() => downloadDocument(documentPreview.item)}>Download file</button></div> : null}{documentPreview.url ? <iframe src={documentPreview.url} title={`Preview ${documentPreview.item.originalFileName}`} /> : null}</section></div> : null}

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
