import { useEffect, useMemo, useState } from 'react';
import './manager-approval.css';

function getProjectPulseAuthHeaders() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return {};

    const session = JSON.parse(raw);
    if (!session?.sessionToken) return {};

    return {
      'X-ProjectPulse-Session': session.sessionToken
    };
  } catch {
    return {};
  }
}

async function readApiErrorMessage(response, path) {
  const raw = await response.text();

  if (!raw) {
    return `${path} returned HTTP ${response.status}`;
  }

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
    headers: {
      'Content-Type': 'application/json',
      ...getProjectPulseAuthHeaders()
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, path));
  }

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

export default function ManagerApprovalPanel() {
  const [weekStart, setWeekStart] = useState(getSundayForDate());
  const [includeAll, setIncludeAll] = useState(false);
  const [approvalData, setApprovalData] = useState({ loading: true, data: null, error: null });
  const [selectedKeys, setSelectedKeys] = useState(new Set());
  const [expandedKeys, setExpandedKeys] = useState(new Set());
  const [statusMessage, setStatusMessage] = useState('Ready.');
  const [busy, setBusy] = useState(false);

  async function loadApprovals() {
    setApprovalData((current) => ({ ...current, loading: true, error: null }));

    try {
      const result = await fetchJson(`/api/manager/approvals?weekStart=${weekStart}&includeAll=${includeAll}`);
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
  }, [weekStart, includeAll]);

  const items = approvalData.data?.items ?? [];
  const pendingItems = useMemo(() => items.filter((item) => item.status === 'submitted'), [items]);
  const selectedPendingItems = useMemo(
    () => pendingItems.filter((item) => selectedKeys.has(getApprovalKey(item))),
    [pendingItems, selectedKeys]
  );

  function toggleSelected(item) {
    if (item.status !== 'submitted') return;

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
    setStatusMessage('Processing manager action...');

    try {
      const result = await postJson(path, {
        timesheetId: item.timesheetId,
        workDate: item.workDate,
        ...payload
      });

      setStatusMessage(result.message ?? 'Manager action completed.');
      setSelectedKeys(new Set());
      await loadApprovals();
    } catch (error) {
      setStatusMessage(error instanceof Error ? error.message : 'Manager action failed.');
    } finally {
      setBusy(false);
    }
  }

  async function approveItem(item) {
    await runApprovalAction('/api/manager/approvals/approve', item, {
      comment: 'Approved by manager.'
    });
  }

  async function declineItem(item) {
    const reason = window.prompt('Enter the reason this day is being returned to the engineer:');
    if (reason === null) return;

    const trimmedReason = reason.trim();
    if (!trimmedReason) {
      setStatusMessage('A decline reason is required before returning time to the engineer.');
      return;
    }

    await runApprovalAction('/api/manager/approvals/decline', item, {
      comment: trimmedReason
    });
  }

  async function bulkApproveSelected() {
    if (selectedPendingItems.length === 0 || busy) return;

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
      await loadApprovals();
    } catch (error) {
      setStatusMessage(error instanceof Error ? error.message : 'Bulk approval failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section id="manager-approval" className="panel manager-approval-shell">
      <div className="manager-approval-header">
        <div>
          <p className="eyebrow">Approval Inbox</p>
          <h2>Submitted time awaiting review</h2>
          <p>
            Review submitted day-level time, approve it for the next workflow stage, or return the day to the engineer with a required reason.
          </p>
        </div>

        <div className="manager-toolbar">
          <button type="button" onClick={() => setWeekStart(shiftWeek(weekStart, -7))}>← Previous</button>
          <button type="button" onClick={() => setWeekStart(getSundayForDate())}>Current week</button>
          <button type="button" onClick={() => setWeekStart(shiftWeek(weekStart, 7))}>Next →</button>
          <button type="button" onClick={() => setIncludeAll((current) => !current)}>
            {includeAll ? 'Pending only' : 'Show all'}
          </button>
          <button type="button" onClick={loadApprovals} disabled={approvalData.loading}>Refresh</button>
        </div>
      </div>

      <div className="manager-status-row">
        <span>Week starts: <strong>{approvalData.data?.weekStart ?? weekStart}</strong></span>
        <span>Week ends: <strong>{approvalData.data?.weekEnd ?? shiftWeek(weekStart, 6)}</strong></span>
        <span>Pending: <strong>{pendingItems.length}</strong></span>
        <span>Status: <strong>{statusMessage}</strong></span>
      </div>

      <div className="manager-bulk-actions">
        <button type="button" className="secondary-action" onClick={toggleAllPending} disabled={pendingItems.length === 0 || busy}>
          Select all pending
        </button>
        <button type="button" className="primary-action" onClick={bulkApproveSelected} disabled={selectedPendingItems.length === 0 || busy}>
          Approve selected day(s)
        </button>
        <span>{selectedPendingItems.length} selected</span>
      </div>

      {approvalData.error ? (
        <div className="manager-empty-state error">{approvalData.error}</div>
      ) : null}

      {approvalData.loading ? (
        <div className="manager-empty-state">Loading submitted time...</div>
      ) : null}

      {!approvalData.loading && !approvalData.error && items.length === 0 ? (
        <div className="manager-empty-state">No submitted time is waiting for manager review for this week.</div>
      ) : null}

      <div className="manager-approval-list">
        {items.map((item) => {
          const key = getApprovalKey(item);
          const isPending = item.status === 'submitted';
          const isExpanded = expandedKeys.has(key);
          const entries = item.entries ?? [];
          const hasMissingComments = Number(item.commentCount ?? 0) < Number(item.entryCount ?? 0);

          return (
            <article className="manager-approval-card" key={key}>
              <div className="manager-approval-card-main">
                <label className="manager-select-row">
                  <input
                    type="checkbox"
                    checked={selectedKeys.has(key)}
                    onChange={() => toggleSelected(item)}
                    disabled={!isPending || busy}
                  />
                  <span>Select</span>
                </label>

                <div>
                  <h3>{item.resourceName}</h3>
                  <p>{item.resourceEmail}</p>
                  <small>{item.workDate} • Submitted {formatDateTime(item.submittedAt)}</small>
                </div>

                <div className="manager-approval-metrics">
                  <span><strong>{formatNumber(item.totalHours)}</strong> total</span>
                  <span><strong>{formatNumber(item.normalHours)}</strong> normal</span>
                  <span><strong>{formatNumber(item.afterhours)}</strong> afterhours</span>
                  <span><strong>{item.entryCount}</strong> entries</span>
                </div>

                <div className="manager-approval-actions">
                  <span className={`badge ${isPending ? 'active' : ''}`}>{item.status}</span>
                  <button type="button" className="secondary-action" onClick={() => toggleExpanded(item)}>
                    {isExpanded ? 'Hide details' : 'Review details'}
                  </button>
                  <button type="button" className="primary-action" onClick={() => approveItem(item)} disabled={!isPending || busy}>
                    Approve day
                  </button>
                  <button type="button" className="danger-action" onClick={() => declineItem(item)} disabled={!isPending || busy}>
                    Return day
                  </button>
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
