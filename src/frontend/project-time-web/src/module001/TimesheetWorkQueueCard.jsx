export default function TimesheetWorkQueueCard({
  item,
  disabled = false,
  onAdd = () => {},
  onStartTimer = () => {},
  onOpenTask = () => {}
}) {
  return (
    <article className="module001-work-card">
      <header>
        <div>
          <small>{item.customerName || 'Customer'} · {item.projectCode || 'Project'}</small>
          <h4>{item.taskName || item.workItemName || 'Assigned work'}</h4>
        </div>
        <span>{item.taskStatus || item.status || 'Assigned'}</span>
      </header>
      <p>{item.taskDescription || 'No task description provided.'}</p>
      <dl>
        <div><dt>Project</dt><dd>{item.projectName || 'Unspecified'}</dd></div>
        <div><dt>Work type</dt><dd>{item.workType || 'Project task'}</dd></div>
        <div><dt>Engineer</dt><dd>{item.assignedEngineerName || 'Current user'}</dd></div>
        <div><dt>Project Manager</dt><dd>{item.projectManagerName || 'Unassigned'}</dd></div>
        <div><dt>Due</dt><dd>{item.dueDate || 'Not set'}</dd></div>
        <div><dt>Allocated</dt><dd>{Number(item.assignedHours || 0).toFixed(2)} hrs</dd></div>
        <div><dt>This week</dt><dd>{Number(item.weekHours || 0).toFixed(2)} hrs</dd></div>
        <div><dt>Remaining</dt><dd>{Number(item.remainingHours || 0).toFixed(2)} hrs</dd></div>
      </dl>
      <footer>
        <button type="button" disabled={disabled || item.addedThisWeek} onClick={() => onAdd(item)}>
          {item.addedThisWeek ? 'Added to Timesheet' : 'Add to Timesheet'}
        </button>
        <button type="button" className="secondary" disabled={disabled} onClick={() => onStartTimer(item)}>
          Start timer
        </button>
        {item.openTaskHref ? (
          <button type="button" className="secondary" onClick={() => onOpenTask(item)}>
            Open task
          </button>
        ) : null}
      </footer>
    </article>
  );
}
