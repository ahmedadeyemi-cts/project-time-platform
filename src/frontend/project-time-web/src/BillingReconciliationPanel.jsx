import { useEffect, useRef, useState } from 'react';
import { requestHeaders } from './UnifiedProjectFinancialWorkspace.jsx';

export default function BillingReconciliationPanel({ invoiceId }) {
  const [data, setData] = useState(null), [revision, setRevision] = useState(0);
  const [action, setAction] = useState('hold_delivery'), [reference, setReference] = useState(''), [reason, setReason] = useState('');
  const [confirmed, setConfirmed] = useState(false), [busy, setBusy] = useState(false), [uncertain, setUncertain] = useState(false), [message, setMessage] = useState('');
  const operation = useRef(null), inFlight = useRef(false);
  const endpoint = `/api/billing/invoices/${encodeURIComponent(invoiceId)}/reconciliation`;
  useEffect(() => {
    const abort = new AbortController(); setData(null);
    fetch(endpoint, { credentials: 'include', cache: 'no-store', headers: requestHeaders(), signal: abort.signal })
      .then(async response => { const payload = await response.json(); if (!response.ok) throw new Error(payload.message || 'Reconciliation is not available to this session.'); if (!abort.signal.aborted) setData(payload); })
      .catch(error => { if (!abort.signal.aborted) setMessage(error.message); });
    return () => abort.abort();
  }, [endpoint, revision]);
  async function save(event) {
    event.preventDefault(); if (inFlight.current || !confirmed || !data?.canRecord) return;
    operation.current ||= { operationId: crypto.randomUUID(), action, reference, reason, confirmed };
    inFlight.current = true; setBusy(true);
    const abort = new AbortController(); const timer = setTimeout(() => abort.abort(), 30000);
    try {
      const response = await fetch(endpoint, { method: 'POST', credentials: 'include', headers: { ...requestHeaders(), 'Content-Type': 'application/json' }, body: JSON.stringify(operation.current), signal: abort.signal });
      const result = await response.json();
      if (response.status >= 500) throw new Error('unconfirmed');
      setMessage(result.message || 'Unable to record reconciliation.');
      if (response.ok && !['reconciliation_recorded','already_recorded'].includes(result.status)) throw new Error('unconfirmed');
      operation.current = null; setUncertain(false); setConfirmed(false); setRevision(value => value + 1);
    } catch { setUncertain(true); setMessage('The result is unconfirmed. Retry the same record, or refresh and inspect history. Do not resend an invoice.'); }
    finally { clearTimeout(timer); inFlight.current = false; setBusy(false); }
  }
  const evidence = data?.originalEvidence;
  return <details className="m042-manual-panel"><summary>Billing reconciliation and offline tracking</summary>
    <p>Keep local invoices linked to their commercial terms and delivery evidence. These actions record your verification; they do not send an invoice, verify an external system automatically, or confirm payment.</p>
    {evidence?.request ? <p>Original basis: {evidence.request.billingBasis || 'Legacy manual amount'}. Commercial reference: {evidence.request.commercialReference || evidence.request.authorizationReference}. {evidence.commercialReconciliationRequired ? 'Created using fallback information; review against SELL when available.' : 'Commercial snapshot retained.'} {evidence.exceptionApprovedBy ? `Exception approved by user ${evidence.exceptionApprovedBy}.` : ''}</p> : null}
    {data?.canRecord ? <form onSubmit={save}>
      <fieldset disabled={busy || uncertain}><legend>Record verified billing evidence</legend><div className="m042-manual-fields">
        <label>Reconciliation action<select aria-label="Reconciliation action" value={action} onChange={event => { setAction(event.target.value); setConfirmed(false); }}>
          <option value="hold_delivery">Hold automatic delivery for review</option>
          <option value="resume_delivery">Release a delivery hold after verifying no external invoice exists</option>
          <option value="manual_handoff">Record an already completed manual handoff</option>
          <option value="certinia_match">Match an existing Certinia invoice</option>
          <option value="sell_verified">Record verification against restored SELL information</option>
        </select></label>
        <label>Verified reference<input required minLength={2} maxLength={500} value={reference} onChange={event => { setReference(event.target.value); setConfirmed(false); }} /></label>
        <label>Reconciliation reason<textarea required minLength={10} maxLength={1000} value={reason} onChange={event => { setReason(event.target.value); setConfirmed(false); }} /></label>
      </div>
      <p>A hold, manual handoff or Certinia match stops queued retries. Releasing a hold permits existing queued work to retry; this is blocked after a manual handoff or external invoice match. A delivery already in progress must finish before you can record a handoff or match. For a Certinia match, verify the customer, invoice number, currency and amount in Certinia first. Resolve differences before recording SELL verification.</p>
      <label className="m042-manual-confirm"><input type="checkbox" required checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />I verified this evidence and understand that it does not confirm payment or project completion.</label></fieldset>
      <button type="submit" disabled={busy || !confirmed}>{busy ? 'Recording…' : uncertain ? 'Retry same reconciliation' : 'Record reconciliation'}</button>
    </form> : null}
    <button type="button" disabled={busy} onClick={() => { operation.current = null; setUncertain(false); setConfirmed(false); setRevision(value => value + 1); }}>Refresh reconciliation history</button>
    {message ? <p role="status">{message}</p> : null}
    {data?.history?.length ? <ol>{data.history.map((item, index) => <li key={index}>{item.action.replace('billing_recovery_', '').replaceAll('_', ' ')} · {item.reference} · {new Date(item.recordedAt).toLocaleString()}<p>{item.reason}</p></li>)}</ol> : <p>No reconciliation records yet.</p>}
  </details>;
}
