import { useEffect, useMemo, useState } from 'react';
import './approval-center.css';

function getProjectPulseAuthHeaders() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return {};

    const session = JSON.parse(raw);
    return session?.sessionToken
      ? { 'X-ProjectPulse-Session': session.sessionToken }
      : {};
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

function shiftDate(isoDate, days) {
  const date = new Date(`${isoDate}T00:00:00Z`);
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

function getItemKey(item) {
  return `${item.timesheetId}|${item.workDate}`;
}

export default function ProjectManagerApprovalPanel() {
  const [weekStart, setWeekStart] = useState(getSundayForDate());
  const [data, setData] = useState({
    loading: true,
    items: [],
    access: null,
    error: null
  });
  const [searchText, setSearchText] = useState('');
  const [commentByKey, setCommentByKey] = useState({});
  const [expandedKey, setExpandedKey] = useState('');
  const [busyKey, setBusyKey] = useState('');
  const [statusMessage, setStatusMessage] = useState('Ready.');

  const weekEnd = shiftDate(weekStart, 6);

  async function loadItems() {
    setData((current) => ({ ...current, loading: true, error: null }));

    try {
      const result = await fetchJson(
        `/api/workflow/approval-items?weekStart=${weekStart}&weekEnd=${weekEnd}`
      );

      setData({
        loading: false,
        items: Array.isArray(result.items) ? result.items : [],
        access: result.access ?? null,
        error: null
      });
    } catch (error) {
      setData({
        loading: false,
        items: [],
        access: null,
        error: error instanceof Error ? error.message : 'Unable to load PM approvals.'
      });
    }
  }

  useEffect(() => {
    void loadItems();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [weekStart]);

  const pendingItems = useMemo(
    () => data.items.filter((item) => item.status === 'manager_approved'),
    [data.items]
  );

  const visibleItems = useMemo(() => {
    const normalizedSearch = searchText.trim().toLowerCase();
    if (!normalizedSearch) return pendingItems;

    return pendingItems.filter((item) => (
      [
        item.employeeName,
        item.employeeEmail,
        item.workDate,
        item.projectCodes,
        item.projectNames,
        item.managerComment
      ]
        .filter(Boolean)
        .some((value) => String(value).toLowerCase().includes(normalizedSearch))
    ));
  }, [pendingItems, searchText]);

  async function approveItem(item) {
    const key = getItemKey(item);
    const comment = (commentByKey[key] ?? '').trim()
      || 'Approved by project manager from the Approval Center.';

    setBusyKey(key);
    setStatusMessage(`Approving ${item.employeeName || item.employeeEmail} for ${item.workDate}...`);

    try {
      const result = await postJson('/api/workflow/approval-items/action', {
        timesheetId: item.timesheetId,
        workDate: item.workDate,
        action: 'pm_approve',
        comment
      });

      setStatusMessage(result.message ?? 'Project time approved.');
      setCommentByKey((current) => {
        const next = { ...current };
        delete next[key];
        return next;
      });
      setExpandedKey('');
      await loadItems();
    } catch (error) {
      setStatusMessage(
        error instanceof Error ? error.message : 'Unable to approve project time.'
      );
    } finally {
      setBusyKey('');
    }
  }

  return (
    <section id="pm-review" className="pm-approval-shell">
      <div className="manager-approval-header">
        <div>
          <p className="eyebrow">PM Review</p>
          <h2>Manager-approved project time</h2>
          <p>
            Assigned project managers review manager-approved project time before it proceeds to accounting. Project/team coordinators and administrators can review across project scope.
          </p>
        </div>

        <div className="manager-toolbar">
          <button type="button" onClick={() => setWeekStart(shiftDate(weekStart, -7))}>← Previous</button>
          <button type="button" onClick={() => setWeekStart(getSundayForDate())}>Current week</button>
          <button type="button" onClick={() => setWeekStart(shiftDate(weekStart, 7))}>Next →</button>
          <button type="button" onClick={loadItems} disabled={data.loading || Boolean(busyKey)}>Refresh</button>
        </div>
      </div>

      <div className="manager-status-row">
        <span>Week starts: <strong>{weekStart}</strong></span>
        <span>Week ends: <strong>{weekEnd}</strong></span>
        <span>Pending PM review: <strong>{pendingItems.length}</strong></span>
        <span>Can approve: <strong>{data.access?.CanProjectApprove ? 'Yes' : 'No'}</strong></span>
        <span>Status: <strong>{statusMessage}</strong></span>
      </div>

      <div className="pm-approval-notice">
        <strong>Current backend capability:</strong>
        <span>
          PM approval is active and audited. PM return/rejection remains unavailable until the matching backend transition is implemented.
        </span>
      </div>

      <label className="pm-approval-search">
        Search PM approvals
        <input
          type="search"
          value={searchText}
          placeholder="Engineer, email, date, project code, or project name"
          onChange={(event) => setSearchText(event.target.value)}
        />
      </label>

      {data.error ? <div className="manager-empty-state error">{data.error}</div> : null}
      {data.loading ? <div className="manager-empty-state">Loading manager-approved project time...</div> : null}

      {!data.loading && !data.error && visibleItems.length === 0 ? (
        <div className="manager-empty-state">
          No manager-approved project time is waiting for PM review for this week.
        </div>
      ) : null}

      <div className="pm-approval-list">
        {visibleItems.map((item) => {
          const key = getItemKey(item);
          const isExpanded = expandedKey === key;
          const isBusy = busyKey === key;

          return (
            <article className="pm-approval-card" key={key}>
              <div className="pm-approval-card-header">
                <div>
                  <span className="badge active">Awaiting PM review</span>
                  <h3>{item.employeeName}</h3>
                  <p>{item.employeeEmail}</p>
                  <small>
                    {item.workDate} • Manager approved {formatDateTime(item.managerApprovedAt)}
                  </small>
                </div>

                <div className="pm-approval-metrics">
                  <span>
                    <strong>{formatNumber(item.totalHours)}</strong>
                    total hours
                  </span>
                  <span>
                    <strong>{item.projectCodes || 'Not listed'}</strong>
                    project code(s)
                  </span>
                  <span>
                    <strong>{item.projectNames || 'Not listed'}</strong>
                    project name(s)
                  </span>
                </div>
              </div>

              <div className="pm-manager-comment">
                <strong>Manager decision comment</strong>
                <p>{item.managerComment || 'No manager comment was recorded.'}</p>
              </div>

              <div className="manager-row-actions">
                <button
                  type="button"
                  onClick={() => setExpandedKey(isExpanded ? '' : key)}
                  disabled={Boolean(busyKey)}
                >
                  {isExpanded ? 'Close decision' : 'Review and approve'}
                </button>
              </div>

              {isExpanded ? (
                <div className="pm-approval-decision">
                  <label htmlFor={`pm-comment-${key}`}>
                    PM approval comment
                    <textarea
                      id={`pm-comment-${key}`}
                      value={commentByKey[key] ?? ''}
                      placeholder="Add a project approval comment for the audit record."
                      onChange={(event) => setCommentByKey((current) => ({
                        ...current,
                        [key]: event.target.value
                      }))}
                    />
                  </label>

                  <div className="manager-row-actions">
                    <button
                      type="button"
                      className="approve"
                      onClick={() => approveItem(item)}
                      disabled={isBusy || !data.access?.CanProjectApprove}
                    >
                      {isBusy ? 'Approving...' : 'Approve project time'}
                    </button>
                    <button
                      type="button"
                      onClick={() => setExpandedKey('')}
                      disabled={isBusy}
                    >
                      Cancel
                    </button>
                  </div>
                </div>
              ) : null}
            </article>
          );
        })}
      </div>
    </section>
  );
}
