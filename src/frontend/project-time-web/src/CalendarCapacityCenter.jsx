import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import './calendar-capacity-center.css';

function getHeaders() {
  try {
    const session = JSON.parse(
      localStorage.getItem('projectPulseAuthSession') || 'null'
    );
    const result = session?.sessionToken
      ? { Authorization: `Bearer ${session.sessionToken}` }
      : {};
    const viewAs = localStorage.getItem('projectPulseViewAsUserId');

    if (viewAs) {
      result['X-ProjectPulse-View-As-User'] = viewAs;
    }

    return result;
  } catch {
    return {};
  }
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: {
      ...getHeaders(),
      ...(options.body ? { 'Content-Type': 'application/json' } : {})
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
    throw new Error(
      payload.message
      || payload.detail
      || `${path} returned HTTP ${response.status}`
    );
  }

  return payload;
}

const monthStart = (date) =>
  new Date(date.getFullYear(), date.getMonth(), 1);

const monthEnd = (date) =>
  new Date(date.getFullYear(), date.getMonth() + 1, 1);

const addMonths = (date, count) =>
  new Date(date.getFullYear(), date.getMonth() + count, 1);

const dateKey = (date) =>
  [
    date.getFullYear(),
    String(date.getMonth() + 1).padStart(2, '0'),
    String(date.getDate()).padStart(2, '0')
  ].join('-');

function startOfWorkWeek(date) {
  const result = new Date(date);
  const day = result.getDay();
  result.setDate(result.getDate() + (day === 0 ? -6 : 1 - day));
  result.setHours(0, 0, 0, 0);
  return result;
}

function startOfQuarter(date) {
  const month = Math.floor(date.getMonth() / 3) * 3;
  return new Date(date.getFullYear(), month, 1);
}

function getRange(anchor, view) {
  if (view === 'day') {
    const start = new Date(anchor);
    start.setHours(0, 0, 0, 0);
    const end = new Date(start);
    end.setDate(end.getDate() + 1);
    return { start, end };
  }

  if (view === 'workweek' || view === 'thisweek') {
    const start = startOfWorkWeek(anchor);
    const end = new Date(start);
    end.setDate(end.getDate() + 5);
    return { start, end };
  }

  if (view === 'thisquarter' || view === 'nextquarter') {
    const start = startOfQuarter(anchor);
    const end = addMonths(start, 3);
    return { start, end };
  }

  return {
    start: monthStart(anchor),
    end: monthEnd(anchor)
  };
}

function getWorkingDays(start, end) {
  const days = [];
  const cursor = new Date(start);
  cursor.setHours(0, 0, 0, 0);

  while (cursor < end) {
    if (cursor.getDay() !== 0 && cursor.getDay() !== 6) {
      days.push(new Date(cursor));
    }

    cursor.setDate(cursor.getDate() + 1);
  }

  return days;
}

function getVisibleDays(range, view) {
  if (view === 'day') {
    return [new Date(range.start)];
  }

  return getWorkingDays(range.start, range.end);
}

function getInitials(value) {
  const words = String(value || '')
    .trim()
    .split(/\s+/)
    .filter(Boolean);

  if (!words.length) {
    return '?';
  }

  if (words.length === 1) {
    return words[0].slice(0, 2).toUpperCase();
  }

  return `${words[0][0] || ''}${words.at(-1)?.[0] || ''}`.toUpperCase();
}

function parseDate(value) {
  if (!value) {
    return null;
  }

  const result = new Date(value);
  return Number.isNaN(result.getTime()) ? null : result;
}

function subjectOf(item) {
  const subject = String(item?.subject || '').trim();

  if (subject) {
    return subject;
  }

  if (item?.isPrivate) {
    return 'Private appointment';
  }

  return 'Calendar event';
}

function eventOverlapsDay(item, day) {
  const start = parseDate(item.start);
  const end = parseDate(item.end);

  if (!start || !end) {
    return String(item.start || '').slice(0, 10) === dateKey(day);
  }

  const dayStart = new Date(day);
  dayStart.setHours(0, 0, 0, 0);
  const dayEnd = new Date(dayStart);
  dayEnd.setDate(dayEnd.getDate() + 1);

  return start < dayEnd && end > dayStart;
}

function durationHours(item) {
  const supplied = Number(item?.durationHours);

  if (Number.isFinite(supplied) && supplied > 0) {
    return supplied;
  }

  const start = parseDate(item?.start);
  const end = parseDate(item?.end);

  if (!start || !end || end <= start) {
    return 0;
  }

  return (end - start) / 3600000;
}

function durationLabel(item) {
  const hours = durationHours(item);

  if (!hours) {
    return '';
  }

  return `${Number.isInteger(hours) ? hours : hours.toFixed(1)}h`;
}

function eventTone(item) {
  if (item.isPrivate) {
    return 'private';
  }

  const status = String(item.status || '').toLowerCase();

  if (status === 'oof') {
    return 'oof';
  }

  if (status === 'tentative') {
    return 'tentative';
  }

  if (status === 'workingelsewhere') {
    return 'working-elsewhere';
  }

  const subject = subjectOf(item);
  let hash = 0;

  for (let index = 0; index < subject.length; index += 1) {
    hash = ((hash << 5) - hash + subject.charCodeAt(index)) | 0;
  }

  return `tone-${Math.abs(hash) % 5}`;
}

function formatRange(days, anchor) {
  if (!days.length) {
    return anchor.toLocaleString(undefined, {
      month: 'long',
      year: 'numeric'
    });
  }

  const first = days[0];
  const last = days.at(-1);

  if (days.length === 1) {
    return first.toLocaleDateString(undefined, {
      weekday: 'long',
      month: 'long',
      day: 'numeric',
      year: 'numeric'
    });
  }

  if (
    first.getMonth() === last.getMonth()
    && first.getFullYear() === last.getFullYear()
  ) {
    return `${first.toLocaleString(undefined, {
      month: 'long'
    })} ${first.getDate()}–${last.getDate()}, ${first.getFullYear()}`;
  }

  return `${first.toLocaleDateString()} – ${last.toLocaleDateString()}`;
}

function formatTime(value) {
  const date = parseDate(value);

  if (!date) {
    return '';
  }

  return date.toLocaleTimeString(undefined, {
    hour: 'numeric',
    minute: '2-digit'
  });
}

function formatLongDate(value) {
  const date = value instanceof Date ? value : parseDate(value);

  if (!date) {
    return '';
  }

  return date.toLocaleDateString(undefined, {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    year: 'numeric'
  });
}

function getMonthSegments(start, end) {
  const segments = [];
  let cursor = monthStart(start);

  while (cursor < end) {
    const segmentStart = cursor < start ? new Date(start) : new Date(cursor);
    const naturalEnd = monthEnd(cursor);
    const segmentEnd = naturalEnd > end ? new Date(end) : naturalEnd;

    segments.push({
      key: `${cursor.getFullYear()}-${cursor.getMonth()}`,
      label: cursor.toLocaleDateString(undefined, {
        month: 'long',
        year: 'numeric'
      }),
      start: segmentStart,
      end: segmentEnd
    });

    cursor = naturalEnd;
  }

  return segments;
}

function getMonthWeeks(start, end) {
  const weeks = [];
  let cursor = startOfWorkWeek(start);

  while (cursor < end) {
    const weekStart = new Date(cursor);
    const days = [];

    for (let offset = 0; offset < 5; offset += 1) {
      const day = new Date(weekStart);
      day.setDate(day.getDate() + offset);

      days.push(day >= start && day < end ? day : null);
    }

    weeks.push({
      key: dateKey(weekStart),
      label: `Week of ${weekStart.toLocaleDateString(undefined, {
        month: 'short',
        day: 'numeric'
      })}`,
      days
    });

    cursor.setDate(cursor.getDate() + 7);
  }

  return weeks;
}

function ResourceAvatar({ resource }) {
  return (
    <div className="calendar-resource-avatar">
      {resource.profilePhotoDataUrl ? (
        <img
          src={resource.profilePhotoDataUrl}
          alt={`${resource.displayName} profile`}
        />
      ) : (
        <span>{getInitials(resource.displayName)}</span>
      )}
    </div>
  );
}

function ResourceProfile({ resource, compact = false }) {
  const utilization =
    resource.utilizationPercent
    ?? resource.capacityPercent
    ?? 0;
  const totalCapacity =
    resource.workingHours
    ?? (
      Number(resource.scheduledHours || 0)
      + Number(resource.availableHours || 0)
    );
  const remaining =
    resource.remainingHours
    ?? resource.availableHours
    ?? Math.max(
      0,
      totalCapacity - Number(resource.scheduledHours || 0)
    );

  return (
    <article
      className={[
        'calendar-resource-profile',
        compact ? 'calendar-resource-profile-compact' : ''
      ].filter(Boolean).join(' ')}
    >
      <div className="calendar-resource-identity">
        <ResourceAvatar resource={resource} />

        <div>
          <strong>{resource.displayName}</strong>
          <span>{resource.jobTitle || 'Engineer'}</span>
          <small>{resource.teamName}</small>
        </div>
      </div>

      <div className="calendar-resource-utilization">
        <div>
          <strong>{utilization}%</strong>
          <span>utilized</span>
        </div>

        <progress
          max="100"
          value={Math.min(100, Number(utilization) || 0)}
          aria-label={`${resource.displayName} utilization`}
        />

        <small>
          {resource.scheduledHours || 0}h scheduled
          {' / '}
          {totalCapacity || 0}h capacity
        </small>
        <small>{remaining || 0}h remaining</small>
      </div>
    </article>
  );
}

function CalendarEvent({ item }) {
  const timeRange = [
    formatTime(item.start),
    formatTime(item.end)
  ].filter(Boolean).join('–');

  return (
    <div
      className={`calendar-event-card ${eventTone(item)}`}
      title={`${subjectOf(item)}${timeRange ? ` · ${timeRange}` : ''}`}
    >
      <strong>{subjectOf(item)}</strong>
      <span>
        {[timeRange, durationLabel(item)]
          .filter(Boolean)
          .join(' · ')}
      </span>
      {item.location ? <small>{item.location}</small> : null}
    </div>
  );
}

function DayCell({ resource, day, period = false }) {
  if (!day) {
    return <div className="calendar-period-day-cell calendar-period-day-empty" />;
  }

  const items = (resource.scheduleItems || [])
    .filter((item) => eventOverlapsDay(item, day))
    .sort((left, right) =>
      String(left.start).localeCompare(String(right.start))
    );

  return (
    <div
      className={
        period
          ? 'calendar-period-day-cell'
          : 'calendar-resource-day-cell'
      }
    >
      <div className="calendar-day-cell-heading">
        <strong>
          {day.toLocaleDateString(undefined, {
            weekday: 'short'
          })}
        </strong>
        <span>
          {day.toLocaleDateString(undefined, {
            month: 'short',
            day: 'numeric'
          })}
        </span>
      </div>

      {items.length ? (
        items.map((item, index) => (
          <CalendarEvent
            item={item}
            key={`${item.start}-${item.end}-${index}`}
          />
        ))
      ) : (
        <span className="calendar-resource-available">
          Available
        </span>
      )}
    </div>
  );
}

function PeriodCapacityBoard({ scheduleRows, range }) {
  const monthSegments = getMonthSegments(range.start, range.end);

  return (
    <div className="calendar-period-board">
      {monthSegments.map((month) => {
        const weeks = getMonthWeeks(month.start, month.end);

        return (
          <section className="calendar-period-month" key={month.key}>
            <header className="calendar-period-month-heading">
              <h3>{month.label}</h3>
              <span>
                {getWorkingDays(month.start, month.end).length} workdays
              </span>
            </header>

            {scheduleRows.map((resource) => (
              <article
                className="calendar-period-resource"
                key={`${month.key}-${resource.email}`}
              >
                <ResourceProfile resource={resource} compact />

                <div className="calendar-period-weeks">
                  {weeks.map((week) => (
                    <section
                      className="calendar-period-week"
                      key={`${resource.email}-${month.key}-${week.key}`}
                    >
                      <div className="calendar-period-week-label">
                        {week.label}
                      </div>

                      <div className="calendar-period-week-grid">
                        {week.days.map((day, index) => (
                          <DayCell
                            resource={resource}
                            day={day}
                            period
                            key={
                              day
                                ? `${resource.email}-${dateKey(day)}`
                                : `${resource.email}-${week.key}-blank-${index}`
                            }
                          />
                        ))}
                      </div>
                    </section>
                  ))}
                </div>
              </article>
            ))}
          </section>
        );
      })}
    </div>
  );
}

function buildAgendaGroups(scheduleRows) {
  const byDate = new Map();

  scheduleRows.forEach((resource) => {
    (resource.scheduleItems || []).forEach((item) => {
      const start = parseDate(item.start);

      if (!start) {
        return;
      }

      const dayKey = dateKey(start);

      if (!byDate.has(dayKey)) {
        byDate.set(dayKey, {
          date: start,
          resources: new Map()
        });
      }

      const day = byDate.get(dayKey);
      const resourceKey = resource.email || resource.userId;

      if (!day.resources.has(resourceKey)) {
        day.resources.set(resourceKey, {
          resource,
          items: []
        });
      }

      day.resources.get(resourceKey).items.push(item);
    });
  });

  return Array.from(byDate.values())
    .sort((left, right) => left.date - right.date)
    .map((day) => {
      const resources = Array.from(day.resources.values())
        .map((entry) => ({
          ...entry,
          items: entry.items.sort((left, right) =>
            String(left.start).localeCompare(String(right.start))
          ),
          hours: entry.items.reduce(
            (total, item) => total + durationHours(item),
            0
          )
        }))
        .sort((left, right) =>
          String(left.resource.displayName)
            .localeCompare(String(right.resource.displayName))
        );

      return {
        ...day,
        resources,
        hours: resources.reduce(
          (total, resource) => total + resource.hours,
          0
        )
      };
    });
}

function AgendaBoard({ scheduleRows }) {
  const days = buildAgendaGroups(scheduleRows);

  if (!days.length) {
    return (
      <p className="calendar-empty-state">
        No calendar events were returned for this range.
      </p>
    );
  }

  return (
    <div className="calendar-agenda-days">
      {days.map((day) => (
        <section className="calendar-agenda-day" key={dateKey(day.date)}>
          <header className="calendar-agenda-day-heading">
            <div>
              <h3>{formatLongDate(day.date)}</h3>
              <span>
                {day.resources.length} engineer
                {day.resources.length === 1 ? '' : 's'}
              </span>
            </div>

            <strong>
              {day.hours.toFixed(1).replace('.0', '')}h scheduled
            </strong>
          </header>

          <div className="calendar-agenda-engineers">
            {day.resources.map((entry) => (
              <article
                className="calendar-agenda-engineer"
                key={`${dateKey(day.date)}-${entry.resource.email}`}
              >
                <header>
                  <ResourceAvatar resource={entry.resource} />

                  <div>
                    <strong>{entry.resource.displayName}</strong>
                    <span>
                      {entry.resource.jobTitle || 'Engineer'}
                      {' · '}
                      {entry.resource.teamName}
                    </span>
                  </div>

                  <small>
                    {entry.hours.toFixed(1).replace('.0', '')}h
                  </small>
                </header>

                <div className="calendar-agenda-events">
                  {entry.items.map((item, index) => (
                    <div
                      className={`calendar-agenda-event ${eventTone(item)}`}
                      key={`${item.start}-${item.end}-${index}`}
                    >
                      <time>
                        {formatTime(item.start)}–{formatTime(item.end)}
                      </time>

                      <div>
                        <strong>{subjectOf(item)}</strong>
                        <small>
                          {[durationLabel(item), item.location]
                            .filter(Boolean)
                            .join(' · ')}
                        </small>
                      </div>
                    </div>
                  ))}
                </div>
              </article>
            ))}
          </div>
        </section>
      ))}
    </div>
  );
}

export default function CalendarCapacityCenter() {
  useEffect(() => {
    const hidden = new Map();

    const suppressUnrelatedDashboardContent = () => {
      const headings = Array.from(
        document.querySelectorAll('h1, h2, h3, [role="heading"]')
      );

      headings.forEach((heading) => {
        const label = String(heading.textContent || '')
          .replace(/\s+/g, ' ')
          .trim()
          .toLowerCase();

        if (!label.includes('operational command center for time')) {
          return;
        }

        const container =
          heading.closest('section')
          || heading.closest('article')
          || heading.closest('main > div')
          || heading.parentElement;

        if (!container) {
          return;
        }

        if (!hidden.has(container)) {
          hidden.set(container, container.style.display);
        }

        container.style.display = 'none';
        container.setAttribute(
          'data-module-057-hidden-dashboard-content',
          'true'
        );
      });
    };

    suppressUnrelatedDashboardContent();

    const frame = window.requestAnimationFrame(
      suppressUnrelatedDashboardContent
    );
    const timer = window.setTimeout(
      suppressUnrelatedDashboardContent,
      250
    );

    return () => {
      window.cancelAnimationFrame(frame);
      window.clearTimeout(timer);

      hidden.forEach((previousDisplay, element) => {
        element.style.display = previousDisplay;
        element.removeAttribute(
          'data-module-057-hidden-dashboard-content'
        );
      });
    };
  }, []);

  const [resources, setResources] = useState([]);
  const [teams, setTeams] = useState([]);
  const [departments, setDepartments] = useState([]);
  const [scope, setScope] = useState('individual');
  const [userId, setUserId] = useState('');
  const [userSearch, setUserSearch] = useState('');
  const [team, setTeam] = useState('');
  const [department, setDepartment] = useState('');
  const [view, setView] = useState('day');
  const [anchor, setAnchor] = useState(() => new Date());
  const [schedule, setSchedule] = useState(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [calendarReady, setCalendarReady] = useState(false);
  const requestSequence = useRef(0);

  useEffect(() => {
    api('/api/calendar/resources')
      .then((resourcePayload) => {
        const loadedResources = resourcePayload.resources || [];
        const currentUserId = String(resourcePayload.currentUserId || '');
        const currentUser = loadedResources.find(
          (resource) => String(resource.userId) === currentUserId
        );

        setResources(loadedResources);
        setTeams(resourcePayload.teams || []);
        setDepartments(resourcePayload.departments || []);
        setUserId(
          currentUser?.userId
          || loadedResources[0]?.userId
          || ''
        );
      })
      .catch((loadError) => setError(loadError.message))
      .finally(() => {
        setCalendarReady(true);
        setLoading(false);
      });
  }, []);

  const filteredResources = useMemo(() => {
    const query = userSearch.trim().toLowerCase();

    if (!query) {
      return resources;
    }

    return resources.filter((resource) =>
      [
        resource.displayName,
        resource.email,
        resource.jobTitle,
        resource.teamName,
        resource.departmentName
      ]
        .filter(Boolean)
        .some((value) =>
          String(value).toLowerCase().includes(query)
        )
    );
  }, [resources, userSearch]);

  const range = useMemo(
    () => getRange(anchor, view),
    [anchor, view]
  );

  const visibleDays = useMemo(
    () => getVisibleDays(range, view),
    [range, view]
  );

  const periodView = [
    'month',
    'thismonth',
    'nextmonth',
    'thisquarter',
    'nextquarter'
  ].includes(view);

  const hasSelection = useMemo(() => {
    if (scope === 'individual') {
      return Boolean(userId);
    }

    if (scope === 'team') {
      return Boolean(team);
    }

    return scope === 'department'
      ? Boolean(department)
      : false;
  }, [scope, userId, team, department]);

  const load = useCallback(async () => {
    if (!calendarReady || !hasSelection) {
      return;
    }

    const requestId = ++requestSequence.current;

    setLoading(true);
    setError('');

    try {
      const nextSchedule = await api('/api/calendar/schedule', {
        method: 'POST',
        body: JSON.stringify({
          start: range.start.toISOString(),
          end: range.end.toISOString(),
          timeZone:
            Intl.DateTimeFormat().resolvedOptions().timeZone
            || 'UTC',
          view,
          intervalMinutes:
            view === 'day'
            || view === 'workweek'
            || view === 'thisweek'
              ? 15
              : 30,
          resourceIds:
            scope === 'individual' && userId
              ? [userId]
              : [],
          teamName: scope === 'team' ? team : '',
          departmentName:
            scope === 'department'
              ? department
              : ''
        })
      });

      if (requestId === requestSequence.current) {
        setSchedule(nextSchedule);
      }
    } catch (loadError) {
      if (requestId === requestSequence.current) {
        setSchedule(null);
        setError(
          loadError instanceof Error
            ? loadError.message
            : 'Unable to load the calendar.'
        );
      }
    } finally {
      if (requestId === requestSequence.current) {
        setLoading(false);
      }
    }
  }, [
    calendarReady,
    department,
    hasSelection,
    range,
    scope,
    team,
    userId,
    view
  ]);

  useEffect(() => {
    if (!calendarReady || !hasSelection) {
      requestSequence.current += 1;
      setSchedule(null);
      setError('');
      return undefined;
    }

    const timer = window.setTimeout(() => {
      void load();
    }, 150);

    return () => {
      window.clearTimeout(timer);
      requestSequence.current += 1;
    };
  }, [calendarReady, hasSelection, load]);

  function selectView(nextView) {
    const today = new Date();

    setView(nextView);

    if (nextView === 'day' || nextView === 'thisweek') {
      setAnchor(today);
    } else if (nextView === 'thismonth') {
      setAnchor(monthStart(today));
    } else if (nextView === 'nextmonth') {
      setAnchor(addMonths(today, 1));
    } else if (nextView === 'thisquarter') {
      setAnchor(startOfQuarter(today));
    } else if (nextView === 'nextquarter') {
      setAnchor(addMonths(startOfQuarter(today), 3));
    }
  }

  function move(direction) {
    const next = new Date(anchor);

    if (view === 'day') {
      next.setDate(next.getDate() + direction);
    } else if (view === 'workweek' || view === 'thisweek') {
      next.setDate(next.getDate() + (7 * direction));
    } else if (view === 'thisquarter' || view === 'nextquarter') {
      next.setMonth(next.getMonth() + (3 * direction));
    } else {
      next.setMonth(next.getMonth() + direction);
    }

    setAnchor(next);
  }

  function goToday() {
    setView('day');
    setAnchor(new Date());
  }

  const scheduleRows = schedule?.schedules || [];

  return (
    <div
      className="calendar-capacity-center"
      data-projectpulse-module="057"
    >
      <header className="calendar-capacity-hero">
        <div>
          <p className="eyebrow">MODULE 057</p>
          <h1>Resource & Team Calendar Capacity</h1>
          <p>
            Review real Outlook work, Microsoft profile photos,
            engineer utilization, and remaining capacity.
          </p>
        </div>
      </header>

      <section className="calendar-capacity-controls">
        <div className="calendar-control-grid">
          <label className="calendar-control-field">
            <span>Scope</span>
            <select
              value={scope}
              onChange={(event) => {
                const nextScope = event.target.value;
                setScope(nextScope);

                if (nextScope !== 'team') {
                  setTeam('');
                }

                if (nextScope !== 'department') {
                  setDepartment('');
                }
              }}
            >
              <option value="individual">Individual user</option>
              <option value="team">Team</option>
              <option value="department">Department</option>
            </select>
          </label>

          {scope === 'individual' ? (
            <div className="calendar-control-field calendar-engineer-field">
              <span>User</span>

              <div className="calendar-engineer-picker">
                <input
                  type="search"
                  value={userSearch}
                  onChange={(event) => setUserSearch(event.target.value)}
                  placeholder="Search users"
                  aria-label="Search users"
                />

                <select
                  value={userId}
                  onChange={(event) => setUserId(event.target.value)}
                >
                  {filteredResources.map((resource) => (
                    <option key={resource.userId} value={resource.userId}>
                      {resource.displayName}
                      {' — '}
                      {resource.jobTitle || 'Engineer'}
                      {' — '}
                      {resource.teamName}
                    </option>
                  ))}
                </select>
              </div>

              <small>{filteredResources.length} eligible users</small>
            </div>
          ) : null}

          {scope === 'team' ? (
            <label className="calendar-control-field calendar-resource-field">
              <span>Team</span>
              <select
                value={team}
                onChange={(event) => setTeam(event.target.value)}
              >
                <option value="">Select a team</option>
                {teams.map((item) => (
                  <option key={item.teamName} value={item.teamName}>
                    {item.teamName} ({item.resourceCount})
                  </option>
                ))}
              </select>
            </label>
          ) : null}

          {scope === 'department' ? (
            <label className="calendar-control-field calendar-resource-field">
              <span>Department</span>
              <select
                value={department}
                onChange={(event) => setDepartment(event.target.value)}
              >
                <option value="">Select a department</option>
                {departments.map((item) => (
                  <option
                    key={item.departmentName}
                    value={item.departmentName}
                  >
                    {item.departmentName} ({item.resourceCount})
                  </option>
                ))}
              </select>
            </label>
          ) : null}

          <label className="calendar-control-field">
            <span>View</span>
            <select
              value={view}
              onChange={(event) => selectView(event.target.value)}
            >
              <option value="day">Day</option>
              <option value="workweek">Work week</option>
              <option value="thisweek">This week</option>
              <option value="month">Month capacity</option>
              <option value="thismonth">This month</option>
              <option value="nextmonth">Next month</option>
              <option value="thisquarter">This quarter</option>
              <option value="nextquarter">Next quarter</option>
              <option value="agenda">Agenda</option>
            </select>
          </label>

          <label className="calendar-control-field">
            <span>Month / year</span>
            <input
              type="month"
              value={`${anchor.getFullYear()}-${String(
                anchor.getMonth() + 1
              ).padStart(2, '0')}`}
              onChange={(event) => {
                const [year, month] = event.target.value
                  .split('-')
                  .map(Number);

                setAnchor(new Date(year, month - 1, 1));
              }}
            />
          </label>
        </div>

        <div className="calendar-capacity-buttons">
          <div className="calendar-navigation-buttons">
            <button type="button" onClick={() => move(-1)}>
              Previous
            </button>
            <button type="button" onClick={goToday}>
              Today
            </button>
            <button type="button" onClick={() => move(1)}>
              Next
            </button>
          </div>

          <div className="calendar-range-summary">
            <strong>{formatRange(visibleDays, anchor)}</strong>
            <span>
              Monday–Friday · 8 hours/day · 40 hours/week
            </span>
          </div>

          <button
            type="button"
            className="primary-action calendar-load-button"
            onClick={() => void load()}
            disabled={loading || !calendarReady || !hasSelection}
          >
            {loading ? 'Loading…' : 'Refresh calendar'}
          </button>
        </div>
      </section>

      {error ? (
        <div className="calendar-capacity-error">{error}</div>
      ) : null}

      {view === 'agenda' ? (
        <section className="calendar-agenda">
          <div className="calendar-board-heading">
            <div>
              <p className="eyebrow">Calendar agenda</p>
              <h2>{formatRange(visibleDays, anchor)}</h2>
            </div>

            <span>
              {scheduleRows.length} engineer
              {scheduleRows.length === 1 ? '' : 's'}
            </span>
          </div>

          <AgendaBoard scheduleRows={scheduleRows} />
        </section>
      ) : (
        <section className="calendar-resource-board">
          <div className="calendar-board-heading">
            <div>
              <p className="eyebrow">
                {periodView ? 'Period capacity' : 'Calendar capacity'}
              </p>
              <h2>{formatRange(visibleDays, anchor)}</h2>
            </div>

            <span>
              {scheduleRows.length} engineer
              {scheduleRows.length === 1 ? '' : 's'}
              {' · '}
              {visibleDays.length} day
              {visibleDays.length === 1 ? '' : 's'}
            </span>
          </div>

          {periodView ? (
            <PeriodCapacityBoard
              scheduleRows={scheduleRows}
              range={range}
            />
          ) : (
            <div className="calendar-resource-board-scroll">
              <div
                className="calendar-resource-board-grid"
                style={{
                  '--calendar-day-count': Math.max(visibleDays.length, 1)
                }}
              >
                <div className="calendar-resource-corner">
                  <strong>Engineer</strong>
                  <span>Utilization and capacity</span>
                </div>

                {visibleDays.map((day) => (
                  <div
                    className="calendar-resource-date-header"
                    key={`header-${dateKey(day)}`}
                  >
                    <strong>
                      {day.toLocaleString(undefined, { weekday: 'short' })}
                    </strong>
                    <span>
                      {day.toLocaleString(undefined, {
                        month: 'short',
                        day: 'numeric'
                      })}
                    </span>
                  </div>
                ))}

                {scheduleRows.map((resource) => (
                  <div
                    className="calendar-resource-row"
                    key={resource.email}
                  >
                    <ResourceProfile resource={resource} />

                    {visibleDays.map((day) => (
                      <DayCell
                        resource={resource}
                        day={day}
                        key={`${resource.email}-${dateKey(day)}`}
                      />
                    ))}
                  </div>
                ))}
              </div>
            </div>
          )}

          {!loading && scheduleRows.length === 0 ? (
            <p className="calendar-empty-state">
              No engineer calendar data was returned for this selection.
            </p>
          ) : null}
        </section>
      )}

      <footer className="calendar-capacity-privacy">
        <strong>Calendar privacy:</strong>
        {' '}
        Outlook titles appear when Microsoft Graph supplies them.
        Private events display as “Private appointment.” Events whose
        details are not shared display as “Calendar event.”
      </footer>
    </div>
  );
}
