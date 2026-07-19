import { useEffect, useMemo, useState } from 'react';
import './manager-approval.css';

function getProjectPulseAuthHeaders() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return {};
    const session = JSON.parse(raw);
    return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
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
  const response = await fetch(path, { headers: getProjectPulseAuthHeaders() });
  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...getProjectPulseAuthHeaders() },
    body: JSON.stringify(payload)
  });
  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

function toIsoDate(date) {
  return date.toISOString().slice(0, 10);
}

function getSundayForDate(date = new Date()) {
  const current = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
  current.setUTCDate(current.getUTCDate() - current.getUTCDay());
  return toIsoDate(current);
}

function shiftWeek(weekStart, days) {
  const date = new Date(`${weekStart}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return toIsoDate(date);
}

function formatNumber(value) {
  const number = Number(value ?? 0);
  return Number.isFinite(number) ? number.toFixed(2) : '0.00';
}

function formatDateTime(value) {
  if (!value) return 'Not available';
  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value);
  }
}

function getApprovalKey(item) {
  return `${item.timesheetId}|${item.workDate}`;
}

function getEntryLabel(entry) {
  const projectBits = [entry.projectCode, entry.projectName].filter(Boolean).join(' - ');
  const taskBits = [entry.taskCode, entry.taskName].filter(Boolean).join(' - ');
  const categoryBits = [entry.categoryCode, entry.categoryName].filter(Boolean).join(' - ');
  if (projectBits && taskBits) return `${projectBits} / ${taskBits}`;
  if (taskBits) return taskBits;
  if (projectBits) return projectBits;
  if (categoryBits) return categoryBits;
  return 'Time entry';
}

function getStatusLabel(status) {
  const labels = {
    submitted: 'Awaiting manager review',
    manager_approved: 'Manager approved',
    manager_declined: 'Returned to engineer',
    pm_approved: 'PM approved',
    accounting_ready: 'Accounting ready',
    reconciled: 'Reconciled',
    locked: 'Locked'
  };
  return labels[status] ?? status ?? 'Unknown';
}

export default function ManagerApprovalPanel({ mode = 'review' }) {
  const historyMode = mode === 'history';
  const [weekStart, setWeekStart] = useState(getSundayForDate());
  const [includeAll, setIncludeAll] = useState(historyMode);
  const [allDates, setAllDates] = useState(false);
  const [statusFilter, setStatusFilter] = useState(historyMode ? 'all' : 'submitted');
  const [searchText, setSearchText] = useState('');
  const [approvalData, setApprovalData] = useState({ loading: true, data: null, error: null });
  const [selectedKeys, setSelectedKeys] = useState(new Set());
  const [expandedKeys, setExpandedKeys] = useState(new Set());
  const [declineKey, setDeclineKey] = useState('');
  const [declineReasons, setDeclineReasons] = useState({});
  const [statusMessage, setStatusMessage] = useState('Ready.');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (historyMode) {
      setIncludeAll(true);
      setStatusFilter('all');
    }
  }, [historyMode]);

  async function loadApprovals() {
    setApprovalData((current) => ({ ...current, loading: true, error: null }));
    try {
      const params = new URLSearchParams({
        weekStart,
        includeAll: String(includeAll || historyMode),
        allDates: String(allDates && !historyMode),
        search: searchText.trim()
      });
      const result = await fetchJson(`/api/manager/approvals?${params.toString()}`);
      setApprovalData({ loading: false, data: result, error: null });
    } catch (error) {
      setApprovalData({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load manager approvals.'
      });
    }
  }

  useEffect(() => {
    void loadApprovals();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [weekStart, includeAll, historyMode, allDates]);

  const items = approvalData.data?.items ?? [];
  const access = approvalData.data?.access ?? {};
  const pendingItems = useMemo(() => items.filter((item) => item.status === 'submitted'), [items]);
  const visibleItems = useMemo(() => {
    const normalizedSearch = searchText.trim().toLowerCase();
    return items.filter((item) => {
      if (statusFilter !== 'all' && item.status !== statusFilter) return false;
      if (!normalizedSearch) return true;
      return [item.resourceName, item.resourceEmail, item.workDate, item.activitySummary, item.status]
        .filter(Boolean)
        .some((value) => String(value).toLowerCase().includes(normalizedSearch));
    });
  }, [items, searchText, statusFilter]);

  const selectedPendingItems = useMemo(
    () => pendingItems.filter((item) => selectedKeys.has(getApprovalKey(item))),
    [pendingItems, selectedKeys]
  );

  function toggleSelected(item) {
    if (item.status !== 'submitted' || historyMode) return;
    const key = getApprovalKey(item);
    setSelectedKeys((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  function toggleAllPending() {
    const pendingKeys = pendingItems.map(getApprovalKey);
    const allSelected = pendingKeys.length > 0 && pendingKeys.every((key) => selectedKeys.has(key));
    setSelectedKeys(allSelected ? new Set() : new Set(pendingKeys));
  }

  function toggleExpanded(item) {
    const key = getApprovalKey(item);
    setExpandedKeys((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  async function runApprovalAction(path, item, payload = {}) {
    setBusy(true);
    setStatusMessage('Processing approval action...');
    try {
      const result = await postJson(path, {
        timesheetId: item.timesheetId,
        workDate: item.workDate,
        ...payload
      });
      const emailStatus = result.emailNotificationStatus
        ? ` Engineer email: ${result.emailNotificationStatus}.`
        : '';
      setStatusMessage(`${result.message ?? 'Approval action completed.'}${emailStatus}`);
      setSelectedKeys(new Set());
      setDeclineKey('');
      window.dispatchEvent(
        new CustomEvent(
          'projectpulse:approval-queue-changed'
        )
      );
      await loadApprovals();
    } catch (error) {
      setStatusMessage(error instanceof Error ? error.message : 'Approval action failed.');
    } finally {
      setBusy(false);
    }
  }

  async function approveItem(item) {
    await runApprovalAction('/api/manager/approvals/approve', item, { comment: 'Approved by manager.' });
  }

  async function confirmDecline(item) {
    const key = getApprovalKey(item);
    const reason = (declineReasons[key] ?? '').trim();
    if (!reason) {
      setStatusMessage('A return reason is required before sending time back to the engineer.');
      return;
    }
    await runApprovalAction('/api/manager/approvals/decline', item, { comment: reason });
  }

  async function confirmStaleResolution(item) {
    const key = getApprovalKey(item);
    const reason = (declineReasons[key] ?? '').trim();

    if (!reason) {
      setStatusMessage(
        'A specific stale-item resolution reason is required.'
      );
      return;
    }

    await runApprovalAction(
      '/api/manager/approvals/resolve-stale',
      item,
      { comment: reason }
    );
  }

  async function unlockItem(item) {
    await runApprovalAction('/api/manager/approvals/unlock', item, {
      comment: 'Manager unlocked time for correction.'
    });
  }

  async function bulkApproveSelected() {
    if (selectedPendingItems.length === 0 || busy || historyMode) return;
    setBusy(true);
    setStatusMessage(`Approving ${selectedPendingItems.length} selected submitted day(s)...`);
    try {
      const result = await postJson('/api/manager/approvals/bulk-approve', {
        items: selectedPendingItems.map((item) => ({
          timesheetId: item.timesheetId,
          workDate: item.workDate
        })),
        comment: 'Bulk approved by manager.'
      });
      setStatusMessage(result.message ?? `Approved ${selectedPendingItems.length} selected day(s).`);
      setSelectedKeys(new Set());
      window.dispatchEvent(
        new CustomEvent(
          'projectpulse:approval-queue-changed'
        )
      );
      await loadApprovals();
    } catch (error) {
      setStatusMessage(error instanceof Error ? error.message : 'Bulk approval failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section id={historyMode ? 'approval-history' : 'manager-review'} className="manager-approval-shell">
      <div className="manager-approval-header">
        <div>
          <p className="eyebrow">{historyMode ? 'Decision History' : 'Manager Review'}</p>
          <h2>{historyMode ? 'Previous time approval decisions' : 'Submitted time awaiting review'}</h2>
          <p>
            {historyMode
              ? 'Review day-level approval outcomes and the comments recorded with each manager decision.'
              : 'Review submitted time, approve it for the next workflow stage, or return it with a clear reason.'}
          </p>
        </div>

        <div className="manager-scope-chip">
          <span>Scope</span>
          <strong>
            {access.scopeLabel || 'Assigned approvals'}
          </strong>
        </div>

        <div className="manager-toolbar">
          {!allDates ? (
            <>
              <button type="button" onClick={() => setWeekStart(shiftWeek(weekStart, -7))}>← Previous</button>
              <button type="button" onClick={() => setWeekStart(getSundayForDate())}>Current week</button>
              <button type="button" onClick={() => setWeekStart(shiftWeek(weekStart, 7))}>Next →</button>
            </>
          ) : null}
          {!historyMode ? (
            <button type="button" onClick={() => setIncludeAll((current) => !current)}>
              {includeAll ? 'Pending only' : 'Show all'}
            </button>
          ) : null}

          {!historyMode && access.canViewAllTimeApprovals ? (
            <button type="button" onClick={() => setAllDates((current) => !current)}>
              {allDates ? 'Selected week' : 'All dates'}
            </button>
          ) : null}

          <button type="button" onClick={loadApprovals} disabled={approvalData.loading || busy}>Refresh</button>
        </div>
      </div>

      <div className="manager-status-row">
        {allDates ? (
          <span>Date range: <strong>All available dates</strong></span>
        ) : (
          <>
            <span>Week starts: <strong>{approvalData.data?.weekStart ?? weekStart}</strong></span>
            <span>Week ends: <strong>{approvalData.data?.weekEnd ?? shiftWeek(weekStart, 6)}</strong></span>
          </>
        )}
        <span>Pending: <strong>{pendingItems.length}</strong></span>
        <span>Visible: <strong>{visibleItems.length}</strong></span>
        <span>Status: <strong>{statusMessage}</strong></span>
      </div>

      <div className="manager-filter-row">
        <label>
          Search approvals
          <input
            type="search"
            value={searchText}
            placeholder="Name, email, date, activity, or status"
            onChange={(event) => setSearchText(event.target.value)}
          />
        </label>
        <label>
          Status
          <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
            <option value="all">All statuses</option>
            <option value="submitted">Awaiting manager review</option>
            <option value="manager_approved">Manager approved</option>
            <option value="manager_declined">Returned to engineer</option>
            <option value="pm_approved">PM approved</option>
            <option value="accounting_ready">Accounting ready</option>
            <option value="reconciled">Reconciled</option>
            <option value="locked">Locked</option>
          </select>
        </label>
      </div>

      {!historyMode ? (
        <div className="manager-bulk-actions">
          <button type="button" className="secondary-action" onClick={toggleAllPending} disabled={pendingItems.length === 0 || busy}>
            Select all pending
          </button>
          <button type="button" className="primary-action" onClick={bulkApproveSelected} disabled={selectedPendingItems.length === 0 || busy}>
            Approve selected day(s)
          </button>
          <span>{selectedPendingItems.length} selected</span>
        </div>
      ) : null}

      {approvalData.error ? <div className="manager-empty-state error">{approvalData.error}</div> : null}
      {approvalData.loading ? <div className="manager-empty-state">Loading approval inbox...</div> : null}
      {!approvalData.loading && !approvalData.error && visibleItems.length === 0 ? (
        <div className="manager-empty-state">No approval records match the selected week and filters.</div>
      ) : null}

      <div className="manager-approval-list">
        {visibleItems.map((item) => {
          const key = getApprovalKey(item);
          const isPending = item.status === 'submitted';
          const canManagerUnlock = ['submitted', 'manager_approved', 'manager_declined'].includes(item.status);
          const isStale = isPending && Number(item.ageDays ?? 0) >= 7;
          const canResolveStale = isStale && Boolean(access.canResolveStaleApprovals);
          const isExpanded = expandedKeys.has(key);
          const entries = item.entries ?? [];
          const hasMissingComments = Number(item.commentCount ?? 0) < Number(item.entryCount ?? 0);
          const isDeclining = declineKey === key;

          return (
            <article className={isPending ? 'manager-approval-card pending' : 'manager-approval-card'} key={key}>
              <div className="manager-approval-card-main">
                {!historyMode ? (
                  <label className="manager-select-row">
                    <input
                      type="checkbox"
                      checked={selectedKeys.has(key)}
                      onChange={() => toggleSelected(item)}
                      disabled={!isPending || busy}
                    />
                    <span>Select</span>
                  </label>
                ) : null}

                <div>
                  <h3>{item.resourceName}</h3>
                  <p>{item.resourceEmail}</p>
                  <small>{item.workDate} • Submitted {formatDateTime(item.submittedAt)} • {item.ageDays ?? 0} day(s) old</small>
                </div>

                <div className="manager-approval-metrics">
                  <span><strong>{formatNumber(item.totalHours)}</strong> total</span>
                  <span><strong>{formatNumber(item.normalHours)}</strong> normal</span>
                  <span><strong>{formatNumber(item.afterhours)}</strong> afterhours</span>
                  <span><strong>{item.entryCount}</strong> entries</span>
                </div>

                <div className="manager-approval-actions">
                  <span className={`badge ${isPending ? 'active' : ''}`}>{getStatusLabel(item.status)}</span>
                  <button type="button" className="secondary-action" onClick={() => toggleExpanded(item)}>
                    {isExpanded ? 'Hide details' : 'Review details'}
                  </button>
                  {!historyMode && isPending ? (
                    <>
                      <button type="button" className="primary-action" onClick={() => approveItem(item)} disabled={busy}>
                        Approve day
                      </button>
                      <button
                        type="button"
                        className="danger-action"
                        onClick={() => setDeclineKey(isDeclining ? '' : key)}
                        disabled={busy}
                      >
                        Return day
                      </button>

                      {canResolveStale ? (
                        <button
                          type="button"
                          className="stale"
                          onClick={() => setDeclineKey(key)}
                          disabled={busy}
                        >
                          Resolve stale
                        </button>
                      ) : null}
                    </>
                  ) : null}
                  {!historyMode && !isPending && canManagerUnlock ? (
                    <button type="button" className="secondary-action" onClick={() => unlockItem(item)} disabled={busy}>
                      Unlock for correction
                    </button>
                  ) : null}
                </div>
              </div>

              <div className="manager-approval-summary">
                <strong>Activity summary:</strong> {item.activitySummary || 'No summary available'}
                {hasMissingComments ? (
                  <span className="manager-warning">Some entries are missing descriptions.</span>
                ) : (
                  <span className="manager-ok">Descriptions present for submitted entries.</span>
                )}
              </div>

              {isDeclining ? (
                <div
                  className="approval-inline-decision approval-decision-modal"
                  role="dialog"
                  aria-label="Approval decision"
                >
                  <label htmlFor={`return-reason-${key}`}>Specific reason</label>
                  <textarea
                    id={`return-reason-${key}`}
                    value={declineReasons[key] ?? ''}
                    placeholder="Explain what the engineer must correct before resubmitting."
                    onChange={(event) => setDeclineReasons((current) => ({
                      ...current,
                      [key]: event.target.value
                    }))}
                  />
                  <div className="manager-row-actions">
                    <button type="button" className="decline" onClick={() => confirmDecline(item)} disabled={busy}>
                      Confirm return
                    </button>

                    {canResolveStale ? (
                      <button
                        type="button"
                        className="stale"
                        onClick={() => confirmStaleResolution(item)}
                        disabled={busy}
                      >
                        Resolve stale and notify
                      </button>
                    ) : null}

                    <button type="button" onClick={() => setDeclineKey('')} disabled={busy}>Cancel</button>
                  </div>
                </div>
              ) : null}

              {isExpanded ? (
                <div className="manager-entry-review">
                  {entries.length === 0 ? (
                    <div className="manager-empty-state compact">
                      Detailed entry rows are not available yet. The API summary is still available above.
                    </div>
                  ) : (
                    entries.map((entry) => (
                      <div className="manager-entry-row" key={entry.timeEntryId ?? `${key}-${entry.timeType}-${entry.hours}-${getEntryLabel(entry)}`}>
                        <div>
                          <strong>{getEntryLabel(entry)}</strong>
                          <span>{entry.timeType === 'afterhours' ? 'Afterhours' : 'Normal time'} • {formatNumber(entry.hours)} hours</span>
                        </div>
                        <p>{entry.description || 'No description provided.'}</p>
                      </div>
                    ))
                  )}
                  {item.managerDecisionComment ? (
                    <p className="manager-decision-comment">
                      <strong>Manager decision comment:</strong> {item.managerDecisionComment}
                    </p>
                  ) : null}
                </div>
              ) : null}
            </article>
          );
        })}
      </div>
    </section>
  );
}
