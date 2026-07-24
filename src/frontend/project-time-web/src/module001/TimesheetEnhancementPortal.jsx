import { useCallback, useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import TimesheetTimerView from './TimesheetTimerView.jsx';
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

export default function TimesheetEnhancementPortal() {
  const [hosts, setHosts] = useState({ page: null, switcher: null, toolbar: null, workspace: null });
  const [snapshot, setSnapshot] = useState(() => window.__projectPulseModule001Snapshot || null);
  const [timerMode, setTimerMode] = useState(false);
  const [mobileMode, setMobileMode] = useState(() => localStorage.getItem(MOBILE_KEY) === 'true');
  const [timerTargets, setTimerTargets] = useState([]);
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
        setHosts((current) => current.page ? { page: null, switcher: null, toolbar: null, workspace: null } : current);
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
      setHosts((current) => (
        current.page === next.page
        && current.switcher === next.switcher
        && current.toolbar === next.toolbar
        && current.workspace === next.workspace
      ) ? current : next);
    };

    syncHosts();
    const observer = new MutationObserver(syncHosts);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', syncHosts);
    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', syncHosts);
    };
  }, []);

  useEffect(() => {
    const receive = (event) => setSnapshot(event.detail);
    window.addEventListener('projectpulse:module001-state', receive);
    return () => window.removeEventListener('projectpulse:module001-state', receive);
  }, []);

  useEffect(() => {
    if (!hosts.page) return undefined;
    hosts.page.classList.remove('module001-enhanced-queue', 'module001-enhanced-calendar');
    hosts.page.classList.toggle('module001-mobile-mode', mobileMode);
    hosts.page.classList.toggle('module001-timer-mode', timerMode);
    localStorage.setItem(MOBILE_KEY, String(mobileMode));
    return () => hosts.page?.classList.remove(
      'module001-mobile-mode',
      'module001-timer-mode',
      'module001-enhanced-queue',
      'module001-enhanced-calendar'
    );
  }, [hosts.page, mobileMode, timerMode]);

  useEffect(() => {
    if (!hosts.switcher) return undefined;
    const clearTimer = (event) => {
      if (event.target.closest('.timesheet-view-button') && !event.target.closest('#module001-start-stop-tab')) {
        setTimerMode(false);
      }
    };
    hosts.switcher.parentElement?.addEventListener('click', clearTimer, true);
    return () => hosts.switcher?.parentElement?.removeEventListener('click', clearTimer, true);
  }, [hosts.switcher]);

  const loadTimerData = useCallback(async () => {
    if (!snapshot?.selectedWeekStart || !hosts.page) return;

    const [targetResult, activeResult, historyResult] = await Promise.allSettled([
      api(`/api/timesheet/timers/targets?weekStart=${snapshot.selectedWeekStart}`),
      api('/api/timesheet/timers/active'),
      api(`/api/timesheet/timers/history?weekStart=${snapshot.selectedWeekStart}`)
    ]);

    const errors = [];
    if (targetResult.status === 'fulfilled') {
      setTimerTargets(targetResult.value.targets || []);
    } else {
      setTimerTargets([]);
      errors.push(targetResult.reason?.message || 'Unable to load timer activities.');
    }

    if (activeResult.status === 'fulfilled') {
      setActiveTimer(activeResult.value.activeTimer || null);
      if (activeResult.value.activeTimer) {
        setDescription(activeResult.value.activeTimer.description || '');
        setClassification(activeResult.value.activeTimer.timeClassification || 'normal');
      }
      if (activeResult.value.autoStoppedTimer) {
        errors.push('A timer was automatically stopped at 12 hours. Review its draft entry before submission.');
      }
    } else {
      errors.push(activeResult.reason?.message || 'Unable to load the active timer.');
    }

    if (historyResult.status === 'fulfilled') {
      setTimerHistory(historyResult.value.timers || []);
    } else {
      errors.push(historyResult.reason?.message || 'Unable to load timer history.');
    }

    if (errors.length) setStatusMessage(errors.join(' '));
  }, [snapshot?.selectedWeekStart, hosts.page]);

  useEffect(() => { void loadTimerData(); }, [loadTimerData]);

  useEffect(() => {
    if (selectedTarget && !timerTargets.some((target) => target.selectionValue === selectedTarget)) {
      setSelectedTarget('');
    }
  }, [selectedTarget, timerTargets]);

  const startTimer = async () => {
    const target = timerTargets.find((item) => item.selectionValue === selectedTarget);
    if (!target || !['assignment', 'category'].includes(target.targetType)) {
      setStatusMessage('Select an assigned project task or authorized non-project activity before starting the timer.');
      return;
    }

    setBusy(true);
    setStatusMessage('');
    try {
      const targetId = target.targetId;
      const result = await api('/api/timesheet/timers/start', {
        method: 'POST',
        body: JSON.stringify({
          assignmentId: target.targetType === 'assignment' ? targetId : null,
          nonProjectTimeCategoryId: target.targetType === 'category' ? targetId : null,
          timeClassification: classification,
          description,
          timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
        })
      });
      setActiveTimer(result.timer);
      setDescription(result.timer?.description || description);
      setStatusMessage('Timer started. It will continue across refreshes and devices.');
      await loadTimerData();
    } catch (error) {
      setStatusMessage(error.message);
    } finally {
      setBusy(false);
    }
  };

  const stopTimer = async () => {
    if (!activeTimer) return;
    setBusy(true);
    setStatusMessage('');
    try {
      const result = await api(`/api/timesheet/timers/${activeTimer.timerSessionId}/stop`, {
        method: 'POST',
        body: JSON.stringify({
          description,
          reason: 'Stopped from Module 001 Timesheet.',
          expectedRowVersion: activeTimer.rowVersion
        })
      });
      setStatusMessage(result.message);
      setActiveTimer(null);
      await loadTimerData();
      window.setTimeout(() => window.location.reload(), 500);
    } catch (error) {
      setStatusMessage(error.message);
    } finally {
      setBusy(false);
    }
  };

  const discardTimer = async () => {
    if (!activeTimer || !window.confirm('Discard this running timer? No Timesheet time will be created.')) return;
    setBusy(true);
    setStatusMessage('');
    try {
      const result = await api(`/api/timesheet/timers/${activeTimer.timerSessionId}/discard`, {
        method: 'POST',
        body: JSON.stringify({
          reason: 'Discarded after user confirmation.',
          expectedRowVersion: activeTimer.rowVersion
        })
      });
      setStatusMessage(result.message);
      setActiveTimer(null);
      setDescription('');
      await loadTimerData();
    } catch (error) {
      setStatusMessage(error.message);
    } finally {
      setBusy(false);
    }
  };

  const prepareSubmission = async () => {
    if (!snapshot?.draftPayload || snapshot.isViewAs) return;
    setBusy(true);
    setStatusMessage('Saving the shared weekly draft…');
    try {
      await api('/api/timesheets/week/draft', {
        method: 'POST',
        body: JSON.stringify(snapshot.draftPayload)
      });
      const validation = await api(`/api/timesheet/weeks/${snapshot.selectedWeekStart}/validate-submission`, {
        method: 'POST',
        body: '{}'
      });
      setReview(validation);
      setStatusMessage(validation.valid
        ? 'Review the summary and confirm submission.'
        : 'Submission is blocked until the listed items are corrected.');
    } catch (error) {
      setStatusMessage(error.message);
    } finally {
      setBusy(false);
    }
  };

  const confirmSubmission = async () => {
    if (!review?.valid || snapshot?.isViewAs) return;
    setBusy(true);
    try {
      const result = await api(`/api/timesheet/weeks/${snapshot.selectedWeekStart}/submit`, {
        method: 'POST',
        body: JSON.stringify({ confirmed: true, reason: 'Confirmed from the Module 001 weekly review.' })
      });
      setStatusMessage(result.message);
      setReview(null);
      window.setTimeout(() => window.location.reload(), 500);
    } catch (error) {
      setStatusMessage(error.message);
    } finally {
      setBusy(false);
    }
  };

  if (!hosts.page || !snapshot) return null;
  const disabled = snapshot.isViewAs || busy;

  return (
    <>
      {hosts.switcher ? createPortal(
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
        hosts.switcher
      ) : null}

      {hosts.toolbar ? createPortal(
        <>
          <label className="module001-mobile-toggle">
            <input type="checkbox" checked={mobileMode} onChange={(event) => setMobileMode(event.target.checked)} />
            <span>Mobile mode</span>
          </label>
          <button
            type="button"
            className="primary-action module001-submit-week"
            disabled={disabled || !snapshot.isAnyDayEditable}
            onClick={prepareSubmission}
          >
            Submit week
          </button>
        </>,
        hosts.toolbar
      ) : null}

      {hosts.workspace ? createPortal(
        timerMode ? (
          <TimesheetTimerView
            targets={timerTargets}
            history={timerHistory}
            activeTimer={activeTimer}
            selectedTargetValue={selectedTarget}
            classification={classification}
            description={description}
            isViewAs={snapshot.isViewAs}
            busy={busy}
            statusMessage={statusMessage}
            onSelectTarget={setSelectedTarget}
            onClassificationChange={setClassification}
            onDescriptionChange={setDescription}
            onStart={startTimer}
            onStop={stopTimer}
            onDiscard={discardTimer}
          />
        ) : null,
        hosts.workspace
      ) : null}

      {review ? createPortal(
        <div className="module001-review-backdrop" role="presentation">
          <section className="module001-review-dialog" role="dialog" aria-modal="true" aria-labelledby="module001-review-title">
            <header>
              <div><p className="eyebrow">WEEKLY SUBMISSION REVIEW</p><h2 id="module001-review-title">Submit Timesheet week</h2></div>
              <button type="button" onClick={() => setReview(null)}>Close</button>
            </header>
            <dl>
              <div><dt>Week</dt><dd>{review.weekStart} through {review.weekEnd}</dd></div>
              <div><dt>Total</dt><dd>{Number(review.totalHours || 0).toFixed(2)} hours</dd></div>
              <div><dt>Entries</dt><dd>{review.entryCount || 0}</dd></div>
              <div><dt>Active timer</dt><dd>{review.runningTimer ? 'Must be stopped' : 'None'}</dd></div>
            </dl>
            {(review.errors || []).length ? (
              <div className="module001-review-errors">
                <h3>Corrections required</h3>
                <ul>{review.errors.map((error) => <li key={error}>{error}</li>)}</ul>
                {(review.incompleteEntries || []).map((entry) => (
                  <article key={entry.timeEntryId}>
                    <strong>{entry.workDate} · {entry.projectCode || entry.projectName || 'Non-project'}</strong>
                    <span>{entry.taskName}</span>
                    <small>{(entry.reasons || []).join('; ')}</small>
                  </article>
                ))}
              </div>
            ) : (
              <p className="module001-review-ready">All validation checks passed. Confirm to route this week into Module 002 Approval Inbox.</p>
            )}
            <footer>
              <button type="button" className="secondary" onClick={() => setReview(null)}>Cancel</button>
              <button
                type="button"
                className="primary-action"
                disabled={!review.valid || busy || snapshot.isViewAs}
                onClick={confirmSubmission}
              >
                {busy ? 'Submitting…' : 'Confirm and submit week'}
              </button>
            </footer>
          </section>
        </div>,
        document.body
      ) : null}
    </>
  );
}
