import { useCallback, useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { authoritativeApi } from '../projectpulse-authoritative-api.js';
import TimesheetTimerView from './TimesheetTimerView.jsx';
import { calculateTimerDuration, formatElapsedSeconds } from './timesheet-duration.js';
import './timesheet-prep.css';
import './module001-runtime-v2.css';
import './module001-multi-timer.css';

const MOBILE_KEY = 'projectPulseModule001MobileMode';
const UUID_PATTERN = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;
const TIMER_TARGET_PATTERN = /^(?:(?:assignment|category):[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}|category-code:[A-Z0-9][A-Z0-9_-]{0,99})$/i;

function isTimesheetRoute() {
  return String(window.location.hash || '#dashboard').replace(/^#/, '').split('?')[0] === 'timesheet';
}

function readSlots() {
  const empty = { page: null, switcher: null, toolbar: null, workspace: null, recovery: null };
  if (!isTimesheetRoute()) return empty;
  const page = document.querySelector('#timesheet.timesheet-page');
  if (!page) return empty;
  return {
    page,
    switcher: page.querySelector('#module001-view-tab-host[data-projectpulse-react-owned-slot="true"]'),
    toolbar: page.querySelector('#module001-toolbar-host[data-projectpulse-react-owned-slot="true"]'),
    workspace: page.querySelector('#module001-enhancement-view-host[data-projectpulse-react-owned-slot="true"]'),
    recovery: page.querySelector('#module001-active-timer-recovery-host[data-projectpulse-react-owned-slot="true"]')
  };
}

async function module001Api(path, options = {}) {
  return authoritativeApi(path, { ...options, moduleNumber: '001' });
}

async function loadTimerHistory(weekStart) {
  const encodedWeekStart = encodeURIComponent(weekStart);
  try {
    return await module001Api(`/api/timesheet/timers/history-v2?weekStart=${encodedWeekStart}`, {
      requiredCollections: ['timers']
    });
  } catch (error) {
    const status = Number(error?.status || 0);
    if (![404, 405, 501].includes(status)) throw error;
    return module001Api(`/api/timesheet/timers/history?weekStart=${encodedWeekStart}`, {
      requiredCollections: ['timers']
    });
  }
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

function timerValue(timer = {}) {
  if (timer.assignmentId) return `assignment:${timer.assignmentId}`;
  if (timer.nonProjectCategoryId) return `category:${timer.nonProjectCategoryId}`;
  if (timer.nonProjectCategoryCode) return `category-code:${timer.nonProjectCategoryCode}`;
  return '';
}

function activeTimerLabel(timer) {
  return [
    timer?.customerName,
    timer?.projectCode,
    timer?.taskName || timer?.nonProjectCategoryName
  ].filter(Boolean).join(' · ') || 'Authorized activity';
}

function batchTarget(value) {
  if (value.startsWith('assignment:')) {
    return { assignmentId: value.slice('assignment:'.length), nonProjectTimeCategoryId: null, nonProjectCategoryCode: null };
  }
  if (value.startsWith('category:')) {
    return { assignmentId: null, nonProjectTimeCategoryId: value.slice('category:'.length), nonProjectCategoryCode: null };
  }
  if (value.startsWith('category-code:')) {
    return { assignmentId: null, nonProjectTimeCategoryId: null, nonProjectCategoryCode: value.slice('category-code:'.length) };
  }
  return null;
}

function normalizeTimerArray(payload, pluralName, singularName) {
  const plural = payload?.[pluralName] || payload?.[pluralName[0].toUpperCase() + pluralName.slice(1)];
  if (Array.isArray(plural)) return plural;
  const singular = payload?.[singularName] || payload?.[singularName[0].toUpperCase() + singularName.slice(1)];
  return singular ? [singular] : [];
}

export default function TimesheetEnhancementPortal() {
  const [slots, setSlots] = useState(() => readSlots());
  const [snapshot, setSnapshot] = useState(() => window.__projectPulseModule001Snapshot || null);
  const [timerMode, setTimerMode] = useState(false);
  const [mobileMode, setMobileMode] = useState(() => window.localStorage.getItem(MOBILE_KEY) === 'true');
  const [targets, setTargets] = useState([]);
  const [activeTimers, setActiveTimers] = useState([]);
  const [autoStoppedTimers, setAutoStoppedTimers] = useState([]);
  const [history, setHistory] = useState([]);
  const [selectedTargets, setSelectedTargets] = useState([]);
  const [classification, setClassification] = useState('normal');
  const [draftDescription, setDraftDescription] = useState('');
  const [timerDescriptions, setTimerDescriptions] = useState({});
  const [maximumConcurrentTimers, setMaximumConcurrentTimers] = useState(5);
  const [busyAction, setBusyAction] = useState('');
  const [message, setMessage] = useState('');
  const [clock, setClock] = useState(() => new Date());

  useEffect(() => {
    const synchronize = () => {
      const next = readSlots();
      setSlots((current) => (
        current.page === next.page
        && current.switcher === next.switcher
        && current.toolbar === next.toolbar
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
    slots.page?.classList.toggle('module001-mobile-mode', mobileMode);
    window.localStorage.setItem(MOBILE_KEY, String(mobileMode));
    return () => {
      document.body.classList.remove('projectpulse-module001-timer-mode');
      slots.page?.classList.remove('module001-mobile-mode');
    };
  }, [mobileMode, slots.page, timerMode]);

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
      module001Api('/api/timesheet/timers/active-set', { requiredCollections: ['activeTimers', 'autoStoppedTimers'] }),
      loadTimerHistory(selectedWeekStart),
      module001Api(`/api/timesheet/timers/targets?weekStart=${encodeURIComponent(selectedWeekStart)}`, {
        requiredCollections: ['targets']
      })
    ]);

    const errors = [];
    if (activeResult.status === 'fulfilled') {
      const payload = activeResult.value || {};
      const running = normalizeTimerArray(payload, 'activeTimers', 'activeTimer');
      const stopped = normalizeTimerArray(payload, 'autoStoppedTimers', 'autoStoppedTimer');
      setActiveTimers(running);
      setAutoStoppedTimers(stopped);
      setMaximumConcurrentTimers(Math.max(1, Number(payload.maximumConcurrentTimers || 5)));
      setTimerDescriptions((current) => Object.fromEntries(running.map((timer) => [
        timer.timerSessionId,
        Object.prototype.hasOwnProperty.call(current, timer.timerSessionId)
          ? current[timer.timerSessionId]
          : timer.description || ''
      ])));
      if (Array.isArray(payload.warnings)) errors.push(...payload.warnings.filter(Boolean));
    } else {
      errors.push(activeResult.reason?.message || 'Unable to load active timers.');
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

  useEffect(() => { void loadRuntime(); }, [loadRuntime]);

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
    const validTargetValues = new Set(targets.map((target) => target.selectionValue));
    const runningValues = new Set(activeTimers.map(timerValue));
    setSelectedTargets((current) => current.filter((value) => validTargetValues.has(value) && !runningValues.has(value)));
  }, [activeTimers, targets]);

  useEffect(() => {
    setClock(new Date());
    if (activeTimers.length === 0) return undefined;
    const interval = window.setInterval(() => setClock(new Date()), 1000);
    return () => window.clearInterval(interval);
  }, [activeTimers]);

  const recoveryDuration = useMemo(() => activeTimers.reduce((longest, timer) => {
    const duration = calculateTimerDuration(timer.startedAtUtc, clock);
    return duration.cappedSeconds > longest.cappedSeconds ? duration : longest;
  }, { cappedSeconds: 0, roundedMinutes: 0 }), [activeTimers, clock]);

  function updateTimerDescription(timerSessionId, value) {
    setTimerDescriptions((current) => ({ ...current, [timerSessionId]: value }));
  }

  function notifyTimerChanged(action, timerCount) {
    window.dispatchEvent(new CustomEvent('projectpulse:module001-timer-changed', {
      detail: { action, timerCount }
    }));
  }

  async function startTimers() {
    const requests = selectedTargets.map(batchTarget).filter(Boolean);
    if (requests.length === 0) {
      setMessage('Select at least one Project Task, Request / Service Request, or Non-Project Time activity.');
      return;
    }

    setBusyAction('start');
    setMessage(`Starting ${requests.length} server ${requests.length === 1 ? 'timer' : 'timers'}…`);
    try {
      const result = await module001Api('/api/timesheet/timers/start-batch', {
        method: 'POST',
        body: JSON.stringify({
          targets: requests,
          timeClassification: classification,
          description: draftDescription,
          timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
        })
      });
      const started = Array.isArray(result.timers) ? result.timers : result.timer ? [result.timer] : [];
      setSelectedTargets([]);
      setDraftDescription('');
      const startedCount = started.length || requests.length;
      setMessage(startedCount === 1
        ? 'Timer started. The server continues tracking it through refreshes, sign-out, and session expiration.'
        : `${startedCount} timers started. The server continues tracking them through refreshes, sign-out, and session expiration.`);
      notifyTimerChanged('started', started.length || requests.length);
      await loadRuntime({ preserveMessage: true });
    } catch (error) {
      const recovered = normalizeTimerArray(error?.payload || {}, 'activeTimers', 'activeTimer');
      if (recovered.length > 0) setActiveTimers(recovered);
      setMessage(error.message || 'The selected timers could not be started.');
    } finally {
      setBusyAction('');
    }
  }

  async function stopTimer(timer) {
    if (!timer) return;
    setBusyAction(`stop:${timer.timerSessionId}`);
    setMessage(`Stopping ${activeTimerLabel(timer)}…`);
    try {
      const result = await module001Api(`/api/timesheet/timers/v2/${timer.timerSessionId}/stop`, {
        method: 'POST',
        body: JSON.stringify({
          description: timerDescriptions[timer.timerSessionId] ?? timer.description ?? '',
          reason: 'Stopped individually from Module 001 Timesheet.',
          expectedRowVersion: timer.rowVersion
        })
      });
      setMessage(result.message || 'Timer stopped and its draft time entry was created.');
      notifyTimerChanged('stopped', 1);
      await loadRuntime({ preserveMessage: true });
    } catch (error) {
      setMessage(error.message || 'The timer could not be stopped.');
    } finally {
      setBusyAction('');
    }
  }

  async function stopAllTimers() {
    if (activeTimers.length === 0) return;
    const confirmed = window.confirm(`Stop all ${activeTimers.length} running timers and create their draft time entries?`);
    if (!confirmed) return;

    setBusyAction('stop-all');
    setMessage(`Stopping all ${activeTimers.length} timers…`);
    try {
      const result = await module001Api('/api/timesheet/timers/v2/stop-all', {
        method: 'POST',
        body: JSON.stringify({
          reason: 'Stopped together from Module 001 Timesheet.',
          timers: activeTimers.map((timer) => ({
            timerSessionId: timer.timerSessionId,
            description: timerDescriptions[timer.timerSessionId] ?? timer.description ?? '',
            expectedRowVersion: timer.rowVersion
          }))
        })
      });
      setMessage(result.message || 'All running timers stopped and their draft time entries were created.');
      notifyTimerChanged('stopped-all', activeTimers.length);
      await loadRuntime({ preserveMessage: true });
    } catch (error) {
      setMessage(error.message || 'The running timers could not be stopped together. No partial stop was committed.');
    } finally {
      setBusyAction('');
    }
  }

  async function discardTimer(timer) {
    if (!timer || !window.confirm(`Discard the running timer for ${activeTimerLabel(timer)} without creating a time entry?`)) return;
    setBusyAction(`discard:${timer.timerSessionId}`);
    setMessage(`Discarding ${activeTimerLabel(timer)}…`);
    try {
      const result = await module001Api(`/api/timesheet/timers/v2/${timer.timerSessionId}/discard`, {
        method: 'POST',
        body: JSON.stringify({
          reason: 'Discarded after user confirmation.',
          expectedRowVersion: timer.rowVersion
        })
      });
      setMessage(result.message || 'Timer discarded.');
      notifyTimerChanged('discarded', 1);
      await loadRuntime({ preserveMessage: true });
    } catch (error) {
      setMessage(error.message || 'The timer could not be discarded.');
    } finally {
      setBusyAction('');
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
        <small>Up to five simultaneous activities</small>
      </button>,
      slots.switcher
    ) : null}

    {slots.toolbar ? createPortal(
      <label className="module001-mobile-toggle" title="Use larger touch targets and a stacked Timesheet layout on this device.">
        <input type="checkbox" checked={mobileMode} onChange={(event) => setMobileMode(event.target.checked)} />
        <span>Mobile mode</span>
      </label>,
      slots.toolbar
    ) : null}

    {slots.workspace ? createPortal(
      timerMode ? <TimesheetTimerView
        targets={targets}
        history={history}
        activeTimers={activeTimers}
        selectedTargetValues={selectedTargets}
        classification={classification}
        draftDescription={draftDescription}
        timerDescriptions={timerDescriptions}
        maximumConcurrentTimers={maximumConcurrentTimers}
        isViewAs={viewAs}
        busyAction={busyAction}
        statusMessage={message}
        onSelectTargets={setSelectedTargets}
        onClassificationChange={setClassification}
        onDraftDescriptionChange={setDraftDescription}
        onTimerDescriptionChange={updateTimerDescription}
        onStart={startTimers}
        onStopOne={stopTimer}
        onStopAll={stopAllTimers}
        onDiscardOne={discardTimer}
      /> : null,
      slots.workspace
    ) : null}

    {slots.recovery && !timerMode && activeTimers.length > 0 ? createPortal(
      <section className="module001-server-timer-recovery module001-multi-timer-recovery" aria-label="Running timers recovered from server">
        <div>
          <p className="eyebrow">RUNNING TIMERS</p>
          <h3>{activeTimers.length} active {activeTimers.length === 1 ? 'activity' : 'activities'}</h3>
          <p>{activeTimers.map(activeTimerLabel).join(' • ')}</p>
          <small>The timers are server-owned and remain active through refreshes, sign-out, and session expiration.</small>
        </div>
        <strong className="module001-server-timer-clock" aria-live="polite">{formatElapsedSeconds(recoveryDuration.cappedSeconds)}</strong>
        <div className="module001-server-timer-actions">
          <button type="button" onClick={() => setTimerMode(true)}>Open timers</button>
          <button type="button" disabled={Boolean(busyAction) || viewAs} onClick={stopAllTimers}>{busyAction === 'stop-all' ? 'Stopping…' : 'Stop all'}</button>
        </div>
      </section>,
      slots.recovery
    ) : null}

    {slots.recovery && activeTimers.length === 0 && autoStoppedTimers.length > 0 ? createPortal(
      <section className="module001-server-timer-recovery auto-stopped" aria-label="Automatically stopped timers">
        <div>
          <p className="eyebrow">TIMER SAFETY LIMIT</p>
          <h3>{autoStoppedTimers.length} {autoStoppedTimers.length === 1 ? 'timer was' : 'timers were'} stopped automatically</h3>
          <p>The server stopped {autoStoppedTimers.map(activeTimerLabel).join(' • ')} at the 24-hour limit. Review the resulting draft entries before submission.</p>
        </div>
        <button type="button" onClick={() => setAutoStoppedTimers([])}>Dismiss</button>
      </section>,
      slots.recovery
    ) : null}
  </>;
}