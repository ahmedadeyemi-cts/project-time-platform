import { useEffect, useId, useMemo, useRef, useState } from 'react';

export default function AnalyticsMultiSelect({
  label,
  options = [],
  values = [],
  onChange,
  locked = false,
  lockedReason = '',
  placeholder = 'All',
  detail = '',
  maxChips = 4
}) {
  const id = useId();
  const rootRef = useRef(null);
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  const selected = useMemo(() => new Set((values ?? []).map(String)), [values]);
  const available = useMemo(() => options.filter((option) => {
    const query = search.trim().toLowerCase();
    return !query
      || `${option.label ?? ''} ${option.detail ?? ''}`.toLowerCase().includes(query);
  }), [options, search]);
  const selectedOptions = useMemo(() => options.filter((option) => selected.has(String(option.value))), [options, selected]);

  useEffect(() => {
    const close = (event) => {
      if (!rootRef.current?.contains(event.target)) setOpen(false);
    };
    document.addEventListener('pointerdown', close);
    return () => document.removeEventListener('pointerdown', close);
  }, []);

  function toggle(value) {
    if (locked) return;
    const next = new Set(selected);
    if (next.has(String(value))) next.delete(String(value));
    else next.add(String(value));
    onChange?.([...next]);
  }

  function selectVisible() {
    if (locked) return;
    const next = new Set(selected);
    available.filter((option) => !option.locked).forEach((option) => next.add(String(option.value)));
    onChange?.([...next]);
  }

  function clear() {
    if (!locked) onChange?.([]);
  }

  return (
    <div className={`analytics-multiselect ${open ? 'is-open' : ''} ${locked ? 'is-locked' : ''}`} ref={rootRef}>
      <div className="analytics-multiselect-label-row">
        <label id={`${id}-label`}>{label}</label>
        <span>{selectedOptions.length ? `${selectedOptions.length} selected` : placeholder}</span>
      </div>
      <button
        type="button"
        className="analytics-multiselect-trigger"
        aria-labelledby={`${id}-label`}
        aria-expanded={open}
        aria-controls={`${id}-menu`}
        disabled={locked}
        onClick={() => setOpen((current) => !current)}
      >
        <span className="analytics-multiselect-chips">
          {selectedOptions.slice(0, maxChips).map((option) => (
            <span className="analytics-selection-chip" key={option.value}>
              {option.label}
              {!locked ? (
                <span
                  role="button"
                  tabIndex={0}
                  aria-label={`Remove ${option.label}`}
                  onClick={(event) => { event.stopPropagation(); toggle(option.value); }}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                      event.preventDefault();
                      event.stopPropagation();
                      toggle(option.value);
                    }
                  }}
                >×</span>
              ) : null}
            </span>
          ))}
          {selectedOptions.length > maxChips ? <span className="analytics-selection-chip more">+{selectedOptions.length - maxChips} more</span> : null}
          {!selectedOptions.length ? <span className="analytics-multiselect-placeholder">{placeholder}</span> : null}
        </span>
        <span aria-hidden="true">⌄</span>
      </button>
      {locked && lockedReason ? <small className="analytics-locked-reason">{lockedReason}</small> : detail ? <small>{detail}</small> : null}
      {open && !locked ? (
        <div className="analytics-multiselect-menu" id={`${id}-menu`} role="listbox" aria-multiselectable="true">
          <div className="analytics-multiselect-toolbar">
            <input
              type="search"
              value={search}
              autoFocus
              onChange={(event) => setSearch(event.target.value)}
              placeholder={`Search ${label.toLowerCase()}…`}
              aria-label={`Search ${label}`}
            />
            <button type="button" onClick={selectVisible}>Select visible</button>
            <button type="button" onClick={clear}>Clear</button>
          </div>
          <div className="analytics-multiselect-options">
            {available.map((option) => (
              <label className={option.locked ? 'is-disabled' : ''} key={option.value}>
                <input
                  type="checkbox"
                  checked={selected.has(String(option.value))}
                  disabled={option.locked}
                  onChange={() => toggle(option.value)}
                />
                <span>
                  <strong>{option.label}</strong>
                  {option.detail ? <small>{option.detail}</small> : null}
                </span>
              </label>
            ))}
            {!available.length ? <div className="analytics-multiselect-empty">No authorized options match this search.</div> : null}
          </div>
        </div>
      ) : null}
    </div>
  );
}
