import { useCallback, useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import TimesheetTimerView from './TimesheetTimerView.jsx';
import TimesheetWorkQueueCard from './TimesheetWorkQueueCard.jsx';
import './timesheet-prep.css';

const MOBILE_KEY = 'projectPulseModule001MobileMode';

function authHeaders() {
  try {
    const session = JSON.parse(localStorage.getItem('projectPulseAuthSession') || 'null');
    const headers = session?.sessionToken ? { Authorization: `Bearer ${session.sessionToken}` } : {};
    const viewAs = localStorage.getItem('projectPulseViewAsUserId');
    if (viewAs) headers['X-ProjectPulse-View-As-User'] = viewAs;
    return headers;
  } catch {
    return {};
  }
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    credentials: 'include',
    cache: 'no-store',
    ...options,
    headers: {
      ...authHeaders(),
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...(options.headers || {})
    }
  });
  const raw = await response.text();
  let payload = {};
  try { payload = raw ? JSON.parse(raw) : {}; } catch { payload = { message: raw }; }
  if (!response.ok) {
    const error = new Error(payload.message || payload.detail || `${path} returned HTTP ${response.status}`);
    error.payload = payload;
    error.status = response.status;
    throw error;
  }
  return payload;
}

function ensureHost(parent, id, className) {
  if (!parent) return null;
  let host = parent.querySelector(`:scope > #${id}`);
  if (!host) {
    host = document.createElement('div');
    host.id = id;
    host.className = className;
    parent.appendChild(host);
  }
  return host;
}

function switchToExistingView(label) {
  const buttons = [...document.querySelectorAll('#timesheet .timesheet-view-switcher .timesheet-view-button')];
  const button = buttons.find((item) => item.textContent?.includes(label));
  button?.click();
}

function dispatchModule001Action(detail) {
  window.dispatchEvent(new CustomEvent('projectpulse:module001-action', { detail }));
}

function CalendarEnhancement({ snapshot, tasks, disabled, onChangeTask, onRemove }) {
  const [selected, setSelected] = useState(null);
  const days = snapshot?.days || [];
  const entries = snapshot?.calendarEntries || [];

  return (
    <section className="module001-calendar-enhancement" aria-label="Task-aware Calendar and Timeline">
      <div className="timesheet-view-heading">
        <div><p className="eyebrow">TASK-AWARE WEEK</p><h3>Calendar / Timeline</h3></div>
        <span className="pill">{Number(snapshot?.grandTotal || 0).toFixed(2)} total hours</span>
      </div>
      <div className="module001-calendar-grid">
        {days.map((day) => {
          const dayItems = entries.filter((item) => item.day?.date === day.date && Number(item.entry?.hours || 0) > 0);
          const total = dayItems.reduce((sum, item) => sum + Number(item.entry?.hours || 0), 0);
          return (
            <article className="module001-calendar-day" key={day.date}>
              <header><div><strong>{day.dayName}</strong><span>{day.date}</span></div><b>{total.toFixed(2)} hrs</b></header>
              <button type="button" className="module001-add-work" onClick={() => switchToExistingView('My Work Queue')}>+ Add work</button>
              <div className="module001-calendar-items">
                {dayItems.map((item) => {
                  const missingDescription = !String(item.entry?.comment || '').trim();
                  const missingTask = item.row?.rowType === 'project' && !item.row?.taskId;
                  const status = item.entry?.savedStatus || snapshot.submissionStatus || 'draft';
                  return (
                    <button
                      type="button"
                      className={`module001-calendar-item ${missingDescription || missingTask ? 'incomplete' : ''}`}
                      key={`${item.row?.id}-${day.date}-${item.timeType?.key}`}
                      onClick={() => setSelected(item)}
                    >
                      <span>{item.row?.activity}</span>
                      <small>{item.row?.projectDescription}</small>
                      <small>{item.timeType?.key === 'afterhours' ? 'Afterhours' : 'Normal'} · {status}</small>
                      <strong>{Number(item.entry?.hours || 0).toFixed(2)} hrs</strong>
                      {missingDescription ? <em>Description required</em> : null}
                      {missingTask ? <em>Task association required</em> : null}
                    </button>
                  );
                })}
                {dayItems.length === 0 ? <span className="muted">No time entered</span> : null}
              </div>
            </article>
          );
        })}
      </div>

      {selected ? (
        <aside className="module001-calendar-drawer" aria-label="Calendar entry task details">
          <header><div><small>{selected.day?.date}</small><h3>{selected.row?.activity}</h3></div><button type="button" onClick={() => setSelected(null)}>Close</button></header>
          <dl>
            <div><dt>Project / category</dt><dd>{selected.row?.projectDescription}</dd></div>
            <div><dt>Classification</dt><dd>{selected.timeType?.key === 'afterhours' ? 'Afterhours' : 'Normal'}</dd></div>
            <div><dt>Rounded hours</dt><dd>{Number(selected.entry?.hours || 0).toFixed(2)}</dd></div>
            <div><dt>Status</dt><dd>{selected.entry?.savedStatus || snapshot.submissionStatus || 'draft'}</dd></div>
          </dl>
          {selected.row?.rowType === 'project' ? (
            <label className="module001-field"><span>Assigned task</span><select defaultValue="" disabled={disabled} onChange={(event) => event.target.value && onChangeTask(selected, event.target.value)}><option value="">Change assigned task</option>{tasks.map((task) => <option key={task.assignmentId} value={task.assignmentId}>{task.projectCode} · {task.taskName}</option>)}</select></label>
          ) : null}
          <div className="module001-calendar-drawer-actions">
            <button type="button" onClick={() => dispatchModule001Action({ type: 'open-entry', rowId: selected.row?.id, workDate: selected.day?.date, timeType: selected.timeType?.key })}>Open full entry editor</button>
            <button type="button" className="secondary" disabled={disabled || !selected.entry?.timeEntryId} onClick={() => onRemove(selected)}>Remove draft</button>
          </div>
        </aside>
      ) : null}
    </section>
  );
}

export default function TimesheetEnhancementPortal() {
  const [targets, setTargets] = useState({ page: null, switcher: null, toolbar: null, workspace: null });
  const [snapshot, setSnapshot] = useState(() => window.__projectPulseModule001Snapshot || null);
  const [timerMode, setTimerMode] = useState(false);
  const [mobileMode, setMobileMode] = useState(() => localStorage.getItem(MOBILE_KEY) === 'true');
  const [workQueue, setWorkQueue] = useState([]);
  const [activeTimer, setActiveTimer] = useState(null);
  const [timerHistory, setTimerHistory] = useState([]);
  const [selectedTarget, setSelectedTarget] = useState('');
  const [classification, setClassification] = useState('normal');
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [statusMessage, setStatusMessage] = useState('');
  const [review, setReview] = useState(null);

  useEffect(() => {
    const syncHosts = () => {
      const onTimesheet = window.location.hash.replace('#', '') === 'timesheet';
      const page = onTimesheet ? document.querySelector('#timesheet.timesheet-page') : null;
      if (!page) {
        setTargets((current) => current.page ? { page: null, switcher: null, toolbar: null, workspace: null } : current);
        return;
      }
      const switcher = page.querySelector('.timesheet-view-switcher');
      const toolbar = page.querySelector('.timesheet-toolbar .toolbar-actions');
      const workspace = page.querySelector('.timesheet-workspace');
      const next = {
        page,
        switcher: ensureHost(switcher, 'module001-view-tab-host', 'module001-view-tab-host'),
        toolbar: ensureHost(toolbar, 'module001-toolbar-host', 'module001-toolbar-host'),
        workspace: ensureHost(workspace, 'module001-enhancement-view-host', 'module001-enhancement-view-host')
      };
      setTargets((current) => current.page === next.page && current.switcher === next.switcher && current.toolbar === next.toolbar && current.workspace === next.workspace ? current : next);
    };
    syncHosts();
    const observer = new MutationObserver(syncHosts);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', syncHosts);
    return () => { observer.disconnect(); window.removeEventListener('hashchange', syncHosts); };
  }, []);

  useEffect(() => {
    const receive = (event) => setSnapshot(event.detail);
    window.addEventListener('projectpulse:module001-state', receive);
    return () => window.removeEventListener('projectpulse:module001-state', receive);
  }, []);

  useEffect(() => {
    if (!targets.page) return undefined;
    targets.page.classList.toggle('module001-mobile-mode', mobileMode);
    targets.page.classList.toggle('module001-timer-mode', timerMode);
    targets.page.classList.toggle('module001-enhanced-queue', !timerMode && snapshot?.timesheetView === 'queue');
    targets.page.classList.toggle('module001-enhanced-calendar', !timerMode && snapshot?.timesheetView === 'calendar');
    localStorage.setItem(MOBILE_KEY, String(mobileMode));
    return () => targets.page?.classList.remove('module001-mobile-mode', 'module001-timer-mode', 'module001-enhanced-queue', 'module001-enhanced-calendar');
  }, [targets.page, mobileMode, timerMode, snapshot?.timesheetView]);

  useEffect(() => {
    if (!targets.switcher) return undefined;
    const clearTimer = (event) => {
      if (event.target.closest('.timesheet-view-button') && !event.target.closest('#module001-start-stop-tab')) setTimerMode(false);
    };
    targets.switcher.parentElement?.addEventListener('click', clearTimer, true);
    return () => targets.switcher?.parentElement?.removeEventListener('click', clearTimer, true);
  }, [targets.switcher]);

  const loadEnhancementData = useCallback(async () => {
    if (!snapshot?.selectedWeekStart || !targets.page) return;
    try {
      const [queueResult, activeResult, historyResult] = await Promise.all([
        api(`/api/timesheet/work-queue?weekStart=${snapshot.selectedWeekStart}`),
        api('/api/timesheet/timers/active'),
        api(`/api/timesheet/timers/history?weekStart=${snapshot.selectedWeekStart}`)
      ]);
      setWorkQueue(queueResult.tasks || []);
      setActiveTimer(activeResult.activeTimer || null);
      if (activeResult.activeTimer) {
        setDescription(activeResult.activeTimer.description || '');
        setClassification(activeResult.activeTimer.timeClassification || 'normal');
      }
      if (activeResult.autoStoppedTimer) setStatusMessage('A timer was automatically stopped at 12 hours. Review its draft entry before submission.');
      setTimerHistory(historyResult.timers || []);
    } catch (error) {
      setStatusMessage(error.message);
    }
  }, [snapshot?.selectedWeekStart, targets.page]);

  useEffect(() => { void loadEnhancementData(); }, [loadEnhancementData]);

  const timerTargets = useMemo(() => [
    ...workQueue.map((task) => ({ ...task, selectionValue: `assignment:${task.assignmentId}` })),
    ...(snapshot?.nonProjectCategories || []).map((category) => ({
      nonProjectCategoryId: category.id || category.nonProjectTimeCategoryId,
      nonProjectCategoryName: category.name,
      selectionValue: `category:${category.id || category.nonProjectTimeCategoryId}`,
      selectionLabel: `Non-project · ${category.name}`
    }))
  ], [workQueue, snapshot?.nonProjectCategories]);

  const addTask = async (item) => {
    if (snapshot?.isViewAs) return;
    setBusy(true); setStatusMessage('');
    try {
      await api(`/api/timesheet/work-queue/${item.assignmentId}/add`, { method: 'POST', body: JSON.stringify({ weekStart: snapshot.selectedWeekStart }) });
      dispatchModule001Action({ type: 'add-assignment', assignmentId: item.assignmentId, projectId: item.projectId, taskId: item.taskId });
      setWorkQueue((current) => current.map((task) => task.assignmentId === item.assignmentId ? { ...task, addedThisWeek: true } : task));
      setStatusMessage('Assigned task added to the current Timesheet week.');
    } catch (error) { setStatusMessage(error.message); } finally { setBusy(false); }
  };

  const startFromQueue = (item) => { setSelectedTarget(`assignment:${item.assignmentId}`); setDescription(''); setTimerMode(true); };

  const startTimer = async () => {
    const [kind, id] = selectedTarget.split(':');
    if (!id) return;
    setBusy(true); setStatusMessage('');
    try {
      const result = await api('/api/timesheet/timers/start', {
        method: 'POST',
        body: JSON.stringify({ assignmentId: kind === 'assignment' ? id : null, nonProjectTimeCategoryId: kind === 'category' ? id : null, timeClassification: classification, description, timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC' })
      });
      setActiveTimer(result.timer); setDescription(result.timer?.description || description); setStatusMessage('Timer started. It will continue across refreshes and devices.');
      await loadEnhancementData();
    } catch (error) { setStatusMessage(error.message); } finally { setBusy(false); }
  };

  const stopTimer = async () => {
    if (!activeTimer) return;
    setBusy(true); setStatusMessage('');
    try {
      const result = await api(`/api/timesheet/timers/${activeTimer.timerSessionId}/stop`, { method: 'POST', body: JSON.stringify({ description, reason: 'Stopped from Module 001 Timesheet.', expectedRowVersion: activeTimer.rowVersion }) });
      setStatusMessage(result.message); setActiveTimer(null); await loadEnhancementData();
      window.setTimeout(() => window.location.reload(), 500);
    } catch (error) { setStatusMessage(error.message); } finally { setBusy(false); }
  };

  const discardTimer = async () => {
    if (!activeTimer || !window.confirm('Discard this running timer? No Timesheet time will be created.')) return;
    setBusy(true); setStatusMessage('');
    try {
      const result = await api(`/api/timesheet/timers/${activeTimer.timerSessionId}/discard`, { method: 'POST', body: JSON.stringify({ reason: 'Discarded after user confirmation.', expectedRowVersion: activeTimer.rowVersion }) });
      setStatusMessage(result.message); setActiveTimer(null); setDescription(''); await loadEnhancementData();
    } catch (error) { setStatusMessage(error.message); } finally { setBusy(false); }
  };

  const prepareSubmission = async () => {
    if (!snapshot?.draftPayload || snapshot.isViewAs) return;
    setBusy(true); setStatusMessage('Saving the shared weekly draft…');
    try {
      await api('/api/timesheets/week/draft', { method: 'POST', body: JSON.stringify(snapshot.draftPayload) });
      const validation = await api(`/api/timesheet/weeks/${snapshot.selectedWeekStart}/validate-submission`, { method: 'POST', body: '{}' });
      setReview(validation); setStatusMessage(validation.valid ? 'Review the summary and confirm submission.' : 'Submission is blocked until the listed items are corrected.');
    } catch (error) { setStatusMessage(error.message); } finally { setBusy(false); }
  };

  const confirmSubmission = async () => {
    if (!review?.valid || snapshot?.isViewAs) return;
    setBusy(true);
    try {
      const result = await api(`/api/timesheet/weeks/${snapshot.selectedWeekStart}/submit`, { method: 'POST', body: JSON.stringify({ confirmed: true, reason: 'Confirmed from the Module 001 weekly review.' }) });
      setStatusMessage(result.message); setReview(null); window.setTimeout(() => window.location.reload(), 500);
    } catch (error) { setStatusMessage(error.message); } finally { setBusy(false); }
  };

  const changeCalendarTask = async (item, assignmentId) => {
    const timeEntryId = item.entry?.timeEntryId || item.entry?.id;
    if (!timeEntryId) { setStatusMessage('Save the draft entry before changing its persisted task association.'); return; }
    try {
      await api(`/api/timesheet/entries/${timeEntryId}/association`, { method: 'POST', body: JSON.stringify({ assignmentId, reason: 'Task changed from Calendar / Timeline.' }) });
      window.location.reload();
    } catch (error) { setStatusMessage(error.message); }
  };

  const removeCalendarEntry = async (item) => {
    const timeEntryId = item.entry?.timeEntryId || item.entry?.id;
    if (!timeEntryId || !window.confirm('Remove this unsubmitted draft entry?')) return;
    try { await api(`/api/timesheet/entries/${timeEntryId}`, { method: 'DELETE' }); window.location.reload(); } catch (error) { setStatusMessage(error.message); }
  };

  if (!targets.page || !snapshot) return null;
  const disabled = snapshot.isViewAs || busy;

  return (
    <>
      {targets.switcher ? createPortal(<button id="module001-start-stop-tab" type="button" role="tab" aria-selected={timerMode} className={timerMode ? 'timesheet-view-button active' : 'timesheet-view-button'} onClick={() => setTimerMode(true)}><strong>Start / Stop Timer</strong><small>Track active work in real time</small></button>, targets.switcher) : null}
      {targets.toolbar ? createPortal(<><label className="module001-mobile-toggle"><input type="checkbox" checked={mobileMode} onChange={(event) => setMobileMode(event.target.checked)} /><span>Mobile mode</span></label><button type="button" className="primary-action module001-submit-week" disabled={disabled || !snapshot.isAnyDayEditable} onClick={prepareSubmission}>Submit week</button></>, targets.toolbar) : null}
      {targets.workspace ? createPortal(<>
        {timerMode ? <TimesheetTimerView targets={timerTargets} history={timerHistory} activeTimer={activeTimer} selectedTargetValue={selectedTarget} classification={classification} description={description} isViewAs={snapshot.isViewAs} busy={busy} statusMessage={statusMessage} onSelectTarget={setSelectedTarget} onClassificationChange={setClassification} onDescriptionChange={setDescription} onStart={startTimer} onStop={stopTimer} onDiscard={discardTimer} /> : null}
        {!timerMode && snapshot.timesheetView === 'queue' ? <section className="module001-work-queue-enhancement"><div className="timesheet-view-heading"><div><p className="eyebrow">ASSIGNED WORK</p><h3>My Work Queue</h3><p>Authoritative source: project assignments and project tasks.</p></div><span className="pill">{workQueue.length} items</span></div>{statusMessage ? <div className="module001-status">{statusMessage}</div> : null}<div className="module001-work-grid">{workQueue.map((item) => <TimesheetWorkQueueCard key={item.assignmentId} item={item} disabled={disabled} onAdd={addTask} onStartTimer={startFromQueue} onOpenTask={() => { window.location.hash = 'project-workspace'; }} />)}</div>{workQueue.length === 0 ? <div className="module001-empty">No assigned work is available for this week.</div> : null}</section> : null}
        {!timerMode && snapshot.timesheetView === 'calendar' ? <CalendarEnhancement snapshot={snapshot} tasks={workQueue} disabled={disabled} onChangeTask={changeCalendarTask} onRemove={removeCalendarEntry} /> : null}
      </>, targets.workspace) : null}
      {review ? createPortal(<div className="module001-review-backdrop" role="presentation"><section className="module001-review-dialog" role="dialog" aria-modal="true" aria-labelledby="module001-review-title"><header><div><p className="eyebrow">WEEKLY SUBMISSION REVIEW</p><h2 id="module001-review-title">Submit Timesheet week</h2></div><button type="button" onClick={() => setReview(null)}>Close</button></header><dl><div><dt>Week</dt><dd>{review.weekStart} through {review.weekEnd}</dd></div><div><dt>Total</dt><dd>{Number(review.totalHours || 0).toFixed(2)} hours</dd></div><div><dt>Entries</dt><dd>{review.entryCount || 0}</dd></div><div><dt>Active timer</dt><dd>{review.runningTimer ? 'Must be stopped' : 'None'}</dd></div></dl>{(review.errors || []).length ? <div className="module001-review-errors"><h3>Corrections required</h3><ul>{review.errors.map((error) => <li key={error}>{error}</li>)}</ul>{(review.incompleteEntries || []).map((entry) => <article key={entry.timeEntryId}><strong>{entry.workDate} · {entry.projectCode || entry.projectName || 'Non-project'}</strong><span>{entry.taskName}</span><small>{(entry.reasons || []).join('; ')}</small></article>)}</div> : <p className="module001-review-ready">All validation checks passed. Confirm to route this week into Module 002 Approval Inbox.</p>}<footer><button type="button" className="secondary" onClick={() => setReview(null)}>Cancel</button><button type="button" className="primary-action" disabled={!review.valid || busy || snapshot.isViewAs} onClick={confirmSubmission}>{busy ? 'Submitting…' : 'Confirm and submit week'}</button></footer></section></div>, document.body) : null}
    </>
  );
}
