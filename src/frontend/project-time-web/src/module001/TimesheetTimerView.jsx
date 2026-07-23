import { useEffect, useMemo, useState } from 'react';
import TimesheetTaskPicker from './TimesheetTaskPicker.jsx';
import { calculateTimerDuration, formatElapsedSeconds } from './timesheet-duration.js';
import './timesheet-prep.css';

export default function TimesheetTimerView({
  assignedTasks = [],
  activeTimer = null,
  selectedAssignmentId = '',
  isViewAs = false,
  onSelectAssignment = () => {},
  onStart = () => {},
  onStop = () => {},
  onDiscard = () => {},
  onDescriptionChange = () => {}
}) {
  const [clock, setClock] = useState(() => new Date());

  useEffect(() => {
    if (!activeTimer?.startedAtUtc) return undefined;
    const handle = window.setInterval(() => setClock(new Date()), 1000);
    return () => window.clearInterval(handle);
  }, [activeTimer?.startedAtUtc]);

  const duration = useMemo(
    () => activeTimer?.startedAtUtc
      ? calculateTimerDuration(activeTimer.startedAtUtc, clock)
      : { cappedSeconds: 0, roundedMinutes: 0, isExpired: false },
    [activeTimer?.startedAtUtc, clock]
  );

  const mutationDisabled = isViewAs;

  return (
    <section className="module001-timer-view" aria-labelledby="module001-timer-title">
      <header>
        <div>
          <p className="eyebrow">REAL-TIME TIMESHEET</p>
          <h3 id="module001-timer-title">Start / Stop Timer</h3>
          <p>Track active work in real time. Server timestamps remain authoritative after integration.</p>
        </div>
        <strong className="module001-timer-clock">{formatElapsedSeconds(duration.cappedSeconds)}</strong>
      </header>

      {isViewAs ? <div className="module001-warning">Timer actions are disabled during View-As.</div> : null}
      {duration.isExpired ? <div className="module001-warning">This timer reached the 12-hour maximum and must be auto-stopped by the server.</div> : null}

      <TimesheetTaskPicker
        tasks={assignedTasks}
        value={activeTimer?.assignmentId || selectedAssignmentId}
        disabled={Boolean(activeTimer) || mutationDisabled}
        onChange={onSelectAssignment}
      />

      <div className="module001-timer-meta">
        <span>Started: {activeTimer?.startedAtUtc ? new Date(activeTimer.startedAtUtc).toLocaleString() : 'Not running'}</span>
        <span>Rounded draft: {(duration.roundedMinutes / 60).toFixed(2)} hours</span>
        <span>Maximum: 12.00 hours</span>
      </div>

      <label className="module001-field">
        <span>Work description</span>
        <textarea
          value={activeTimer?.description || ''}
          disabled={mutationDisabled}
          placeholder="Describe the work before submitting the week."
          onChange={(event) => onDescriptionChange(event.target.value)}
        />
        <small>A description may be added later, but submission must remain blocked until it is complete.</small>
      </label>

      <div className="module001-timer-actions">
        {!activeTimer ? (
          <button type="button" disabled={!selectedAssignmentId || mutationDisabled} onClick={onStart}>Start timer</button>
        ) : (
          <>
            <button type="button" disabled={mutationDisabled} onClick={onStop}>Stop timer</button>
            <button type="button" className="secondary" disabled={mutationDisabled} onClick={onDiscard}>Discard</button>
          </>
        )}
      </div>
    </section>
  );
}
