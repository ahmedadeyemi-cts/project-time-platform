import { useCallback, useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { authoritativeApi } from '../projectpulse-authoritative-api.js';
import TimesheetTimerView from './TimesheetTimerView.jsx';
import { calculateTimerDuration, formatElapsedSeconds } from './timesheet-duration.js';
import './timesheet-prep.css';
import './module001-runtime-v2.css';

const UUID_PATTERN = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;
const TIMER_TARGET_PATTERN = /^(?:(?:assignment|category):[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}|category-code:[A-Z0-9][A-Z0-9_-]{0,99})$/i;

function isTimesheetRoute() {
  return String(window.location.hash || '#dashboard').replace(/^#/, '').split('?')[0] === 'timesheet';
}

function readSlots() {
  if (!isTimesheetRoute()) return { page: null, switcher: null, workspace: null, recovery: null };
  const page = document.querySelector('#timesheet.timesheet-page');
  if (!page) return { page: null, switcher: null, workspace: null, recovery: null };
  return {
    page,
    switcher: page.querySelector('#module001-view-tab-host[data-projectpulse-react-owned-slot="true"]'),
    workspace: page.querySelector('#module001-enhancement-view-host[data-projectpulse-react-owned-slot="true"]'),
    recovery: page.querySelector('#module001-active-timer-recovery-host[data-projectpulse-react-owned-slot="true"]')
  };
}

async function module001Api(path, options = {}) {
  return authoritativeApi(path, {
    ...options,
    moduleNumber: '001'
  });
}

function timerTargetValue(target = {}) {
  const existing = String(target.selectionValue || '').trim();
  if (TIMER_TARGET_PATTERN.test(existing)) return existing;
  const assignmentId = String(target.assignmentId || target.projectAssignmentId || '').trim();
  if (UUID_PATTERN.test(assignmentId)) return `assignment:${assignmentId}`;
  const categoryId = String(target.nonProjectTimeCategoryId || target.nonProjectCategoryId || target.targetId || '').trim();
  if (UUID_PATTERN.test(categoryId)) return `category:${categoryId}`;
  const categoryCode = String(target.categoryCode || target.targetCode || '').trim().toUpperCase();
  return categoryCode ? `category-code:${categoryCode}` : '';
}

function normalizeTargets(rows = []) {
  const seen = new Set();
  return rows.map((target) => {
    const selectionValue = timerTargetValue(target);
    return {
      ...target,
      selectionValue,
      selectionLabel: target.selectionLabel || target.categoryName || target.taskName || 'Authorized activity',
      groupLabel: target.groupLabel || (target.targetType === 'category' ? 'Non-Project Time' : 'Project Tasks')
    };
  }).filter((target) => {
    if (!target.selectionValue || seen.has(target.selectionValue)) return false;
    seen.add(target.selectionValue);
    return true;
  });
}

function activeTimerLabel(timer) {
  return [
    timer?.customerName,
    timer?.projectCode,
    timer?.taskName || timer?.nonProjectCategoryName
  ].filter(Boolean).join(' · ') || 'Authorized activity';
}

export default function TimesheetEnhancementPortal() {
  const [slots, setSlots] = useState(() => readSlots());
  const [snapshot, setSnapshot] = useState(() => window.__projectPulseModule001Snapshot || null);
  const [timerMode, setTimerMode] = useState(false);
  const [targets, setTargets] = useState([]);
  const [activeTimer, setActiveTimer] = useState(null);
  const [autoStoppedTimer, setAutoStoppedTimer] = useState(null);
  const [history, setHistory] = useState([]);
  const [selectedTarget, setSelectedTarget] = useState('');
  const [classification, setClassification] = useState('normal');
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState('');
  const [message, setMessage] = useState('');
  const [clock, setClock] = useState(() => new Date());

  useEffect(() => {
    const synchronize = () => {
      const next = readSlots();
      setSlots((current) => (
        current.page === next.page
        && current.switcher === next.switcher
        && current.workspace === next.workspace
        && current.recovery === next.recovery
      ) ? current : next);
    };
    synchronize();
    const observer = new MutationObserver(synchronize);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', synchronize);
    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', synchronize);
    };
  }, []);

  useEffect(() => {
    const receive = (event) => setSnapshot(event?.detail || window.__projectPulseModule001Snapshot || null);
    window.addEventListener('projectpulse:module001-state', receive);
    return () => window.removeEventListener('projectpulse:module001-state', receive);
  }, []);

  useEffect(() => {
    document.body.classList.toggle('projectpulse-module001-timer-mode', Boolean(timerMode && slots.page));
    return () => document.body.classList.remove('projectpulse-module001-timer-mode');
  }, [slots.page, timerMode]);

  useEffect(() => {
    if (!slots.switcher) return undefined;
    const leaveTimerMode = (event) => {
      if (event.target.closest('.timesheet-view-button') && !event.target.closest('#module001-start-stop-tab')) {
        setTimerMode(false);
      }
    };
    const switcher = slots.switcher.parentElement;
    switcher?.addEventListener('click', leaveTimerMode, true);
    return () => switcher?.removeEventListener('click', leaveTimerMode, true);
  }, [slots.switcher]);

  const selectedWeekStart = snapshot?.selectedWeekStart || '';

  const loadRuntime = useCallback(async ({ preserveMessage = false } = {}) => {
    if (!slots.page || !selectedWeekStart) return;
    const [activeResult, historyResult, targetResult] = await Promise.allSettled([
      module001Api('/api/timesheet/timers/active'),
      module001Api(`/api/timesheet/timers/history?weekStart=${encodeURIComponent(selectedWeekStart)}`, {
        requiredCollections: ['timers']
      }),
      module001Api(`/api/timesheet/timers/targets?weekStart=${encodeURIComponent(selectedWeekStart)}`, {
        requiredCollections: ['targets']
      })
    ]);

    const errors = [];
    if (activeResult.status === 'fulfilled') {
      const payload = activeResult.value || {};
      if (!Object.prototype.hasOwnProperty.call(payload, 'activeTimer')
          && !Object.prototype.hasOwnProperty.call(payload, 'autoStoppedTimer')) {
        errors.push('The active-timer service returned an incomplete response.');
      } else {
        const timer = payload.activeTimer || payload.ActiveTimer || null;
        const stopped = payload.autoStoppedTimer || payload.AutoStoppedTimer || null;
        setActiveTimer(timer);
        setAutoStoppedTimer(stopped);
        if (timer) {
          setDescription(timer.description || '');
          setClassification(timer.timeClassification || 'normal');
        }
      }
    } else {
      errors.push(activeResult.reason?.message || 'Unable to load the active timer.');
    }

    if (historyResult.status === 'fulfilled') {
      setHistory(historyResult.value.timers || []);
    } else {
      errors.push(historyResult.reason?.message || 'Unable to load timer history.');
    }

    if (targetResult.status === 'fulfilled') {
      setTargets(normalizeTargets(targetResult.value.targets || []));
    } else {
      errors.push(targetResult.reason?.message || 'Unable to load timer activities.');
    }

    if (errors.length) setMessage(errors.join(' '));
    else if (!preserveMessage) setMessage('');
  }, [selectedWeekStart, slots.page]);

  useEffect(() => {
    void loadRuntime();
  }, [loadRuntime]);

  useEffect(() => {
    if (!slots.page) return undefined;
    const refresh = () => void loadRuntime({ preserveMessage: true });
    const interval = window.setInterval(refresh, 5000);
    const visible = () => { if (!document.hidden) refresh(); };
    window.addEventListener('focus', refresh);
    window.addEventListener('projectpulse:auth-session-ready', refresh);
    document.addEventListener('visibilitychange', visible);
    return () => {
      window.clearInterval(interval);
      window.removeEventListener('focus', refresh);
      window.removeEventListener('projectpulse:auth-session-ready', refresh);
      document.removeEventListener('visibilitychange', visible);
    };
  }, [slots.page, loadRuntime]);

  useEffect(() => {
    if (selectedTarget && !targets.some((target) => target.selectionValue === selectedTarget)) {
      setSelectedTarget('');
    }
  }, [selectedTarget, targets]);

  useEffect(() => {
    setClock(new Date());
    if (!activeTimer?.startedAtUtc) return undefined;
    const interval = window.setInterval(() => setClock(new Date()), 1000);
    return () => window.clearInterval(interval);
  }, [activeTimer?.startedAtUtc]);

  const recoveryDuration = useMemo(
    () => activeTimer?.startedAtUtc
      ? calculateTimerDuration(activeTimer.startedAtUtc, clock)
      : { cappedSeconds: 0, roundedMinutes: 0 },
    [activeTimer?.startedAtUtc, clock]
  );

  async function startTimer() {
    const target = targets.find((item) => item.selectionValue === selectedTarget);
    if (!target) {
      setMessage('Select a Project Task, Request / Service Request, or Non-Project Time activity.');
      return;
    }

    setBusy('start');
    setMessage('Starting the server timer…');
    try {
      const codeTarget = target.selectionValue.startsWith('category-code:');
      const result = await module001Api(
        codeTarget ? '/api/timesheet/timers/start-by-code' : '/api/timesheet/timers/start',
        {
          method: 'POST',
          body: JSON.stringify(codeTarget ? {
            nonProjectCategoryCode: target.selectionValue.slice('category-code:'.length),
            timeClassification: classification,
            description,
            timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
          } : {
            assignmentId: target.selectionValue.startsWith('assignment:')
              ? target.selectionValue.slice('assignment:'.length)
              : null,
            nonProjectTimeCategoryId: target.selectionValue.startsWith('category:')
              ? target.selectionValue.slice('category:'.length)
              : null,
            timeClassification: classification,
            description,
            timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
          })
        }
      );
      const timer = result.timer || result.activeTimer || null;
      if (timer) {
        setActiveTimer(timer);
        setDescription(timer.description || description);
        setClassification(timer.timeClassification || classification);
      }
      setMessage('Timer started. The server continues tracking it through refreshes, sign-out, and session expiration.');
      await loadRuntime({ preserveMessage: true });
    } catch (error) {
      const existingTimer = error?.payload?.activeTimer || null;
      if (error?.status === 409 && existingTimer) {
        setActiveTimer(existingTimer);
        setDescription(existingTimer.description || '');
        setClassification(existingTimer.timeClassification || 'normal');
        setMessage('A timer was already running. It has been recovered from the server.');
      } else {
        setMessage(error.message || 'The timer could not be started.');
      }
    } finally {
      setBusy('');
    }
  }

  async function stopTimer() {
    if (!activeTimer) return;
    setBusy('stop');
    setMessage('Stopping the server timer…');
    try {
      const result = await module001Api(`/api/timesheet/timers/${activeTimer.timerSessionId}/stop`, {
        method: 'POST',
        body: JSON.stringify({
          description,
          reason: 'Stopped from Module 001 Timesheet.',
          expectedRowVersion: activeTimer.rowVersion
        })
      });
      setActiveTimer(null);
      setMessage(result.message || 'Timer stopped and its draft time entry was created.');
      window.dispatchEvent(new CustomEvent('projectpulse:module001-timer-changed', { detail: { action: 'stopped' } }));
      await loadRuntime({ preserveMessage: true });
    } catch (error) {
      setMessage(error.message || 'The timer could not be stopped.');
    } finally {
      setBusy('');
    }
  }

  async function discardTimer() {
    if (!activeTimer || !window.confirm('Discard this running timer without creating a time entry?')) return;
    setBusy('discard');
    setMessage('Discarding the server timer…');
    try {
      const result = await module001Api(`/api/timesheet/timers/${activeTimer.timerSessionId}/discard`, {
        method: 'POST',
        body: JSON.stringify({
          reason: 'Discarded after user confirmation.',
          expectedRowVersion: activeTimer.rowVersion
        })
      });
      setActiveTimer(null);
      setDescription('');
      setMessage(result.message || 'Timer discarded.');
      window.dispatchEvent(new CustomEvent('projectpulse:module001-timer-changed', { detail: { action: 'discarded' } }));
      await loadRuntime({ preserveMessage: true });
    } catch (error) {
      setMessage(error.message || 'The timer could not be discarded.');
    } finally {
      setBusy('');
    }
  }

  if (!slots.page || !snapshot) return null;
  const viewAs = Boolean(snapshot.isViewAs);

  return <>
    {slots.switcher ? createPortal(
      <button
        id="module001-start-stop-tab"
        type="button"
        role="tab"
        aria-selected={timerMode}
        className={timerMode ? 'timesheet-view-button active' : 'timesheet-view-button'}
        onClick={() => setTimerMode(true)}
      >
        <strong>Start / Stop Timer</strong>
        <small>Track active work in real time</small>
      </button>,
      slots.switcher
    ) : null}

    {slots.workspace ? createPortal(
      timerMode ? <TimesheetTimerView
        targets={targets}
        history={history}
        activeTimer={activeTimer}
        selectedTargetValue={selectedTarget}
        classification={classification}
        description={description}
        isViewAs={viewAs}
        busy={Boolean(busy)}
        statusMessage={message}
        onSelectTarget={setSelectedTarget}
        onClassificationChange={setClassification}
        onDescriptionChange={setDescription}
        onStart={startTimer}
        onStop={stopTimer}
        onDiscard={discardTimer}
      /> : null,
      slots.workspace
    ) : null}

    {slots.recovery && !timerMode && activeTimer ? createPortal(
      <section className="module001-server-timer-recovery" aria-label="Running timer recovered from server">
        <div>
          <p className="eyebrow">RUNNING TIMER</p>
          <h3>{activeTimerLabel(activeTimer)}</h3>
          <p>The timer is server-owned and remains active through refreshes, sign-out, and session expiration.</p>
          <small>Started {new Date(activeTimer.startedAtUtc).toLocaleString()} · Rounded draft {(recoveryDuration.roundedMinutes / 60).toFixed(2)} hours</small>
        </div>
        <strong className="module001-server-timer-clock" aria-live="polite">{formatElapsedSeconds(recoveryDuration.cappedSeconds)}</strong>
        <div className="module001-server-timer-actions">
          <button type="button" onClick={() => setTimerMode(true)}>Open timer</button>
          <button type="button" disabled={Boolean(busy) || viewAs} onClick={stopTimer}>{busy === 'stop' ? 'Stopping…' : 'Stop timer'}</button>
          <button type="button" className="danger" disabled={Boolean(busy) || viewAs} onClick={discardTimer}>{busy === 'discard' ? 'Discarding…' : 'Discard'}</button>
        </div>
      </section>,
      slots.recovery
    ) : null}

    {slots.recovery && !activeTimer && autoStoppedTimer ? createPortal(
      <section className="module001-server-timer-recovery auto-stopped" aria-label="Automatically stopped timer">
        <div><p className="eyebrow">TIMER SAFETY LIMIT</p><h3>{activeTimerLabel(autoStoppedTimer)}</h3><p>The server stopped this timer at the 12-hour limit. Review the resulting draft entry before submission.</p></div>
        <button type="button" onClick={() => setAutoStoppedTimer(null)}>Dismiss</button>
      </section>,
      slots.recovery
    ) : null}
  </>;
}
