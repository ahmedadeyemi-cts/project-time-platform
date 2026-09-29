import { useId, useState } from 'react';
import { projectOverview } from './flowhive-psa-overview.js';
import './project-flowhive-home.css';

const filters = { all: 'Open work', attention: 'Needs attention', mine: 'My work', overdue: 'Overdue', dueSoon: 'Due within 3 days', unassigned: 'Unassigned', blocked: 'Blocked', critical: 'Critical path' };
const readableDate = value => value ? new Date(`${value}T12:00:00`).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }) : 'Not scheduled';
const isAttention = task => task.overdue || task.blocked || task.unassigned;
export default function ProjectFlowHiveOverview({ plan, schedule, userId, dirty, projectName, sharingPanel, onOpenTask, onNavigate }) {
  const [filter, setFilter] = useState('all');
  const id = useId();
  const data = projectOverview(plan, schedule, new Date().toISOString().slice(0, 10), userId);
  const rows = data.tasks.filter(task => !task.closed && (filter === 'all' || (filter === 'attention' ? isAttention(task) : task[filter])))
    .sort((a, b) => Number(b.overdue) - Number(a.overdue) || Number(b.blocked) - Number(a.blocked) || (a.due || '9999').localeCompare(b.due || '9999'));
  const citedTasks = data.tasks.filter(task => Array.isArray(task.citationIds) && task.citationIds.length > 0).length;
  const traceability = data.tasks.length ? Math.round((citedTasks / data.tasks.length) * 100) : 0;
  const attentionIds = new Set(data.tasks.filter(task => !task.closed && isAttention(task)).map(task => task.clientTaskId || task.wbsNumber));
  const closedPercent = data.tasks.length ? Math.round(data.completed / data.tasks.length * 100) : 0;
  const exceedsTarget = data.finish && data.target && data.finish > data.target;
  return <section className="flowhive-overview flowhive-command-center" aria-label="Project delivery overview">
    <header className="flowhive-home-heading"><div><span className="flowhive-home-eyebrow">Project home</span><h3>Project command center</h3><p>{projectName ? `${projectName} · ` : ''}See priorities, manage customer access, and move work forward.</p></div>
      <span className={`flowhive-home-badge ${dirty ? 'attention' : ''}`}>{dirty ? 'Unsaved WBS edits' : plan ? 'Saved working copy' : 'No working copy loaded'}</span>
    </header>
    {!plan ? <div className="flowhive-home-empty"><strong>Your project workspace is ready.</strong><p>Load or create the project WBS to see delivery work and schedule exceptions.</p><button type="button" className="primary" onClick={() => onNavigate?.('planner')}>Open WBS plan</button></div> : <>
      <div className="flowhive-project-home-summary">
        <article className={attentionIds.size ? 'attention' : 'healthy'}><span>Needs attention</span><strong>{attentionIds.size}</strong><small>Overdue, blocked, or unassigned work</small><button type="button" className="flowhive-home-text-action" onClick={() => setFilter('attention')}>Review priorities <span aria-hidden="true">→</span></button></article>
        <article><span>Due soon</span><strong>{data.totals.dueSoon}</strong><small>Open tasks due within 3 days</small><button type="button" className="flowhive-home-text-action" onClick={() => setFilter('dueSoon')}>View upcoming work <span aria-hidden="true">→</span></button></article>
        <article className={traceability === 100 && data.tasks.length ? 'healthy' : ''}><span>SOW traceability</span><strong>{data.tasks.length ? `${traceability}%` : 'Not available'}</strong><small>{citedTasks}/{data.tasks.length} executable tasks carry scope citations</small></article>
        <article className={exceedsTarget ? 'attention' : ''}><span>Schedule finish</span><strong className="flowhive-home-date">{data.finish ? readableDate(data.finish) : 'Not calculated'}</strong><small>{data.target ? `Target ${readableDate(data.target)}` : 'No target date set'}</small>{exceedsTarget && <small>Schedule exceeds the target date.</small>}</article>
      </div>
      <div className="flowhive-home-progress"><span>Closed work <strong>{data.completed}/{data.tasks.length}</strong></span><progress aria-label="Closed work" value={closedPercent} max="100" /><span>{closedPercent}%</span></div>
      <nav className="flowhive-project-home-actions" aria-label="Project quick actions">
        <button type="button" className="primary" onClick={() => onNavigate?.('planner')}>Open WBS plan <span aria-hidden="true">→</span></button>
        <button type="button" onClick={() => onNavigate?.('kanban')}>Open Board</button>
        <button type="button" onClick={() => onNavigate?.('calendar')}>Open Calendar</button>
        <button type="button" onClick={() => onNavigate?.('status')}>Open Status &amp; RAID</button>
        <button type="button" onClick={() => onNavigate?.('governance')}>Open Notifications</button>
      </nav>
    </>}
    {sharingPanel}
    {plan && <section className="flowhive-home-work" aria-labelledby={`${id}-work`}>
      <div className="flowhive-home-work-heading"><div><h4 id={`${id}-work`}>Work to focus on</h4><p>Choose a filter, then open a task to review its details.</p></div><label htmlFor={`${id}-filter`}>Show work<select id={`${id}-filter`} value={filter} onChange={event => setFilter(event.target.value)}>{Object.entries(filters).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label></div>
      <div className="flowhive-overview-metrics" role="group" aria-label="Work filters">{Object.entries(data.totals).map(([key, value]) => <button key={key} type="button" aria-pressed={filter === key} onClick={() => setFilter(filter === key ? 'all' : key)}><span>{filters[key]}</span><strong>{value}</strong></button>)}</div>
      <p className="flowhive-home-results" role="status">{rows.length} open {rows.length === 1 ? 'task' : 'tasks'} · {filters[filter]}</p>
      <div className="flowhive-psa-table-wrap" tabIndex="0" role="region" aria-label="Project work"><table><thead><tr><th scope="col">Task</th><th scope="col">Owner</th><th scope="col">Due</th><th scope="col">Status</th><th scope="col">Attention</th></tr></thead><tbody>{rows.map(task => <tr key={task.clientTaskId || task.wbsNumber}><td><button type="button" className="flowhive-home-task-link" onClick={() => onOpenTask?.(task.wbsNumber)}>{task.wbsNumber} · {task.name}</button></td><td>{task.assignments.map(item => item.resourceDisplayName || 'Assigned team member').join(', ') || 'Unassigned'}</td><td>{task.due ? readableDate(task.due) : 'Not calculated'}</td><td>{String(task.status || 'not_started').replaceAll('_', ' ')}</td><td><div className="flowhive-home-task-flags">{[task.overdue && 'Overdue', task.blocked && 'Blocked', task.unassigned && 'Needs owner', task.critical && 'Critical path'].filter(Boolean).map(flag => <span key={flag}>{flag}</span>)}</div></td></tr>)}</tbody></table></div>
      {!rows.length && <div className="flowhive-home-empty"><strong>No open tasks match this filter.</strong>{filter !== 'all' && <button type="button" onClick={() => setFilter('all')}>Show all open work</button>}</div>}
    </section>}
  </section>;
}
