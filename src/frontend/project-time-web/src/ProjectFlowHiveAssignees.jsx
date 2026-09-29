import { useState } from 'react';
import { assigneeKey } from './flowhive-assignees.js';

export default function ProjectFlowHiveAssignees({task,assignments,people,disabled,onToggle,onHours}) {
  const [query,setQuery]=useState('');
  const chosen=new Set(assignments.map(assigneeKey));
  const available=people.filter(person=>`${person.displayName} ${person.role || ''}`.toLowerCase().includes(query.toLowerCase()));
  return <details className="flowhive-people-picker">
    <summary aria-label={`Assigned people for ${task.name}`}>{assignments.length ? `${assignments.length} assigned` : 'Assign people'}<span>{assignments.map(person=>person.resourceDisplayName).filter(Boolean).join(', ') || 'Internal team or customer contacts'}</span></summary>
    <div className="flowhive-people-picker-content">
      <label>Find a person<input type="search" value={query} disabled={disabled} onChange={e=>setQuery(e.target.value)} /></label>
      <div role="group" aria-label={`Choose assignees for ${task.name}`}>
        {available.map(person=><label className="flowhive-person-option" key={assigneeKey(person)}>
          <input type="checkbox" checked={chosen.has(assigneeKey(person))} disabled={disabled} onChange={e=>onToggle(person,e.target.checked)} />
          <span>{person.displayName}<small>{person.projectContactId ? 'External contact · no Pulse login' : person.role || 'Internal team'}</small></span>
        </label>)}
      </div>
      {!available.length && <p>No matching project people. Add customer contacts from Project home.</p>}
      {assignments.length>0 && <div className="flowhive-assignment-effort"><p>Adding a person does not multiply the task estimate. Set each person’s planned hours explicitly.</p>{assignments.map(person=><div key={assigneeKey(person)}>
        <strong>{person.resourceDisplayName || 'Unresolved role'}{person.projectContactId ? ' (External)' : ''}</strong>
        <label>Hours<input aria-label={`Planned hours for ${person.resourceDisplayName} on ${task.wbsNumber}`} type="number" min="0" step="0.25" value={person.plannedHours ?? 0} disabled={disabled} onChange={e=>onHours(assigneeKey(person),'plannedHours',e.target.value===''?'':Number(e.target.value))} /></label>
        <label>Allocation %<input type="number" min="1" max="100" step="1" value={person.allocationPercent ?? 100} disabled={disabled} onChange={e=>onHours(assigneeKey(person),'allocationPercent',e.target.value===''?'':Number(e.target.value))} /></label>
        <button type="button" disabled={disabled} onClick={()=>onToggle(person,false)} aria-label={`Remove ${person.resourceDisplayName} from ${task.name}`}>Remove</button>
      </div>)}</div>}
    </div>
  </details>;
}
