import { useEffect, useMemo, useRef, useState } from 'react';

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
    searchText: [
      groupLabel,
      label,
      target.workType,
      target.workTaskCategory,
      target.serviceRequestNumber,
      target.customerName,
      target.projectCode,
      target.projectName,
      target.taskCode,
      target.taskName
    ].filter(Boolean).join(' ').toLowerCase()
  };
}

function orderedGroups(options) {
  const ordered = GROUP_ORDER.map((groupLabel) => ({
    groupLabel,
    options: options.filter((option) => option.groupLabel === groupLabel)
  })).filter((group) => group.options.length > 0);

  const remaining = options
    .filter((option) => !GROUP_ORDER.includes(option.groupLabel))
    .reduce((groups, option) => {
      const current = groups.find((group) => group.groupLabel === option.groupLabel);
      if (current) current.options.push(option);
      else groups.push({ groupLabel: option.groupLabel, options: [option] });
      return groups;
    }, []);

  return [...ordered, ...remaining];
}

export default function TimesheetTaskPicker({
  tasks = [],
  value = '',
  disabled = false,
  onChange = () => {}
}) {
  const rootRef = useRef(null);
  const inputRef = useRef(null);
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);

  const options = useMemo(() => tasks.map(toTimerOption).filter(Boolean), [tasks]);
  const selectedOption = options.find((option) => option.optionValue === value) || null;
  const hasOptions = options.length > 0;
  const normalizedQuery = query.trim().toLowerCase();
  const effectiveQuery = selectedOption && query === selectedOption.label ? '' : normalizedQuery;
  const filteredOptions = options.filter((option) => (
    !effectiveQuery || option.searchText.includes(effectiveQuery)
  ));
  const groups = orderedGroups(filteredOptions);

  useEffect(() => {
    if (selectedOption) setQuery(selectedOption.label);
  }, [selectedOption?.optionValue, selectedOption?.label]);

  useEffect(() => {
    const handleOutsidePointer = (event) => {
      if (!rootRef.current?.contains(event.target)) setOpen(false);
    };

    document.addEventListener('pointerdown', handleOutsidePointer);
    return () => document.removeEventListener('pointerdown', handleOutsidePointer);
  }, []);

  useEffect(() => {
    if (activeIndex >= filteredOptions.length) {
      setActiveIndex(Math.max(0, filteredOptions.length - 1));
    }
  }, [activeIndex, filteredOptions.length]);

  const chooseOption = (option) => {
    if (!option) return;
    onChange(option.optionValue);
    setQuery(option.label);
    setOpen(false);
    setActiveIndex(0);
    inputRef.current?.focus();
  };

  const handleInputChange = (event) => {
    const nextQuery = event.target.value;
    setQuery(nextQuery);
    setOpen(true);
    setActiveIndex(0);

    if (!selectedOption || nextQuery !== selectedOption.label) {
      onChange('');
    }
  };

  const handleKeyDown = (event) => {
    if (disabled || !hasOptions) return;

    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setOpen(true);
      setActiveIndex((current) => Math.min(current + 1, Math.max(0, filteredOptions.length - 1)));
      return;
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault();
      setOpen(true);
      setActiveIndex((current) => Math.max(0, current - 1));
      return;
    }

    if (event.key === 'Enter' && open) {
      event.preventDefault();
      chooseOption(filteredOptions[activeIndex] || (filteredOptions.length === 1 ? filteredOptions[0] : null));
      return;
    }

    if (event.key === 'Escape') {
      event.preventDefault();
      setOpen(false);
      if (selectedOption) setQuery(selectedOption.label);
    }
  };

  let optionIndex = -1;

  return (
    <div className="module001-field module001-task-picker" ref={rootRef}>
      <span id="module001-task-picker-label">Assigned task or authorized activity</span>
      <div className="module001-task-combobox">
        <input
          ref={inputRef}
          className="module001-task-search"
          type="search"
          role="combobox"
          aria-labelledby="module001-task-picker-label"
          aria-autocomplete="list"
          aria-expanded={open && hasOptions}
          aria-controls="module001-task-results"
          aria-activedescendant={open && filteredOptions[activeIndex]
            ? `module001-task-option-${activeIndex}`
            : undefined}
          value={query}
          disabled={disabled || !hasOptions}
          placeholder={hasOptions
            ? 'Search activity, task, project, customer, or request'
            : 'No authorized timer activity available'}
          onFocus={(event) => {
            setOpen(true);
            event.currentTarget.select();
          }}
          onChange={handleInputChange}
          onKeyDown={handleKeyDown}
        />

        {open && hasOptions ? (
          <div
            id="module001-task-results"
            className="module001-task-results"
            role="listbox"
            aria-label="Matching timer activities"
          >
            {groups.length ? groups.map(({ groupLabel, options: groupOptions }) => (
              <section className="module001-task-result-group" key={groupLabel} aria-label={groupLabel}>
                <strong>{groupLabel}</strong>
                {groupOptions.map((option) => {
                  optionIndex += 1;
                  const currentIndex = optionIndex;
                  const selected = option.optionValue === value;
                  return (
                    <button
                      id={`module001-task-option-${currentIndex}`}
                      key={option.optionValue}
                      type="button"
                      role="option"
                      aria-selected={selected}
                      className={currentIndex === activeIndex ? 'active' : ''}
                      onMouseDown={(event) => event.preventDefault()}
                      onMouseEnter={() => setActiveIndex(currentIndex)}
                      onClick={() => chooseOption(option)}
                    >
                      <span>{option.label}</span>
                      {selected ? <small>Selected</small> : null}
                    </button>
                  );
                })}
              </section>
            )) : (
              <div className="module001-task-no-results">No matching activity or task.</div>
            )}
          </div>
        ) : null}
      </div>

      <small>
        {!hasOptions
          ? 'No active assigned tasks or non-project activities are currently available.'
          : selectedOption
            ? `Selected from ${selectedOption.groupLabel}. Start the timer when ready.`
            : `${filteredOptions.length} matching choice${filteredOptions.length === 1 ? '' : 's'} available. Type to open the matching list, then select a result.`}
      </small>
    </div>
  );
}
