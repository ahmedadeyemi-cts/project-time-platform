import { useEffect, useMemo, useState } from 'react';
import './approval-export-audit-workflow-center.css';

function formatWorkflowDate(value) {
  if (!value) return 'No date recorded';

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return String(value);
  }

  return parsed.toLocaleString();
}

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

async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...getProjectPulseAuthHeaders() },
    body: JSON.stringify(payload ?? {})
  });

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

function getSundayIso(date = new Date()) {
  const copy = new Date(date);
  const day = copy.getDay();
  copy.setDate(copy.getDate() - day);
  return copy.toISOString().slice(0, 10);
}

function addDaysIso(isoDate, days) {
  const date = new Date(`${isoDate}T00:00:00`);
  date.setDate(date.getDate() + days);
  return date.toISOString().slice(0, 10);
}

function formatNumber(value) {
  return Number(value ?? 0).toLocaleString(undefined, { maximumFractionDigits: 2 });
}

function formatDateTime(value) {
  if (!value) return 'Not recorded';
  return new Date(value).toLocaleString();
}

function labelStatus(status) {
  return String(status ?? 'unknown').replaceAll('_', ' ').replace(/\b\w/g, (match) => match.toUpperCase());
}

export default function ApprovalExportAuditWorkflowCenter() {
  const [weekStart, setWeekStart] = useState(getSundayIso());
  const [summary, setSummary] = useState({ loading: true, data: null, error: null });
  const [items, setItems] = useState({ loading: true, data: null, error: null });
  const [exportsData, setExportsData] = useState({ loading: true, data: null, error: null });
  const [operational, setOperational] = useState({ loading: true, data: null, error: null });
  const [statusMessage, setStatusMessage] = useState('');
  const [notes, setNotes] = useState({});

  const weekEnd = useMemo(() => addDaysIso(weekStart, 6), [weekStart]);
  const access = summary.data?.access ?? items.data?.access ?? {};

  async function loadWorkflow() {
    setSummary((current) => ({ ...current, loading: true, error: null }));
    setItems((current) => ({ ...current, loading: true, error: null }));
    setExportsData((current) => ({ ...current, loading: true, error: null }));
    setOperational((current) => ({ ...current, loading: true, error: null }));

    try {
      const [summaryResult, itemsResult, exportsResult, operationalResult] = await Promise.all([
        fetchJson('/api/workflow/approval-export-summary'),
        fetchJson(`/api/workflow/approval-items?weekStart=${weekStart}&weekEnd=${weekEnd}`),
        fetchJson('/api/time-exports'),
        fetchJson(`/api/workflow/operational-readiness?weekStart=${weekStart}&weekEnd=${weekEnd}`)
      ]);

      setSummary({ loading: false, data: summaryResult, error: null });
      setItems({ loading: false, data: itemsResult, error: null });
      setExportsData({ loading: false, data: exportsResult, error: null });
      setOperational({ loading: false, data: operationalResult, error: null });
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unable to load workflow.';
      setSummary((current) => ({ ...current, loading: false, error: message }));
      setItems((current) => ({ ...current, loading: false, error: message }));
      setExportsData((current) => ({ ...current, loading: false, error: message }));
      setOperational((current) => ({ ...current, loading: false, error: message }));
    }
  }

  useEffect(() => {
    void loadWorkflow();
  }, [weekStart]);

  const workflowItems = items.data?.items ?? [];
  const exportsList = exportsData.data?.exports ?? [];
  const operationalData = operational.data;
  const workflowStages = operationalData?.workflowStages ?? [];
  const statusBreakdown = operationalData?.statusBreakdown ?? [];
  const roleGuidance = operationalData?.roleGuidance ?? [];
  const auditEvidence = operationalData?.auditEvidence ?? [];
  const exportReadiness = operationalData?.exportReadiness ?? {};
  const counts = summary.data?.summary ?? {};

  function noteKey(item) {
    return `${item.timesheetId}-${item.workDate}`;
  }

  function getNote(item) {
    return notes[noteKey(item)] ?? '';
  }

  function setNote(item, value) {
    setNotes((current) => ({ ...current, [noteKey(item)]: value }));
  }

  async function runAction(item, action) {
    try {
      setStatusMessage(`Applying ${action} for ${item.employeeName}...`);

      const result = await postJson('/api/workflow/approval-items/action', {
        timesheetId: item.timesheetId,
        workDate: item.workDate,
        action,
        comment: getNote(item)
      });

      setStatusMessage(result.message ?? 'Workflow action applied.');
      setNote(item, '');
      await loadWorkflow();
    } catch (error) {
      setStatusMessage(error instanceof Error ? error.message : 'Workflow action failed.');
    }
  }

  async function createExport(exportFormat) {
    try {
      setStatusMessage(`Preparing ${exportFormat.toUpperCase()} export package record...`);

      const result = await postJson('/api/time-exports', {
        exportFormat,
        weekStart,
        weekEnd,
        notes: `Prepared from Approval / Export / Audit Workflow screen for ${weekStart} through ${weekEnd}.`
      });

      setStatusMessage(result.message ?? 'Export package record prepared.');
      await loadWorkflow();
    } catch (error) {
      setStatusMessage(error instanceof Error ? error.message : 'Export preparation failed.');
    }
  }

  async function downloadExportPackage(item) {
    try {
      if (!item?.exportId) {
        setStatusMessage('No export package selected.');
        return;
      }

      setStatusMessage(`Downloading ${item.fileName || 'export package'}...`);

      const response = await fetch(`/api/time-exports/${item.exportId}/download`, {
        headers: getProjectPulseAuthHeaders()
      });

      if (!response.ok) {
        const raw = await response.text();
        throw new Error(raw || `Download failed with HTTP ${response.status}`);
      }

      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      const fallbackName = `projectpulse-time-export-${weekStart}-${weekEnd}.csv`;

      link.href = url;
      link.download = String(item.fileName || fallbackName).replace(/\.xlsx$|\.pdf$/i, '.csv');
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(url);

      setStatusMessage('Export package downloaded and audit evidence recorded.');
      await loadWorkflow();
    } catch (error) {
      setStatusMessage(error instanceof Error ? error.message : 'Export package download failed.');
    }
  }


  return (
    <section className="approval-export-center">
      <div className="approval-export-header">
        <div>
          <p className="eyebrow">019M-AL</p>
          <h2>Approval / Export / Audit Workflow</h2>
          <p className="muted">
            Manage the post-manager approval workflow: PM validation, accounting readiness, reconciliation, lock, export preparation, and audit visibility.
          </p>
        </div>
        <div className="approval-export-actions">
          <label>
            Week start
            <input type="date" value={weekStart} onChange={(event) => setWeekStart(event.target.value)} />
          </label>
          <button type="button" className="secondary-action" onClick={loadWorkflow}>Refresh</button>
        </div>
      </div>

      {statusMessage ? <div className="approval-export-banner">{statusMessage}</div> : null}
      {summary.error ? <div className="approval-export-banner error">{summary.error}</div> : null}

      <div className="approval-export-summary-grid">
        <article>
          <span>Manager approved</span>
          <strong>{summary.loading ? '...' : formatNumber(counts.pendingProjectApprovals)}</strong>
          <small>Ready for PM validation</small>
        </article>
        <article>
          <span>Pending accounting</span>
          <strong>{summary.loading ? '...' : formatNumber(counts.pendingAccountingReview)}</strong>
          <small>Ready for accounting review</small>
        </article>
        <article>
          <span>Accounting ready</span>
          <strong>{summary.loading ? '...' : formatNumber(counts.accountingReady)}</strong>
          <small>Ready to reconcile</small>
        </article>
        <article>
          <span>Exports</span>
          <strong>{summary.loading ? '...' : formatNumber(counts.exportsLast30Days)}</strong>
          <small>Prepared in the last 30 days</small>
        </article>
      </div>

      <article className="approval-export-panel approval-export-operational-panel">
        <div className="approval-export-panel-heading">
          <div>
            <h3>Operational Readiness</h3>
            <p className="muted">Role-specific workflow status, export readiness, and audit evidence for the selected date range.</p>
          </div>
          <span>{operational.loading ? 'Loading' : `${workflowStages.length} stage(s)`}</span>
        </div>

        {operational.error ? <div className="approval-export-banner error">{operational.error}</div> : null}

        <div className="approval-operational-grid">
          {workflowStages.map((stage) => (
            <article key={stage.stage} className={`approval-operational-card status-${stage.stage}`}>
              <span>{stage.title}</span>
              <strong>{formatNumber(stage.entryCount)} item(s)</strong>
              <small>{formatNumber(stage.totalHours)} hour(s)</small>
              <p>{stage.guidance}</p>
              {stage.actionRequired ? <em>Action required</em> : <em>No action required</em>}
            </article>
          ))}
        </div>

        <div className="approval-export-readiness-callout">
          <div>
            <span>Export readiness</span>
            <strong>{formatNumber(exportReadiness.readyEntryCount)} ready / {formatNumber(exportReadiness.blockedEntryCount)} blocked</strong>
            <p>{exportReadiness.message || 'Export readiness will appear after workflow data loads.'}</p>
          </div>
        </div>

        <div className="approval-role-guidance-grid">
          {roleGuidance.map((item) => (
            <article key={item.role}>
              <strong>{item.role}</strong>
              <span>{item.access ? 'In scope' : 'No workflow controls'}</span>
              <p>{item.guidance}</p>
            </article>
          ))}
        </div>

        <div className="approval-export-table-wrap">
          <table className="approval-export-table">
            <thead>
              <tr>
                <th>Status</th>
                <th>Items</th>
                <th>Hours</th>
              </tr>
            </thead>
            <tbody>
              {statusBreakdown.map((item) => (
                <tr key={item.status}>
                  <td>{labelStatus(item.status)}</td>
                  <td>{formatNumber(item.entryCount)}</td>
                  <td>{formatNumber(item.totalHours)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>

      <article className="approval-export-panel approval-audit-evidence-panel">
        <div className="approval-export-panel-heading">
          <div>
            <h3>Audit Evidence</h3>
            <p className="muted">Recent workflow-related audit events supporting approvals, exports, reconciliation, and locks.</p>
          </div>
          <span>{auditEvidence.length} event(s)</span>
        </div>

        <div className="approval-audit-evidence-list">
          {auditEvidence.map((event) => (
            <article key={event.auditLogId}>
              <strong>{labelStatus(event.action)}</strong>
              <small>{event.entityType} · {event.actorName} · {formatWorkflowDate(event.createdAt)}</small>
              <p>{event.evidencePreview || 'Audit event recorded.'}</p>
            </article>
          ))}
        </div>

        {!operational.loading && auditEvidence.length === 0 ? <p className="muted">No workflow audit evidence was found.</p> : null}
      </article>

      <div className="approval-export-layout">
        <article className="approval-export-panel">
          <div className="approval-export-panel-heading">
            <div>
              <h3>Workflow Items</h3>
              <p className="muted">Role-scoped submitted, manager-approved, PM-approved, accounting-ready, reconciled, and locked days.</p>
            </div>
            <span>{workflowItems.length} item(s)</span>
          </div>

          <div className="approval-export-item-list">
            {workflowItems.map((item) => {
              const canPmApprove = Boolean(access.canProjectApprove) && item.status === 'manager_approved';
              const canAccountingReady = Boolean(access.canManageAccounting) && ['pm_approved', 'manager_approved'].includes(item.status);
              const canReconcile = Boolean(access.canManageAccounting) && ['accounting_ready', 'pm_approved'].includes(item.status);
              const canLock = Boolean(access.canManageAccounting) && ['reconciled', 'accounting_ready'].includes(item.status);

              return (
                <div className="approval-export-item" key={`${item.timesheetId}-${item.workDate}`}>
                  <div className="approval-export-item-main">
                    <div>
                      <span className={`workflow-status-badge status-${item.status}`}>{labelStatus(item.status)}</span>
                      <strong>{item.employeeName}</strong>
                      <small>{item.employeeEmail} • {item.workDate}</small>
                    </div>
                    <div className="approval-export-metrics">
                      <span>{formatNumber(item.totalHours)} hrs</span>
                      <span>{item.projectCodes}</span>
                    </div>
                  </div>

                  <p className="muted">{item.projectNames}</p>

                  <div className="approval-export-history">
                    <small>Submitted: {formatDateTime(item.submittedAt)}</small>
                    <small>Manager approved: {formatDateTime(item.managerApprovedAt)}</small>
                    <small>PM approved: {formatDateTime(item.pmApprovedAt)}</small>
                    <small>Accounting ready: {formatDateTime(item.accountingReadyAt)}</small>
                    <small>Reconciled: {formatDateTime(item.reconciledAt)}</small>
                    <small>Locked: {formatDateTime(item.lockedAt)}</small>
                  </div>

                  {(canPmApprove || canAccountingReady || canReconcile || canLock) ? (
                    <div className="approval-export-action-panel">
                      <textarea
                        value={getNote(item)}
                        onChange={(event) => setNote(item, event.target.value)}
                        placeholder="Optional workflow note"
                      />
                      <div className="approval-export-button-row">
                        {canPmApprove ? <button type="button" className="primary-action" onClick={() => runAction(item, 'pm_approve')}>PM approve</button> : null}
                        {canAccountingReady ? <button type="button" className="secondary-action" onClick={() => runAction(item, 'accounting_ready')}>Mark accounting-ready</button> : null}
                        {canReconcile ? <button type="button" className="secondary-action" onClick={() => runAction(item, 'reconcile')}>Reconcile</button> : null}
                        {canLock ? <button type="button" className="secondary-action" onClick={() => runAction(item, 'lock')}>Lock</button> : null}
                      </div>
                    </div>
                  ) : null}
                </div>
              );
            })}

            {!items.loading && workflowItems.length === 0 ? <p className="muted">No workflow items were found for this week and role scope.</p> : null}
          </div>
        </article>

        <article className="approval-export-panel">
          <div className="approval-export-panel-heading">
            <div>
              <h3>Export Readiness</h3>
              <p className="muted">Prepare Excel/CSV-ready export records for accounting review, then download the generated export package with audit evidence.</p>
            </div>
            <span>{exportsList.length} export(s)</span>
          </div>

          {access.canExport || access.canViewAll ? (
            <div className="approval-export-create-row">
              <button type="button" className="primary-action" onClick={() => createExport('excel')}>Prepare Excel/CSV export</button>
              <button type="button" className="secondary-action" onClick={() => createExport('pdf')}>Prepare PDF export</button>
            </div>
          ) : null}

          <div className="approval-export-table-wrap">
            <table className="approval-export-table">
              <thead>
                <tr>
                  <th>Created</th>
                  <th>Format</th>
                  <th>Week</th>
                  <th>Items</th>
                  <th>Hours</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {exportsList.map((item) => (
                  <tr key={item.exportId}>
                    <td>{formatDateTime(item.createdAt)}</td>
                    <td>{String(item.exportFormat ?? '').toUpperCase()}</td>
                    <td>{item.weekStart ?? 'Any'} → {item.weekEnd ?? 'Any'}</td>
                    <td>{formatNumber(item.itemCount)}</td>
                    <td>{formatNumber(item.totalHours)}</td>
                    <td>{labelStatus(item.exportStatus)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {(access.canExport || access.canViewAll) && exportsList.length > 0 ? (
            <div className="approval-export-package-list">
              {exportsList.map((item) => (
                <article key={`package-${item.exportId}`} className="approval-export-package-card">
                  <div>
                    <strong>{item.fileName || 'Export package'}</strong>
                    <small>
                      {String(item.exportFormat ?? '').toUpperCase()} · {labelStatus(item.exportStatus)} · {formatNumber(item.itemCount)} item(s) · {formatNumber(item.totalHours)} hour(s)
                    </small>
                    <p>
                      Download-ready: {item.downloadReady ? 'Yes' : 'No'} · Downloads: {formatNumber(item.packageDownloadCount)}
                    </p>
                  </div>
                  <button type="button" className="secondary-action" onClick={() => downloadExportPackage(item)}>
                    Download CSV package
                  </button>
                </article>
              ))}
            </div>
          ) : null}

          {!exportsData.loading && exportsList.length === 0 ? <p className="muted">No export records have been prepared yet.</p> : null}
        </article>
      </div>

      <article className="approval-export-panel compact">
        <h3>Dashboard Placement</h3>
        <p className="muted">
          This workflow is connected to the role dashboard cards through Project Approval, Account Reconciliation, Audit Trail, and Exports. Engineers do not receive workflow controls.
        </p>
      </article>
    </section>
  );
}
