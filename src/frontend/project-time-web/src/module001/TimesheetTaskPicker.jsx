import { useMemo, useState } from 'react';

const TIMER_TARGET_PATTERN = /^(?:(?:assignment|category):[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}|category-code:[A-Z0-9][A-Z0-9_-]{0,99})$/i;
const GROUP_ORDER = ['Non-Project Time', 'Regular Tasks', 'Service Request Tasks'];

function toTimerOption(target) {
  const categoryCode = target.categoryCode || target.targetCode || target.nonProjectCategoryCode || '';
  const optionValue = target.selectionValue
    || (target.assignmentId ? `assignment:${target.assignmentId}` : '')
    || (target.nonProjectTimeCategoryId ? `category:${target.nonProjectTimeCategoryId}` : '')
    || (target.nonProjectCategoryId ? `category:${target.nonProjectCategoryId}` : '')
    || (categoryCode ? `category-code:${categoryCode}` : '');

  if (!TIMER_TARGET_PATTERN.test(optionValue)) return null;

  const label = target.selectionLabel
    || [target.customerName, target.projectCode, target.taskName].filter(Boolean).join(' · ')
    || target.nonProjectCategoryName
    || target.categoryName
    || 'Authorized activity';

  const groupLabel = target.groupLabel
    || (optionValue.startsWith('assignment:') ? 'Regular Tasks' : 'Non-Project Time');

  return {
    optionValue,
    label,
    groupLabel,
    searchText: `${groupLabel} ${label} ${target.workType || ''}`.toLowerCase()
  };
}

export default function TimesheetTaskPicker({
  tasks = [],
  value = '',
  disabled = false,
  onChange = () => {}
}) {
  const [search, setSearch] = useState('');
  const options = useMemo(() => tasks.map(toTimerOption).filter(Boolean), [tasks]);
  const hasOptions = options.length > 0;
  const normalizedSearch = search.trim().toLowerCase();
  const filteredOptions = options.filter((option) => (
    !normalizedSearch
    || option.searchText.includes(normalizedSearch)
    || option.optionValue === value
  ));

  const groups = GROUP_ORDER
    .map((groupLabel) => ({
      groupLabel,
      options: filteredOptions.filter((option) => option.groupLabel === groupLabel)
    }))
    .filter((group) => group.options.length > 0);

  const unmatchedGroups = filteredOptions
    .filter((option) => !GROUP_ORDER.includes(option.groupLabel))
    .reduce((result, option) => {
      const existing = result.find((group) => group.groupLabel === option.groupLabel);
      if (existing) existing.options.push(option);
      else result.push({ groupLabel: option.groupLabel, options: [option] });
      return result;
    }, []);

  return (
    <label className="module001-field module001-task-picker">
      <span>Assigned task or authorized activity</span>
      <input
        className="module001-task-search"
        type="search"
        value={search}
        disabled={disabled || !hasOptions}
        placeholder="Search activity, task, project, customer, or request"
        aria-label="Search timer activities"
        onChange={(event) => setSearch(event.target.value)}
      />
      <select
        value={TIMER_TARGET_PATTERN.test(value) ? value : ''}
        disabled={disabled || !hasOptions}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">{hasOptions ? 'Select activity or task' : 'No authorized timer activity available'}</option>
        {[...groups, ...unmatchedGroups].map(({ groupLabel, options: groupOptions }) => (
          <optgroup key={groupLabel} label={groupLabel}>
            {groupOptions.map((option) => (
              <option key={option.optionValue} value={option.optionValue}>{option.label}</option>
            ))}
          </optgroup>
        ))}
      </select>
      <small>
        {hasOptions
          ? `${filteredOptions.length} of ${options.length} timer choices shown across Non-Project Time, Regular Tasks, and Service Request Tasks.`
          : 'No active assigned tasks or non-project activities are currently available.'}
      </small>
    </label>
  );
}
