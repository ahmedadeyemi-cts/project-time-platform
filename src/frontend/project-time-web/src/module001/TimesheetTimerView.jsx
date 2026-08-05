import { useEffect, useMemo, useState } from 'react';
import TimesheetAiDescriptionAssistant from './TimesheetAiDescriptionAssistant.jsx';
import TimesheetTaskPicker from './TimesheetTaskPicker.jsx';
import { calculateTimerDuration, formatElapsedSeconds } from './timesheet-duration.js';

function timerTargetValue(timer) {
  if (timer?.assignmentId) return `assignment:${timer.assignmentId}`;
  if (timer?.nonProjectCategoryId) return `category:${timer.nonProjectCategoryId}`;
  if (timer?.nonProjectCategoryCode) return `category-code:${timer.nonProjectCategoryCode}`;
  return '';
}

function timerLabel(timer) {
  return [
    timer?.customerName,
    timer?.projectCode,
    timer?.taskName || timer?.nonProjectCategoryName
  ].filter(Boolean).join(' · ') || 'Authorized activity';
}

function timerAsTarget(timer, targets) {
  const selectionValue = timerTargetValue(timer);
  const authoritativeTarget = targets.find((target) => target.selectionValue === selectionValue) || {};
  return {
    ...authoritativeTarget,
    targetType: timer?.assignmentId ? 'assignment' : 'category',
    selectionValue,
    selectionLabel: timerLabel(timer),
    assignmentId: timer?.assignmentId || authoritativeTarget.assignmentId || null,
    projectId: timer?.projectId || authoritativeTarget.projectId || null,
    taskId: timer?.taskId || authoritativeTarget.taskId || null,
    nonProjectTimeCategoryId: timer?.nonProjectCategoryId
      || authoritativeTarget.nonProjectTimeCategoryId
      || authoritativeTarget.nonProjectCategoryId
      || null,
    customerName: timer?.customerName || '',
    projectCode: timer?.projectCode || '',
    projectName: timer?.projectName || '',
    taskCode: timer?.taskCode || '',
    taskName: timer?.taskName || '',
    categoryCode: timer?.nonProjectCategoryCode || '',
    categoryName: timer?.nonProjectCategoryName || ''
  };
}

function TimerHistory({ history }) {
  return (
    <section className="module001-timer-history">
      <div className="module001-timer-section-heading">
        <div>
          <p className="eyebrow">Timer history</p>
          <h3>This week</h3>
        </div>
        <span>{history.length}</span>
      </div>
      {history.length === 0 ? (
        <p className="module001-timer-empty">No timer sessions for this week.</p>
      ) : (
        <div className="module001-timer-history-list">
          {history.map((timer) => (
            <article key={timer.timerSessionId}>
              <div>
                <strong>{timerLabel(timer)}</strong>
                <span>{new Date(timer.startedAtUtc).toLocaleString()} · {timer.timeClassification === 'afterhours' ? 'Afterhours' : 'Normal time'}</span>
              </div>
              <div>
                <strong>{(Number(timer.roundedMinutes || 0) / 60).toFixed(2)} hrs</strong>
                <span>{String(timer.timerStatus || '').replaceAll('_', ' ')}</span>
              </div>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}

export default function TimesheetTimerView({
  targets = [],
  history = [],
  activeTimers = [],
  selectedTargetValues = [],
  classification = 'normal',
  draftDescription = '',
  timerDescriptions = {},
  maximumConcurrentTimers = 5,
  isViewAs = false,
  busyAction = '',
  statusMessage = '',
  onSelectTargets,
  onClassificationChange,
  onDraftDescriptionChange,
  onTimerDescriptionChange,
  onStart,
  onStopOne,
  onStopAll,
  onDiscardOne
}) {
  const [clock, setClock] = useState(() => new Date());

  useEffect(() => {
    setClock(new Date());
    if (activeTimers.length === 0) return undefined;
    const interval = window.setInterval(() => setClock(new Date()), 1000);
    return () => window.clearInterval(interval);
  }, [activeTimers]);

  const durations = useMemo(() => new Map(activeTimers.map((timer) => [
    timer.timerSessionId,
    calculateTimerDuration(timer.startedAtUtc, clock)
  ])), [activeTimers, clock]);

  const activeValues = useMemo(
    () => activeTimers.map(timerTargetValue).filter(Boolean),
    [activeTimers]
  );
  const selectedTargets = useMemo(
    () => selectedTargetValues.map((value) => targets.find((target) => target.selectionValue === value)).filter(Boolean),
    [selectedTargetValues, targets]
  );
  const availableSlots = Math.max(0, maximumConcurrentTimers - activeTimers.length);
  const longestElapsed = activeTimers.reduce(
    (maximum, timer) => Math.max(maximum, durations.get(timer.timerSessionId)?.cappedSeconds || 0),
    0
  );
  const busy = Boolean(busyAction);

  return (
    <section className="module001-timer-shell" aria-label="Real-time Timesheet timer">
      <header className="module001-timer-header">
        <div>
          <p className="eyebrow">Real-time Timesheet</p>
          <h2>Start / Stop Timer</h2>
          <p>
            Run up to five authorized activity timers at once. Each timer is server-owned, survives refreshes and sign-out, rounds to the next quarter hour, and has a 24-hour safety cap.
          </p>
        </div>
        <div className="module001-timer-header-metric" aria-live="polite">
          <strong>{activeTimers.length > 0 ? formatElapsedSeconds(longestElapsed) : '00:00:00'}</strong>
          <span>{activeTimers.length} of {maximumConcurrentTimers} active</span>
        </div>
      </header>

      {statusMessage ? <div className="module001-timer-status" role="status">{statusMessage}</div> : null}
      {isViewAs ? (
        <div className="module001-timer-view-as" role="note">
          Administrator View-As is read-only. Exit View-As to start, stop, or discard timers.
        </div>
      ) : null}

      {activeTimers.length > 0 ? (
        <section className="module001-active-timers">
          <div className="module001-timer-section-heading">
            <div>
              <p className="eyebrow">Active now</p>
              <h3>{activeTimers.length} running {activeTimers.length === 1 ? 'timer' : 'timers'}</h3>
            </div>
            <button
              type="button"
              className="module001-stop-all"
              disabled={busy || isViewAs}
              onClick={onStopAll}
            >
              {busyAction === 'stop-all' ? 'Stopping all…' : 'Stop all timers'}
            </button>
          </div>

          <div className="module001-active-timer-grid">
            {activeTimers.map((timer) => {
              const duration = durations.get(timer.timerSessionId) || { cappedSeconds: 0, roundedMinutes: 0, isExpired: false };
              const description = timerDescriptions[timer.timerSessionId] ?? timer.description ?? '';
              const stopBusy = busyAction === `stop:${timer.timerSessionId}`;
              const discardBusy = busyAction === `discard:${timer.timerSessionId}`;
              return (
                <article className={`module001-active-timer-card ${duration.isExpired || timer.expired ? 'is-expired' : ''}`} key={timer.timerSessionId}>
                  <header>
                    <div>
                      <span className="module001-running-indicator">Running</span>
                      <h4>{timerLabel(timer)}</h4>
                    </div>
                    <strong className="module001-active-timer-clock">{formatElapsedSeconds(duration.cappedSeconds)}</strong>
                  </header>

                  <dl>
                    <div><dt>Started</dt><dd>{new Date(timer.startedAtUtc).toLocaleString()}</dd></div>
                    <div><dt>Classification</dt><dd>{timer.timeClassification === 'afterhours' ? 'Afterhours' : 'Normal time'}</dd></div>
                    <div><dt>Rounded draft</dt><dd>{(duration.roundedMinutes / 60).toFixed(2)} hours</dd></div>
                    <div><dt>Maximum</dt><dd>24.00 hours</dd></div>
                  </dl>

                  {duration.isExpired || timer.expired ? (
                    <p className="module001-timer-expired-warning">
                      This timer reached the 24-hour safety limit. Stop it to create its draft entry, or discard it if the timer was left running accidentally.
                    </p>
                  ) : null}

                  <label className="module001-timer-description-field">
                    Work description
                    <textarea
                      value={description}
                      maxLength={4000}
                      disabled={busy || isViewAs}
                      placeholder="Describe what you reviewed, configured, tested, documented, coordinated, or troubleshot."
                      onChange={(event) => onTimerDescriptionChange(timer.timerSessionId, event.target.value)}
                    />
                  </label>

                  <TimesheetAiDescriptionAssistant
                    compact
                    targets={[timerAsTarget(timer, targets)]}
                    classification={timer.timeClassification || 'normal'}
                    value={description}
                    disabled={busy || isViewAs}
                    onApply={(suggestion) => onTimerDescriptionChange(timer.timerSessionId, suggestion)}
                  />

                  <footer>
                    <button
                      type="button"
                      className="primary-action"
                      disabled={busy || isViewAs}
                      onClick={() => onStopOne(timer)}
                    >
                      {stopBusy ? 'Stopping…' : 'Stop this timer'}
                    </button>
                    <button
                      type="button"
                      className="danger"
                      disabled={busy || isViewAs}
                      onClick={() => onDiscardOne(timer)}
                    >
                      {discardBusy ? 'Discarding…' : 'Discard'}
                    </button>
                  </footer>
                </article>
              );
            })}
          </div>
        </section>
      ) : (
        <div className="module001-ready-banner">
          <strong>Ready to start</strong>
          <span>Select one or more authorized activities below, then choose Start selected timers.</span>
        </div>
      )}

      <section className="module001-start-timers-panel">
        <div className="module001-timer-section-heading">
          <div>
            <p className="eyebrow">Add work</p>
            <h3>{availableSlots > 0 ? `Start up to ${availableSlots} more ${availableSlots === 1 ? 'timer' : 'timers'}` : 'Maximum active timers reached'}</h3>
          </div>
          <span>{maximumConcurrentTimers} maximum</span>
        </div>

        <TimesheetTaskPicker
          targets={targets}
          selectedValues={selectedTargetValues}
          activeValues={activeValues}
          maxSelections={availableSlots}
          disabled={busy || isViewAs}
          onChange={onSelectTargets}
        />

        <fieldset className="module001-time-classification" disabled={busy || isViewAs || availableSlots === 0}>
          <legend>Time classification</legend>
          <label><input type="radio" name="module001-timer-classification" value="normal" checked={classification === 'normal'} onChange={() => onClassificationChange('normal')} /> Normal</label>
          <label><input type="radio" name="module001-timer-classification" value="afterhours" checked={classification === 'afterhours'} onChange={() => onClassificationChange('afterhours')} /> Afterhours</label>
        </fieldset>

        <label className="module001-timer-description-field module001-new-timer-description">
          Work description
          <textarea
            value={draftDescription}
            maxLength={4000}
            disabled={busy || isViewAs || availableSlots === 0}
            placeholder="Type a rough note. The same starting description will be applied to every selected timer and can be edited separately before each timer is stopped."
            onChange={(event) => onDraftDescriptionChange(event.target.value)}
          />
        </label>

        <TimesheetAiDescriptionAssistant
          targets={selectedTargets}
          classification={classification}
          value={draftDescription}
          disabled={busy || isViewAs || availableSlots === 0}
          onApply={onDraftDescriptionChange}
        />

        <div className="module001-start-timer-actions">
          <div>
            <strong>{selectedTargetValues.length} selected</strong>
            <span>{selectedTargetValues.length > 1 ? 'All selected timers will begin from the same server timestamp.' : 'The server timestamp is authoritative.'}</span>
          </div>
          <button
            type="button"
            className="primary-action"
            disabled={busy || isViewAs || selectedTargetValues.length === 0 || availableSlots === 0}
            onClick={onStart}
          >
            {busyAction === 'start' ? 'Starting…' : `Start selected ${selectedTargetValues.length === 1 ? 'timer' : 'timers'}`}
          </button>
        </div>
      </section>

      <TimerHistory history={history} />
    </section>
  );
}
