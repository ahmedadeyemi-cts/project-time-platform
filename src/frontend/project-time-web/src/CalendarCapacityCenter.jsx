import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import './calendar-capacity-center.css';

function headers() {
  try {
    const s = JSON.parse(localStorage.getItem('projectPulseAuthSession') || 'null');
    const h = s?.sessionToken ? { Authorization: `Bearer ${s.sessionToken}` } : {};
    const viewAs = localStorage.getItem('projectPulseViewAsUserId');
    if (viewAs) h['X-ProjectPulse-View-As-User'] = viewAs;
    return h;
  } catch { return {}; }
}

async function api(path, options = {}) {
  const response = await fetch(path, { ...options, headers: { ...headers(), ...(options.body ? { 'Content-Type': 'application/json' } : {}) } });
  const raw = await response.text();
  let payload = {};
  try { payload = raw ? JSON.parse(raw) : {}; } catch { payload = { message: raw }; }
  if (!response.ok) throw new Error(payload.message || payload.detail || `${path} returned HTTP ${response.status}`);
  return payload;
}

const monthStart = (d) => new Date(d.getFullYear(), d.getMonth(), 1);
const monthEnd = (d) => new Date(d.getFullYear(), d.getMonth() + 1, 1);
const keyOf = (d) => `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;

function monthCells(anchor) {
  const first = monthStart(anchor);
  const grid = new Date(first);
  grid.setDate(first.getDate() - first.getDay());
  return Array.from({ length: 42 }, (_, i) => { const d = new Date(grid); d.setDate(grid.getDate() + i); return d; });
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
          heading.closest('section') ||
          heading.closest('article') ||
          heading.closest('main > div') ||
          heading.parentElement;

        if (!container) return;

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
  const [config, setConfig] = useState(null);
  const [resources, setResources] = useState([]);
  const [teams, setTeams] = useState([]);
  const [departments, setDepartments] = useState([]);
  const [scope, setScope] = useState('individual');
  const [userId, setUserId] = useState('');
  const [userSearch, setUserSearch] = useState('');
  const [team, setTeam] = useState('');
  const [department, setDepartment] = useState('');
  const [view, setView] = useState('month');
  const [anchor, setAnchor] = useState(monthStart(new Date()));
  const [schedule, setSchedule] = useState(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [calendarReady, setCalendarReady] = useState(false);
  const scheduleRequestId = useRef(0);

  useEffect(() => {
    Promise.all([api('/api/calendar/configuration'), api('/api/calendar/resources')])
      .then(([c, r]) => {
        setConfig(c);
        setResources(r.resources || []);
        setTeams(r.teams || []);
        setDepartments(r.departments || []);
        setUserId(r.resources?.[0]?.userId || '');
      })
      .catch((e) => setError(e.message))
      .finally(() => {
        setCalendarReady(true);
        setLoading(false);
      });
  }, []);

  const filteredResources = useMemo(() => {
    const query = userSearch.trim().toLowerCase();
    if (!query) return resources;

    return resources.filter((resource) =>
      [
        resource.displayName,
        resource.email,
        resource.teamName,
        resource.departmentName
      ]
        .filter(Boolean)
        .some((value) => String(value).toLowerCase().includes(query))
    );
  }, [resources, userSearch]);

  const range = useMemo(() => {
    if (view === 'day') { const s = new Date(anchor); s.setHours(0,0,0,0); const e = new Date(s); e.setDate(e.getDate()+1); return { s, e }; }
    if (['week','workweek','timeline'].includes(view)) { const s = new Date(anchor); s.setDate(s.getDate()-s.getDay()+(view==='workweek'?1:0)); s.setHours(0,0,0,0); const e = new Date(s); e.setDate(e.getDate()+(view==='workweek'?5:7)); return { s, e }; }
    return { s: monthStart(anchor), e: monthEnd(anchor) };
  }, [anchor, view]);

  const hasSelection = useMemo(() => {
    if (scope === 'individual') return Boolean(userId);
    if (scope === 'team') return Boolean(team);
    if (scope === 'department') return Boolean(department);
    return false;
  }, [scope, userId, team, department]);

  const load = useCallback(async () => {
    if (!calendarReady || !hasSelection) return;

    const requestId = ++scheduleRequestId.current;

    setLoading(true);
    setError('');

    try {
      const nextSchedule = await api('/api/calendar/schedule', {
        method: 'POST',
        body: JSON.stringify({
          start: range.s.toISOString(),
          end: range.e.toISOString(),
          timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC',
          view,
          intervalMinutes: view === 'month' ? 60 : 30,
          resourceIds: scope === 'individual' && userId ? [userId] : [],
          teamName: scope === 'team' ? team : '',
          departmentName: scope === 'department' ? department : ''
        })
      });

      if (requestId === scheduleRequestId.current) {
        setSchedule(nextSchedule);
      }
    } catch (e) {
      if (requestId === scheduleRequestId.current) {
        setSchedule(null);
        setError(
          e instanceof Error ? e.message : 'Unable to load the calendar.'
        );
      }
    } finally {
      if (requestId === scheduleRequestId.current) {
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
      scheduleRequestId.current += 1;
      setSchedule(null);
      setError('');
      return undefined;
    }

    const timer = window.setTimeout(() => {
      void load();
    }, 150);

    return () => {
      window.clearTimeout(timer);
      scheduleRequestId.current += 1;
    };
  }, [calendarReady, hasSelection, load]);

  function move(n) { const d = new Date(anchor); if (view==='day') d.setDate(d.getDate()+n); else if (['week','workweek','timeline'].includes(view)) d.setDate(d.getDate()+7*n); else d.setMonth(d.getMonth()+n); setAnchor(d); }

  const eventsByDate = useMemo(() => {
    const map = new Map();
    for (const s of schedule?.schedules || []) for (const item of s.scheduleItems || []) { const k = String(item.start).slice(0,10); if (!map.has(k)) map.set(k, []); map.get(k).push({ ...item, resourceName: s.displayName }); }
    return map;
  }, [schedule]);

  return <div className="calendar-capacity-center" data-projectpulse-module="057">
    <header className="calendar-capacity-hero"><div><p className="eyebrow">MODULE 057</p><h1>Resource & Team Calendar Capacity</h1><p>Review individual, team, and department availability across day, week, month, agenda, timeline, and future months.</p></div><div><strong>{config?.environmentMode || 'test'}</strong><span>{config?.testDomain || 'onenecklab.com'}</span></div></header>

    <section className="calendar-capacity-controls">
      <div className="calendar-control-grid">
        <label className="calendar-control-field">
          <span>Scope</span>
          <select value={scope} onChange={e=>setScope(e.target.value)}>
            <option value="individual">Individual user</option>
            <option value="team">Team</option>
            <option value="department">Department</option>
          </select>
        </label>

        {scope==='individual' ? <div className="calendar-control-field calendar-engineer-field">
          <span>User</span>
          <div className="calendar-engineer-picker">
            <input
              type="search"
              value={userSearch}
              onChange={e=>setUserSearch(e.target.value)}
              placeholder="Search users"
              aria-label="Search users by name, email, team, or department"
            />
            <select value={userId} onChange={e=>setUserId(e.target.value)}>
              {filteredResources.map(r=><option key={r.userId} value={r.userId}>{r.displayName} — {r.teamName} — {r.departmentName}</option>)}
            </select>
          </div>
          <small>{filteredResources.length} eligible users</small>
        </div> : null}

        {scope==='team' ? <label className="calendar-control-field calendar-resource-field">
          <span>Team</span>
          <select value={team} onChange={e=>setTeam(e.target.value)}>
            <option value="">Select a team</option>
            {teams.map(t=><option key={t.teamName} value={t.teamName}>{t.teamName} ({t.resourceCount})</option>)}
          </select>
        </label> : null}

        {scope==='department' ? <label className="calendar-control-field calendar-resource-field">
          <span>Department</span>
          <select value={department} onChange={e=>setDepartment(e.target.value)}>
            <option value="">Select a department</option>
            {departments.map(d=><option key={d.departmentName} value={d.departmentName}>{d.departmentName} ({d.resourceCount})</option>)}
          </select>
        </label> : null}

        <label className="calendar-control-field">
          <span>View</span>
          <select value={view} onChange={e=>setView(e.target.value)}>
            <option value="day">Day</option>
            <option value="workweek">Work week</option>
            <option value="week">Week</option>
            <option value="month">Month</option>
            <option value="agenda">Agenda</option>
            <option value="timeline">Timeline</option>
          </select>
        </label>

        <label className="calendar-control-field">
          <span>Month / year</span>
          <input
            type="month"
            value={`${anchor.getFullYear()}-${String(anchor.getMonth()+1).padStart(2,'0')}`}
            onChange={e=>{const [y,m]=e.target.value.split('-').map(Number); setAnchor(new Date(y,m-1,1));}}
          />
        </label>
      </div>

      <div className="calendar-capacity-buttons">
        <div className="calendar-navigation-buttons">
          <button onClick={()=>move(-1)}>Previous</button>
          <button onClick={()=>setAnchor(new Date())}>Today</button>
          <button onClick={()=>move(1)}>Next</button>
        </div>
        <button
          className="primary-action calendar-load-button"
          onClick={() => void load()}
          disabled={loading || !calendarReady || !hasSelection}
        >
          {loading ? 'Loading…' : 'Refresh calendar'}
        </button>
      </div>
    </section>

    {error ? <div className="calendar-capacity-error">{error}</div> : null}
    <section className="calendar-capacity-summary">{(schedule?.schedules || []).map(s=><article key={s.email}><span>{s.displayName}</span><strong>{s.capacityPercent}% busy</strong><small>{s.scheduledHours} scheduled / {s.availableHours} available hours</small></article>)}</section>

    {view==='month' ? <section className="calendar-month"><div className="calendar-month-heading"><h2>{anchor.toLocaleString(undefined,{month:'long',year:'numeric'})}</h2><span>Use Next or month/year to look months ahead.</span></div><div className="calendar-weekdays">{['Sun','Mon','Tue','Wed','Thu','Fri','Sat'].map(x=><strong key={x}>{x}</strong>)}</div><div className="calendar-grid">{monthCells(anchor).map(d=>{const k=keyOf(d); const items=eventsByDate.get(k)||[]; return <article key={k} className={d.getMonth()===anchor.getMonth()?'':'other-month'}><b>{d.getDate()}</b>{items.slice(0,4).map((x,i)=><span className="calendar-busy" key={i}>{x.resourceName}: Busy</span>)}{items.length>4?<small>+{items.length-4} more</small>:null}</article>;})}</div></section> : <section className="calendar-agenda"><h2>{view==='timeline'?'Resource timeline':'Calendar agenda'}</h2>{(schedule?.schedules || []).flatMap(s=>(s.scheduleItems||[]).map(i=>({...i,resourceName:s.displayName}))).sort((a,b)=>String(a.start).localeCompare(String(b.start))).map((i,n)=><article key={`${i.start}-${n}`}><span>{i.status}</span><div><strong>{i.resourceName}</strong><small>{i.start} – {i.end}</small></div></article>)}</section>}

    <footer className="calendar-capacity-privacy"><strong>Privacy default:</strong> Team and department views show free/busy availability only.</footer>
  </div>;
}
