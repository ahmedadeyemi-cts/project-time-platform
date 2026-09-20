import { useEffect, useRef, useState } from 'react';
import { formatTargetDate, isTrackingOverdue, trackingForm, trackingIssues } from './work-tracking.js';
import './work-tracking.css';

function timeLabel(value) {
  if (!value) return 'Not recorded';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Not recorded' : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

export default function WorkTrackingPanel({ engagementId, identityKey, request, readOnly = false, onDirtyChanged, onBusyChanged, onSaved }) {
  const [payload, setPayload] = useState(null);
  const [form, setForm] = useState(() => trackingForm());
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [conflict, setConflict] = useState(false);
  const [message, setMessage] = useState('');
  const sequence = useRef(0);
  const callbacks = useRef({ onDirtyChanged, onBusyChanged, onSaved });
  callbacks.current = { onDirtyChanged, onBusyChanged, onSaved };
  const url = `/api/module025/sow-gsd/${engagementId}/work-tracking`;
  useEffect(() => { callbacks.current.onDirtyChanged?.(dirty); }, [dirty]);
  useEffect(() => { callbacks.current.onBusyChanged?.(busy); }, [busy]);
  useEffect(() => () => {
    sequence.current++;
    callbacks.current.onDirtyChanged?.(false);
    callbacks.current.onBusyChanged?.(false);
  }, []);

  async function reload() {
    const current = ++sequence.current;
    setLoading(true); setError(''); setConflict(false); setMessage('');
    try {
      const result = await request(url);
      if (current !== sequence.current) return;
      setPayload(result); setForm(trackingForm(result.tracking)); setDirty(false);
    } catch (e) {
      if (current === sequence.current) setError(e.message || 'Work tracking could not be loaded.');
    } finally { if (current === sequence.current) setLoading(false); }
  }

  useEffect(() => {
    setPayload(null); setForm(trackingForm()); setDirty(false); setBusy(false);
    void reload();
    return () => { sequence.current++; };
  }, [engagementId, identityKey, request]);

  const canEdit = Boolean(payload?.schemaReady && payload?.canEdit && !readOnly);
  const locked = !canEdit || busy || loading;
  const tracking = payload?.tracking || {};
  function change(field, value) {
    if (locked) return;
    setForm(current => ({ ...current, [field]: value })); setDirty(true); setMessage('');
  }
  async function save(event) {
    event.preventDefault();
    if (locked || !dirty || conflict) return;
    const issues = trackingIssues(form);
    if (issues.length) { setError(issues[0]); return; }
    const current = ++sequence.current;
    setBusy(true); setError(''); setMessage('');
    try {
      const result = await request(url, { method: 'PUT', body: JSON.stringify({
        expectedRevision: tracking.revision ?? 0, targetDate: form.targetDate || null, priority: form.priority,
        blockerReason: form.blockerReason.trim(), blockerOwnerUserId: form.blockerOwnerUserId || null,
        authoringHours: form.authoringHours === '' ? null : Number(form.authoringHours)
      }) });
      if (current !== sequence.current) return;
      if (!result?.tracking || !Number.isInteger(result.tracking.revision))
        throw new Error('The saved tracking revision could not be verified. Reload tracking before trying again.');
      setPayload(previous => ({ ...previous, ...result })); setForm(trackingForm(result.tracking)); setDirty(false);
      setMessage('Work tracking saved. The SOW/GSD document revision is unchanged.');
      callbacks.current.onSaved?.(result);
    } catch (e) {
      if (current !== sequence.current) return;
      const revisionConflict = e.status === 409;
      setConflict(revisionConflict);
      setError(revisionConflict ? 'Work tracking changed since you opened it. Your entries are still shown. Reload saved tracking to inspect the latest values before editing again.'
        : e.message || 'Work tracking could not be saved. Your entries are still shown.');
    } finally { if (current === sequence.current) setBusy(false); }
  }

  return <section className="m025-tracking" aria-label="Work tracking">
    <div className="m025-tracking__heading"><div><h3>Work tracking</h3><p>Coordinate the request, its target date, and the next person needed to move it forward.</p></div>
      <span role="status">{loading ? 'Loading tracking…' : busy ? 'Saving tracking…' : dirty ? 'Unsaved tracking changes' : `Tracking revision ${tracking.revision ?? 0}`}</span>
    </div>
    {error && <div role="alert" className="m025-tracking__error"><p>{error}</p>
      {(conflict || !payload || error) && <button type="button" className="m025-button" disabled={busy || loading} onClick={reload}>{payload ? 'Reload saved tracking (discard my entries)' : 'Retry loading tracking'}</button>}
    </div>}
    {message && <p role="status" className="m025-tracking__saved">{message}</p>}
    {!loading && payload?.schemaReady === false && <p role="status">Work tracking is awaiting its database update. Existing SOW/GSD authoring remains available.</p>}
    {!loading && payload && <>
      <div className="m025-tracking__summary">
        <span className={isTrackingOverdue(tracking.targetDate) ? 'is-overdue' : ''}>Target: <strong>{formatTargetDate(tracking.targetDate)}</strong>{isTrackingOverdue(tracking.targetDate) ? ' · Overdue' : ''}</span>
        {tracking.blockerReason && <span>Blocker owner: <strong>{tracking.blockerOwnerDisplayName || 'Review assignment'}</strong></span>}
        {Number.isFinite(payload.workflowIdleDays) && <span>No workflow activity for <strong>{payload.workflowIdleDays} day(s)</strong></span>}
        {payload.lastWorkflowActivityAt && <span>Last workflow activity: {timeLabel(payload.lastWorkflowActivityAt)}</span>}
      </div>
      {!canEdit && payload.schemaReady && <p className="m025-tracking__help">Read-only tracking. Changes require the current owner, their reporting manager, or an authorized administrator outside View-As.</p>}
      <form onSubmit={save}>
        <fieldset disabled={locked}><legend className="m025-tracking__legend">Request coordination</legend>
          <div className="m025-tracking__grid">
            <label>Target date<input type="date" value={form.targetDate} onChange={event => change('targetDate', event.target.value)} /></label>
            <label>Priority<select value={form.priority} onChange={event => change('priority', event.target.value)}>
              <option value="low">Low</option><option value="normal">Normal</option><option value="high">High</option><option value="urgent">Urgent</option>
            </select></label>
            <label>Remaining SA authoring effort (hours)<input type="number" min="0" max="1000" step="0.01" value={form.authoringHours} onChange={event => change('authoringHours', event.target.value)} /></label>
          </div>
          <p className="m025-tracking__help">Authoring effort estimates the SA’s remaining preparation work. It is separate from project delivery LOE. Leave it blank when unknown; enter 0 only when no preparation remains.</p>
          <div className="m025-tracking__blocker">
            <label>What is blocking progress?<textarea rows={2} maxLength={2000} value={form.blockerReason} onChange={event => change('blockerReason', event.target.value)} placeholder="Describe the missing input, decision, or assistance needed." /></label>
            <label>Person responsible for resolving the blocker<select value={form.blockerOwnerUserId} onChange={event => change('blockerOwnerUserId', event.target.value)}>
              <option value="">Choose a responsible person</option>
              {form.blockerOwnerUserId && !(payload.blockerOwners || []).some(person => person.userId === form.blockerOwnerUserId) &&
                <option value={form.blockerOwnerUserId} disabled>{tracking.blockerOwnerDisplayName || 'Saved blocker owner'} (no longer assignable)</option>}
              {(payload.blockerOwners || []).map(person => <option key={person.userId} value={person.userId}>{person.displayName}</option>)}
            </select></label>
          </div>
          {(form.blockerReason || form.blockerOwnerUserId) && <button type="button" className="m025-button" onClick={() => { setForm(current => ({ ...current, blockerReason: '', blockerOwnerUserId: '' })); setDirty(true); setMessage(''); }}>Clear blocker</button>}
        </fieldset>
        <div className="m025-tracking__actions"><button type="submit" className="m025-button m025-button--primary" disabled={locked || !dirty || conflict}>{busy ? 'Saving tracking…' : 'Save work tracking'}</button>
          <span>{dirty ? 'Save tracking changes before leaving this record.' : 'Scheduling updates are recorded separately from document versions.'}</span>
        </div>
      </form>
      {payload.history?.length > 0 && <details className="m025-tracking__history"><summary>Recent tracking history ({payload.history.length}, up to {payload.historyLimit || 30} shown)</summary><ol>
        {payload.history.map((entry, index) => <li key={entry.revision ?? index}><strong>{entry.actorDisplayName || 'Recorded actor'}</strong> · {timeLabel(entry.updatedAt)}
          <span>Revision {entry.revision} · {entry.priority || 'normal'} priority · Target {formatTargetDate(entry.targetDate)}</span>
          <span>SA authoring remaining: {entry.authoringHours == null ? 'Unknown' : `${entry.authoringHours}h`}</span>
          {entry.blockerReason ? <p>{entry.blockerReason}<br />Blocker owner: {entry.blockerOwnerDisplayName || 'Recorded owner unavailable'}</p> : <p>No blocker recorded.</p>}
        </li>)}
      </ol></details>}
    </>}
  </section>;
}
