export default function TimesheetWorkQueueCard({ item, onAdd = () => {}, onStartTimer = () => {} }) {
  return (
    <article className="module001-work-card">
      <header>
        <div>
          <small>{item.customerName || 'Customer'} · {item.projectCode || 'Project'}</small>
          <h4>{item.taskName || item.workItemName}</h4>
        </div>
        <span>{item.status || 'Assigned'}</span>
      </header>
      <p>{item.taskDescription || 'No task description provided.'}</p>
      <dl>
        <div><dt>Project</dt><dd>{item.projectName || 'Unspecified'}</dd></div>
        <div><dt>Project Manager</dt><dd>{item.projectManagerName || 'Unassigned'}</dd></div>
        <div><dt>Due</dt><dd>{item.dueDate || 'Not set'}</dd></div>
        <div><dt>This week</dt><dd>{Number(item.weekHours || 0).toFixed(2)} hrs</dd></div>
      </dl>
      <footer>
        <button type="button" onClick={() => onAdd(item)}>Add to Timesheet</button>
        <button type="button" className="secondary" onClick={() => onStartTimer(item)}>Start timer</button>
      </footer>
    </article>
  );
}
