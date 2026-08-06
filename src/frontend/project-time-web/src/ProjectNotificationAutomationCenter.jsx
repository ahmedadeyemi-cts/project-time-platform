import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './project-notification-automation-center.css';
import './projectpulse-module-standard.css';

const workspaceConfiguration = {
  routing: {
    module: '022',
    eyebrow: 'Module 022 · Cost Alert Routing Rules',
    title: 'Project Cost Alert Routing',
    description: 'Configure authoritative project-cost thresholds and automatically derive recipients from each project team.'
  },
  scheduling: {
    module: '023',
    eyebrow: 'Module 023 · Notification Scheduling',
    title: 'Configurable Notification Schedules',
    description: 'Configure weekly, Monday, month-end, escalation, timezone, quiet-hours, and governed delivery boundaries without editing code.'
  },
  delivery: {
    module: '032',
    eyebrow: 'Module 032 · Notification Delivery Monitor',
    title: 'Notification Delivery Monitor',
    description: 'Review dispatches, automatically derived recipients, Module 065 readiness, source failures, delivery attempts, retries, and audit evidence.'
  },
  closeout: {
    module: '041',
    eyebrow: 'Module 041 · Closeout Notification Routing',
    title: 'Governed Closeout Notification Routing',
    description: 'Closeout messages now derive recipients from the server-side project team and route all delivery through Module 065.'
  },
  pm: {
    module: '018',
    eyebrow: 'Module 018 · Project Notification Status',
    title: 'Project Cost Notification Status',
    description: 'See project-cost dispatches, recipient derivation, and delivery status alongside the authoritative Project Manager workspace.'
  }
};

const recipientOptions = [
  ['project_manager', 'Project Manager'],
  ['assigned_engineers', 'Assigned engineer(s)'],
  ['solution_architect', 'Solution Architect'],
  ['account_executive', 'Account Executive'],
  ['project_team_coordinator', 'Project Team Coordinator'],
  ['escalation_manager', 'Optional escalation manager']
];

const timezoneOptions = [
  'America/Chicago',
  'America/New_York',
  'America/Denver',
  'America/Los_Angeles',
  'UTC'
];

function storedSession() {
  try {
    const value = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    return value?.sessionToken ? value : null;
  } catch {
    return null;
  }
}

function headers(extra = {}) {
  const session = storedSession();
  return {
    Accept: 'application/json',
    ...(session?.sessionToken ? {
      Authorization: `Bearer ${session.sessionToken}`,
      'X-ProjectPulse-Session': session.sessionToken
    } : {}),
    ...extra
  };
}

async function requestJson(path, options = {}) {
  const response = await fetch(path, {
    credentials: 'include',
    cache: 'no-store',
    ...options,
    headers: headers(options.headers || {})
  });
  const text = await response.text();
  let body = null;
  try {
    body = text ? JSON.parse(text) : null;
  } catch {
    body = null;
  }
  if (!response.ok) {
    const error = new Error(body?.message || `${path} returned HTTP ${response.status}.`);
    error.source = body?.source || path;
    error.diagnosticCode = body?.diagnosticCode || `HTTP_${response.status}`;
    throw error;
  }
  return body;
}

function label(value) {
  return String(value ?? 'not_recorded')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function dateTime(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Not recorded' : parsed.toLocaleString();
}

function tone(value) {
  const normalized = String(value ?? '').toLowerCase();
  if (['sent', 'healthy', 'ready', 'active', 'completed', 'production_governed'].some((item) => normalized.includes(item))) return 'healthy';
  if (['critical', 'failed', 'over_budget', 'unavailable'].some((item) => normalized.includes(item))) return 'critical';
  if (['warning', 'approaching', 'held', 'queued', 'test_only', 'partial'].some((item) => normalized.includes(item))) return 'warning';
  return 'neutral';
}

function Status({ value }) {
  return <span className={`group4-status ${tone(value)}`}>{label(value)}</span>;
}

function ErrorPanel({ error, onRetry }) {
  if (!error) return null;
  return (
    <div className="group4-error" role="alert">
      <div>
        <strong>Project notification source needs attention.</strong>
        <p>{error.message}</p>
        <small>Source: {error.source || 'Project notification API'} · Diagnostic: {error.diagnosticCode || 'Not reported'}</small>
      </div>
      <button type="button" onClick={onRetry}>Retry source</button>
    </div>
  );
}

function Module065Card({ readiness }) {
  const value = readiness?.readiness || readiness;
  if (!value) return null;
  return (
    <article className="group4-module065-card">
      <div>
        <p className="group4-eyebrow">Module 065 delivery authority</p>
        <h3>{value.configuredProvider ? label(value.configuredProvider) : 'Provider not configured'}</h3>
        <p>{value.message || 'Module 065 readiness has not been reported.'}</p>
      </div>
      <dl>
        <div><dt>Runtime</dt><dd>{label(value.runtimeEnvironment)}</dd></div>
        <div><dt>Configured profile</dt><dd>{label(value.configuredEnvironment)}</dd></div>
        <div><dt>Recipient boundary</dt><dd><Status value={value.recipientBoundary} /></dd></div>
        <div><dt>Delivery mode</dt><dd>{label(value.deliveryMode)}</dd></div>
        <div><dt>Sender</dt><dd>{value.senderMailbox || 'Not recorded'}</dd></div>
        <div><dt>Live delivery</dt><dd><Status value={value.liveDeliveryEnabled ? 'enabled' : 'disabled'} /></dd></div>
      </dl>
      <p className="group4-security-note">Group 4 never accepts or displays mail credentials. Retired Module 067 configuration is not read.</p>
      {!value.liveDeliveryEnabled ? <div className="group4-readiness-actions"><strong>Enable automatic delivery</strong><ol><li>Open Module 065 and select the environment-specific provider profile.</li><li>Validate the sender mailbox and approved recipient boundary.</li><li>Set this schedule to Production governed and run the connection test.</li></ol><a href="#global-mail-configuration">Open mail delivery configuration</a></div> : <div className="group4-readiness-actions is-ready"><strong>Automatic delivery is active</strong><span>Due schedules and eligible queued dispatches send without manual release. Held items remain blocked when a recipient or boundary check fails.</span></div>}
    </article>
  );
}

function RoutingRules({ payload, onSave, saving }) {
  const canManage = Boolean(payload?.access?.canManageRouting);
  const [drafts, setDrafts] = useState({});

  useEffect(() => {
    const next = {};
    (payload?.rules || []).forEach((rule) => {
      next[rule.ruleId] = {
        ...rule,
        thresholdValue: rule.thresholdValue ?? '',
        recipientRoles: rule.recipientRoles || []
      };
    });
    setDrafts(next);
  }, [payload]);

  function update(ruleId, field, value) {
    setDrafts((current) => ({
      ...current,
      [ruleId]: { ...current[ruleId], [field]: value }
    }));
  }

  function toggleRecipient(ruleId, role) {
    setDrafts((current) => {
      const currentRule = current[ruleId];
      const selected = new Set(currentRule.recipientRoles || []);
      if (selected.has(role)) selected.delete(role); else selected.add(role);
      return {
        ...current,
        [ruleId]: { ...currentRule, recipientRoles: [...selected] }
      };
    });
  }

  return (
    <section className="group4-card">
      <div className="group4-section-heading">
        <div>
          <p className="group4-eyebrow">Configurable thresholds</p>
          <h3>Cost routing rules</h3>
          <p>Rules evaluate the authoritative project financial model. Recipient addresses come from the project record and assignments, not from browser input.</p>
        </div>
        <Status value={canManage ? 'editable' : 'view_only'} />
      </div>
      <div className="group4-rule-list">
        {(payload?.rules || []).map((rule) => {
          const draft = drafts[rule.ruleId] || rule;
          return (
            <article className="group4-rule" key={rule.ruleId}>
              <header>
                <div>
                  <strong>{rule.ruleName}</strong>
                  <span>{rule.ruleCode}</span>
                </div>
                <div className="group4-rule-status">
                  <Status value={rule.alertSeverity} />
                  <Status value={draft.enabled ? 'enabled' : 'disabled'} />
                </div>
              </header>
              <div className="group4-rule-grid">
                <label>
                  Metric
                  <select value={draft.metricCode} disabled={!canManage} onChange={(event) => update(rule.ruleId, 'metricCode', event.target.value)}>
                    {(payload?.supportedMetrics || []).map((metric) => <option key={metric} value={metric}>{label(metric)}</option>)}
                  </select>
                </label>
                <label>
                  Comparison
                  <select value={draft.comparisonOperator} disabled={!canManage} onChange={(event) => update(rule.ruleId, 'comparisonOperator', event.target.value)}>
                    {['gte', 'gt', 'lte', 'lt', 'eq', 'state', 'event'].map((operator) => <option key={operator} value={operator}>{label(operator)}</option>)}
                  </select>
                </label>
                <label>
                  Threshold
                  <input type="number" min="0" step="0.01" disabled={!canManage || ['state', 'event'].includes(draft.comparisonOperator)} value={draft.thresholdValue} onChange={(event) => update(rule.ruleId, 'thresholdValue', event.target.value)} />
                </label>
                <label>
                  Unit
                  <select value={draft.thresholdUnit} disabled={!canManage} onChange={(event) => update(rule.ruleId, 'thresholdUnit', event.target.value)}>
                    {['percent', 'currency', 'state', 'event'].map((unit) => <option key={unit} value={unit}>{label(unit)}</option>)}
                  </select>
                </label>
                <label>
                  Severity
                  <select value={draft.alertSeverity} disabled={!canManage} onChange={(event) => update(rule.ruleId, 'alertSeverity', event.target.value)}>
                    {['informational', 'warning', 'high', 'critical'].map((severity) => <option key={severity} value={severity}>{label(severity)}</option>)}
                  </select>
                </label>
                <label>
                  Escalate after
                  <input type="number" min="0" max="43200" disabled={!canManage} value={draft.escalationAfterMinutes ?? ''} onChange={(event) => update(rule.ruleId, 'escalationAfterMinutes', event.target.value)} />
                  <small>minutes</small>
                </label>
                <label>
                  Delivery boundary
                  <select value={draft.deliveryBoundary} disabled={!canManage} onChange={(event) => update(rule.ruleId, 'deliveryBoundary', event.target.value)}>
                    {['test_only', 'production_governed', 'locked'].map((boundary) => <option key={boundary} value={boundary}>{label(boundary)}</option>)}
                  </select>
                </label>
                <label className="group4-toggle">
                  <input type="checkbox" checked={Boolean(draft.enabled)} disabled={!canManage} onChange={(event) => update(rule.ruleId, 'enabled', event.target.checked)} />
                  Enabled
                </label>
              </div>
              <fieldset className="group4-recipient-options" disabled={!canManage}>
                <legend>Automatically derived recipients</legend>
                {recipientOptions.map(([code, text]) => (
                  <label key={code}>
                    <input type="checkbox" checked={(draft.recipientRoles || []).includes(code)} onChange={() => toggleRecipient(rule.ruleId, code)} />
                    {text}
                  </label>
                ))}
              </fieldset>
              <p>{rule.description}</p>
              {canManage ? (
                <footer>
                  <button type="button" className="group4-primary" disabled={saving === rule.ruleId} onClick={() => onSave(rule.ruleId, {
                    ...draft,
                    thresholdValue: draft.thresholdValue === '' ? null : Number(draft.thresholdValue),
                    escalationAfterMinutes: draft.escalationAfterMinutes === '' ? null : Number(draft.escalationAfterMinutes),
                    changeReason: 'Updated by a nontechnical administrator from Module 022.'
                  })}>
                    {saving === rule.ruleId ? 'Saving…' : 'Save governed rule'}
                  </button>
                </footer>
              ) : null}
            </article>
          );
        })}
      </div>
    </section>
  );
}

function Schedules({ payload, onSave, onRunDue, saving, running }) {
  const canManage = Boolean(payload?.access?.canManageSchedules);
  const [drafts, setDrafts] = useState({});

  useEffect(() => {
    const next = {};
    (payload?.schedules || []).forEach((schedule) => {
      next[schedule.scheduleId] = {
        ...schedule,
        localTime: String(schedule.localTime || '06:00').slice(0, 5),
        quietHoursStart: schedule.quietHoursStart ? String(schedule.quietHoursStart).slice(0, 5) : '',
        quietHoursEnd: schedule.quietHoursEnd ? String(schedule.quietHoursEnd).slice(0, 5) : ''
      };
    });
    setDrafts(next);
  }, [payload]);

  function update(scheduleId, field, value) {
    setDrafts((current) => ({
      ...current,
      [scheduleId]: { ...current[scheduleId], [field]: value }
    }));
  }

  return (
    <section className="group4-card">
      <div className="group4-section-heading">
        <div>
          <p className="group4-eyebrow">Nontechnical administration</p>
          <h3>Notification schedules</h3>
          <p>Schedules remain durable, timezone-aware, quiet-hours-aware, and bounded by Module 065 delivery governance.</p>
        </div>
        <div className="group4-heading-actions">
          <Status value={canManage ? 'editable' : 'view_only'} />
          {canManage ? <button type="button" onClick={onRunDue} disabled={running}>{running ? 'Running…' : 'Run due schedules'}</button> : null}
        </div>
      </div>
      <div className="group4-schedule-grid">
        {(payload?.schedules || []).map((schedule) => {
          const draft = drafts[schedule.scheduleId] || schedule;
          return (
            <article className="group4-schedule" key={schedule.scheduleId}>
              <header>
                <div><strong>{schedule.scheduleName}</strong><span>{schedule.scheduleCode}</span></div>
                <Status value={schedule.lastStatus} />
              </header>
              <div className="group4-schedule-fields">
                <label>Type<select disabled={!canManage} value={draft.scheduleType} onChange={(event) => update(schedule.scheduleId, 'scheduleType', event.target.value)}>{['cost_alert_evaluation', 'weekly_reminder', 'monday_reminder', 'month_end_reminder', 'escalation'].map((item) => <option key={item} value={item}>{label(item)}</option>)}</select></label>
                <label>Day of week<select disabled={!canManage} value={draft.dayOfWeek ?? ''} onChange={(event) => update(schedule.scheduleId, 'dayOfWeek', event.target.value === '' ? null : Number(event.target.value))}><option value="">Not applicable</option>{['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'].map((day, index) => <option key={day} value={index}>{day}</option>)}</select></label>
                <label>Local time<input type="time" disabled={!canManage} value={draft.localTime} onChange={(event) => update(schedule.scheduleId, 'localTime', event.target.value)} /></label>
                <label>Timezone<select disabled={!canManage} value={draft.timezoneName} onChange={(event) => update(schedule.scheduleId, 'timezoneName', event.target.value)}>{timezoneOptions.map((timezone) => <option key={timezone} value={timezone}>{timezone}</option>)}</select></label>
                <label>Days before month-end<input type="number" min="0" max="31" disabled={!canManage} value={draft.daysBeforeMonthEnd ?? ''} onChange={(event) => update(schedule.scheduleId, 'daysBeforeMonthEnd', event.target.value === '' ? null : Number(event.target.value))} /></label>
                <label>Escalation timing<input type="number" min="0" max="43200" disabled={!canManage} value={draft.escalationAfterMinutes ?? ''} onChange={(event) => update(schedule.scheduleId, 'escalationAfterMinutes', event.target.value === '' ? null : Number(event.target.value))} /><small>minutes</small></label>
                <label>Quiet hours start<input type="time" disabled={!canManage} value={draft.quietHoursStart || ''} onChange={(event) => update(schedule.scheduleId, 'quietHoursStart', event.target.value)} /></label>
                <label>Quiet hours end<input type="time" disabled={!canManage} value={draft.quietHoursEnd || ''} onChange={(event) => update(schedule.scheduleId, 'quietHoursEnd', event.target.value)} /></label>
                <label>Delivery boundary<select disabled={!canManage} value={draft.deliveryBoundary} onChange={(event) => update(schedule.scheduleId, 'deliveryBoundary', event.target.value)}>{['test_only', 'production_governed', 'locked'].map((boundary) => <option key={boundary} value={boundary}>{label(boundary)}</option>)}</select></label>
                <label className="group4-toggle"><input type="checkbox" disabled={!canManage} checked={Boolean(draft.enabled)} onChange={(event) => update(schedule.scheduleId, 'enabled', event.target.checked)} />Enabled</label>
              </div>
              <dl className="group4-schedule-history">
                <div><dt>Last started</dt><dd>{dateTime(schedule.lastStartedAt)}</dd></div>
                <div><dt>Last completed</dt><dd>{dateTime(schedule.lastCompletedAt)}</dd></div>
                <div><dt>Next run</dt><dd>{dateTime(schedule.nextRunAt)}</dd></div>
              </dl>
              {canManage ? <footer><button type="button" className="group4-primary" disabled={saving === schedule.scheduleId} onClick={() => onSave(schedule.scheduleId, { ...draft, changeReason: 'Updated by a nontechnical administrator from Module 023.' })}>{saving === schedule.scheduleId ? 'Saving…' : 'Save schedule'}</button></footer> : null}
            </article>
          );
        })}
      </div>
    </section>
  );
}

function DeliveryMonitor({ payload, onAction, busy }) {
  const dispatches = payload?.dispatches || [];
  const attempts = payload?.deliveryAttempts || [];
  const canDeliver = Boolean(payload?.access?.canDeliver);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('all');
  const filteredDispatches = useMemo(() => dispatches.filter((dispatch) => {
    if (statusFilter !== 'all' && dispatch.deliveryStatus !== statusFilter) return false;
    const query = search.trim().toLowerCase();
    return !query || [dispatch.subject, dispatch.sourceModule, dispatch.notificationType, dispatch.deliveryStatus, ...(dispatch.recipients || []).flatMap((recipient) => [recipient.email, recipient.displayName])].some((value) => String(value ?? '').toLowerCase().includes(query));
  }).slice(0, 100), [dispatches, search, statusFilter]);
  return (
    <>
      <section className="group4-summary-grid">
        {[
          ['Dispatches', payload?.summary?.dispatchCount ?? dispatches.length, 'Current role scope'],
          ['Queued / held', Number(payload?.summary?.queued || 0) + Number(payload?.summary?.held || 0), 'Awaiting governed boundary'],
          ['Sent', payload?.summary?.sent ?? 0, 'Module 065 delivery evidence'],
          ['Failed', payload?.summary?.failed ?? 0, 'Source-specific retry available'],
          ['Active rules', payload?.summary?.activeRules ?? 0, 'Module 022 configuration'],
          ['Active schedules', payload?.summary?.activeSchedules ?? 0, 'Module 023 configuration']
        ].map(([title, value, detail]) => <article key={title}><span>{title}</span><strong>{value}</strong><small>{detail}</small></article>)}
      </section>
      <section className="group4-card">
        <div className="group4-section-heading"><div><p className="group4-eyebrow">Operational inbox</p><h3>Dispatches and recipients</h3><p>Recipients show the exact project field or assignment used to derive each address.</p></div><div className="group4-monitor-actions"><Status value={canDeliver ? 'delivery_authorized' : 'view_only'} /><a href="#audit-history">Open consolidated Audit History</a></div></div>
        <div className="group4-monitor-filters"><input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search subject, module, recipient…" aria-label="Search notification history" /><select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)} aria-label="Filter notification status"><option value="all">All statuses</option>{['queued', 'held', 'sent', 'failed', 'suppressed'].map((status) => <option key={status} value={status}>{label(status)}</option>)}</select><span>{filteredDispatches.length} shown · latest 100</span></div>
        <div className="group4-table-wrap">
          <table className="group4-table">
            <thead><tr><th>Created</th><th>Notification</th><th>Recipients</th><th>Boundary</th><th>Status</th><th>Sent</th><th>Diagnostic</th><th>Actions</th></tr></thead>
            <tbody>
              {filteredDispatches.map((dispatch) => (
                <tr key={dispatch.dispatchId}>
                  <td>{dateTime(dispatch.createdAt)}</td>
                  <td><strong>{dispatch.subject}</strong><span>Module {dispatch.sourceModule} · {label(dispatch.notificationType)}</span></td>
                  <td><details><summary>{dispatch.recipients?.length || 0} recipient(s)</summary><ul>{(dispatch.recipients || []).map((recipient) => <li key={`${recipient.recipientType}-${recipient.email}`}><strong>{recipient.displayName || recipient.email}</strong><span>{recipient.email} · {label(recipient.role)} · {recipient.derivationSource}</span></li>)}</ul></details></td>
                  <td><Status value={dispatch.deliveryBoundary} /></td>
                  <td><Status value={dispatch.deliveryStatus} /><small>{dispatch.attemptCount || 0} attempt(s)</small></td>
                  <td>{dateTime(dispatch.sentAt)}</td>
                  <td>{dispatch.lastErrorCode ? <><strong>{dispatch.lastErrorCode}</strong><span>{dispatch.lastErrorMessage}</span></> : 'None'}</td>
                  <td>{canDeliver && dispatch.deliveryStatus !== 'sent' ? <div className="group4-row-actions"><button type="button" disabled={busy === dispatch.dispatchId} onClick={() => onAction(dispatch.dispatchId, 'release')}>Release</button>{dispatch.deliveryStatus === 'failed' ? <button type="button" disabled={busy === dispatch.dispatchId} onClick={() => onAction(dispatch.dispatchId, 'retry')}>Retry</button> : null}</div> : '—'}</td>
                </tr>
              ))}
              {filteredDispatches.length === 0 ? <tr><td colSpan="8"><div className="group4-empty">No notification dispatches match the current filters.</div></td></tr> : null}
            </tbody>
          </table>
        </div>
      </section>
      <section className="group4-card">
        <div className="group4-section-heading"><div><p className="group4-eyebrow">Immutable evidence</p><h3>Recent delivery attempts</h3></div></div>
        <div className="group4-attempt-list">
          {attempts.slice(0, 100).map((attempt) => <article key={attempt.attemptId}><div><strong>Attempt {attempt.attemptNumber}</strong><span>{attempt.configuredProvider} · {attempt.recipientBoundary}</span></div><Status value={attempt.attemptStatus} /><div><span>{attempt.diagnosticCode || 'No diagnostic code'}</span><small>{attempt.diagnosticMessage || 'No failure detail'} · {dateTime(attempt.attemptedAt)}</small></div></article>)}
          {attempts.length === 0 ? <div className="group4-empty">No delivery attempts have been recorded.</div> : null}
        </div>
      </section>
    </>
  );
}

function SourceHealth({ sources = [] }) {
  return (
    <section className="group4-card">
      <div className="group4-section-heading"><div><p className="group4-eyebrow">Source health</p><h3>Financial and delivery dependencies</h3><p>One optional source failure does not blank otherwise usable notification data.</p></div></div>
      <div className="group4-source-grid">
        {sources.map((source) => <article key={source.key}><div><strong>{source.name}</strong><span>{source.required ? 'Required' : 'Optional'}</span></div><Status value={source.status} /><p>{source.message}</p><small>{source.diagnosticCode || 'No diagnostic code'} · {source.recordCount ?? 0} record(s)</small></article>)}
      </div>
    </section>
  );
}

export default function ProjectNotificationAutomationCenter({ workspace = 'delivery' }) {
  const configuration = workspaceConfiguration[workspace] || workspaceConfiguration.delivery;
  const [routing, setRouting] = useState({ loading: false, data: null, error: null });
  const [scheduling, setScheduling] = useState({ loading: false, data: null, error: null });
  const [monitor, setMonitor] = useState({ loading: false, data: null, error: null });
  const [readiness, setReadiness] = useState({ loading: false, data: null, error: null });
  const [saving, setSaving] = useState('');
  const [running, setRunning] = useState(false);
  const [actionBusy, setActionBusy] = useState('');
  const [notice, setNotice] = useState('');

  const load = useCallback(async () => {
    const needRouting = workspace === 'routing';
    const needScheduling = workspace === 'scheduling';
    const needMonitor = ['delivery', 'closeout', 'pm'].includes(workspace);
    setNotice('');
    if (needRouting) setRouting((current) => ({ ...current, loading: true, error: null }));
    if (needScheduling) setScheduling((current) => ({ ...current, loading: true, error: null }));
    if (needMonitor) setMonitor((current) => ({ ...current, loading: true, error: null }));
    setReadiness((current) => ({ ...current, loading: true, error: null }));

    const requests = [];
    if (needRouting) requests.push(['routing', requestJson('/api/project-notifications/routing-rules')]);
    if (needScheduling) requests.push(['scheduling', requestJson('/api/project-notifications/schedules')]);
    if (needMonitor) requests.push(['monitor', requestJson('/api/project-notifications/delivery-monitor')]);
    requests.push(['readiness', requestJson('/api/project-notifications/module-065-readiness')]);

    const results = await Promise.allSettled(requests.map(([, promise]) => promise));
    results.forEach((result, index) => {
      const key = requests[index][0];
      const next = result.status === 'fulfilled'
        ? { loading: false, data: result.value, error: null }
        : { loading: false, data: null, error: result.reason };
      if (key === 'routing') setRouting(next);
      if (key === 'scheduling') setScheduling(next);
      if (key === 'monitor') setMonitor(next);
      if (key === 'readiness') setReadiness(next);
    });
  }, [workspace]);

  useEffect(() => { load(); }, [load]);

  async function saveRule(ruleId, payload) {
    setSaving(ruleId);
    try {
      const result = await requestJson(`/api/project-notifications/routing-rules/${ruleId}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
      setNotice(result.message || 'Routing rule saved.');
      await load();
    } catch (error) {
      setRouting((current) => ({ ...current, error }));
    } finally { setSaving(''); }
  }

  async function saveSchedule(scheduleId, payload) {
    setSaving(scheduleId);
    try {
      const result = await requestJson(`/api/project-notifications/schedules/${scheduleId}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
      setNotice(result.message || 'Schedule saved.');
      await load();
    } catch (error) {
      setScheduling((current) => ({ ...current, error }));
    } finally { setSaving(''); }
  }

  async function evaluate() {
    setRunning(true);
    try {
      const result = await requestJson('/api/project-notifications/evaluate', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ releaseEligible: false, evaluationReason: 'Manual evaluation from the Group 4 enterprise workspace.' }) });
      setNotice(`${result.triggeredRuleCount || 0} rule(s) triggered; ${result.dispatchesQueued || 0} dispatch(es) recorded.`);
      await load();
    } catch (error) {
      setRouting((current) => ({ ...current, error }));
    } finally { setRunning(false); }
  }

  async function runDue() {
    setRunning(true);
    try {
      const result = await requestJson('/api/project-notifications/run-due', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
      setNotice(result.message || 'Due schedules processed.');
      await load();
    } catch (error) {
      setScheduling((current) => ({ ...current, error }));
    } finally { setRunning(false); }
  }

  async function dispatchAction(dispatchId, action) {
    setActionBusy(dispatchId);
    try {
      const result = await requestJson(`/api/project-notifications/dispatches/${dispatchId}/${action}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reason: `${label(action)} requested from Module 032 Notification Delivery Monitor.` }) });
      setNotice(result.message || `Dispatch ${action} completed.`);
      await load();
    } catch (error) {
      setMonitor((current) => ({ ...current, error }));
    } finally { setActionBusy(''); }
  }

  const primaryError = routing.error || scheduling.error || monitor.error || readiness.error;
  const module065 = readiness.data?.readiness || routing.data?.module065 || scheduling.data?.module065 || monitor.data?.module065;
  const sourceHealth = monitor.data?.sources || [];
  const loading = routing.loading || scheduling.loading || monitor.loading || readiness.loading;
  const summary = useMemo(() => monitor.data?.summary || {}, [monitor.data]);

  return (
    <section className="group4-notification-center projectpulse-module-standard" data-group4-workspace={workspace} data-module={configuration.module}>
      <header className="group4-hero">
        <div className="group4-brand-lockup">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div><p className="group4-eyebrow">{configuration.eyebrow}</p><h2>{configuration.title}</h2><p>{configuration.description}</p></div>
        </div>
        <div className="group4-hero-actions">
          <Status value={module065?.recipientBoundary || 'not_loaded'} />
          {workspace === 'routing' ? <button type="button" onClick={evaluate} disabled={running}>{running ? 'Evaluating…' : 'Evaluate rules'}</button> : null}
          <button type="button" onClick={load} disabled={loading}>{loading ? 'Refreshing…' : 'Refresh'}</button>
        </div>
      </header>

      <div className="group4-governance-banner">
        <strong>Module 065 is the only mail-delivery authority.</strong>
        <span>Project Manager, engineer, Solution Architect, Account Executive, Project Team Coordinator, and escalation recipients are resolved from authoritative server data. No second mail credential or retired Module 067 configuration is used.</span>
      </div>

      {notice ? <div className="group4-notice" role="status"><span>{notice}</span><button type="button" onClick={() => setNotice('')}>Dismiss</button></div> : null}
      <ErrorPanel error={primaryError} onRetry={load} />
      {loading && !routing.data && !scheduling.data && !monitor.data ? <div className="group4-loading">Loading routing, scheduling, recipient, and delivery evidence…</div> : null}

      <Module065Card readiness={module065} />
      {workspace === 'routing' ? <RoutingRules payload={routing.data} onSave={saveRule} saving={saving} /> : null}
      {workspace === 'scheduling' ? <Schedules payload={scheduling.data} onSave={saveSchedule} onRunDue={runDue} saving={saving} running={running} /> : null}
      {['delivery', 'closeout', 'pm'].includes(workspace) && monitor.data ? <DeliveryMonitor payload={monitor.data} onAction={dispatchAction} busy={actionBusy} /> : null}
      {sourceHealth.length > 0 ? <SourceHealth sources={sourceHealth} /> : null}

      {workspace === 'closeout' ? (
        <section className="group4-card group4-closeout-contract">
          <p className="group4-eyebrow">Module 041 compatibility contract</p>
          <h3>Existing closeout controls remain available below</h3>
          <p>The historical send route now ignores browser-provided recipient lists, derives the authoritative project team on the server, records a durable dispatch, and delegates live delivery exclusively to Module 065. A Test-only or locked boundary records evidence without sending external mail.</p>
        </section>
      ) : null}

      {workspace === 'pm' ? (
        <section className="group4-card group4-pm-summary">
          <p className="group4-eyebrow">Project Manager notification summary</p>
          <h3>{summary.failed || 0} failed · {summary.queued || 0} queued · {summary.sent || 0} sent</h3>
          <p>Open Module 032 for recipient derivation, source diagnostics, release, retry, and immutable delivery evidence.</p>
        </section>
      ) : null}

      <footer className="group4-footer"><span>Group 4 contract 2026-07-28.1</span><span>Modules 018 · 022 · 023 · 032 · 041 · 065</span><span>View-As never transfers mutation or delivery authority</span></footer>
    </section>
  );
}
