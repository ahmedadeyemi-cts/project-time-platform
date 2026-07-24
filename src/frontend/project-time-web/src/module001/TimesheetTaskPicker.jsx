const TIMER_TARGET_PATTERN = /^(assignment|category):[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function toTimerOption(task) {
  const optionValue = task.selectionValue
    || (task.assignmentId ? `assignment:${task.assignmentId}` : '')
    || (task.nonProjectCategoryId ? `category:${task.nonProjectCategoryId}` : '');

  if (!TIMER_TARGET_PATTERN.test(optionValue)) return null;

  const label = task.selectionLabel
    || [task.customerName, task.projectCode, task.projectName, task.taskName].filter(Boolean).join(' · ')
    || task.nonProjectCategoryName
    || task.categoryName
    || 'Authorized activity';

  return { optionValue, label };
}

export default function TimesheetTaskPicker({
  tasks = [],
  value = '',
  disabled = false,
  onChange = () => {}
}) {
  const options = tasks.map(toTimerOption).filter(Boolean);
  const hasOptions = options.length > 0;

  return (
    <label className="module001-field">
      <span>Assigned task or authorized activity</span>
      <select
        value={TIMER_TARGET_PATTERN.test(value) ? value : ''}
        disabled={disabled || !hasOptions}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">{hasOptions ? 'Select assigned work or activity' : 'No authorized timer activity available'}</option>
        {options.map((option) => (
          <option key={option.optionValue} value={option.optionValue}>{option.label}</option>
        ))}
      </select>
      <small>
        {hasOptions
          ? 'Project work is limited to assignments returned for the authenticated user.'
          : 'Add an active project assignment or use an authorized non-project category before starting a timer.'}
      </small>
    </label>
  );
}
