import { useState } from 'react';
import { phaseTaskIssues, proposeTasks, taskTotal } from './task-estimates.js';

export default function PhaseTaskReview({ phase, proposals, readOnly, onChange }) {
  const [error, setError] = useState('');
  const tasks = phase.tasks || [];
  const total = taskTotal(tasks);
  const issues = phaseTaskIssues(phase);
  const afterHours = tasks.reduce((sum, task) => sum + Number(task.afterHours || 0), 0);
  function update(index, changes) {
    onChange('tasks', tasks.map((task, i) => i === index ? { ...task, ...changes, reviewed: false } : task));
  }
  return <section className="m025-task-review" aria-label={`${phase.label || phase.phaseCode} task estimates`}>
    <div className="m025-task-review__heading">
      <div><h4>Tasks, effort &amp; work windows</h4><p>Review the detailed GSD estimate. The SOW presents the phase approach and deliverables.</p></div>
      <strong>{total === null ? 'Hours incomplete' : `${total.toFixed(2)} total hours`}</strong>
    </div>
    {!tasks.length && <div className="m025-estimate-empty">
      <p>New scope generation proposes tasks and hours together. For this saved scope, use its phase estimate to prepare a draft allocation.</p>
      <button type="button" className="m025-button" disabled={readOnly} onClick={() => {
        try { onChange('tasks', proposeTasks(phase)); setError(''); } catch (e) { setError(e.message); }
      }}>Propose tasks and hours</button>
    </div>}
    {error && <p role="alert">{error}</p>}
    {tasks.length > 0 && proposals?.length > 0 && <details className="m025-proposal-comparison">
      <summary>Compare the latest generated task proposal</summary>
      <p>Your current task edits are preserved. Replacing them applies the entire proposal for this phase and requires a new review.</p>
      <ol>{proposals.map(task => <li key={task.taskId}>{task.description} · {task.hours == null ? 'Hours need estimation' : `${task.hours}h proposed`}</li>)}</ol>
      <button type="button" className="m025-button" disabled={readOnly} onClick={() => onChange('tasks', proposals.map(task => ({ ...task, reviewed: false })))}>Replace this phase’s task estimates with the proposal</button>
    </details>}
    <div className="m025-task-review__list">{tasks.map((task, index) => <article key={task.taskId} className="m025-task-review__item">
      <div className="m025-task-review__number">Task {index + 1}<span>{task.reviewed === false ? 'Needs review' : 'Reviewed'}</span></div>
      <label>Task description<textarea rows={2} maxLength={6000} disabled={readOnly} value={task.description || ''}
        aria-label={`${phase.phaseCode} task ${index + 1} description`} onChange={event => update(index, { description: event.target.value })} /></label>
      <div className="m025-task-review__allocation">
        <label>Total engineering hours<input type="number" min="0" max="100000" step="0.01" disabled={readOnly} value={task.hours ?? ''}
          aria-label={`${phase.phaseCode} task ${index + 1} total hours`} onChange={event => {
            const hours = event.target.value === '' ? null : Number(event.target.value);
            const after = Math.min(Number(task.afterHours || 0), hours ?? 0);
            update(index, { hours, afterHours: hours === null ? null : after, regularHours: hours === null ? null : Math.round((hours - after) * 100) / 100 });
          }} /></label>
        <label className="m025-task-review__check"><input type="checkbox" disabled={readOnly} checked={Boolean(task.afterHoursRequired)} onChange={event => {
          const after = event.target.checked ? Number(task.hours || 0) : 0;
          update(index, { afterHoursRequired: event.target.checked, afterHours: task.hours == null ? null : after, regularHours: task.hours == null ? null : Number(task.hours) - after });
        }} /> After-hours required</label>
        {task.afterHoursRequired && <label>After-hours portion<input type="number" min="0" max={task.hours ?? 100000} step="0.01" disabled={readOnly} value={task.afterHours ?? ''}
          aria-label={`${phase.phaseCode} task ${index + 1} after-hours portion`} onChange={event => {
            const afterHours = event.target.value === '' ? null : Number(event.target.value);
            update(index, { afterHours, regularHours: task.hours == null || afterHours == null ? null : Math.round((Number(task.hours) - afterHours) * 100) / 100 });
          }} /></label>}
        <span>Regular: {task.hours == null ? 'Not estimated' : `${Number(task.regularHours ?? (task.hours - (task.afterHours || 0))).toFixed(2)}h`}</span>
      </div>
      {task.afterHoursSuggested && !task.afterHoursRequired && <p className="m025-task-review__suggestion">Suggested maintenance-window review: {task.afterHoursReason || 'This task may interrupt service. Confirm the customer work window.'}</p>}
      {task.estimateBasis && <p className="m025-task-review__basis">{task.estimateBasis}</p>}
      <details><summary>Notes and work-window rationale</summary>
        <label>Task notes<textarea rows={2} maxLength={4000} value={task.notes || ''} disabled={readOnly} onChange={event => update(index, { notes: event.target.value })} /></label>
        <label>After-hours rationale<textarea rows={2} maxLength={1000} value={task.afterHoursReason || ''} disabled={readOnly} onChange={event => update(index, { afterHoursReason: event.target.value })} /></label>
      </details>
      <button type="button" className="m025-button" disabled={readOnly} onClick={() => onChange('tasks', tasks.filter((_, i) => i !== index))}>Remove task {index + 1}</button>
    </article>)}</div>
    <div className="m025-task-review__footer">
      <button type="button" className="m025-button" disabled={readOnly || tasks.length >= 200} onClick={() => onChange('tasks', [...tasks, { taskId: crypto.randomUUID(), description: '', hours: null, notes: '', reviewed: false }])}>Add task</button>
      {tasks.length > 0 && <label className="m025-task-review__check"><input type="checkbox" disabled={readOnly || total === null}
        checked={total !== null && tasks.every(task => task.reviewed !== false)} onChange={event => onChange('tasks', tasks.map(task => ({ ...task, reviewed: event.target.checked })))} /> I reviewed this phase’s task estimates and work windows</label>}
    </div>
    {tasks.length > 0 && <p role="status">{issues.length ? `${issues.length} review item(s). ${issues[0]}` : `Phase reviewed. ${afterHours.toFixed(2)} after-hours hours included in the total.`}</p>}
    <p className="m025-task-review__basis">After-hours identifies the work window. Premium pricing requires the applicable approved rate; checking this box does not apply a billing multiplier.</p>
  </section>;
}
