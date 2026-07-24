export default function TimesheetTaskPicker({
  tasks = [],
  value = '',
  disabled = false,
  onChange = () => {}
}) {
  return (
    <label className="module001-field">
      <span>Assigned task or authorized activity</span>
      <select value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)}>
        <option value="">Select assigned work</option>
        {tasks.map((task) => {
          const optionValue = task.selectionValue
            || (task.assignmentId ? `assignment:${task.assignmentId}` : '')
            || (task.nonProjectCategoryId ? `category:${task.nonProjectCategoryId}` : '');
          const label = task.selectionLabel
            || [task.customerName, task.projectCode, task.projectName, task.taskName].filter(Boolean).join(' · ')
            || task.nonProjectCategoryName
            || task.categoryName
            || 'Authorized activity';
          return <option key={optionValue} value={optionValue}>{label}</option>;
        })}
      </select>
      <small>Project work is limited to assignments returned for the authenticated user.</small>
    </label>
  );
}
