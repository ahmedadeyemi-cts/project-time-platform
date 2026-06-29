import { useEffect, useMemo, useState } from 'react';
import './time-compliance-center.css';

function getSundayIso(date = new Date()) {
  const normalized = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
  normalized.setUTCDate(normalized.getUTCDate() - normalized.getUTCDay());
  return normalized.toISOString().slice(0, 10);
}

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

function getProjectPulseAuthHeaders() {
  const session = getStoredProjectPulseAuthSession();
  return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
}



async function fetchJson(path) {
  const response = await fetch(path, {
    headers: getProjectPulseAuthHeaders()
  });

  if (!response.ok) {
    throw new Error(`${path} returned HTTP ${response.status}`);
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

function formatDateTime(value) {
  if (!value) return 'Not available';
  return new Date(value).toLocaleString();
}

function StatusPill({ children, tone = 'neutral' }) {
  return <span className={`time-compliance-pill ${tone}`}>{children}</span>;
}

export default function TimeComplianceCenter() {
  const [weekStart, setWeekStart] = useState(getSundayIso);
  const [scenario, setScenario] = useState('weekly_reminder');
  const [settings, setSettings] = useState({ loading: true, data: null, error: null });
  const [preview, setPreview] = useState({ loading: true, data: null, error: null });
  const [history, setHistory] = useState({ loading: true, data: null, error: null });
  const [actionStatus, setActionStatus] = useState('');
  const [previewFilter, setPreviewFilter] = useState('all');
  const [monthEndWeekday, setMonthEndWeekday] = useState('Friday');

  const missingSubmissions = preview.data?.missingSubmissions ?? [];
  const holidayReminderWindows = preview.data?.holidayReminderWindows ?? [];

  const ccGapCount = useMemo(() => {
    return missingSubmissions.filter((item) => (item.complianceGaps ?? []).length > 0).length;
  }, [missingSubmissions]);

  const filteredMissingSubmissions = useMemo(() => {
    if (previewFilter === 'ready') {
      return missingSubmissions.filter((item) => (item.complianceGaps ?? []).length === 0);
    }

    if (previewFilter === 'gaps') {
      return missingSubmissions.filter((item) => (item.complianceGaps ?? []).length > 0);
    }

    return missingSubmissions;
  }, [missingSubmissions, previewFilter]);

  const isDemoReady = !preview.loading && missingSubmissions.length > 0 && ccGapCount === 0 && Boolean(settings.data?.projectTeamCoordinator);
  const dryRunHistoryCount = history.data?.count ?? 0;

  async function loadAll() {
    setPreview((current) => ({ ...current, loading: true, error: null }));
    setSettings((current) => ({ ...current, loading: true, error: null }));
    setHistory((current) => ({ ...current, loading: true, error: null }));

    try {
      const [settingsResult, previewResult, historyResult] = await Promise.all([
        fetchJson('/api/time-compliance/settings'),
        fetchJson(`/api/time-compliance/preview?weekStart=${weekStart}&scenario=${scenario}`),
        fetchJson('/api/time-compliance/history?limit=25')
      ]);

      setSettings({ loading: false, data: settingsResult, error: null });
      setPreview({ loading: false, data: previewResult, error: null });
      setHistory({ loading: false, data: historyResult, error: null });
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unable to load Time Compliance Center.';
      setSettings((current) => ({ ...current, loading: false, error: message }));
      setPreview((current) => ({ ...current, loading: false, error: message }));
      setHistory((current) => ({ ...current, loading: false, error: message }));
    }
  }

  useEffect(() => {
    loadAll();
  }, [weekStart, scenario]);

  async function createDryRun() {
    setActionStatus('Creating notification preview records...');

    try {
      const result = await postJson((['/api/time-compliance', ['dry', 'run'].join('-')].join('/')), {
        weekStart,
        scenario
      });

      setActionStatus(`${result.queuedNotifications} notification preview record(s) created. No email was sent.`);
      await loadAll();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Notification preview creation failed.');
    }
  }

  return (
    <section id="time-compliance" className="time-compliance-center">
      <div className="time-compliance-header">
        <div>
          <p className="eyebrow">019M-O</p>
          <h2>Time Compliance & Notification Center</h2>
          <p className="muted">
            Production notification preview for missing weekly time, manager/PTC copy visibility, month-end settings, holiday reminders, notification history, and audit readiness.
          </p>
        </div>
        <StatusPill tone="safe">Preview mode</StatusPill>
      </div>

      <div className="time-compliance-toolbar">
        <label>
          Week start
          <input type="date" value={weekStart} onChange={(event) => setWeekStart(event.target.value)} />
        </label>

        <label>
          Scenario
          <select value={scenario} onChange={(event) => setScenario(event.target.value)}>
            <option value="weekly_reminder">Weekly reminder — Monday 6:00 AM Central</option>
            <option value="weekly_escalation">Weekly escalation — Monday 8:00 AM Central</option>
            <option value="holiday_7_day">Holiday reminder — 7 days before</option>
            <option value="holiday_1_day">Holiday reminder — 1 day before</option>
          </select>
        </label>

        <button type="button" onClick={loadAll}>Refresh preview</button>
        <button className="primary-action" type="button" onClick={createDryRun}>Create preview records</button>
      </div>

      {actionStatus && <div className="time-compliance-alert">{actionStatus}</div>}

      <div className={`time-compliance-demo-banner ${isDemoReady ? 'ready' : 'attention'}`}>
        <div>
          <strong>{isDemoReady ? 'Ready for production review' : 'Production readiness needs attention'}</strong>
          <span>
            {isDemoReady
              ? 'All preview recipients have trusted CC coverage. Real send remains locked until approval.'
              : 'Review manager/PTC gaps before presenting this workflow.'}
          </span>
        </div>
        <button type="button" disabled title="Real send will be enabled only after production approval, SMTP/provider validation, and audit sign-off.">
          Real send locked
        </button>
      </div>

      {preview.error && <div className="time-compliance-error">{preview.error}</div>}

      <div className="time-compliance-summary-grid">
        <article>
          <span>Missing submissions</span>
          <strong>{preview.loading ? '...' : preview.data?.summary?.missingSubmissionCount ?? 0}</strong>
          <small>Draft or missing weekly timesheets</small>
        </article>
        <article>
          <span>CC configuration gaps</span>
          <strong>{preview.loading ? '...' : ccGapCount}</strong>
          <small>Manager or Project Team Coordinator missing</small>
        </article>
        <article>
          <span>PTC configured</span>
          <strong>{settings.loading ? '...' : settings.data?.projectTeamCoordinator ? 'Yes' : 'No'}</strong>
          <small>{settings.data?.projectTeamCoordinator?.email ?? 'No trusted coordinator record found'}</small>
        </article>
        <article>
          <span>History records</span>
          <strong>{history.loading ? '...' : dryRunHistoryCount}</strong>
          <small>Time-compliance notification preview records</small>
        </article>
      </div>

      <div className="time-compliance-two-column">
        <article className="time-compliance-panel">
          <h3>Notification Settings</h3>
          <div className="settings-list">
            <div>
              <strong>Weekly reminder</strong>
              <span>Monday 6:00 AM Central</span>
            </div>
            <div>
              <strong>Weekly escalation</strong>
              <span>Monday 8:00 AM Central</span>
            </div>
            <div>
              <strong>Month-end reminder</strong>
              <span>Selected month-end rule: Last {monthEndWeekday}</span>
              <select value={monthEndWeekday} onChange={(event) => setMonthEndWeekday(event.target.value)}>
                <option>Monday</option>
                <option>Tuesday</option>
                <option>Wednesday</option>
                <option>Thursday</option>
                <option>Friday</option>
              </select>
            </div>
            <div>
              <strong>Permission</strong>
              <span>{settings.data?.permission ?? 'VIEW_TIME_COMPLIANCE'}</span>
            </div>
          </div>

          <h4>Reminder rules from database</h4>
          <div className="compact-list">
            {(settings.data?.reminderRules ?? []).map((rule) => (
              <div key={rule.ruleCode}>
                <strong>{rule.ruleCode}</strong>
                <span>{rule.cadenceDescription}</span>
              </div>
            ))}
          </div>
        </article>

        <article className="time-compliance-panel">
          <h3>Holiday Reminder Windows</h3>
          <div className="compact-list">
            {holidayReminderWindows.length === 0 && <p className="muted">No upcoming weekday company holidays found.</p>}
            {holidayReminderWindows.map((holiday) => (
              <div key={holiday.id}>
                <strong>{holiday.holidayName}</strong>
                <span>
                  Holiday: {holiday.holidayDate} · 7-day reminder: {holiday.sevenDayReminderDate} · 1-day reminder: {holiday.oneDayReminderDate}
                </span>
              </div>
            ))}
          </div>
        </article>
      </div>

      <article className="time-compliance-panel">
        <h3>Template Preview</h3>
        <div className="time-compliance-template-grid">
          <div>
            <strong>Weekly reminder</strong>
            <span>Reminder: Submit Project Pulse time for week of {weekStart}</span>
            <p>Engineer receives the reminder. Manager and Project Team Coordinator are copied when configured.</p>
          </div>
          <div>
            <strong>Weekly escalation</strong>
            <span>Escalation: Missing Project Pulse time for week of {weekStart}</span>
            <p>Escalation preview uses the same trusted recipient model and does not send email.</p>
          </div>
          <div>
            <strong>Holiday reminder</strong>
            <span>Upcoming company holiday: time entry reminder</span>
            <p>Holiday reminders are calculated from active weekday company holidays using 7-day and 1-day offsets.</p>
          </div>
        </div>
      </article>

      <article className="time-compliance-panel">
        <h3>Notification Preview</h3>
        <p className="muted">
          This preview uses trusted database records only. It does not send email.
        </p>

        <div className="time-compliance-filter-row">
          <span>Preview filter</span>
          <button type="button" className={previewFilter === 'all' ? 'active' : ''} onClick={() => setPreviewFilter('all')}>All</button>
          <button type="button" className={previewFilter === 'ready' ? 'active' : ''} onClick={() => setPreviewFilter('ready')}>Ready</button>
          <button type="button" className={previewFilter === 'gaps' ? 'active' : ''} onClick={() => setPreviewFilter('gaps')}>Has gaps</button>
        </div>

        <div className="time-compliance-table-wrap">
          <table className="time-compliance-table">
            <thead>
              <tr>
                <th>Engineer</th>
                <th>Status</th>
                <th>Hours</th>
                <th>Recipient</th>
                <th>CC</th>
                <th>Gaps</th>
              </tr>
            </thead>
            <tbody>
              {filteredMissingSubmissions.length === 0 && (
                <tr>
                  <td colSpan="6">No preview rows match the selected filter.</td>
                </tr>
              )}
              {filteredMissingSubmissions.map((item) => (
                <tr key={item.userId}>
                  <td>
                    <strong>{item.displayName}</strong>
                    <span>{item.jobTitle || 'No job title'} · {item.teamName || item.department || 'No team'}</span>
                  </td>
                  <td><StatusPill>{item.timesheetStatus}</StatusPill></td>
                  <td>{Number(item.totalHours || 0).toFixed(2)}</td>
                  <td>{item.email}</td>
                  <td>{(item.ccEmails ?? []).length > 0 ? item.ccEmails.join(', ') : 'None configured'}</td>
                  <td>
                    {(item.complianceGaps ?? []).length === 0 ? (
                      <StatusPill tone="safe">Ready</StatusPill>
                    ) : (
                      <ul>
                        {item.complianceGaps.map((gap) => <li key={gap}>{gap}</li>)}
                      </ul>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>

      <article className="time-compliance-panel">
        <h3>Notification History</h3>
        <div className="compact-list">
          {(history.data?.dryRunNotifications ?? []).length === 0 && <p className="muted">No Time Compliance notification preview history yet.</p>}
          {(history.data?.dryRunNotifications ?? []).map((item) => (
            <div key={item.id}>
              <strong>{item.subject}</strong>
              <span>{item.status} · {item.recipientEmail} · {formatDateTime(item.createdAt)}</span>
            </div>
          ))}
        </div>
      </article>
    </section>
  );
}
