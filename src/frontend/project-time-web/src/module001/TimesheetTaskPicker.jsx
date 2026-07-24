const TIMER_TARGET_PATTERN = /^(assignment|category):[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;

function toTimerOption(target) {
  const optionValue = target.selectionValue
    || (target.assignmentId ? `assignment:${target.assignmentId}` : '')
    || (target.nonProjectTimeCategoryId ? `category:${target.nonProjectTimeCategoryId}` : '')
    || (target.nonProjectCategoryId ? `category:${target.nonProjectCategoryId}` : '');

  if (!TIMER_TARGET_PATTERN.test(optionValue)) return null;

  const label = target.selectionLabel
    || [target.customerName, target.projectCode, target.taskName].filter(Boolean).join(' · ')
    || target.nonProjectCategoryName
    || target.categoryName
    || 'Authorized activity';

  return {
    optionValue,
    label,
    groupLabel: target.groupLabel
      || (optionValue.startsWith('assignment:') ? 'Assigned project work' : 'Authorized non-project activities')
  };
}

export default function TimesheetTaskPicker({
  tasks = [],
  value = '',
  disabled = false,
  onChange = () => {}
}) {
  const options = tasks.map(toTimerOption).filter(Boolean);
  const hasOptions = options.length > 0;
  const groups = options.reduce((result, option) => {
    const group = result.get(option.groupLabel) || [];
    group.push(option);
    result.set(option.groupLabel, group);
    return result;
  }, new Map());

  return (
    <label className="module001-field">
      <span>Assigned task or authorized activity</span>
      <select
        value={TIMER_TARGET_PATTERN.test(value) ? value : ''}
        disabled={disabled || !hasOptions}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">{hasOptions ? 'Select assigned work or activity' : 'No authorized timer activity available'}</option>
        {[...groups.entries()].map(([groupLabel, groupOptions]) => (
          <optgroup key={groupLabel} label={groupLabel}>
            {groupOptions.map((option) => (
              <option key={option.optionValue} value={option.optionValue}>{option.label}</option>
            ))}
          </optgroup>
        ))}
      </select>
      <small>
        {hasOptions
          ? 'Choose directly from your assigned project tasks or the active non-project activity catalog.'
          : 'No active assigned tasks or non-project activities are currently available.'}
      </small>
    </label>
  );
}
