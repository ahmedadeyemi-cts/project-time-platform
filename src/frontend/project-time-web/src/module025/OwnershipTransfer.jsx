import { useEffect, useState } from 'react';

export default function OwnershipTransfer({ engagement, disabled, request, onTransferred, onBusyChanged }) {
  const [options, setOptions] = useState(null);
  const [target, setTarget] = useState('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let disposed = false;
    setOptions(null); setTarget(''); setReason(''); setError('');
    request(`/api/module025/sow-gsd/${engagement.engagementId}/transfer-options`)
      .then(result => { if (!disposed) setOptions(result); })
      .catch(e => { if (!disposed) setError(e.message); });
    return () => { disposed = true; };
  }, [engagement.engagementId, engagement.revision, request]);
  async function transfer() {
    if (disabled || busy || !options?.canTransfer || !target || reason.trim().length < 5) return;
    setBusy(true); onBusyChanged?.(true); setError('');
    try {
      const result = await request(`/api/module025/sow-gsd/${engagement.engagementId}/transfer`, {
        method: 'POST', body: JSON.stringify({ targetOwnerUserId: target, expectedRevision: options.revision, reason: reason.trim() })
      });
      onTransferred(result);
    } catch (e) { setError(e.message); }
    finally { setBusy(false); onBusyChanged?.(false); }
  }
  return <details className="m025-transfer">
    <summary>Transfer ownership or arrange PTO coverage</summary>
    <p>Transfer this same SOW/GSD record to an authorized teammate. Its version history and handoff evidence stay with the record. The previous owner loses editing access.</p>
    {error && <p role="alert">{error}</p>}
    {!options && !error && <p role="status">Checking your team’s transfer permissions…</p>}
    {options?.blockedReason && <p role="status">{options.blockedReason}</p>}
    {options?.canTransfer && <>
      <label>New responsible Solution Architect<select value={target} disabled={disabled || busy} onChange={event => setTarget(event.target.value)}>
        <option value="">Choose a teammate</option>
        {options.destinations.map(person => <option key={person.userId} value={person.userId}>{person.displayName}{person.teamName ? ` · ${person.teamName}` : ''}</option>)}
      </select></label>
      <label>Handoff reason and context<textarea value={reason} rows={3} maxLength={1000} disabled={disabled || busy} onChange={event => setReason(event.target.value)} placeholder="For example: PTO coverage, remaining scope questions, and next steps." /></label>
      {disabled && <p>Wait for changes to be saved and any current action to finish before transferring.</p>}
      <button type="button" className="m025-button" disabled={disabled || busy || !target || reason.trim().length < 5} onClick={transfer}>{busy ? 'Transferring…' : 'Transfer this SOW / GSD'}</button>
    </>}
  </details>;
}
