import { useCallback, useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { authoritativeApi } from '../projectpulse-authoritative-api.js';
import { calculateTimerDuration, formatElapsedSeconds } from './timesheet-duration.js';
import './module001-active-timer-recovery.css';

function viewAsUserId() {
  try {
    const selected = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    return selected?.userId || window.localStorage.getItem('projectPulseViewAsUserId') || '';
  } catch {
    return window.localStorage.getItem('projectPulseViewAsUserId') || '';
  }
}

function ensureHost(page) {
  if (!page) return null;
  let host = page.querySelector(':scope > #module001-active-timer-recovery-host');
  if (!host) {
    host = document.createElement('div');
    host.id = 'module001-active-timer-recovery-host';
    host.className = 'module001-active-timer-recovery-host';
    const ptcHost = page.querySelector('#module001-ptc-time-steward-host');
    const workspace = page.querySelector('.timesheet-workspace');
    if (ptcHost) page.insertBefore(host, ptcHost);
    else if (workspace) page.insertBefore(host, workspace);
    else page.prepend(host);
  }
  return host;
}

function activeLabel(timer) {
  return [
    timer?.customerName,
    timer?.projectCode,
    timer?.taskName || timer?.nonProjectCategoryName
  ].filter(Boolean).join(' · ') || 'Authorized activity';
}

export default function Module001ActiveTimerRecoveryPortal() {
  const [host, setHost] = useState(null);
  const [timer, setTimer] = useState(null);
  const [autoStoppedTimer, setAutoStoppedTimer] = useState(null);
  const [clock, setClock] = useState(() => new Date());
  const [busy, setBusy] = useState('');
  const [message, setMessage] = useState('');
  const [checkError, setCheckError] = useState('');

  useEffect(() => {
    const sync = () => {
      const onTimesheet = window.location.hash.replace(/^#/, '') === 'timesheet';
      const page = onTimesheet ? document.querySelector('#timesheet') : null;
      setHost(page ? ensureHost(page) : null);
    };
    sync();
    const observer = new MutationObserver(sync);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', sync);
    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', sync);
    };
  }, []);

  const load = useCallback(async () => {
    if (!host) return;
    try {
      const payload = await authoritativeApi('/api/timesheet/timers/active');
      const activeTimer = payload?.activeTimer || payload?.ActiveTimer || null;
      const stoppedTimer = payload?.autoStoppedTimer || payload?.AutoStoppedTimer || null;
      setTimer(activeTimer);
      setAutoStoppedTimer(stoppedTimer);
      setCheckError('');
      if (activeTimer) setMessage('A running timer was recovered from the server.');
      else if (stoppedTimer) setMessage('The server automatically stopped a timer at the 12-hour safety limit. Review the resulting draft entry.');
      else setMessage('');
    } catch (error) {
      setTimer(null);
      setAutoStoppedTimer(null);
      setCheckError(error.message || 'The active timer could not be checked.');
    }
  }, [host]);

  useEffect(() => {
    if (!host) return undefined;
    void load();
    const interval = window.setInterval(() => void load(), 5000);
    const focus = () => void load();
    window.addEventListener('focus', focus);
    window.addEventListener('projectpulse:auth-session-ready', focus);
    return () => {
      window.clearInterval(interval);
      window.removeEventListener('focus', focus);
      window.removeEventListener('projectpulse:auth-session-ready', focus);
    };
  }, [host, load]);

  useEffect(() => {
    setClock(new Date());
    if (!timer?.startedAtUtc) return undefined;
    const interval = window.setInterval(() => setClock(new Date()), 1000);
    return () => window.clearInterval(interval);
  }, [timer?.startedAtUtc]);

  const duration = useMemo(
    () => timer?.startedAtUtc
      ? calculateTimerDuration(timer.startedAtUtc, clock)
      : { cappedSeconds: 0, roundedMinutes: 0 },
    [timer?.startedAtUtc, clock]
  );

  async function stop() {
    if (!timer || busy) return;
    setBusy('stop');
    setMessage('Stopping the recovered timer…');
    try {
      const result = await authoritativeApi(`/api/timesheet/timers/${timer.timerSessionId}/stop`, {
        method: 'POST',
        body: JSON.stringify({
          description: timer.description || '',
          reason: 'Stopped from Module 001 active timer recovery.',
          expectedRowVersion: timer.rowVersion
        })
      });
      setTimer(null);
      setMessage(result.message || 'Timer stopped and its draft time entry was created.');
      window.dispatchEvent(new CustomEvent('projectpulse:module001-timer-recovered', { detail: { action: 'stopped' } }));
    } catch (error) {
      setMessage(error.message || 'The timer could not be stopped.');
    } finally {
      setBusy('');
      void load();
    }
  }

  async function discard() {
    if (!timer || busy) return;
    const confirmed = window.confirm('Discard this running timer without creating a time entry?');
    if (!confirmed) return;
    setBusy('discard');
    setMessage('Discarding the recovered timer…');
    try {
      const result = await authoritativeApi(`/api/timesheet/timers/${timer.timerSessionId}/discard`, {
        method: 'POST',
        body: JSON.stringify({
          reason: 'Discarded from Module 001 active timer recovery.',
          expectedRowVersion: timer.rowVersion
        })
      });
      setTimer(null);
      setMessage(result.message || 'Timer discarded.');
      window.dispatchEvent(new CustomEvent('projectpulse:module001-timer-recovered', { detail: { action: 'discarded' } }));
    } catch (error) {
      setMessage(error.message || 'The timer could not be discarded.');
    } finally {
      setBusy('');
      void load();
    }
  }

  if (!host) return null;
  if (!timer && !autoStoppedTimer && !checkError) return null;

  const viewAsActive = Boolean(viewAsUserId());
  const missingDescription = !String(timer?.description || '').trim();

  if (checkError) {
    return createPortal(
      <section className="module001-active-timer-recovery module001-active-timer-recovery-error" aria-label="Timer status unavailable">
        <div>
          <p className="eyebrow">Timer status check failed</p>
          <h3>The server timer could not be verified</h3>
          <p>{checkError}</p>
        </div>
        <div className="module001-active-timer-recovery-actions">
          <button type="button" onClick={() => void load()}>Try timer check again</button>
        </div>
      </section>,
      host
    );
  }

  if (!timer && autoStoppedTimer) {
    return createPortal(
      <section className="module001-active-timer-recovery" aria-label="Automatically stopped timer">
        <div>
          <p className="eyebrow">Timer automatically stopped</p>
          <h3>{activeLabel(autoStoppedTimer)}</h3>
          <p>{message}</p>
        </div>
        <div className="module001-active-timer-recovery-actions">
          <button type="button" onClick={() => setAutoStoppedTimer(null)}>Dismiss</button>
        </div>
      </section>,
      host
    );
  }

  return createPortal(
    <section className="module001-active-timer-recovery" aria-label="Recovered running timer">
      <div>
        <p className="eyebrow">Running timer recovered</p>
        <h3>{activeLabel(timer)}</h3>
        <p>{message}</p>
        <small>Started {timer.startedAtUtc ? new Date(timer.startedAtUtc).toLocaleString() : 'on the server'} · Rounded draft {(duration.roundedMinutes / 60).toFixed(2)} hours</small>
        {missingDescription ? <small className="module001-active-timer-description-warning">No work description is recorded. After stopping, add the actual work detail before submitting the week.</small> : null}
      </div>
      <strong className="module001-active-timer-recovery-clock" aria-live="polite">{formatElapsedSeconds(duration.cappedSeconds)}</strong>
      <div className="module001-active-timer-recovery-actions">
        <button type="button" disabled={busy !== '' || viewAsActive} onClick={() => void stop()}>{busy === 'stop' ? 'Stopping…' : 'Stop timer'}</button>
        <button type="button" className="danger" disabled={busy !== '' || viewAsActive} onClick={() => void discard()}>{busy === 'discard' ? 'Discarding…' : 'Discard'}</button>
      </div>
    </section>,
    host
  );
}
