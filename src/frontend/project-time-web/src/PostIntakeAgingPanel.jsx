import { useEffect, useMemo, useState } from 'react';
import './post-intake-aging-panel.css';

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

async function putJson(path, payload) {
  const response = await fetch(path, {
    method: 'PUT',
    headers: {
      ...getProjectPulseAuthHeaders(),
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

function formatDate(value) {
  if (!value) return 'Not recorded';
  return new Date(`${value}T00:00:00`).toLocaleDateString();
}

function formatDateTime(value) {
  if (!value) return 'Not recorded';
  return new Date(value).toLocaleString();
}

function formatStage(stage) {
  return String(stage ?? 'unknown').replaceAll('_', ' ').replace(/\b\w/g, (match) => match.toUpperCase());
}

function stageTone(stage) {
  if (stage === 'escalation_21_day') return 'critical';
  if (stage === 'reminder_14_day') return 'warning';
  if (stage === 'reminder_7_day') return 'attention';
  if (stage === 'missing_signed_date') return 'missing';
  return 'safe';
}

export default function PostIntakeAgingPanel() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });
  const [selectedIntakeId, setSelectedIntakeId] = useState('');
  const [editDraft, setEditDraft] = useState({
    projectSignedDate: '',
    intakeStatus: '',
    priority: '',
    requestTitle: '',
    requestDescription: '',
    updateNote: ''
  });
  const [uploadDraft, setUploadDraft] = useState({
    documentType: 'supporting_document',
    replaceExisting: false,
    engineeringVisible: true,
    aiTimesheetContextEnabled: false,
    file: null
  });
  const [actionStatus, setActionStatus] = useState('');

  async function loadAging() {
    setPayload((current) => ({ ...current, loading: true, error: null }));

    try {
      const result = await fetchJson('/api/project-intake/aging-summary');
      setPayload({ loading: false, data: result, error: null });

      if (!selectedIntakeId && result.items?.length) {
        setSelectedIntakeId(result.items[0].intakeId);
      }
    } catch (error) {
      setPayload({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load post-intake aging.'
      });
    }
  }

  useEffect(() => {
    loadAging();
  }, []);

  const items = payload.data?.items ?? [];
  const summary = payload.data?.summary ?? {};
  const canManage = Boolean(payload.data?.canManage);
  const selectedItem = useMemo(
    () => items.find((item) => item.intakeId === selectedIntakeId) ?? items[0],
    [items, selectedIntakeId]
  );

  useEffect(() => {
    if (!selectedItem) return;

    setSelectedIntakeId(selectedItem.intakeId);
    setEditDraft({
      projectSignedDate: selectedItem.projectSignedDate ?? '',
      intakeStatus: selectedItem.intakeStatus ?? '',
      priority: selectedItem.priority ?? '',
      requestTitle: selectedItem.requestTitle ?? '',
      requestDescription: '',
      updateNote: ''
    });
  }, [selectedItem?.intakeId]);

  async function savePostIntake() {
    if (!selectedItem || !canManage) return;

    setActionStatus('Saving post-intake update...');

    try {
      const result = await putJson(`/api/project-intake/${selectedItem.intakeId}/post-intake`, editDraft);
      setActionStatus(result.message ?? 'Post-intake update saved.');
      await loadAging();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to save post-intake update.');
    }
  }

  async function uploadSupportingDocument() {
    if (!selectedItem || !canManage) return;

    if (!uploadDraft.file) {
      setActionStatus('Select a supporting document before uploading.');
      return;
    }

    setActionStatus('Uploading supporting document...');

    const formData = new FormData();
    formData.append('documentType', uploadDraft.documentType);
    formData.append('replaceExisting', uploadDraft.replaceExisting ? 'true' : 'false');
    formData.append('engineeringVisible', uploadDraft.engineeringVisible ? 'true' : 'false');
    formData.append('aiTimesheetContextEnabled', uploadDraft.aiTimesheetContextEnabled ? 'true' : 'false');
    formData.append('file', uploadDraft.file);

    try {
      const response = await fetch(`/api/project-intake/${selectedItem.intakeId}/supporting-documents/upload`, {
        method: 'POST',
        headers: getProjectPulseAuthHeaders(),
        body: formData
      });

      if (!response.ok) throw new Error(await readApiErrorMessage(response, 'supporting document upload'));

      const result = await response.json();
      setActionStatus(result.message ?? 'Supporting document uploaded.');
      setUploadDraft((current) => ({ ...current, file: null }));
      await loadAging();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to upload supporting document.');
    }
  }

  return (
    <section className="post-intake-aging-panel">
      <div className="post-intake-header">
        <div>
          <p className="eyebrow">019M-AN</p>
          <h2>Post-Intake Editability & Signed Date Aging</h2>
          <p className="muted">
            Track signed date aging, post-intake edits, supporting document updates, and readiness for reminder/escalation notifications.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={loadAging}>Refresh aging</button>
      </div>

      {payload.error ? <div className="post-intake-banner error">{payload.error}</div> : null}
      {actionStatus ? <div className="post-intake-banner">{actionStatus}</div> : null}

      <div className="post-intake-aging-summary">
        <article><span>Total intakes</span><strong>{payload.loading ? '...' : summary.totalIntakes ?? 0}</strong><small>Visible intake requests</small></article>
        <article><span>Missing signed date</span><strong>{payload.loading ? '...' : summary.missingSignedDate ?? 0}</strong><small>Needs signed date</small></article>
        <article><span>7-day reminders</span><strong>{payload.loading ? '...' : summary.reminder7Day ?? 0}</strong><small>No triage movement</small></article>
        <article><span>14-day reminders</span><strong>{payload.loading ? '...' : summary.reminder14Day ?? 0}</strong><small>No resource/PM movement</small></article>
        <article><span>21-day escalations</span><strong>{payload.loading ? '...' : summary.escalation21Day ?? 0}</strong><small>Escalation ready</small></article>
      </div>

      <div className="post-intake-aging-layout">
        <article className="post-intake-card-list">
          <h3>Intake Aging Queue</h3>
          <p className="muted">Select an intake to review aging, signed date, document status, and post-intake changes.</p>

          {items.map((item) => (
            <button
              type="button"
              className={`post-intake-card ${item.intakeId === selectedItem?.intakeId ? 'selected' : ''}`}
              key={item.intakeId}
              onClick={() => setSelectedIntakeId(item.intakeId)}
            >
              <span className={`aging-stage-pill ${stageTone(item.agingStage)}`}>{formatStage(item.agingStage)}</span>
              <strong>{item.requestNumber} · {item.requestTitle}</strong>
              <small>{item.clientName} · Status: {formatStage(item.intakeStatus)} · Priority: {formatStage(item.priority)}</small>
              <small>Signed: {formatDate(item.projectSignedDate)} · Age: {item.signedAgeDays ?? 0} days</small>
              <small>Docs: {item.activeDocumentCount ?? 0} active · Changes: {item.changeCount ?? 0}</small>
            </button>
          ))}

          {!payload.loading && items.length === 0 ? <p className="muted">No intake records found.</p> : null}
        </article>

        <article className="post-intake-detail-panel">
          {selectedItem ? (
            <>
              <div className="post-intake-detail-heading">
                <div>
                  <h3>{selectedItem.requestNumber}</h3>
                  <p className="muted">{selectedItem.requestTitle}</p>
                </div>
                <span className={`aging-stage-pill ${stageTone(selectedItem.agingStage)}`}>{formatStage(selectedItem.agingStage)}</span>
              </div>

              <div className="post-intake-detail-grid">
                <span>Signed date<strong>{formatDate(selectedItem.projectSignedDate)}</strong></span>
                <span>Signed age<strong>{selectedItem.signedAgeDays ?? 0} days</strong></span>
                <span>Triage started<strong>{formatDateTime(selectedItem.triageStartedAt)}</strong></span>
                <span>Resource requests<strong>{selectedItem.resourceRequestCount ?? 0}</strong></span>
                <span>Assigned PM<strong>{selectedItem.assignedPmName || 'Not assigned'}</strong></span>
                <span>Last change<strong>{formatDateTime(selectedItem.lastChangeAt)}</strong></span>
              </div>

              <div className="post-intake-aging-message">
                <strong>Notification readiness</strong>
                <p>{selectedItem.agingMessage}</p>
              </div>

              {canManage ? (
                <div className="post-intake-management-grid">
                  <div className="post-intake-edit-form">
                    <h4>Edit post-intake fields</h4>
                    <label>Project Signed Date
                      <input type="date" value={editDraft.projectSignedDate} onChange={(event) => setEditDraft((current) => ({ ...current, projectSignedDate: event.target.value }))} />
                    </label>
                    <label>Status
                      <select value={editDraft.intakeStatus} onChange={(event) => setEditDraft((current) => ({ ...current, intakeStatus: event.target.value }))}>
                        <option value="new">New</option>
                        <option value="intake">Intake</option>
                        <option value="triage">Triage</option>
                        <option value="requested">Requested</option>
                        <option value="resource_requested">Resource Requested</option>
                        <option value="assigned">Assigned</option>
                        <option value="active">Active</option>
                      </select>
                    </label>
                    <label>Priority
                      <select value={editDraft.priority} onChange={(event) => setEditDraft((current) => ({ ...current, priority: event.target.value }))}>
                        <option value="low">Low</option>
                        <option value="normal">Normal</option>
                        <option value="high">High</option>
                        <option value="urgent">Urgent</option>
                      </select>
                    </label>
                    <label>Title
                      <input value={editDraft.requestTitle} onChange={(event) => setEditDraft((current) => ({ ...current, requestTitle: event.target.value }))} />
                    </label>
                    <label>Update note
                      <textarea rows={3} value={editDraft.updateNote} onChange={(event) => setEditDraft((current) => ({ ...current, updateNote: event.target.value }))} placeholder="Explain what changed after intake..." />
                    </label>
                    <button type="button" className="primary-action" onClick={savePostIntake}>Save post-intake update</button>
                  </div>

                  <div className="post-intake-edit-form">
                    <h4>Upload or replace supporting document</h4>
                    <label>Document Type
                      <select value={uploadDraft.documentType} onChange={(event) => setUploadDraft((current) => ({ ...current, documentType: event.target.value }))}>
                        <option value="supporting_document">Supporting Document</option>
                        <option value="sow">SOW</option>
                        <option value="gsd">GSD</option>
                        <option value="quote">Quote</option>
                        <option value="proposal">Proposal</option>
                        <option value="order_form">Order Form</option>
                      </select>
                    </label>
                    <label>File
                      <input type="file" onChange={(event) => setUploadDraft((current) => ({ ...current, file: event.target.files?.[0] ?? null }))} />
                    </label>
                    <label className="post-intake-check">
                      <input type="checkbox" checked={uploadDraft.replaceExisting} onChange={(event) => setUploadDraft((current) => ({ ...current, replaceExisting: event.target.checked }))} />
                      Replace active document of same type
                    </label>
                    <label className="post-intake-check">
                      <input type="checkbox" checked={uploadDraft.engineeringVisible} onChange={(event) => setUploadDraft((current) => ({ ...current, engineeringVisible: event.target.checked }))} />
                      Engineering visible
                    </label>
                    <label className="post-intake-check">
                      <input type="checkbox" checked={uploadDraft.aiTimesheetContextEnabled} onChange={(event) => setUploadDraft((current) => ({ ...current, aiTimesheetContextEnabled: event.target.checked }))} />
                      Timesheet assistant context
                    </label>
                    <button type="button" className="primary-action" onClick={uploadSupportingDocument}>Upload document</button>
                  </div>
                </div>
              ) : (
                <p className="muted">You can view intake aging, but post-intake edits are restricted to Project Coordinators, PTC, and Administrators.</p>
              )}
            </>
          ) : (
            <p className="muted">Select an intake request to review signed-date aging.</p>
          )}
        </article>
      </div>
    </section>
  );
}
