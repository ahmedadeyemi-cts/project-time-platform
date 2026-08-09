import { useCallback, useEffect, useMemo, useState } from 'react';

function readToken(authSession) {
  if (authSession?.sessionToken || authSession?.token || authSession?.accessToken) {
    return authSession.sessionToken || authSession.token || authSession.accessToken;
  }
  try {
    const stored = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    return stored?.sessionToken || stored?.token || stored?.accessToken || '';
  } catch {
    return '';
  }
}

function authHeaders(authSession, json = false) {
  const token = readToken(authSession);
  return {
    ...(json ? { 'Content-Type': 'application/json' } : {}),
    ...(token ? {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token
    } : {})
  };
}

async function requestJson(path, authSession, options = {}) {
  const response = await fetch(path, {
    credentials: 'include',
    cache: 'no-store',
    ...options,
    headers: {
      ...authHeaders(authSession, Boolean(options.body)),
      ...(options.headers || {})
    }
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.message || `Module 065 returned HTTP ${response.status}.`);
  return payload;
}

function toInputValue(value) {
  if (!value) return '';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return '';
  const local = new Date(parsed.getTime() - parsed.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 16);
}

function dateTime(value, fallback = 'Not recorded') {
  if (!value) return fallback;
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

function freshDraft(profile = {}) {
  return {
    applicationName: profile.applicationName || 'Pulse Microsoft Integration',
    environment: profile.environment || 'test',
    secretLabel: profile.secretLabel || 'Microsoft Entra application client secret',
    secretVersion: profile.secretVersion || '',
    expiresAt: toInputValue(profile.expiresAt),
    reminderStartDays: Number(profile.reminderStartDays || 30),
    criticalStartDays: Number(profile.criticalStartDays || 7),
    reminderIntervalHours: Number(profile.reminderIntervalHours || 24),
    reason: ''
  };
}

export default function EntraSecretExpirationGovernancePanel({ authSession }) {
  const [state, setState] = useState({ loading: true, payload: null, error: '' });
  const [draft, setDraft] = useState(freshDraft());
  const [busy, setBusy] = useState('');
  const [notice, setNotice] = useState({ tone: '', message: '' });

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const payload = await requestJson('/api/entra-secret-expiration/profile', authSession);
      setState({ loading: false, payload, error: '' });
      setDraft(freshDraft(payload.profile || payload.fallbackProfile || {}));
    } catch (error) {
      setState((current) => ({
        ...current,
        loading: false,
        error: error?.message || 'Expiration governance could not be loaded.'
      }));
    }
  }, [authSession]);

  useEffect(() => { void load(); }, [load]);

  const profile = state.payload?.profile || null;
  const access = state.payload?.access || {};
  const summary = state.payload?.summary || {};
  const recipients = state.payload?.recipients || [];
  const health = state.payload?.status?.health || 'not_configured';
  const days = state.payload?.status?.daysUntilExpiration;
  const pendingRecipients = useMemo(
    () => recipients.filter((recipient) => !recipient.acknowledgedAt),
    [recipients]
  );

  function update(field, value) {
    setDraft((current) => ({ ...current, [field]: value }));
  }

  async function save(event) {
    event.preventDefault();
    if (!access.canManage) return;
    if (!draft.secretVersion.trim() || !draft.expiresAt || !draft.reason.trim()) {
      setNotice({ tone: 'error', message: 'Secret version, expiration date, and change reason are required.' });
      return;
    }

    setBusy('save');
    setNotice({ tone: '', message: '' });
    try {
      const result = await requestJson('/api/entra-secret-expiration/profile', authSession, {
        method: 'PUT',
        body: JSON.stringify({
          applicationName: draft.applicationName.trim(),
          environment: draft.environment.trim(),
          secretLabel: draft.secretLabel.trim(),
          secretVersion: draft.secretVersion.trim(),
          expiresAt: new Date(draft.expiresAt).toISOString(),
          reminderStartDays: Number(draft.reminderStartDays),
          criticalStartDays: Number(draft.criticalStartDays),
          reminderIntervalHours: Number(draft.reminderIntervalHours),
          reason: draft.reason.trim()
        })
      });
      setNotice({ tone: 'success', message: result.message || 'Expiration governance profile saved.' });
      window.dispatchEvent(new CustomEvent('projectpulse:entra-secret-expiration-changed'));
      await load();
    } catch (error) {
      setNotice({ tone: 'error', message: error?.message || 'Expiration governance profile could not be saved.' });
    } finally {
      setBusy('');
    }
  }

  async function acknowledge() {
    if (!access.canAcknowledge || access.isAcknowledged || !profile) return;
    setBusy('acknowledge');
    setNotice({ tone: '', message: '' });
    try {
      const result = await requestJson('/api/entra-secret-expiration/acknowledge', authSession, {
        method: 'POST',
        body: JSON.stringify({ acknowledgement: 'I acknowledge this client-secret expiration and will coordinate the required rotation before the deadline.' })
      });
      setNotice({ tone: 'success', message: result.message || 'Acknowledgement recorded.' });
      await load();
    } catch (error) {
      setNotice({ tone: 'error', message: error?.message || 'Acknowledgement could not be recorded.' });
    } finally {
      setBusy('');
    }
  }

  async function runReminders() {
    if (!access.canManage || !profile) return;
    setBusy('reminders');
    setNotice({ tone: '', message: '' });
    try {
      const result = await requestJson('/api/entra-secret-expiration/reminders/run', authSession, {
        method: 'POST',
        body: JSON.stringify({ reason: 'Manual reminder evaluation from Module 065.' })
      });
      setNotice({
        tone: result.failedCount > 0 ? 'warning' : 'success',
        message: result.message || `Reminder evaluation completed for ${result.evaluatedCount || 0} recipient(s).`
      });
      await load();
    } catch (error) {
      setNotice({ tone: 'error', message: error?.message || 'Reminder evaluation could not run.' });
    } finally {
      setBusy('');
    }
  }

  return (
    <section className="entra-expiration-card" aria-labelledby="entra-expiration-governance-title">
      <div className="entra-expiration-card__heading">
        <div>
          <p>Non-secret expiration governance</p>
          <h2 id="entra-expiration-governance-title">Client-secret expiration, reminders, and acknowledgement</h2>
          <span>
            Record only the expiration date and non-sensitive version label. The secret value remains outside this browser and is never returned by these APIs.
          </span>
        </div>
        <span className={`entra-expiration-health ${health}`}>{String(health).replaceAll('_', ' ')}</span>
      </div>

      {state.error ? <div className="entra-expiration-notice error" role="alert">{state.error}</div> : null}
      {notice.message ? <div className={`entra-expiration-notice ${notice.tone}`} role="status">{notice.message}</div> : null}

      <div className="entra-expiration-summary">
        <article><span>Days remaining</span><strong>{days ?? '—'}</strong><small>{profile ? dateTime(profile.expiresAt) : 'Profile not saved'}</small></article>
        <article><span>PTC recipients</span><strong>{summary.recipientCount ?? recipients.length}</strong><small>Snapshotted for this version</small></article>
        <article><span>Acknowledged</span><strong>{summary.acknowledgedCount ?? 0}</strong><small>Individual immutable records</small></article>
        <article><span>Still pending</span><strong>{summary.pendingCount ?? pendingRecipients.length}</strong><small>Daily reminders continue</small></article>
      </div>

      {access.canManage ? (
        <form className="entra-expiration-form" onSubmit={save}>
          <label>
            Application name
            <input value={draft.applicationName} onChange={(event) => update('applicationName', event.target.value)} maxLength={200} />
          </label>
          <label>
            Environment
            <select value={draft.environment} onChange={(event) => update('environment', event.target.value)}>
              <option value="test">Test</option>
              <option value="production">Production</option>
              <option value="development">Development</option>
            </select>
          </label>
          <label>
            Non-secret secret label
            <input value={draft.secretLabel} onChange={(event) => update('secretLabel', event.target.value)} maxLength={200} />
          </label>
          <label>
            Secret version identifier
            <input value={draft.secretVersion} onChange={(event) => update('secretVersion', event.target.value)} placeholder="Example: entra-prod-2026-07" maxLength={120} required />
          </label>
          <label>
            Client secret expiration date and time
            <input type="datetime-local" value={draft.expiresAt} onChange={(event) => update('expiresAt', event.target.value)} required />
          </label>
          <label>
            Reminder begins (days before expiration)
            <input type="number" min="7" max="365" value={draft.reminderStartDays} onChange={(event) => update('reminderStartDays', event.target.value)} />
          </label>
          <label>
            Global critical warning begins (days before expiration)
            <input type="number" min="1" max="30" value={draft.criticalStartDays} onChange={(event) => update('criticalStartDays', event.target.value)} />
          </label>
          <label>
            Reminder interval (hours)
            <input type="number" min="1" max="168" value={draft.reminderIntervalHours} onChange={(event) => update('reminderIntervalHours', event.target.value)} />
          </label>
          <label className="entra-expiration-form__reason">
            Change reason
            <textarea value={draft.reason} onChange={(event) => update('reason', event.target.value)} placeholder="Explain the new version or expiration date." maxLength={1000} required />
          </label>
          <div className="entra-expiration-actions">
            <button type="submit" className="primary-action" disabled={busy === 'save'}>{busy === 'save' ? 'Saving…' : 'Save expiration profile'}</button>
            <button type="button" className="secondary-action" onClick={runReminders} disabled={!profile || busy === 'reminders'}>{busy === 'reminders' ? 'Evaluating…' : 'Evaluate reminders now'}</button>
          </div>
        </form>
      ) : null}

      {access.canAcknowledge ? (
        <div className={`entra-expiration-ack ${access.isAcknowledged ? 'acknowledged' : 'pending'}`}>
          <div>
            <strong>{access.isAcknowledged ? 'You acknowledged this expiration profile' : 'Your acknowledgement is required'}</strong>
            <span>
              {access.isAcknowledged
                ? `Recorded ${dateTime(access.acknowledgedAt)}. A critical seven-day warning still remains visible until the version or expiration date is updated.`
                : 'Acknowledgement stops your recurring reminder messages for this profile. It does not dismiss an imminent organization-wide warning.'}
            </span>
          </div>
          {!access.isAcknowledged ? <button type="button" onClick={acknowledge} disabled={busy === 'acknowledge'}>{busy === 'acknowledge' ? 'Recording…' : 'Acknowledge'}</button> : null}
        </div>
      ) : null}

      <div className="entra-expiration-recipient-table">
        <div className="entra-expiration-recipient-table__heading">
          <div><p>Recipient evidence</p><h3>Project Team Coordinator acknowledgement status</h3></div>
          <button type="button" onClick={load} disabled={state.loading}>{state.loading ? 'Refreshing…' : 'Refresh'}</button>
        </div>
        {recipients.length ? (
          <table>
            <thead><tr><th>Recipient</th><th>Acknowledgement</th><th>Last reminder</th><th>Delivery</th></tr></thead>
            <tbody>
              {recipients.map((recipient) => (
                <tr key={`${recipient.userId}-${recipient.email}`}>
                  <td><strong>{recipient.displayName || recipient.email}</strong><small>{recipient.email}</small></td>
                  <td>{recipient.acknowledgedAt ? dateTime(recipient.acknowledgedAt) : <span className="pending-pill">Pending</span>}</td>
                  <td>{dateTime(recipient.lastReminderAt, 'Not sent')}</td>
                  <td>{String(recipient.lastDeliveryStatus || 'not_sent').replaceAll('_', ' ')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : <p className="entra-expiration-empty">No active Project Team Coordinator recipients were found for the current profile.</p>}
      </div>
    </section>
  );
}
