export default function TimesheetCalendarEntryPanel({ entry, availableTasks = [], onSave = () => {}, onRemove = () => {} }) {
  if (!entry) return null;
  return (
    <aside className="module001-calendar-panel" aria-label="Timesheet calendar entry details">
      <header>
        <div><small>{entry.entryDate}</small><h3>Edit work entry</h3></div>
        <span>{entry.status || 'Draft'}</span>
      </header>
      <label className="module001-field">
        <span>Assigned task</span>
        <select defaultValue={entry.assignmentId || ''}>
          <option value="">Select assigned work</option>
          {availableTasks.map((task) => (
            <option key={task.assignmentId || task.taskId} value={task.assignmentId || task.taskId}>
              {[task.projectCode, task.taskName].filter(Boolean).join(' · ')}
            </option>
          ))}
        </select>
      </label>
      <label className="module001-field"><span>Description</span><textarea defaultValue={entry.description || ''} /></label>
      <div className="module001-calendar-hours">
        <label>Normal <input type="number" min="0" step="0.25" defaultValue={entry.normalHours || 0} /></label>
        <label>Afterhours <input type="number" min="0" step="0.25" defaultValue={entry.afterhours || 0} /></label>
      </div>
      {!entry.description ? <div className="module001-warning">Description required before submission.</div> : null}
      <footer><button type="button" onClick={onSave}>Save draft</button><button type="button" className="secondary" onClick={onRemove}>Remove draft</button></footer>
    </aside>
  );
}
