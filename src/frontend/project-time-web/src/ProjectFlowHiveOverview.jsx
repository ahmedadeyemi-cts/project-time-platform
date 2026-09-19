import { useState } from 'react';
import { projectOverview } from './flowhive-psa-overview.js';

const filters = { all: 'Open work', mine: 'My work', overdue: 'Overdue', dueSoon: 'Due within 3 days', unassigned: 'Unassigned', blocked: 'Blocked', critical: 'Critical path' };
export default function ProjectFlowHiveOverview({ plan, schedule, userId, dirty, onOpenTask }) {
  const [filter, setFilter] = useState('all');
  const data = projectOverview(plan, schedule, new Date().toISOString().slice(0, 10), userId);
  const rows = data.tasks.filter(task => !task.closed && (filter === 'all' || task[filter]))
    .sort((a, b) => Number(b.overdue) - Number(a.overdue) || Number(b.blocked) - Number(a.blocked) || (a.due || '9999').localeCompare(b.due || '9999'));
  return <section className="flowhive-overview" aria-label="Project delivery overview">
    <header><div><span>Delivery overview</span><h3>What needs attention</h3><p>{dirty ? 'Includes unsaved WBS edits.' : 'Current WBS working copy.'} Dates use the validated weekday schedule and UTC day boundaries. Team capacity is not yet included in this forecast.</p></div></header>
    {!plan ? <p>Load or create the project WBS to see delivery work and schedule exceptions.</p> : <>
      <div className="flowhive-overview-metrics">{Object.entries(data.totals).map(([key, value]) => <button key={key} type="button" aria-pressed={filter === key} onClick={() => setFilter(key)}><span>{filters[key]}</span><strong>{value}</strong></button>)}</div>
      <p>Schedule finish: <strong>{data.finish || 'Not calculated'}</strong> · Target: <strong>{data.target || 'Not set'}</strong> · Closed work: {data.completed}/{data.tasks.length}{data.finish && data.target && data.finish > data.target ? ' · Schedule exceeds the target date.' : ''}</p>
      <label>Show work<select value={filter} onChange={event => setFilter(event.target.value)}>{Object.entries(filters).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
      <div className="flowhive-psa-table-wrap"><table><thead><tr><th>Task</th><th>Owner</th><th>Due</th><th>Status</th><th>Attention</th></tr></thead><tbody>{rows.map(task => <tr key={task.clientTaskId || task.wbsNumber}><td><button type="button" onClick={() => onOpenTask(task.wbsNumber)}>{task.wbsNumber} · {task.name}</button></td><td>{task.assignments.map(item => item.resourceDisplayName || 'Assigned team member').join(', ') || 'Unassigned'}</td><td>{task.due || 'Not calculated'}</td><td>{String(task.status || 'not_started').replaceAll('_', ' ')}</td><td>{[task.overdue && 'Overdue', task.blocked && 'Blocked', task.unassigned && 'Needs owner', task.critical && 'Critical path'].filter(Boolean).join(' · ') || '—'}</td></tr>)}</tbody></table></div>
      {!rows.length && <p>No open tasks match this filter.</p>}
    </>}
  </section>;
}
