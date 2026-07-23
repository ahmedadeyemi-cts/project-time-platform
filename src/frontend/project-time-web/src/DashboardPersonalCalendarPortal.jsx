import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import './dashboard-personal-calendar.css';

const DASHBOARD_ROUTE = 'dashboard';
const HOST_ID = 'dashboard-personal-calendar-host';

function authHeaders() {
  try {
    const session = JSON.parse(
      window.localStorage.getItem('projectPulseAuthSession') || 'null'
    );

    return session?.sessionToken
      ? { 'X-ProjectPulse-Session': session.sessionToken }
      : {};
  } catch {
    return {};
  }
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    cache: 'no-store',
    headers: {
      ...authHeaders(),
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...(options.headers || {}),
      'Cache-Control': 'no-cache',
      Pragma: 'no-cache'
    }
  });

  const raw = await response.text();
  let payload = {};

  try {
    payload = raw ? JSON.parse(raw) : {};
  } catch {
    payload = { message: raw };
  }

  if (!response.ok) {
    const error = new Error(
      payload.message
      || payload.detail
      || `${path} returned HTTP ${response.status}`
    );
    error.status = response.status;
    throw error;
  }

  return payload;
}

function currentRoute() {
  return String(window.location.hash || '#dashboard')
    .replace(/^#/, '')
    .trim()
    || DASHBOARD_ROUTE;
}

function localDateKey(date) {
  return [
    date.getFullYear(),
    String(date.getMonth() + 1).padStart(2, '0'),
    String(date.getDate()).padStart(2, '0')
  ].join('-');
}

function startOfWorkWeek(value = new Date()) {
  const date = new Date(value);
  date.setHours(0, 0, 0, 0);
  const day = date.getDay();
  date.setDate(date.getDate() + (day === 0 ? -6 : 1 - day));
  return date;
}

function startOfApprovalWeek(value = new Date()) {
  const date = new Date(value);
  date.setHours(0, 0, 0, 0);
  date.setDate(date.getDate() - date.getDay());
  return localDateKey(date);
}

function addDays(value, count) {
  const date = new Date(value);
  date.setDate(date.getDate() + count);
  return date;
}

function workingDays(weekStart) {
  return Array.from({ length: 5 }, (_, index) => addDays(weekStart, index));
}

function parseDate(value) {
  const date = value ? new Date(value) : null;
  return date && !Number.isNaN(date.getTime()) ? date : null;
}

function eventOverlapsDay(item, day) {
  const start = parseDate(item?.start);
  const end = parseDate(item?.end);
  if (!start || !end) return false;

  const dayStart = new Date(day);
  dayStart.setHours(0, 0, 0, 0);
  const dayEnd = addDays(dayStart, 1);
  return start < dayEnd && end > dayStart;
}

function timeLabel(value) {
  const date = parseDate(value);
  if (!date) return '';
  return date.toLocaleTimeString(undefined, {
    hour: 'numeric',
    minute: '2-digit'
  });
}

function durationLabel(item) {
  const supplied = Number(item?.durationHours);
  if (Number.isFinite(supplied) && supplied > 0) {
    return `${Number.isInteger(supplied) ? supplied : supplied.toFixed(1)}h`;
  }

  const start = parseDate(item?.start);
  const end = parseDate(item?.end);
  if (!start || !end || end <= start) return '';
  const hours = (end - start) / 3600000;
  return `${Number.isInteger(hours) ? hours : hours.toFixed(1)}h`;
}

function initials(value) {
  const words = String(value || '')
    .trim()
    .split(/\s+/)
    .filter(Boolean);

  if (!words.length) return '?';
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return `${words[0][0] || ''}${words.at(-1)?.[0] || ''}`.toUpperCase();
}

function formatWeekRange(days) {
  const first = days[0];
  const last = days.at(-1);
  if (!first || !last) return 'Current work week';

  if (
    first.getMonth() === last.getMonth()
    && first.getFullYear() === last.getFullYear()
  ) {
    return `${first.toLocaleString(undefined, { month: 'long' })} ${first.getDate()}–${last.getDate()}, ${first.getFullYear()}`;
  }

  return `${first.toLocaleDateString()} – ${last.toLocaleDateString()}`;
}

function locateApprovalMetric() {
  const rows = Array.from(document.querySelectorAll(
    '#role-welcome-dashboard .welcome-attention-card .welcome-metric-list > div'
  ));

  return rows.find((row) => {
    const label = String(row.querySelector('dt')?.textContent || '')
      .replace(/\s+/g, ' ')
      .trim()
      .toLowerCase();
    return label === 'time approvals' || label === 'current week approvals';
  }) || null;
}

async function synchronizeCurrentWeekApprovalCount() {
  const row = locateApprovalMetric();
  if (!row) return;

  const params = new URLSearchParams({
    weekStart: startOfApprovalWeek(),
    includeAll: 'false',
    allDates: 'false',
    search: '',
    dashboardScope: `${Date.now()}`
  });

  try {
    const payload = await api(`/api/manager/approvals?${params.toString()}`);
    const items = Array.isArray(payload?.items) ? payload.items : [];
    const count = items.filter((item) => item?.status === 'submitted').length;
    const label = row.querySelector('dt');
    const value = row.querySelector('dd');

    if (label) label.textContent = 'Current week approvals';
    if (value) value.textContent = String(count);
    row.title = 'Matches the currently selected week when Approval Center opens.';
    row.dataset.dashboardApprovalScope = 'current-week';
  } catch (error) {
    if (error?.status === 401 || error?.status === 403) {
      const label = row.querySelector('dt');
      const value = row.querySelector('dd');
      if (label) label.textContent = 'Current week approvals';
      if (value) value.textContent = '0';
      row.dataset.dashboardApprovalScope = 'not-authorized';
    }
  }
}

function CalendarEvent({ item }) {
  const timeRange = [timeLabel(item.start), timeLabel(item.end)]
    .filter(Boolean)
    .join('–');

  return (
    <div className="dashboard-calendar-event" title={item.subject || 'Calendar event'}>
      <strong>{item.subject || (item.isPrivate ? 'Private appointment' : 'Calendar event')}</strong>
      <span>{[timeRange, durationLabel(item)].filter(Boolean).join(' · ')}</span>
      {item.location ? <small>{item.location}</small> : null}
    </div>
  );
}

function DayColumn({ day, schedule }) {
  const items = (schedule?.scheduleItems || [])
    .filter((item) => eventOverlapsDay(item, day))
    .sort((left, right) => String(left.start).localeCompare(String(right.start)));

  return (
    <article className="dashboard-calendar-day">
      <header>
        <strong>{day.toLocaleDateString(undefined, { weekday: 'short' })}</strong>
        <span>{day.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}</span>
      </header>

      <div className="dashboard-calendar-events">
        {items.length
          ? items.map((item, index) => (
            <CalendarEvent item={item} key={`${item.start}-${item.end}-${index}`} />
          ))
          : <span className="dashboard-calendar-available">Available</span>}
      </div>
    </article>
  );
}

export default function DashboardPersonalCalendarPortal() {
  const [route, setRoute] = useState(currentRoute);
  const [portalHost, setPortalHost] = useState(null);
  const [weekStart, setWeekStart] = useState(() => startOfWorkWeek());
  const [state, setState] = useState({ loading: true, error: '', resource: null, schedule: null });
  const requestId = useRef(0);
  const active = route === DASHBOARD_ROUTE;
  const days = useMemo(() => workingDays(weekStart), [weekStart]);

  useEffect(() => {
    const onHashChange = () => setRoute(currentRoute());
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  useEffect(() => {
    const root = document.getElementById('root');
    if (!root) return undefined;

    let currentHost = null;
    const ensureHost = () => {
      const dashboard = document.querySelector('#role-welcome-dashboard');
      if (!dashboard) {
        if (currentHost?.isConnected) currentHost.remove();
        currentHost = null;
        setPortalHost(null);
        return;
      }

      let host = dashboard.querySelector(`:scope > #${HOST_ID}`);
      if (!host) {
        document.getElementById(HOST_ID)?.remove();
        host = document.createElement('div');
        host.id = HOST_ID;
        dashboard.appendChild(host);
      }

      if (currentHost !== host) {
        currentHost = host;
        setPortalHost(host);
      }
    };

    ensureHost();
    const observer = new MutationObserver(ensureHost);
    observer.observe(root, { childList: true, subtree: true });

    return () => {
      observer.disconnect();
      if (currentHost?.isConnected) currentHost.remove();
    };
  }, []);

  const loadCalendar = useCallback(async () => {
    if (!active) return;

    const currentRequest = ++requestId.current;
    setState((current) => ({ ...current, loading: true, error: '' }));

    try {
      const resourcesPayload = await api(`/api/calendar/resources?dashboardScope=${Date.now()}`);
      const currentUserId = String(resourcesPayload?.currentUserId || '');
      const resources = Array.isArray(resourcesPayload?.resources)
        ? resourcesPayload.resources
        : [];
      const resource = resources.find(
        (item) => String(item.userId) === currentUserId
      );

      if (!currentUserId || !resource) {
        throw new Error('Calendar capacity is not available for this account profile.');
      }

      const start = new Date(weekStart);
      const end = addDays(start, 5);
      const schedulePayload = await api('/api/calendar/schedule', {
        method: 'POST',
        body: JSON.stringify({
          start: start.toISOString(),
          end: end.toISOString(),
          timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC',
          view: 'thisweek',
          intervalMinutes: 15,
          resourceIds: [currentUserId],
          teamName: '',
          departmentName: ''
        })
      });
      const schedule = Array.isArray(schedulePayload?.schedules)
        ? schedulePayload.schedules.find((item) => String(item.userId) === currentUserId)
          || schedulePayload.schedules[0]
          || null
        : null;

      if (currentRequest === requestId.current) {
        setState({ loading: false, error: '', resource, schedule });
      }
    } catch (error) {
      if (currentRequest === requestId.current) {
        setState({
          loading: false,
          error: error instanceof Error ? error.message : 'Unable to load your calendar capacity.',
          resource: null,
          schedule: null
        });
      }
    }
  }, [active, weekStart]);

  useEffect(() => {
    if (!active) return undefined;

    const refreshDashboard = () => {
      void synchronizeCurrentWeekApprovalCount();
      void loadCalendar();
    };

    const timer = window.setTimeout(refreshDashboard, 100);
    window.addEventListener('projectpulse:view-as-changed', refreshDashboard);
    window.addEventListener('projectpulse:approval-queue-changed', refreshDashboard);

    return () => {
      window.clearTimeout(timer);
      window.removeEventListener('projectpulse:view-as-changed', refreshDashboard);
      window.removeEventListener('projectpulse:approval-queue-changed', refreshDashboard);
      requestId.current += 1;
    };
  }, [active, loadCalendar]);

  if (!active || !portalHost) return null;

  const schedule = state.schedule;
  const resource = schedule || state.resource;
  const utilization = Number(schedule?.utilizationPercent || 0);
  const workingHours = Number(schedule?.workingHours || 40);
  const scheduledHours = Number(schedule?.scheduledHours || 0);
  const remainingHours = Number(
    schedule?.remainingHours
    ?? Math.max(0, workingHours - scheduledHours)
  );

  return createPortal(
    <section className="dashboard-personal-calendar" aria-labelledby="dashboard-personal-calendar-title">
      <header className="dashboard-personal-calendar-heading">
        <div>
          <p className="eyebrow">My calendar capacity</p>
          <h2 id="dashboard-personal-calendar-title">{formatWeekRange(days)}</h2>
          <p>Your personal Monday–Friday calendar for the current effective user.</p>
        </div>
        <div className="dashboard-personal-calendar-actions">
          <button type="button" onClick={() => setWeekStart((current) => addDays(current, -7))}>← Previous</button>
          <button type="button" onClick={() => setWeekStart(startOfWorkWeek())}>Current week</button>
          <button type="button" onClick={() => setWeekStart((current) => addDays(current, 7))}>Next →</button>
          <a href="#calendar-capacity">Open capacity center</a>
        </div>
      </header>

      {state.error ? (
        <div className="dashboard-personal-calendar-message error">
          <strong>Calendar unavailable</strong>
          <span>{state.error}</span>
          <button type="button" onClick={loadCalendar}>Retry</button>
        </div>
      ) : null}

      {state.loading ? (
        <div className="dashboard-personal-calendar-message">
          Loading your calendar and capacity…
        </div>
      ) : null}

      {!state.loading && !state.error && resource ? (
        <div className="dashboard-personal-calendar-board">
          <aside className="dashboard-personal-calendar-profile">
            <div className="dashboard-personal-calendar-identity">
              {resource.profilePhotoDataUrl ? (
                <img src={resource.profilePhotoDataUrl} alt={`${resource.displayName} profile`} />
              ) : (
                <span className="dashboard-personal-calendar-avatar">{initials(resource.displayName)}</span>
              )}
              <div>
                <strong>{resource.displayName}</strong>
                <span>{resource.jobTitle || 'Team member'}</span>
                <small>{resource.teamName || resource.departmentName || 'Unassigned'}</small>
              </div>
            </div>

            <div className="dashboard-personal-calendar-utilization">
              <div><strong>{utilization.toFixed(1).replace('.0', '')}%</strong><span> utilized</span></div>
              <progress max="100" value={Math.min(100, utilization)} />
              <small>{scheduledHours.toFixed(1).replace('.0', '')}h scheduled / {workingHours.toFixed(1).replace('.0', '')}h capacity</small>
              <small>{remainingHours.toFixed(1).replace('.0', '')}h remaining</small>
            </div>
          </aside>

          <div className="dashboard-personal-calendar-days">
            {days.map((day) => (
              <DayColumn day={day} schedule={schedule} key={localDateKey(day)} />
            ))}
          </div>
        </div>
      ) : null}
    </section>,
    portalHost
  );
}
