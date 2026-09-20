import { useCallback, useEffect, useState } from 'react';
import { calendarDate } from './work-queue.js';

function timestamp(value) {
  const date = new Date(value);
  return value && Number.isFinite(date.getTime()) ? date.toLocaleString() : 'Not yet';
}

export default function OwnershipTransfer({ engagement, disabled, request, onTransferred, onBusyChanged, notificationsEnabled = false }) {
  const [options, setOptions] = useState(null);
  const [target, setTarget] = useState('');
  const [reason, setReason] = useState('');
  const [mode, setMode] = useState('permanent');
  const [returnDate, setReturnDate] = useState('');
  const [returnReason, setReturnReason] = useState('');
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState('');
  const [notifications, setNotifications] = useState(null);
  const [notificationError, setNotificationError] = useState('');
  const [notificationLoading, setNotificationLoading] = useState(false);
  const base = `/api/module025/sow-gsd/${engagement.engagementId}`;
  const todayUtc = new Date().toISOString().slice(0, 10);

  useEffect(() => {
    let disposed = false;
    setOptions(null); setTarget(''); setReason(''); setError(''); setMessage('');
    setMode('permanent'); setReturnDate(''); setReturnReason('');
    request(`${base}/transfer-options`)
      .then(result => { if (!disposed) setOptions(result); })
      .catch(e => { if (!disposed) setError(e.message); });
    return () => { disposed = true; };
  }, [base, engagement.revision, request]);

  const refreshNotifications = useCallback(async () => {
    if (!notificationsEnabled) return;
    setNotificationLoading(true); setNotificationError('');
    try { setNotifications(await request(`${base}/handoff-notifications`)); }
    catch (e) { setNotificationError(e.message); }
    finally { setNotificationLoading(false); }
  }, [base, notificationsEnabled, request]);

  useEffect(() => {
    let disposed = false;
    setNotifications(null); setNotificationError('');
    if (!notificationsEnabled) return undefined;
    setNotificationLoading(true);
    request(`${base}/handoff-notifications`)
      .then(result => { if (!disposed) setNotifications(result); })
      .catch(e => { if (!disposed) setNotificationError(e.message); })
      .finally(() => { if (!disposed) setNotificationLoading(false); });
    return () => { disposed = true; };
  }, [base, engagement.revision, notificationsEnabled, request]);

  async function perform(action, payload) {
    if (disabled || busy) return;
    setBusy(action); onBusyChanged?.(true); setError(''); setMessage('');
    try {
      const result = await request(`${base}/${action}`, { method: 'POST', body: JSON.stringify(payload) });
      if (action === 'handoff/acknowledge') {
        setOptions(await request(`${base}/transfer-options`));
        setMessage('Handoff acknowledged. Your acknowledgement is recorded in the history.');
        await refreshNotifications();
      } else onTransferred(result);
    } catch (e) { setError(e.message); }
    finally { setBusy(''); onBusyChanged?.(false); }
  }

  function transfer() {
    if (!options?.canTransfer || !target || reason.trim().length < 5 || (mode === 'temporary' && (!options.coverageReady || !returnDate || returnDate < todayUtc))) return;
    void perform('transfer', { targetOwnerUserId: target, expectedRevision: options.revision, reason: reason.trim(),
      ...(mode === 'temporary' ? { mode, returnDate } : {}) });
  }

  const handoff = options?.activeCoverage || options?.latestHandoff;
  const coverage = options?.activeCoverage;
  const waiting = Boolean(busy) || disabled;
  return <details className="m025-transfer">
    <summary>Transfer ownership or arrange PTO coverage</summary>
    <p>Transfer this same SOW/GSD record to an authorized teammate. Its version history and handoff evidence stay with the record. The previous owner loses editing access.</p>
    {error && <p role="alert">{error}</p>}
    {message && <p role="status">{message}</p>}
    {!options && !error && <p role="status">Checking your team’s transfer permissions…</p>}
    {handoff && <section className="m025-handoff-state" aria-label="Current handoff">
      <strong>{coverage ? 'Temporary coverage is active' : handoff.mode === 'temporary' ? 'Temporary coverage completed' : 'Latest ownership handoff'}</strong>
      <dl>
        <dt>From</dt><dd>{handoff.previousOwnerDisplayName}</dd>
        <dt>Responsible SA</dt><dd>{handoff.newOwnerDisplayName}</dd>
        {handoff.returnDate && <><dt>Expected return (UTC)</dt><dd className={handoff.returnOverdue ? 'm025-overdue' : ''}>{calendarDate(handoff.returnDate)}{handoff.returnOverdue ? ' · Return overdue' : handoff.returnDue ? ' · Return due' : ''}</dd></>}
        <dt>Acknowledgement</dt><dd>{handoff.acknowledgedAt ? `Recorded ${timestamp(handoff.acknowledgedAt)}` : 'Awaiting the receiving SA'}</dd>
        {handoff.returnedAt && <><dt>Returned</dt><dd>{timestamp(handoff.returnedAt)}</dd></>}
      </dl>
      {options.canAcknowledge && <button type="button" className="m025-button" disabled={waiting} onClick={() => void perform('handoff/acknowledge', { handoffId: options.latestHandoff.handoffId, expectedRevision: options.revision })}>{busy === 'handoff/acknowledge' ? 'Recording acknowledgement…' : 'Acknowledge handoff'}</button>}
      {coverage && <>
        <p>The return date is a reminder. The current SA or authorized manager returns ownership explicitly after checking the work.</p>
        {options.canReturn ? <>
          <label>Return handoff notes<textarea rows={3} maxLength={1000} value={returnReason} disabled={waiting} onChange={event => setReturnReason(event.target.value)} placeholder="What was completed and what remains for the returning teammate?" /></label>
          <button type="button" className="m025-button" disabled={waiting || returnReason.trim().length < 5} onClick={() => void perform('handoff/return', { handoffId: coverage.handoffId, expectedRevision: options.revision, reason: returnReason.trim() })}>{busy === 'handoff/return' ? 'Returning ownership…' : `Return ownership to ${coverage.previousOwnerDisplayName}`}</button>
        </> : <p role="status">{options.returnBlockedReason}</p>}
      </>}
    </section>}
    {options?.blockedReason && <p role="status">{options.blockedReason}</p>}
    {options?.canTransfer && <>
      <label>New responsible Solution Architect<select value={target} disabled={waiting} onChange={event => setTarget(event.target.value)}>
        <option value="">Choose a teammate</option>
        {options.destinations.map(person => <option key={person.userId} value={person.userId}>{person.displayName}{person.teamName ? ` · ${person.teamName}` : ''}</option>)}
      </select></label>
      {options.coverageReady && <label>Handoff type<select value={mode} disabled={waiting} onChange={event => setMode(event.target.value)}>
        <option value="permanent">Permanent ownership transfer</option><option value="temporary">Temporary PTO coverage</option>
      </select></label>}
      {mode === 'temporary' && <label>Expected return date (UTC)<input type="date" value={returnDate} min={todayUtc} disabled={waiting} onChange={event => setReturnDate(event.target.value)} /><small>Ownership returns through an explicit action. Work completed during coverage is retained.</small></label>}
      <label>Handoff reason and context<textarea value={reason} rows={3} maxLength={1000} disabled={waiting} onChange={event => setReason(event.target.value)} placeholder="For example: PTO coverage, remaining scope questions, and next steps." /></label>
      {disabled && <p>Wait for changes to be saved and any current action to finish before transferring.</p>}
      <button type="button" className="m025-button" disabled={waiting || !target || reason.trim().length < 5 || (mode === 'temporary' && (!returnDate || returnDate < todayUtc))} onClick={transfer}>{busy === 'transfer' ? 'Transferring…' : mode === 'temporary' ? 'Start temporary coverage' : 'Transfer this SOW / GSD'}</button>
    </>}
    {notificationsEnabled && <section className="m025-notification-status" aria-label="Handoff notification delivery">
      <strong>Handoff notifications · Module 065</strong>
      <p>Email dispatch status is shown here. Review Teams delivery in Module 065 when enabled.</p>
      {notificationError && <p role="alert">Delivery status could not be loaded: {notificationError}</p>}
      {notifications?.message && <p>{notifications.message}</p>}
      {notifications?.items?.length > 0 && <ul>{notifications.items.slice(0, 5).map(item => <li key={item.eventId}>
        {String(item.kind || 'handoff').replaceAll('_', ' ')}: <strong>{String(item.status || 'unknown').replaceAll('_', ' ')}</strong>
        {item.deliveryBoundary === 'test_only' ? ' · Test delivery boundary' : ''} · {timestamp(item.occurredAt)}
      </li>)}</ul>}
      <button type="button" className="m025-button" disabled={notificationLoading || Boolean(busy)} onClick={() => void refreshNotifications()}>{notificationLoading ? 'Checking delivery…' : 'Refresh delivery status'}</button>
    </section>}
  </details>;
}
