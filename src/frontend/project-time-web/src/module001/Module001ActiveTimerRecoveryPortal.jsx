import { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { calculateTimerDuration, formatElapsedSeconds } from './timesheet-duration.js';
import './module001-active-timer-recovery.css';

function sessionContext() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    const selected = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    return {
      token: session?.sessionToken || session?.token || session?.accessToken || '',
      viewAsUserId: selected?.userId || window.localStorage.getItem('projectPulseViewAsUserId') || ''
    };
  } catch {
    return { token: '', viewAsUserId: '' };
  }
}

function requestHeaders(hasBody = false) {
  const { token, viewAsUserId } = sessionContext();
  return {
    ...(hasBody ? { 'Content-Type': 'application/json' } : {}),
    ...(token ? {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token
    } : {}),
    ...(viewAsUserId ? { 'X-ProjectPulse-View-As-User': viewAsUserId } : {}),
    'Cache-Control': 'no-cache',
    Pragma: 'no-cache'
  };
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    credentials: 'include',
    cache: 'no-store',
    ...options,
    headers: {
      ...requestHeaders(Boolean(options.body)),
      ...(options.headers || {})
    }
  });
  const raw = await response.text();
  let payload = {};
  try { payload = raw ? JSON.parse(raw) : {}; } catch { payload = { message: raw }; }
  if (!response.ok) {
    const error = new Error(payload.message || payload.detail || `${path} returned HTTP ${response.status}`);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload;
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
    else page.appendChild(host);
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
  const [clock, setClock] = useState(() => new Date());
  const [busy, setBusy] = useState('');
  const [message, setMessage] = useState('');

  useEffect(() => {
    const sync = () => {
      const onTimesheet = window.location.hash.replace(/^#/, '') === 'timesheet';
      const page = onTimesheet ? document.querySelector('#timesheet.timesheet-page') : null;
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

  useEffect(() => {
    if (!host) return undefined;
    let disposed = false;
    const load = async () => {
      try {
        const payload = await api('/api/timesheet/timers/active');
        if (disposed) return;
        const activeTimer = payload?.activeTimer || null;
        setTimer(activeTimer);
        if (activeTimer) setMessage('A running timer was recovered from the server.');
      } catch (error) {
        if (!disposed) setMessage(error.message || 'The active timer could not be checked.');
      }
    };
    void load();
    const interval = window.setInterval(load, 5000);
    const focus = () => void load();
    window.addEventListener('focus', focus);
    return () => {
      disposed = true;
      window.clearInterval(interval);
      window.removeEventListener('focus', focus);
    };
  }, [host]);

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
      const result = await api(`/api/timesheet/timers/${timer.timerSessionId}/stop`, {
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
    }
  }

  async function discard() {
    if (!timer || busy) return;
    const confirmed = window.confirm('Discard this running timer without creating a time entry?');
    if (!confirmed) return;
    setBusy('discard');
    setMessage('Discarding the recovered timer…');
    try {
      const result = await api(`/api/timesheet/timers/${timer.timerSessionId}/discard`, {
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
    }
  }

  if (!host || !timer) return null;
  const viewAsActive = Boolean(sessionContext().viewAsUserId);
  const missingDescription = !String(timer.description || '').trim();

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
