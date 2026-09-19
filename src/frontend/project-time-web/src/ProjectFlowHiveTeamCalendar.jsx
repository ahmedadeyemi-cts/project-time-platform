import { useEffect, useMemo, useState } from 'react';
import { calendarRange, intervalsForDay, moveCalendar } from './flowhive-team-calendar.js';

const today = () => new Date().toISOString().slice(0, 10);
const time = value => new Date(value).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', timeZone: 'UTC' });

export default function ProjectFlowHiveTeamCalendar({ projectId, request }) {
  const [anchor, setAnchor] = useState(today);
  const [view, setView] = useState('week');
  const [result, setResult] = useState(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const [memberId, setMemberId] = useState('all');
  const range = useMemo(() => calendarRange(anchor, view), [anchor, view]);
  useEffect(() => { setMemberId('all'); }, [projectId]);
  useEffect(() => {
    const controller = new AbortController();
    setResult(null); setError(''); setLoading(true);
    request(`/api/project-flowhive/projects/${projectId}/team-calendar?start=${range.start}&end=${range.end}`, { signal: controller.signal })
      .then(data => { if (!controller.signal.aborted) setResult({ ...data, projectId, rangeStart: range.start, rangeEnd: range.end }); })
      .catch(failure => { if (!controller.signal.aborted) setError(failure.message || 'Team availability could not be loaded.'); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [projectId, range.start, range.end, request, refresh]);
  const current = result?.projectId === projectId && result?.rangeStart === range.start && result?.rangeEnd === range.end ? result : null;
  const members = (current?.members || []).filter(member => memberId === 'all' || member.userId === memberId);
  return <section className="flowhive-team-calendar" aria-label="Project team calendar">
    <header><div><span>Project team</span><h3>Availability and meetings</h3><p>PM, AE, SA, and assigned team members. Times are shown in UTC. Calendar availability does not include project workload allocations.</p></div></header>
    <div className="flowhive-team-calendar-toolbar">
      <button type="button" onClick={() => setAnchor(moveCalendar(anchor, view, -1))}>Previous {view}</button>
      <button type="button" onClick={() => setAnchor(today())}>Today</button>
      <button type="button" onClick={() => setAnchor(moveCalendar(anchor, view, 1))}>Next {view}</button>
      <label>Date<input type="date" value={anchor} onChange={event => { if (event.target.value) setAnchor(event.target.value); }} /></label>
      <label>View<select aria-label="View" value={view} onChange={event => setView(event.target.value)}><option value="week">Week</option><option value="month">Month</option></select></label>
      <label>Team member<select aria-label="Team member" value={memberId} onChange={event => setMemberId(event.target.value)}><option value="all">Entire project team</option>{(current?.members || []).map(member => <option key={member.userId} value={member.userId}>{member.displayName}</option>)}</select></label>
      <button type="button" disabled={loading} onClick={() => setRefresh(value => value + 1)}>Refresh</button>
    </div>
    <p aria-live="polite">{loading ? 'Loading Microsoft calendar availability…' : error || `${members.length} team members · ${range.start} through ${range.days.at(-1)} · UTC`}</p>
    {current?.status === 'partial' ? <p role="status">Some calendars could not be retrieved. Unknown availability must be checked before booking a meeting.</p> : null}
    {current && !members.length ? <p>No active team members are available for this selection.</p> : null}
    {current && members.length ? <div className="flowhive-team-calendar-scroll"><table><caption>Project team availability, {view} of {range.start}, UTC</caption><thead><tr><th scope="col">Team member</th>{range.days.map(day => <th scope="col" key={day}>{new Date(`${day}T12:00:00Z`).toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric', timeZone: 'UTC' })}</th>)}</tr></thead><tbody>{members.map(member => <tr key={member.userId}><th scope="row"><strong>{member.displayName}</strong><small>{member.role}</small></th>{range.days.map(day => {
      const intervals = intervalsForDay(member.intervals, day);
      return <td key={day}>{member.status !== 'available' ? <span className="flowhive-calendar-unknown">Availability unknown</span> : intervals.length ? intervals.map((item, index) => <div className={`flowhive-calendar-slot status-${item.status}`} key={`${item.start}-${index}`}><strong>{item.status === 'oof' ? 'Out of office' : item.status}</strong><span>{item.start.slice(0, 10) < day ? '00:00' : time(item.start)} – {item.end.slice(0, 10) > day ? '24:00' : time(item.end)}</span></div>) : <span className="flowhive-calendar-clear">No calendar events</span>}</td>;
    })}</tr>)}</tbody></table></div> : null}
  </section>;
}
