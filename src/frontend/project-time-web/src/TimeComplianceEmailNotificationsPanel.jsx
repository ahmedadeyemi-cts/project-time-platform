import { useCallback, useEffect, useMemo, useState } from 'react';

const AUTH_STORAGE_KEY = 'projectPulseAuthSession';
const VIEW_AS_STORAGE_KEY = 'projectPulseViewAsUser';

function readJsonStorage(key) {
  try {
    const raw = window.localStorage.getItem(key);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function getSessionToken() {
  const session = readJsonStorage(AUTH_STORAGE_KEY);
  const token = session?.sessionToken || session?.token || session?.accessToken;
  return typeof token === 'string' ? token.trim() : '';
}

function getViewAsUserId() {
  const viewAs = readJsonStorage(VIEW_AS_STORAGE_KEY);

  if (typeof viewAs === 'string') return viewAs.trim();

  const userId = viewAs?.userId || viewAs?.id || viewAs?.value;
  return typeof userId === 'string' ? userId.trim() : '';
}

function currentRoute() {
  return (window.location.hash || '').replace(/^#\/?/, '').split(/[/?]/)[0];
}

async function readResponse(response, path) {
  const text = await response.text();

  let payload = {};
  try {
    payload = text ? JSON.parse(text) : {};
  } catch {
    payload = { raw: text };
  }

  if (!response.ok) {
    const message = payload.message || payload.detail || payload.status || `${path} returned HTTP ${response.status}`;
    throw new Error(message);
  }

  return payload;
}

async function fetchJson(path, token, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      'X-ProjectPulse-Session': token,
      ...(options.headers || {})
    },
    credentials: 'same-origin'
  });

  return readResponse(response, path);
}

export default function TimeComplianceEmailNotificationsPanel() {
  const [route, setRoute] = useState(() => currentRoute());
  const [token, setToken] = useState(() => getSessionToken());
  const [viewAsUserId, setViewAsUserId] = useState(() => getViewAsUserId());
  const [summary, setSummary] = useState(null);
  const [recentRuns, setRecentRuns] = useState([]);
  const [schedules, setSchedules] = useState([]);
  const [events, setEvents] = useState([]);
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);

  const visible = route === 'time-compliance' && Boolean(token) && !viewAsUserId;

  const deliveryReadiness = useMemo(
    () => summary?.deliveryReadiness || 'outbox_only',
    [summary]
  );

  const preferredDeliveryMode = useMemo(
    () => summary?.preferredDeliveryMode || (deliveryReadiness === 'ready' ? 'provider' : 'outbox_only'),
    [summary, deliveryReadiness]
  );

  const syncRuntimeState = useCallback(() => {
    setRoute(currentRoute());
    setToken(getSessionToken());
    setViewAsUserId(getViewAsUserId());
  }, []);

  const refreshData = useCallback(async () => {
    if (!visible) {
      setSummary(null);
      setRecentRuns([]);
      setSchedules([]);
      setEvents([]);
      return;
    }

    const summaryPayload = await fetchJson('/api/time-compliance/email-notifications/summary', token);
    const eventsPayload = await fetchJson('/api/time-compliance/email-notifications/events?limit=10', token);

    setSummary(summaryPayload.summary || null);
    setSchedules(Array.isArray(summaryPayload.schedules) ? summaryPayload.schedules : []);
    setRecentRuns(Array.isArray(summaryPayload.recentRuns) ? summaryPayload.recentRuns : []);
    setEvents(Array.isArray(eventsPayload.events) ? eventsPayload.events : []);
  }, [token, visible]);

  useEffect(() => {
    const onHashChange = () => syncRuntimeState();
    const onFocus = () => syncRuntimeState();
    const onStorage = (event) => {
      if (event.key === AUTH_STORAGE_KEY || event.key === VIEW_AS_STORAGE_KEY) {
        syncRuntimeState();
      }
    };

    window.addEventListener('hashchange', onHashChange);
    window.addEventListener('focus', onFocus);
    window.addEventListener('storage', onStorage);

    const interval = window.setInterval(syncRuntimeState, 2000);

    return () => {
      window.removeEventListener('hashchange', onHashChange);
      window.removeEventListener('focus', onFocus);
      window.removeEventListener('storage', onStorage);
      window.clearInterval(interval);
    };
  }, [syncRuntimeState]);

  useEffect(() => {
    let cancelled = false;

    if (!visible) {
      setMessage('');
      setSummary(null);
      setRecentRuns([]);
      setSchedules([]);
      setEvents([]);
      return () => {
        cancelled = true;
      };
    }

    refreshData()
      .then(() => {
        if (!cancelled) setMessage('');
      })
      .catch((error) => {
        if (!cancelled) {
          setMessage(error?.message || 'Unable to load automatic email notification controls.');
        }
      });

    return () => {
      cancelled = true;
    };
  }, [visible, refreshKey, refreshData]);

  async function runNotification(deliveryMode) {
    if (!token || busy) return;

    const confirmed = window.confirm(
      deliveryMode === 'brevo_api' || deliveryMode === 'provider'
        ? 'Send automatic time-compliance email notifications through the shared ProjectPulse email provider now?'
        : deliveryMode === 'sendmail'
          ? 'Send automatic time-compliance email notifications through local sendmail now?'
          : 'Record an outbox-only notification run now? No email will be sent.'
    );

    if (!confirmed) return;

    setBusy(true);
    setMessage('');

    try {
      const result = await fetchJson('/api/time-compliance/email-notifications/send', token, {
        method: 'POST',
        body: JSON.stringify({
          scenario: 'weekly_reminder',
          deliveryMode,
          runType: deliveryMode === 'sendmail' ? 'manual_sendmail' : 'manual_outbox'
        })
      });

      setMessage(result.message || 'Time compliance notification run completed.');
      setRefreshKey((value) => value + 1);
    } catch (error) {
      setMessage(error?.message || 'Unable to run automatic engineer notifications.');
    } finally {
      setBusy(false);
    }
  }

  if (!visible) return null;

  return (
    <section className="time-compliance-email-panel">
      <div className="time-compliance-email-heading">
        <div>
          <span className="time-compliance-email-eyebrow">Automatic Email Notifications</span>
          <h2>Engineer time-compliance email automation</h2>
          <p>
            Runs engineer reminder notifications from the time-compliance preview payload and stores delivery evidence.
          </p>
          {message ? <p className="time-compliance-email-message">{message}</p> : null}
        </div>

        <div className="time-compliance-email-summary">
          <span>Delivery readiness</span>
          <strong>{deliveryReadiness}</strong>
          <small>{summary?.runCount ?? 0} total runs</small>
        </div>
      </div>

      <div className="time-compliance-email-actions">
        <button type="button" disabled={busy} onClick={() => runNotification('outbox_only')}>
          {busy ? 'Running…' : 'Run outbox-only'}
        </button>

        <button
          type="button"
          disabled={busy || deliveryReadiness !== 'ready' || preferredDeliveryMode === 'outbox_only'}
          onClick={() => runNotification(preferredDeliveryMode)}
        >
          {busy ? 'Sending…' : 'Send engineer emails now'}
        </button>
      </div>

      <div className="time-compliance-email-grid">
        <article>
          <span>Queued</span>
          <strong>{summary?.queuedCount ?? 0}</strong>
        </article>
        <article>
          <span>Sent</span>
          <strong>{summary?.sentCount ?? 0}</strong>
        </article>
        <article>
          <span>Failed</span>
          <strong>{summary?.failedCount ?? 0}</strong>
        </article>
        <article>
          <span>Schedules</span>
          <strong>{summary?.activeScheduleCount ?? schedules.length}</strong>
        </article>
      </div>

      <div className="time-compliance-email-lists">
        <div>
          <h3>Schedules</h3>
          {schedules.slice(0, 4).map((schedule) => (
            <p key={schedule.scheduleKey}>
              <strong>{schedule.scheduleName}</strong>
              <span>{schedule.nextRunHint}</span>
            </p>
          ))}
        </div>

        <div>
          <h3>Recent runs</h3>
          {recentRuns.slice(0, 4).map((run) => (
            <p key={run.runId}>
              <strong>{run.scenario}</strong>
              <span>{run.runStatus} · queued {run.queuedCount} · sent {run.sentCount} · failed {run.failedCount}</span>
            </p>
          ))}
          {recentRuns.length === 0 ? <p>No notification runs recorded yet.</p> : null}
        </div>

        <div>
          <h3>Delivery events</h3>
          {events.slice(0, 4).map((event) => (
            <p key={event.deliveryEventId}>
              <strong>{event.recipientEmail}</strong>
              <span>{event.deliveryStatus} · {event.subject}</span>
            </p>
          ))}
          {events.length === 0 ? <p>No delivery events recorded yet.</p> : null}
        </div>
      </div>
    </section>
  );
}
