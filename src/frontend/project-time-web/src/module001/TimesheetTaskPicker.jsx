import { useEffect, useId, useMemo, useRef, useState } from 'react';

const GROUP_ORDER = ['Requests / Service Requests', 'Project Tasks', 'Non-Project Time'];

function normalizeGroup(value) {
  const label = String(value || '').trim();
  if (label === 'Service Request Tasks') return 'Requests / Service Requests';
  if (label === 'Regular Tasks') return 'Project Tasks';
  return GROUP_ORDER.includes(label) ? label : 'Project Tasks';
}

function searchableText(target) {
  return [
    target.selectionLabel,
    target.customerName,
    target.projectCode,
    target.projectName,
    target.taskCode,
    target.taskName,
    target.serviceRequestNumber,
    target.categoryCode,
    target.categoryName,
    normalizeGroup(target.groupLabel)
  ].filter(Boolean).join(' ').toLowerCase();
}

function secondaryLabel(target) {
  return [
    target.customerName,
    target.projectCode,
    target.serviceRequestNumber,
    target.taskCode,
    target.categoryCode
  ].filter(Boolean).join(' · ');
}

export default function TimesheetTaskPicker({
  targets = [],
  selectedValues = [],
  activeValues = [],
  maxSelections = 5,
  disabled = false,
  onChange = () => {}
}) {
  const inputId = useId();
  const listId = useId();
  const rootRef = useRef(null);
  const inputRef = useRef(null);
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const [announcement, setAnnouncement] = useState('');

  const normalizedTargets = useMemo(() => targets.map((target) => ({
    ...target,
    normalizedGroup: normalizeGroup(target.groupLabel),
    searchText: searchableText(target)
  })), [targets]);

  const targetByValue = useMemo(
    () => new Map(normalizedTargets.map((target) => [target.selectionValue, target])),
    [normalizedTargets]
  );
  const selectedSet = useMemo(() => new Set(selectedValues), [selectedValues]);
  const activeSet = useMemo(() => new Set(activeValues), [activeValues]);
  const limit = Math.max(0, Number(maxSelections) || 0);
  const limitReached = limit === 0 || selectedValues.length >= limit;

  const filteredGroups = useMemo(() => {
    const needle = query.trim().toLowerCase();
    const visible = normalizedTargets.filter((target) => !needle || target.searchText.includes(needle));
    return GROUP_ORDER.map((group) => ({
      group,
      items: visible.filter((target) => target.normalizedGroup === group)
    })).filter((entry) => entry.items.length > 0);
  }, [normalizedTargets, query]);

  const resultCount = filteredGroups.reduce((sum, entry) => sum + entry.items.length, 0);

  useEffect(() => {
    const closeOnOutsideClick = (event) => {
      if (!rootRef.current?.contains(event.target)) setOpen(false);
    };
    document.addEventListener('pointerdown', closeOnOutsideClick);
    return () => document.removeEventListener('pointerdown', closeOnOutsideClick);
  }, []);

  useEffect(() => {
    const validValues = selectedValues
      .filter((value) => targetByValue.has(value) && !activeSet.has(value))
      .slice(0, limit);
    if (validValues.length !== selectedValues.length
        || validValues.some((value, index) => value !== selectedValues[index])) {
      onChange(validValues);
    }
  }, [activeSet, limit, onChange, selectedValues, targetByValue]);

  function setSelection(value, checked) {
    if (disabled || activeSet.has(value)) return;
    if (!checked) {
      const next = selectedValues.filter((item) => item !== value);
      onChange(next);
      setAnnouncement(`${targetByValue.get(value)?.selectionLabel || 'Activity'} removed.`);
      return;
    }
    if (selectedSet.has(value)) return;
    if (limitReached) {
      setAnnouncement(`You can select up to ${limit} ${limit === 1 ? 'activity' : 'activities'} in this start group. Remove one before selecting another.`);
      return;
    }
    const next = [...selectedValues, value];
    onChange(next);
    setAnnouncement(`${targetByValue.get(value)?.selectionLabel || 'Activity'} selected. ${next.length} of ${limit}.`);
  }

  function removeSelection(value) {
    setSelection(value, false);
    window.requestAnimationFrame(() => inputRef.current?.focus());
  }

  function handleKeyDown(event) {
    if (event.key === 'Escape') {
      setOpen(false);
      return;
    }
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setOpen(true);
      window.requestAnimationFrame(() => rootRef.current?.querySelector('.module001-task-option input:not(:disabled)')?.focus());
    }
  }

  return (
    <div className="module001-task-picker" ref={rootRef}>
      <div className="module001-task-picker-heading">
        <label htmlFor={inputId}>Assigned task or authorized activity</label>
        <span>{selectedValues.length} of {limit} selected</span>
      </div>

      <div className={`module001-task-combobox ${open ? 'is-open' : ''} ${disabled ? 'is-disabled' : ''}`}>
        {selectedValues.length > 0 ? (
          <div className="module001-task-selection-chips" aria-label="Selected activities">
            {selectedValues.map((value) => {
              const target = targetByValue.get(value);
              if (!target) return null;
              return (
                <span className="module001-task-selection-chip" key={value}>
                  <span title={target.selectionLabel}>{target.selectionLabel}</span>
                  <button
                    type="button"
                    aria-label={`Remove ${target.selectionLabel}`}
                    disabled={disabled}
                    onClick={() => removeSelection(value)}
                  >
                    ×
                  </button>
                </span>
              );
            })}
            <button
              type="button"
              className="module001-task-clear"
              disabled={disabled}
              onClick={() => {
                onChange([]);
                setAnnouncement('All selected activities cleared.');
                inputRef.current?.focus();
              }}
            >
              Clear
            </button>
          </div>
        ) : null}

        <div className="module001-task-search-row">
          <span aria-hidden="true" className="module001-task-search-icon">⌕</span>
          <input
            id={inputId}
            ref={inputRef}
            type="search"
            role="combobox"
            aria-autocomplete="list"
            aria-controls={listId}
            aria-expanded={open}
            aria-haspopup="listbox"
            autoComplete="off"
            disabled={disabled || limit === 0}
            value={query}
            placeholder={limit === 0 ? 'Five timers are already active' : 'Search activity, task, project, customer, or request'}
            onFocus={() => setOpen(true)}
            onClick={() => setOpen(true)}
            onChange={(event) => {
              setQuery(event.target.value);
              setOpen(true);
            }}
            onKeyDown={handleKeyDown}
          />
          <button
            type="button"
            className="module001-task-chevron"
            aria-label={open ? 'Close activity list' : 'Open activity list'}
            disabled={disabled || limit === 0}
            onClick={() => {
              setOpen((current) => !current);
              inputRef.current?.focus();
            }}
          >
            {open ? '▴' : '▾'}
          </button>
        </div>
      </div>

      {open && !disabled && limit > 0 ? (
        <div className="module001-task-results" id={listId} role="listbox" aria-multiselectable="true">
          <div className="module001-task-results-summary">
            <span>{resultCount} matching {resultCount === 1 ? 'activity' : 'activities'}</span>
            <span>{limit - selectedValues.length} selection {limit - selectedValues.length === 1 ? 'slot' : 'slots'} remaining</span>
          </div>

          {filteredGroups.length === 0 ? (
            <div className="module001-task-empty">
              <strong>No authorized activity matched “{query}”.</strong>
              <span>Try a customer, project code, task name, service request number, or non-project category.</span>
            </div>
          ) : filteredGroups.map(({ group, items }) => (
            <section className="module001-task-group" key={group} aria-label={group}>
              <header>
                <strong>{group}</strong>
                <span>{items.length}</span>
              </header>
              <div>
                {items.map((target) => {
                  const checked = selectedSet.has(target.selectionValue);
                  const running = activeSet.has(target.selectionValue);
                  const unavailable = running || (!checked && limitReached);
                  return (
                    <label
                      className={`module001-task-option ${checked ? 'is-selected' : ''} ${running ? 'is-running' : ''}`}
                      key={target.selectionValue}
                      role="option"
                      aria-selected={checked}
                      title={running ? 'This activity already has a running timer.' : undefined}
                    >
                      <input
                        type="checkbox"
                        checked={checked}
                        disabled={unavailable}
                        onChange={(event) => setSelection(target.selectionValue, event.target.checked)}
                      />
                      <span className="module001-task-option-copy">
                        <strong>{target.selectionLabel}</strong>
                        {secondaryLabel(target) ? <small>{secondaryLabel(target)}</small> : null}
                      </span>
                      {running ? <span className="module001-task-running-badge">Running</span> : null}
                    </label>
                  );
                })}
              </div>
            </section>
          ))}
        </div>
      ) : null}

      <p className="module001-task-picker-help">
        Select with the checkboxes. You can run up to five activity timers at once and stop them individually or together.
      </p>
      <span className="sr-only" aria-live="polite">{announcement}</span>
    </div>
  );
}
