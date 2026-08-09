import { useEffect, useMemo, useState } from 'react';
import './project-expense-upload.css';

function authHeaders(extra = {}) {
  const headers = { ...extra };
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    const token = session?.sessionToken || session?.token || session?.accessToken;
    if (token) {
      headers['X-ProjectPulse-Session'] = token;
      headers['X-Project-Pulse-Session'] = token;
      headers['X-Session-Token'] = token;
      headers.Authorization = `Bearer ${token}`;
    }
    const viewAs = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    if (viewAs?.userId) headers['X-ProjectPulse-View-As-User'] = viewAs.userId;
  } catch {
    // Global session bridge remains the fallback.
  }
  return headers;
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    credentials: 'same-origin',
    ...options,
    headers: authHeaders({ Accept: 'application/json', ...(options.headers || {}) })
  });
  const raw = await response.text();
  let body = null;
  try { body = raw ? JSON.parse(raw) : null; } catch { body = null; }
  if (!response.ok) throw new Error(body?.message || body?.detail || raw || `HTTP ${response.status}`);
  return body;
}

function money(value, currency = 'USD') {
  const amount = Number(value || 0);
  return amount.toLocaleString(undefined, { style: 'currency', currency: currency || 'USD' });
}

function dateTime(value) {
  if (!value) return 'Not available';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

function period(upload) {
  if (!upload?.periodStart && !upload?.periodEnd) return 'Period not supplied';
  return `${upload.periodStart || 'Open'} – ${upload.periodEnd || 'Open'}`;
}

function formatLabel(value) {
  return String(value || '').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

export default function ProjectAllocationInfoPanel() {
  const [context, setContext] = useState(null);
  const [uploads, setUploads] = useState([]);
  const [loading, setLoading] = useState(true);
  const [status, setStatus] = useState('Ready');
  const [error, setError] = useState('');
  const [customer, setCustomer] = useState('');
  const [projectId, setProjectId] = useState('');
  const [ownerId, setOwnerId] = useState('');
  const [method, setMethod] = useState('excel_csv');
  const [file, setFile] = useState(null);
  const [replaceUploadId, setReplaceUploadId] = useState('');
  const [certifyReportId, setCertifyReportId] = useState('');
  const [periodStart, setPeriodStart] = useState('');
  const [periodEnd, setPeriodEnd] = useState('');

  async function load() {
    setLoading(true);
    setError('');
    try {
      const result = await api('/api/project-expenses/context');
      setContext(result);
      const firstCustomer = customer || result.customers?.[0] || '';
      setCustomer(firstCustomer);
      const firstProject = result.projects?.find((project) => project.customerName === firstCustomer) || result.projects?.[0];
      const nextProjectId = projectId || firstProject?.projectId || '';
      setProjectId(nextProjectId);
      const selectedProject = result.projects?.find((project) => project.projectId === nextProjectId) || firstProject;
      setOwnerId((current) => current || selectedProject?.eligibleOwners?.[0]?.userId || '');
      const history = await api('/api/project-expenses/uploads');
      const baseUploads = history.uploads || [];
      try {
        const lifecycle = await api('/api/project-expenses/uploads/lifecycle');
        const lifecycleById = new Map((lifecycle.uploads || []).map((item) => [String(item.uploadId), item]));
        setUploads(baseUploads.map((upload) => ({ ...upload, ...(lifecycleById.get(String(upload.uploadId)) || {}) })));
        setStatus(`Upload history loaded — ${baseUploads.length} version(s).`);
      } catch (lifecycleFailure) {
        setUploads(baseUploads);
        setStatus(`Upload history loaded. Lifecycle controls are temporarily unavailable: ${lifecycleFailure instanceof Error ? lifecycleFailure.message : 'refresh to retry'}`);
      }
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Unable to load Project Expense Upload.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void load(); }, []);

  const customerProjects = useMemo(
    () => (context?.projects || []).filter((project) => !customer || project.customerName === customer),
    [context, customer]
  );
  const selectedProject = useMemo(
    () => (context?.projects || []).find((project) => project.projectId === projectId) || null,
    [context, projectId]
  );
  const selectedOwner = selectedProject?.eligibleOwners?.find((owner) => owner.userId === ownerId) || null;
  const selectedUploads = useMemo(
    () => uploads.filter((upload) => (!projectId || upload.projectId === projectId) && (!ownerId || upload.expenseOwnerUserId === ownerId)),
    [uploads, projectId, ownerId]
  );
  const activeUploads = selectedUploads.filter((upload) => !upload.deletedAt && upload.isDeleted !== true);
  const deletedUploads = selectedUploads.filter((upload) => Boolean(upload.deletedAt) || upload.isDeleted === true);
  const currentUploads = activeUploads.filter((upload) => upload.isCurrent);
  const trackedTotal = currentUploads.reduce((sum, upload) => sum + Number(upload.totalAmount || 0), 0);
  const invoiceEligible = currentUploads
    .filter((upload) => upload.billingTreatment === 'pass_through_invoice')
    .reduce((sum, upload) => sum + Number(upload.reimbursableAmount || 0), 0);

  function selectCustomer(value) {
    setCustomer(value);
    const first = (context?.projects || []).find((project) => project.customerName === value);
    setProjectId(first?.projectId || '');
    setOwnerId(first?.eligibleOwners?.[0]?.userId || '');
  }

  function selectProject(value) {
    setProjectId(value);
    const project = (context?.projects || []).find((item) => item.projectId === value);
    setCustomer(project?.customerName || customer);
    setOwnerId(project?.eligibleOwners?.[0]?.userId || '');
  }

  async function uploadFile() {
    if (!projectId || !ownerId || !file) {
      setStatus('Select customer, project, expense owner, and file first.');
      return;
    }
    setStatus(`Uploading ${file.name}…`);
    setError('');
    const form = new FormData();
    form.append('projectId', projectId);
    form.append('expenseOwnerUserId', ownerId);
    form.append('file', file);
    if (replaceUploadId) form.append('replaceUploadId', replaceUploadId);
    try {
      const result = await api('/api/project-expenses/upload', { method: 'POST', body: form });
      setStatus(`${result.message} Notification: ${result.notification?.message || result.notification?.status || 'processed'}`);
      setFile(null);
      setReplaceUploadId('');
      await load();
    } catch (failure) {
      setStatus('Upload failed.');
      setError(failure instanceof Error ? failure.message : 'Unable to upload expense file.');
    }
  }

  async function importCertify() {
    if (!projectId || !ownerId || !certifyReportId.trim()) {
      setStatus('Select customer, project, expense owner, and enter the Certify report ID.');
      return;
    }
    setStatus('Importing approved expenses from Certify…');
    setError('');
    try {
      const result = await api('/api/project-expenses/import/certify', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          projectId,
          expenseOwnerUserId: ownerId,
          certifyReportId: certifyReportId.trim(),
          periodStart: periodStart || null,
          periodEnd: periodEnd || null
        })
      });
      setStatus(result.message || 'Certify import completed.');
      setCertifyReportId('');
      await load();
    } catch (failure) {
      setStatus('Certify import failed.');
      setError(failure instanceof Error ? failure.message : 'Unable to import from Certify.');
    }
  }

  function prepareReplacement(upload) {
    setCustomer(upload.customerName || customer);
    setProjectId(upload.projectId || projectId);
    setOwnerId(upload.expenseOwnerUserId || ownerId);
    setMethod('excel_csv');
    setReplaceUploadId(String(upload.uploadId));
    setStatus(`Replacement ready for version ${upload.versionNumber}. Choose a file with the same reporting period.`);
    window.setTimeout(() => {
      document.querySelector('.expense-selection-card input[type="file"]')?.focus();
      document.querySelector('.expense-selection-card')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }, 50);
  }

  async function acceptUpload(upload) {
    if (!window.confirm(`Accept version ${upload.versionNumber}? Delete and replacement will be permanently disabled for this version.`)) return;
    setStatus('Recording assigned Project Manager acceptance…');
    setError('');
    try {
      const result = await api(`/api/project-expenses/uploads/${upload.uploadId}/accept`, { method: 'POST' });
      setStatus(result.message || 'Expense version accepted.');
      await load();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Unable to accept the expense version.');
    }
  }

  async function deleteUpload(upload) {
    const reason = window.prompt(`Why are you deleting version ${upload.versionNumber} for ${upload.expenseOwnerName}?`);
    if (!reason?.trim()) return;
    setStatus('Deleting upload…');
    try {
      const result = await api(`/api/project-expenses/uploads/${upload.uploadId}`, {
        method: 'DELETE',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reason: reason.trim() })
      });
      setStatus(result.message || 'Upload deleted.');
      await load();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Unable to delete upload.');
    }
  }

  async function retryNotification(upload) {
    setStatus('Retrying global-mail delivery…');
    try {
      const result = await api(`/api/project-expenses/uploads/${upload.uploadId}/notification/retry`, { method: 'POST' });
      setStatus(result.notification?.message || 'Notification processed.');
      await load();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Unable to retry notification.');
    }
  }

  return (
    <div className="expense-upload-shell">
      <header className="expense-hero">
        <div>
          <p className="eyebrow">MODULE 005</p>
          <h1>Project Expense Upload</h1>
          <p>Select the customer, project, and expense owner before importing from Certify or uploading an Excel/CSV export.</p>
        </div>
        <button type="button" className="secondary-action" onClick={() => void load()}>Refresh</button>
      </header>

      {error ? <div className="expense-notice error"><strong>Action could not be completed</strong><span>{error}</span></div> : null}
      <div className="expense-status">{loading ? 'Loading project expense access…' : status}</div>

      <section className="expense-selection-card">
        <div className="expense-step"><span>1</span><strong>Select customer</strong></div>
        <div className="expense-step"><span>2</span><strong>Select project</strong></div>
        <div className="expense-step"><span>3</span><strong>Select expense owner</strong></div>
        <div className="expense-step"><span>4</span><strong>Upload or import</strong></div>

        <div className="expense-select-grid">
          <label>Customer
            <select value={customer} onChange={(event) => selectCustomer(event.target.value)}>
              <option value="">Select customer</option>
              {(context?.customers || []).map((value) => <option key={value} value={value}>{value}</option>)}
            </select>
          </label>
          <label>Project
            <select value={projectId} onChange={(event) => selectProject(event.target.value)}>
              <option value="">Select project</option>
              {customerProjects.map((project) => <option key={project.projectId} value={project.projectId}>{project.projectCode} — {project.projectName}</option>)}
            </select>
          </label>
          <label>Expense owner
            <select value={ownerId} onChange={(event) => setOwnerId(event.target.value)}>
              <option value="">Select person</option>
              {(selectedProject?.eligibleOwners || []).map((owner) => (
                <option key={owner.userId} value={owner.userId}>{owner.displayName} — {(owner.roleCodes || []).map(formatLabel).join(', ')}</option>
              ))}
            </select>
          </label>
        </div>

        {selectedProject ? (
          <div className={`billing-rule ${selectedProject.billingTreatment}`}>
            <strong>{selectedProject.contractType || 'Contract type not configured'}</strong>
            <span>{selectedProject.billingTreatment === 'pass_through_invoice'
              ? 'Reimbursable expenses are customer pass-through costs and can be included in Module 042 invoices.'
              : selectedProject.billingTreatment === 'included_fixed_price'
                ? 'Expenses are tracked as project cost already included in the fixed price and are not separately billed.'
                : 'Expenses are retained for internal project-cost tracking.'}</span>
          </div>
        ) : null}

        <div className="expense-method-tabs">
          <button type="button" className={method === 'excel_csv' ? 'active' : ''} onClick={() => setMethod('excel_csv')}>Upload CSV / Excel</button>
          <button type="button" className={method === 'certify' ? 'active' : ''} onClick={() => setMethod('certify')}>Import from Certify</button>
        </div>

        {method === 'excel_csv' ? (
          <div className="expense-import-panel">
            <div>
              <h2>Upload an expense export</h2>
              <p>Accepted: Expenses by GL Dimension or Expenses by Category in .xlsx, .xlsm, or .csv format. Both formats are normalized to the same expense categories and totals.</p>
              <input type="file" accept=".xlsx,.xlsm,.csv" onChange={(event) => setFile(event.target.files?.[0] || null)} />
              {file ? <small>Selected: {file.name}</small> : null}
            </div>
            <button type="button" className="primary-action" disabled={!file || !projectId || !ownerId || context?.actor?.isViewAs} onClick={() => void uploadFile()}>{replaceUploadId ? 'Upload replacement' : 'Upload expenses'}</button>
          </div>
        ) : (
          <div className="expense-import-panel certify">
            <div>
              <h2>Import from Certify</h2>
              <p>{context?.certify?.status === 'connected'
                ? 'The Module 038 connection is active. Enter the approved Certify expense report ID.'
                : 'Complete and test the Module 038 Certify connection before importing.'}</p>
              <label>Certify expense report ID<input value={certifyReportId} onChange={(event) => setCertifyReportId(event.target.value)} placeholder="Certify report ID" /></label>
              <div className="expense-date-grid">
                <label>Period start<input type="date" value={periodStart} onChange={(event) => setPeriodStart(event.target.value)} /></label>
                <label>Period end<input type="date" value={periodEnd} onChange={(event) => setPeriodEnd(event.target.value)} /></label>
              </div>
            </div>
            <button type="button" className="primary-action" disabled={context?.certify?.status !== 'connected' || !certifyReportId.trim() || !projectId || !ownerId || context?.actor?.isViewAs} onClick={() => void importCertify()}>Import approved expenses</button>
          </div>
        )}

        {replaceUploadId ? (
          <div className="expense-notice warning">
            <strong>Replace / Re-upload selected</strong>
            <span>The new file creates a new version. The selected version is never revived or overwritten.</span>
            <button type="button" className="secondary-action" onClick={() => setReplaceUploadId('')}>Cancel replacement</button>
          </div>
        ) : null}

        <div className="expense-mail-note">
          <strong>Automatic notification</strong>
          <span>After a successful upload/import, Pulse emails {selectedOwner?.displayName || 'the expense owner'} a category summary and copies {selectedProject?.projectManagerName || 'the assigned Project Manager'} using Module 067 Global Mail Configuration.</span>
        </div>
      </section>

      <section className="expense-kpis">
        <div><span>Current uploads</span><strong>{currentUploads.length}</strong></div>
        <div><span>Tracked expenses</span><strong>{money(trackedTotal)}</strong></div>
        <div><span>Invoice eligible</span><strong>{money(invoiceEligible)}</strong></div>
        <div><span>Versions shown</span><strong>{selectedUploads.length}</strong></div>
      </section>

      <section className="expense-history-card">
        <header><div><p className="eyebrow">Upload history</p><h2>Current and prior versions</h2></div><span>{selectedProject?.projectCode || 'Select a project'}</span></header>
        <div className="expense-table-wrap">
          <table>
            <thead><tr><th>Version</th><th>Owner / uploaded by</th><th>Period</th><th>Source</th><th>Categories</th><th>Total</th><th>Billing</th><th>Uploaded</th><th>Notification</th><th>Actions</th></tr></thead>
            <tbody>
              {activeUploads.map((upload) => (
                <tr key={upload.uploadId} className={upload.isCurrent && !upload.deletedAt ? 'current' : 'prior'}>
                  <td><strong>v{upload.versionNumber}</strong><small>{upload.isCurrent && !upload.deletedAt ? 'Current' : upload.deletedAt ? 'Deleted' : 'Superseded'}</small></td>
                  <td><strong>{upload.expenseOwnerName}</strong><small>Uploaded by {upload.uploadedByName}</small></td>
                  <td>{period(upload)}</td>
                  <td><strong>{upload.sourceMode === 'certify' ? 'Certify API' : upload.originalFileName || 'File upload'}</strong><small>{formatLabel(upload.sourceFormat)}</small></td>
                  <td><div className="category-pills">{(upload.categoryTotals || []).slice(0, 5).map((category) => <span key={category.category}>{category.category}: {money(category.amount, upload.currency)}</span>)}</div></td>
                  <td><strong>{money(upload.totalAmount, upload.currency)}</strong><small>{upload.lineCount} line(s)</small></td>
                  <td><span className={`billing-chip ${upload.billingTreatment}`}>{formatLabel(upload.billingTreatment)}</span></td>
                  <td>{dateTime(upload.uploadedAt)}</td>
                  <td><strong>{formatLabel(upload.notificationStatus)}</strong><small>{upload.notificationDetail}</small></td>
                  <td><div className="expense-actions">
                    {upload.canDelete ? <button type="button" onClick={() => void deleteUpload(upload)}>Delete</button> : null}
                    {upload.canReplace ? <button type="button" onClick={() => prepareReplacement(upload)}>Replace / Re-upload</button> : null}
                    {upload.canAccept ? <button type="button" className="primary-action" onClick={() => void acceptUpload(upload)}>Accept as PM</button> : null}
                    {upload.lockReason ? <small className="expense-lock-reason">{upload.lockReason}</small> : null}
                    {!upload.isDeleted && ['failed', 'configuration_pending', 'queued'].includes(upload.notificationStatus) ? <button type="button" onClick={() => void retryNotification(upload)}>Retry email</button> : null}
                  </div></td>
                </tr>
              ))}
              {!activeUploads.length ? <tr><td colSpan="10"><div className="expense-empty">No expense uploads match the selected customer, project, and owner.</div></td></tr> : null}
            </tbody>
          </table>
        </div>
        {deletedUploads.length ? (
          <details className="expense-deleted-audit-history">
            <summary>Deleted audit history ({deletedUploads.length})</summary>
            <p>Deleted evidence is immutable, excluded from active totals and billing, and cannot be restored automatically.</p>
            <ul>
              {deletedUploads.map((upload) => (
                <li key={`deleted-${upload.uploadId}`}>
                  <strong>v{upload.versionNumber} · {upload.originalFileName || 'Expense upload'}</strong>
                  <span>{money(upload.totalAmount, upload.currency)} · deleted {dateTime(upload.deletedAt)}</span>
                  <small>{upload.deletionReason || 'Deletion reason retained in immutable audit evidence.'}</small>
                </li>
              ))}
            </ul>
          </details>
        ) : null}
      </section>
    </div>
  );
}
