export default function TimesheetTaskPicker({
  tasks = [],
  value = '',
  disabled = false,
  onChange = () => {}
}) {
  return (
    <label className="module001-field">
      <span>Assigned task or activity</span>
      <select value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)}>
        <option value="">Select assigned work</option>
        {tasks.map((task) => (
          <option key={task.assignmentId || task.taskId} value={task.assignmentId || task.taskId}>
            {[task.projectCode, task.projectName, task.taskName].filter(Boolean).join(' · ')}
          </option>
        ))}
      </select>
      <small>Runtime integration must resolve only work assigned to the authenticated user.</small>
    </label>
  );
}
