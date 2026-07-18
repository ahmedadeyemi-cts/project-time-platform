import { useEffect, useMemo, useState } from 'react';
import './manager-approval.css';

function authHeaders() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
  } catch {
    return {};
  }
}

async function requestJson(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: {
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...authHeaders(),
      ...(options.headers ?? {})
    }
  });
  const raw = await response.text();
  let payload = {};
  try { payload = raw ? JSON.parse(raw) : {}; } catch { payload = { message: raw }; }
  if (!response.ok) throw new Error(payload.message || `${path} returned HTTP ${response.status}`);
  return payload;
}

function iso(date) { return date.toISOString().slice(0, 10); }
function sunday(date = new Date()) {
  const value = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
  value.setUTCDate(value.getUTCDate() - value.getUTCDay());
  return iso(value);
}
function shift(dateValue, days) {
  const value = new Date(`${dateValue}T00:00:00Z`);
  value.setUTCDate(value.getUTCDate() + days);
  return iso(value);
}
function number(value) { return Number(value ?? 0).toFixed(2); }
function dateTime(value) { return value ? new Date(value).toLocaleString() : 'Not available'; }
function key(item) { return `${item.timesheetId}|${item.workDate}`; }
function statusLabel(status) {
  if (status === 'submitted') return 'Action required';
  if (status === 'manager_approved') return 'Approved';
  if (status === 'manager_declined') return 'Returned';
  return status || 'Unknown';
}
function entryLabel(entry) {
  const project = [entry.projectCode, entry.projectName].filter(Boolean).join(' — ');
  const category = [entry.categoryCode, entry.categoryName].filter(Boolean).join(' — ');
  return project || category || 'Time entry';
}

export default function ManagerApprovalPanel() {
  const [weekStart, setWeekStart] = useState(sunday());
  const [includeHistory, setIncludeHistory] = useState(false);
  const [allDates, setAllDates] = useState(false);
  const [search, setSearch] = useState('');
  const [data, setData] = useState({ loading: true, payload: null, error: null });
  const [expanded, setExpanded] = useState(new Set());
  const [decision, setDecision] = useState(null);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('Ready.');

  async function load() {
    setData((current) => ({ ...current, loading: true, error: null }));
    try {
      const params = new URLSearchParams({
        weekStart,
        includeAll: String(includeHistory),
        allDates: String(allDates),
        search
      });
      const payload = await requestJson(`/api/manager/approvals?${params}`);
      setData({ loading: false, payload, error: null });
    } catch (error) {
      setData({ loading: false, payload: null, error: error instanceof Error ? error.message : 'Unable to load time approvals.' });
    }
  }

  useEffect(() => { load(); }, [weekStart, includeHistory, allDates]);

  const items = data.payload?.items ?? [];
  const pending = useMemo(() => items.filter((item) => item.status === 'submitted'), [items]);
  const access = data.payload?.access ?? {};

  function toggle(item) {
    const itemKey = key(item);
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(itemKey)) next.delete(itemKey); else next.add(itemKey);
      return next;
    });
  }

  async function approve(item) {
    setBusy(true);
    setMessage('Approving submitted time…');
    try {
      const result = await requestJson('/api/manager/approvals/approve', {
        method: 'POST',
        body: JSON.stringify({ timesheetId: item.timesheetId, workDate: item.workDate, comment: 'Approved from Module 002 Approval Center.' })
      });
      setMessage(result.message || 'Time approved.');
      window.dispatchEvent(new CustomEvent('projectpulse:approval-queue-changed'));
      await load();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Approval failed.');
    } finally {
      setBusy(false);
    }
  }

  function openDecision(item, mode) {
    setDecision({ item, mode });
    setReason('');
  }

  async function submitDecision(event) {
    event.preventDefault();
    if (!decision || !reason.trim()) return;
    setBusy(true);
    setMessage(decision.mode === 'stale' ? 'Resolving stale approval…' : 'Returning time to the engineer…');
    const path = decision.mode === 'stale'
      ? '/api/manager/approvals/resolve-stale'
      : '/api/manager/approvals/decline';

    try {
      const result = await requestJson(path, {
        method: 'POST',
        body: JSON.stringify({
          timesheetId: decision.item.timesheetId,
          workDate: decision.item.workDate,
          comment: reason.trim()
        })
      });
      setMessage(`${result.message || 'Approval item updated.'} Engineer email: ${result.emailNotificationStatus || 'queued_global_smtp'}.`);
      setDecision(null);
      setReason('');
      window.dispatchEvent(new CustomEvent('projectpulse:approval-queue-changed'));
      await load();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to update the approval.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="panel manager-approval-shell">
      <div className="manager-approval-header">
        <div>
          <p className="eyebrow">Time approvals</p>
          <h2>Submitted time awaiting your review</h2>
          <p>Approve valid submissions or return specific time to the engineer with a required explanation. Returned-time emails are queued through Global SMTP.</p>
        </div>
        <div className="manager-scope-chip">
          <span>Scope</span>
          <strong>{access.scopeLabel || 'Assigned approvals'}</strong>
        </div>
      </div>

      <div className="manager-filter-bar">
        {!allDates ? (
          <div className="manager-week-controls">
            <button type="button" onClick={() => setWeekStart(shift(weekStart, -7))}>← Previous</button>
            <button type="button" onClick={() => setWeekStart(sunday())}>Current week</button>
            <button type="button" onClick={() => setWeekStart(shift(weekStart, 7))}>Next →</button>
          </div>
        ) : null}
        <label>
          <span>Search</span>
          <input value={search} placeholder="Engineer, project, or activity" onChange={(event) => setSearch(event.target.value)} onKeyDown={(event) => event.key === 'Enter' && load()} />
        </label>
        <button type="button" onClick={load}>Apply</button>
        <button type="button" className={includeHistory ? 'active' : ''} onClick={() => setIncludeHistory((value) => !value)}>
          {includeHistory ? 'Action required only' : 'Include decision history'}
        </button>
        {access.canViewAllTimeApprovals ? (
          <button type="button" className={allDates ? 'active' : ''} onClick={() => setAllDates((value) => !value)}>
            {allDates ? 'Selected week' : 'All dates'}
          </button>
        ) : null}
        <button type="button" onClick={load} disabled={data.loading}>Refresh</button>
      </div>

      <div className="manager-status-row">
        <span>Action required <strong>{pending.length}</strong></span>
        <span>Displayed <strong>{items.length}</strong></span>
        {!allDates ? <span>Week <strong>{data.payload?.weekStart ?? weekStart} – {data.payload?.weekEnd ?? shift(weekStart, 6)}</strong></span> : <span>Date range <strong>All available dates</strong></span>}
        <span className="manager-status-message">{message}</span>
      </div>

      {data.error ? <div className="manager-empty-state error">{data.error}</div> : null}
      {data.loading ? <div className="manager-empty-state">Loading approval items…</div> : null}
      {!data.loading && !data.error && items.length === 0 ? <div className="manager-empty-state">No approval items match this scope and filter.</div> : null}

      <div className="manager-approval-list">
        {items.map((item) => {
          const itemKey = key(item);
          const isPending = item.status === 'submitted';
          const entries = Array.isArray(item.entries) ? item.entries : [];
          const isExpanded = expanded.has(itemKey);
          const stale = isPending && Number(item.ageDays ?? 0) >= 7;

          return (
            <article className={isPending ? 'manager-approval-card pending' : 'manager-approval-card'} key={itemKey}>
              <div className="manager-approval-card-main">
                <div className="manager-resource-block">
                  <span className={`manager-status-badge ${item.status}`}>{statusLabel(item.status)}</span>
                  <h3>{item.resourceName}</h3>
                  <p>{item.resourceEmail}</p>
                  <small>{item.workDate} • Submitted {dateTime(item.submittedAt)} • {item.ageDays ?? 0} day(s) old</small>
                </div>

                <div className="manager-approval-metrics">
                  <span><strong>{number(item.totalHours)}</strong> total</span>
                  <span><strong>{number(item.normalHours)}</strong> normal</span>
                  <span><strong>{number(item.afterhours)}</strong> afterhours</span>
                  <span><strong>{item.entryCount}</strong> entries</span>
                </div>

                <div className="manager-approval-actions">
                  <button type="button" onClick={() => toggle(item)}>{isExpanded ? 'Hide details' : 'Review details'}</button>
                  {isPending ? <button type="button" className="approve" onClick={() => approve(item)} disabled={busy}>Approve</button> : null}
                  {isPending ? <button type="button" className="decline" onClick={() => openDecision(item, 'reject')} disabled={busy}>Reject / return</button> : null}
                  {stale && access.canResolveStaleApprovals ? <button type="button" className="stale" onClick={() => openDecision(item, 'stale')} disabled={busy}>Resolve stale</button> : null}
                </div>
              </div>

              <div className="manager-approval-summary">
                <span><strong>Activity:</strong> {item.activitySummary || 'No activity summary'}</span>
                {Number(item.commentCount ?? 0) < Number(item.entryCount ?? 0)
                  ? <span className="manager-warning">Some entries are missing descriptions.</span>
                  : <span className="manager-ok">Descriptions are present.</span>}
              </div>

              {isExpanded ? (
                <div className="manager-entry-review">
                  {entries.length === 0 ? <div className="manager-empty-state compact">No detailed entry rows were returned.</div> : entries.map((entry) => (
                    <div className="manager-entry-row" key={entry.timeEntryId}>
                      <div>
                        <strong>{entryLabel(entry)}</strong>
                        <span>{entry.timeType === 'afterhours' ? 'Afterhours' : 'Normal'} • {number(entry.hours)} hours</span>
                        {entry.taskId ? <small>Task ID: {entry.taskId}</small> : null}
                      </div>
                      <p>{entry.description || 'No description provided.'}</p>
                    </div>
                  ))}
                  {item.managerDecisionComment ? <p className="manager-decision-comment"><strong>Decision comment:</strong> {item.managerDecisionComment}</p> : null}
                </div>
              ) : null}
            </article>
          );
        })}
      </div>

      {decision ? (
        <div className="approval-modal-backdrop" role="presentation" onMouseDown={() => !busy && setDecision(null)}>
          <form className="approval-decision-modal" onSubmit={submitDecision} onMouseDown={(event) => event.stopPropagation()}>
            <p className="eyebrow">{decision.mode === 'stale' ? 'Administrative stale-item resolution' : 'Reject submitted time'}</p>
            <h3>{decision.item.resourceName} — {decision.item.workDate}</h3>
            <p>{decision.mode === 'stale' ? 'This action preserves the audit history and returns the stale submission to the engineer.' : 'The engineer will receive a Global SMTP email listing every returned entry and this reason.'}</p>
            <label>
              <span>Specific reason</span>
              <textarea autoFocus required rows="6" value={reason} onChange={(event) => setReason(event.target.value)} placeholder="State exactly what must be corrected, including the affected project, task, hours, or description." />
            </label>
            <div className="approval-modal-actions">
              <button type="button" onClick={() => setDecision(null)} disabled={busy}>Cancel</button>
              <button type="submit" className="decline" disabled={busy || !reason.trim()}>{busy ? 'Processing…' : decision.mode === 'stale' ? 'Resolve and notify' : 'Return and notify engineer'}</button>
            </div>
          </form>
        </div>
      ) : null}
    </section>
  );
}
