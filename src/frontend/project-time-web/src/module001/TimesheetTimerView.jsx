import { useEffect, useMemo, useState } from 'react';
import TimesheetTaskPicker from './TimesheetTaskPicker.jsx';
import { calculateTimerDuration, formatElapsedSeconds } from './timesheet-duration.js';
import './timesheet-prep.css';

export default function TimesheetTimerView({
  targets = [], history = [], activeTimer = null, selectedTargetValue = '',
  classification = 'normal', description = '', isViewAs = false, busy = false,
  statusMessage = '', onSelectTarget = () => {}, onClassificationChange = () => {},
  onStart = () => {}, onStop = () => {}, onDiscard = () => {}, onDescriptionChange = () => {}
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
  const selectedLabel = activeTimer
    ? [activeTimer.customerName, activeTimer.projectCode, activeTimer.taskName || activeTimer.nonProjectCategoryName].filter(Boolean).join(' · ')
    : '';
  const mutationDisabled = isViewAs || busy;

  return (
    <section className="module001-timer-view" aria-labelledby="module001-timer-title">
      <header>
        <div><p className="eyebrow">REAL-TIME TIMESHEET</p><h3 id="module001-timer-title">Start / Stop Timer</h3><p>Track active work in real time. The server timestamp and 12-hour cap are authoritative.</p></div>
        <strong className="module001-timer-clock" aria-live="polite">{formatElapsedSeconds(duration.cappedSeconds)}</strong>
      </header>
      {statusMessage ? <div className="module001-status" role="status">{statusMessage}</div> : null}
      {isViewAs ? <div className="module001-warning">Timer actions are disabled during View-As.</div> : null}
      {activeTimer?.autoStopped || duration.isExpired ? <div className="module001-warning">This timer reached the 12-hour maximum. Its draft entry must be reviewed before submission.</div> : null}
      {activeTimer ? <div className="module001-active-target"><span>Active task</span><strong>{selectedLabel || 'Authorized non-project activity'}</strong></div> : (
        <TimesheetTaskPicker tasks={targets} value={selectedTargetValue} disabled={mutationDisabled} onChange={onSelectTarget} />
      )}
      <fieldset className="module001-classification" disabled={Boolean(activeTimer) || mutationDisabled}>
        <legend>Time classification</legend>
        <label><input type="radio" name="module001-timer-classification" value="normal" checked={(activeTimer?.timeClassification || classification) === 'normal'} onChange={() => onClassificationChange('normal')} />Normal</label>
        <label><input type="radio" name="module001-timer-classification" value="afterhours" checked={(activeTimer?.timeClassification || classification) === 'afterhours'} onChange={() => onClassificationChange('afterhours')} />Afterhours</label>
      </fieldset>
      <div className="module001-timer-meta">
        <span>Started: {activeTimer?.startedAtUtc ? new Date(activeTimer.startedAtUtc).toLocaleString() : 'Not running'}</span>
        <span>Rounded draft: {(duration.roundedMinutes / 60).toFixed(2)} hours</span><span>Maximum: 12.00 hours</span>
      </div>
      <label className="module001-field"><span>Work description</span><textarea value={description} disabled={mutationDisabled} placeholder="Describe the work before submitting the week." onChange={(event) => onDescriptionChange(event.target.value)} /><small>A description may be added before, during, or after timing, but is required before submission.</small></label>
      <div className="module001-timer-actions">
        {!activeTimer ? <button type="button" disabled={!selectedTargetValue || mutationDisabled} onClick={onStart}>{busy ? 'Starting…' : 'Start timer'}</button> : <><button type="button" disabled={mutationDisabled} onClick={onStop}>{busy ? 'Stopping…' : 'Stop timer'}</button><button type="button" className="secondary" disabled={mutationDisabled} onClick={onDiscard}>Discard</button></>}
      </div>
      <div className="module001-timer-history" aria-label="Current week timer history"><h4>Timer history</h4>{history.length === 0 ? <p>No timer sessions for this week.</p> : <ul>{history.map((timer) => <li key={timer.timerSessionId}><span>{timer.taskName || timer.nonProjectCategoryName || 'Activity'}</span><strong>{(Number(timer.roundedMinutes || 0) / 60).toFixed(2)} hrs</strong><small>{timer.timerStatus}</small></li>)}</ul>}</div>
    </section>
  );
}
