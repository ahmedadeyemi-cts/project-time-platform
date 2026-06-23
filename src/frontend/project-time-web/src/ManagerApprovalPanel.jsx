import { useEffect, useState } from 'react';
import './manager-approval.css';

function toIsoDate(date) {
  return date.toISOString().slice(0, 10);
}

function getSundayIso(date = new Date()) {
  const normalized = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
  normalized.setUTCDate(normalized.getUTCDate() - normalized.getUTCDay());
  return toIsoDate(normalized);
}

function addDaysIso(isoDate, numberOfDays) {
  const [year, month, day] = isoDate.split('-').map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  date.setUTCDate(date.getUTCDate() + numberOfDays);
  return toIsoDate(date);
}

function formatNumber(value) {
  return Number(value || 0).toFixed(2);
}

async function fetchJson(path) {
  const response = await fetch(path);
  if (!response.ok) throw new Error(`${path} returned HTTP ${response.status}`);
  return response.json();
}

async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    let details = '';
    try {
      const body = await response.json();
      details = body.message || body.detail || JSON.stringify(body);
    } catch {
      details = await response.text();
    }
    throw new Error(`${path} returned HTTP ${response.status}${details ? `: ${details}` : ''}`);
  }

  return response.json();
}

function statusLabel(status) {
  if (status === 'submitted') return 'Pending manager approval';
  if (status === 'manager_approved') return 'Manager approved';
  if (status === 'manager_declined') return 'Declined / returned';
  if (status === 'draft') return 'Draft';
  return status ?? 'Unknown';
}

export default function ManagerApprovalPanel() {
  const [weekStart, setWeekStart] = useState(getSundayIso);
  const [includeAll, setIncludeAll] = useState(false);
  const [approvalData, setApprovalData] = useState({ loading: true, data: null, error: null });
  const [actionStatus, setActionStatus] = useState('Ready');
  const [isWorking, setIsWorking] = useState(false);

  async function loadApprovals() {
    setApprovalData({ loading: true, data: null, error: null });
    try {
      const result = await fetchJson(`/api/manager/approvals?weekStart=${weekStart}&includeAll=${includeAll}`);
      setApprovalData({ loading: false, data: result, error: null });
    } catch (error) {
      setApprovalData({ loading: false, data: null, error: error instanceof Error ? error.message : 'Failed to load manager approvals' });
    }
  }

  useEffect(() => {
    loadApprovals();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [weekStart, includeAll]);

  async function runAction(path, item, payload = {}) {
    setIsWorking(true);
    setActionStatus('Processing manager action...');

    try {
      const result = await postJson(path, {
        timesheetId: item.timesheetId,
        workDate: item.workDate,
        ...payload
      });
      setActionStatus(result.message ?? 'Manager action complete');
      await loadApprovals();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Manager action failed');
    } finally {
      setIsWorking(false);
    }
  }

  function declineItem(item) {
    const reason = window.prompt('Enter the reason this time is being returned to the engineer:');
    if (reason === null) return;

    const cleanReason = reason.trim();
    if (!cleanReason) {
      setActionStatus('A decline reason is required before returning time to the engineer.');
      return;
    }

    void runAction('/api/manager/approvals/decline', item, { comment: cleanReason });
  }

  function unlockItem(item) {
    const reason = window.prompt('Enter the manager unlock reason:');
    if (reason === null) return;

    void runAction('/api/manager/approvals/unlock', item, { comment: reason.trim() || 'Manager unlock requested.' });
  }

  const items = approvalData.data?.items ?? [];

  return (
    <section id="manager-approval" className="manager-approval-shell">
      <div className="manager-approval-header">
        <div>
          <p className="eyebrow">Manager Approval</p>
          <h2>Submitted time awaiting review</h2>
          <p>
            Review submitted day-level time, approve it for the next workflow stage, return it to the engineer with a reason, or unlock it when corrections are needed.
          </p>
        </div>

        <div className="manager-toolbar">
          <button type="button" onClick={() => setWeekStart(addDaysIso(weekStart, -7))}>← Previous</button>
          <button type="button" onClick={() => setWeekStart(getSundayIso())}>Current week</button>
          <button type="button" onClick={() => setWeekStart(addDaysIso(weekStart, 7))}>Next →</button>
          <button type="button" onClick={() => setIncludeAll((value) => !value)}>{includeAll ? 'Pending only' : 'Show all'}</button>
          <button type="button" onClick={loadApprovals}>Refresh</button>
        </div>
      </div>

      <div className="manager-status-row">
        <span>Week starts: <strong>{approvalData.data?.weekStart ?? weekStart}</strong></span>
        <span>Week ends: <strong>{approvalData.data?.weekEnd ?? addDaysIso(weekStart, 6)}</strong></span>
        <span>Items: <strong>{approvalData.loading ? 'Loading...' : items.length}</strong></span>
        <span>Action: <strong>{actionStatus}</strong></span>
      </div>

      {approvalData.error ? (
        <div className="manager-empty-state error">{approvalData.error}</div>
      ) : null}

      {!approvalData.loading && !approvalData.error && items.length === 0 ? (
        <div className="manager-empty-state">No submitted time is currently waiting for manager approval for this week.</div>
      ) : null}

      {items.length > 0 ? (
        <div className="manager-table-wrap">
          <table className="manager-table">
            <thead>
              <tr>
                <th>Resource</th>
                <th>Date</th>
                <th>Status</th>
                <th>Activities</th>
                <th>Regular</th>
                <th>Afterhours</th>
                <th>Total</th>
                <th>Comments</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={`${item.timesheetId}-${item.workDate}`}>
                  <td>
                    <strong>{item.resourceName}</strong>
                    <span>{item.resourceEmail}</span>
                  </td>
                  <td>{item.workDate}</td>
                  <td><span className={`manager-status ${item.status}`}>{statusLabel(item.status)}</span></td>
                  <td>{item.activitySummary}</td>
                  <td>{formatNumber(item.normalHours)}</td>
                  <td>{formatNumber(item.afterhours)}</td>
                  <td><strong>{formatNumber(item.totalHours)}</strong></td>
                  <td>{item.commentCount}</td>
                  <td>
                    <div className="manager-row-actions">
                      {item.status === 'submitted' ? (
                        <>
                          <button type="button" className="approve" disabled={isWorking} onClick={() => runAction('/api/manager/approvals/approve', item)}>
                            Approve
                          </button>
                          <button type="button" className="decline" disabled={isWorking} onClick={() => declineItem(item)}>
                            Decline
                          </button>
                          <button type="button" className="unlock" disabled={isWorking} onClick={() => unlockItem(item)}>
                            Unlock
                          </button>
                        </>
                      ) : (
                        <button type="button" disabled={isWorking} onClick={() => unlockItem(item)}>
                          Manager unlock
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
