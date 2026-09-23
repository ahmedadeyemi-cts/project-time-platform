import { useEffect, useId, useRef, useState, useSyncExternalStore } from 'react';
import { requestHeaders } from './UnifiedProjectFinancialWorkspace.jsx';
import { EFFECTIVE_ROLE_AUTHORITY_EVENTS } from './effective-role-authority.js';
import { actionStatus, completionActions, confirmationRequest, mayRecord } from './completion-checklist-model.mjs';
import './project-completion-checklist.css';

// Keep session values private: only a monotonic generation reaches React keys.
let identitySignature = null;
let identityGeneration = 0;
function currentIdentity() {
  try {
    return `${window.localStorage.getItem('projectPulseAuthSession') || ''}|${window.localStorage.getItem('projectPulseViewAsUser') || ''}`;
  } catch { return 'storage-unavailable'; }
}
function snapshot() {
  const next = currentIdentity();
  if (next !== identitySignature) { identitySignature = next; identityGeneration += 1; }
  return identityGeneration;
}
function subscribe(notify) {
  const events = [...new Set([...EFFECTIVE_ROLE_AUTHORITY_EVENTS, 'projectpulse:auth-session-ready', 'projectpulse:auth-session-cleared'])];
  events.forEach(event => window.addEventListener(event, notify));
  return () => events.forEach(event => window.removeEventListener(event, notify));
}
const today = () => {
  const date = new Date();
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
};
const blank = id => ({ occurredOn: today(), reference: '', evidence: '', party: '', notes: '', reason: '', scope: id === 'acceptance' ? 'accepted' : 'partial', confirmed: false, coveredInvoiceIds: [] });
const receiptFor = (id, data) => data?.state?.[id === 'sent' ? 'sent' : id === 'billed' ? 'billed' : id];

export default function ProjectCompletionChecklist({ projectId, onSaved }) {
  const identity = useSyncExternalStore(subscribe, snapshot, () => 0);
  return projectId ? <CompletionChecklist key={`${identity}:${projectId}`} projectId={projectId} identity={identity} onSaved={onSaved} /> : null;
}

function CompletionChecklist({ projectId, identity, onSaved }) {
  const prefix = useId();
  const [loaded, setLoaded] = useState({ data: null, error: '', loading: true });
  const [revision, setRevision] = useState(0);
  const [action, setAction] = useState('');
  const [form, setForm] = useState(blank);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [uncertain, setUncertain] = useState(false);
  const requestRef = useRef(null);
  const abortRef = useRef(null);
  const alive = useRef(true);
  const inFlight = useRef(false);
  const data = loaded.data;
  const endpoint = `/api/work-lifecycle/projects/${encodeURIComponent(projectId)}/completion-checklist`;
  const identityMatches = () => snapshot() === identity;

  useEffect(() => { alive.current = true; return () => { alive.current = false; abortRef.current?.abort(); }; }, []);
  useEffect(() => {
    const controller = new AbortController();
    setLoaded({ data: null, error: '', loading: true });
    fetch(endpoint, { credentials: 'include', cache: 'no-store', headers: { Accept: 'application/json', ...requestHeaders() }, signal: controller.signal })
      .then(async response => {
        const payload = await response.json().catch(() => null);
        if (!response.ok) throw new Error(payload?.message || (response.status === 404 ? 'This project is not available to your current role.' : `Checklist could not be verified (HTTP ${response.status}).`));
        if (payload?.contract !== 'project-completion-evidence-v1' || payload.projectId !== projectId || !Number.isSafeInteger(payload.state?.revision) || !payload.basisFingerprint) throw new Error('The server returned an incomplete checklist. No confirmation is enabled.');
        if (!controller.signal.aborted && identityMatches()) setLoaded({ data: payload, error: '', loading: false });
      }).catch(error => {
        if (!controller.signal.aborted && identityMatches()) setLoaded({ data: null, error: error.message, loading: false });
      });
    return () => controller.abort();
  }, [endpoint, projectId, identity, revision]);

  function open(id) {
    if (inFlight.current || uncertain || !mayRecord(id, data)) return;
    setAction(id); setForm(blank(id)); setMessage(''); requestRef.current = null;
  }
  function update(field, value) {
    if (inFlight.current || uncertain) return;
    setForm(current => ({ ...current, [field]: value })); requestRef.current = null;
  }
  function refresh() {
    if (inFlight.current) return;
    setAction(''); setForm(blank()); requestRef.current = null; setUncertain(false); setRevision(value => value + 1);
  }
  async function save(event) {
    event.preventDefault();
    if (inFlight.current || !identityMatches() || !mayRecord(action, data)) return;
    if (!form.confirmed) { setMessage('Select the confirmation checkbox before saving.'); return; }
    // On an uncertain network result, retry exactly the same operation, not a new financial attestation.
    requestRef.current ||= confirmationRequest(form, data, crypto.randomUUID());
    inFlight.current = true; setBusy(true); setMessage('');
    const controller = new AbortController(); abortRef.current = controller;
    const timeout = window.setTimeout(() => controller.abort(), 30000);
    try {
      if (!identityMatches()) return;
      const response = await fetch(`${endpoint}/${action}`, { method: 'POST', credentials: 'include', cache: 'no-store',
        headers: { Accept: 'application/json', 'Content-Type': 'application/json', ...requestHeaders() }, body: JSON.stringify(requestRef.current), signal: controller.signal });
      const payload = await response.json().catch(() => null);
      if (!alive.current || !identityMatches()) return;
      if (!response.ok) {
        const outcomeUnknown = response.status >= 500 || !payload;
        setUncertain(outcomeUnknown);
        setMessage(payload?.message || 'The result could not be confirmed. Refresh to verify the saved state, or retry the same confirmation.');
        if (response.status === 409 || response.status === 403 || response.status === 404 || response.status === 401) {
          setLoaded(current => ({ ...current, data: null, error: payload?.message || 'Access or project evidence changed. Refresh before continuing.' }));
          setAction(''); requestRef.current = null; setUncertain(false);
        }
        return;
      }
      if (!['completion_evidence_recorded', 'already_recorded'].includes(payload?.status)) throw new Error('Unconfirmed save response');
      setUncertain(false); setMessage(payload.message); setAction(''); requestRef.current = null;
      setRevision(value => value + 1);
      // A parent refresh must never turn a confirmed save into a failed-save message.
      Promise.resolve().then(() => onSaved?.()).catch(() => {});
    } catch {
      if (alive.current && identityMatches()) {
        setUncertain(true); setMessage('The save result is not confirmed. Do not send billing again. Retry this same confirmation, or refresh to verify whether it was recorded.');
      }
    } finally {
      window.clearTimeout(timeout); inFlight.current = false;
      if (alive.current) setBusy(false);
    }
  }
  const selectedAction = completionActions.find(item => item.id === action);
  const reopening = action.startsWith('reopen_');
  const title = selectedAction?.title || (action === 'reopen_delivery' ? 'Reopen delivery evidence' : 'Reopen billing evidence');
  const completed = data ? [data.deliveryComplete, data.customerAcceptanceComplete, Boolean(data.state.sent) || data.automatedFinalDelivered, data.fullyBilled].filter(Boolean).length : 0;
  return <section className="completion-checklist" aria-labelledby={`${prefix}-title`}>
    <header className="completion-heading"><div><p className="completion-eyebrow">Project completion</p><h2 id={`${prefix}-title`}>Delivery, acceptance &amp; billing</h2><p>Record what is finished, see what remains, then complete governed closeout.</p></div><button type="button" onClick={refresh} disabled={busy}>Refresh checklist</button></header>
    <p className="completion-notice">Already handled billing outside Pulse? PTC can record the Certinia handoff here without a SELL connection or rate setup. This checklist does not send an invoice or confirm customer payment.</p>
    {loaded.loading ? <p role="status">Verifying project evidence and your authority…</p> : loaded.error ? <p role="alert" className="completion-warning">{loaded.error}</p> : data ? <>
      <p className="completion-progress" role="status">{completed} of 4 steps recorded{data.closed ? ' · Project closed or archived' : ''}. Saved evidence revision {data.state.revision}.</p>
      <div className="completion-steps">{completionActions.map((item, index) => {
        const receipt = receiptFor(item.id, data);
        return <article key={item.id}><span className="completion-step-number" aria-hidden="true">{index + 1}</span><h3>{item.title}</h3><p><strong>{actionStatus(item.id, data)}</strong></p><p>{item.owner}</p>
          {receipt ? <p className="completion-reference">{receipt.occurredOn} · {receipt.reference}</p> : null}
          {mayRecord(item.id, data) ? <button type="button" disabled={busy || uncertain} onClick={() => open(item.id)}>{receipt ? `Update ${item.title.toLowerCase()}` : item.button}</button> : <p className="completion-role-note">{data.closed ? 'Reopen the project to change evidence.' : 'Recorded by the responsible role.'}</p>}
          {receipt ? <details><summary>Saved evidence</summary><dl><dt>Supporting reference</dt><dd>{receipt.evidence}</dd>{receipt.party ? <><dt>Customer representative</dt><dd>{receipt.party}</dd></> : null}<dt>Recorded at</dt><dd>{receipt.recordedAt}</dd><dt>Recorded by (user ID)</dt><dd>{receipt.recordedBy}</dd>{receipt.notes ? <><dt>Notes</dt><dd>{receipt.notes}</dd></> : null}</dl></details> : null}
        </article>;
      })}</div>
      {data.pendingTimeCount > 0 ? <p className="completion-warning">{data.pendingTimeCount} billable time entries still require approval. Fully billed cannot be confirmed yet.</p> : null}
      {data.openTransmissionCount > 0 ? <p className="completion-warning">{data.openTransmissionCount} Certinia deliveries are queued, processing or retryable. Resolve those deliveries before recording manual billing to avoid sending charges twice.</p> : null}
      {data.billingEvidenceStale ? <p className="completion-warning">Charges or delivery evidence changed after billing was confirmed. Review the differences and record a new final reconciliation before closeout.</p> : null}
      {action ? <form className="completion-form" onSubmit={save} aria-labelledby={`${prefix}-form`}>
        <h3 id={`${prefix}-form`}>{title}</h3>
        <fieldset disabled={busy || uncertain}><legend>{reopening ? 'Correct the recorded evidence without deleting its history' : 'Record evidence already obtained'}</legend>
        {!reopening ? <div className="completion-fields">
          <label>Actual date<input type="date" value={form.occurredOn} max={today()} required onChange={event => update('occurredOn', event.target.value)} /></label>
          <label>{action === 'billed' ? 'Final invoice / Billing completion reference' : action === 'sent' ? 'Certinia package / handoff reference' : 'Delivery / deliverable version reference'}<input value={form.reference} required minLength={2} maxLength={500} onChange={event => update('reference', event.target.value)} /></label>
          {action === 'acceptance' ? <><label>Customer decision<select value={form.scope} onChange={event => update('scope', event.target.value)}><option value="accepted">Accepted</option><option value="conditional">Accepted with outstanding conditions</option><option value="rejected">Corrections requested</option></select></label><label>Customer representative<input value={form.party} required maxLength={200} onChange={event => update('party', event.target.value)} /></label><p>Viewing a plan or opening a link is not acceptance. Outstanding conditions must be resolved before closeout.</p></> : null}
          {action === 'sent' ? <><label>Package type<select value={form.scope} onChange={event => update('scope', event.target.value)}><option value="partial">Partial billing — project remains open</option><option value="final">Final handoff — includes reconciliation of prior partial billing</option></select></label><p>Final handoff covers the current project charges and reconciles prior invoices. It prevents another automatic send until billing evidence is explicitly reopened.</p>{form.scope === 'partial' && data.invoices?.length ? <fieldset className="completion-invoice-selection"><legend>Existing Pulse invoices included in this partial handoff</legend>{data.invoices.map(invoice => <label key={invoice.invoiceId}><input type="checkbox" checked={form.coveredInvoiceIds.includes(invoice.invoiceId)} onChange={event => update('coveredInvoiceIds', event.target.checked ? [...form.coveredInvoiceIds, invoice.invoiceId] : form.coveredInvoiceIds.filter(id => id !== invoice.invoiceId))} />{invoice.invoiceNumber}</label>)}</fieldset> : null}</> : null}
          <label className="completion-wide">Supporting email, document or ticket reference<textarea value={form.evidence} required minLength={5} maxLength={2000} rows={2} onChange={event => update('evidence', event.target.value)} placeholder="Reference an existing approved document or recorded customer/Billing confirmation. No upload or external lookup occurs here." /></label>
          <label className="completion-wide">Notes / conditions<textarea value={form.notes} maxLength={2000} rows={2} onChange={event => update('notes', event.target.value)} /></label>
        </div> : <p>Previous receipts remain in the audit history. Reopening billing does not cancel a real Certinia invoice or authorize resending an invoice with a recorded manual handoff.</p>}
        <label>Audit reason<textarea value={form.reason} required minLength={5} maxLength={500} rows={2} onChange={event => update('reason', event.target.value)} /></label>
        <label className="completion-confirm"><input type="checkbox" checked={form.confirmed} required onChange={event => update('confirmed', event.target.checked)} /><span>{selectedAction?.confirm || 'I confirm this evidence needs review and understand that the previous record will remain in history.'}</span></label>
        </fieldset>
        <div className="completion-actions"><button type="submit" disabled={busy || !form.confirmed}>{busy ? 'Saving confirmation…' : uncertain ? 'Retry the same confirmation' : 'Save confirmation'}</button><button type="button" disabled={busy} onClick={() => { if (uncertain) refresh(); else setAction(''); }}>{uncertain ? 'Refresh and verify outcome' : 'Cancel'}</button></div>
      </form> : null}
      <details className="completion-corrections"><summary>Corrections and final closeout</summary><p>After these steps, finish the time/expense review and resolve the remaining project tasks in Project Closeout. Payment collection is not part of this checklist. Existing non-billable and approved write-off dispositions remain separate.</p><div className="completion-actions">{mayRecord('reopen_delivery', data) && data.state.delivery ? <button type="button" disabled={busy || uncertain} onClick={() => open('reopen_delivery')}>Reopen delivery evidence</button> : null}{mayRecord('reopen_billing', data) && (data.state.sent || data.state.billed) ? <button type="button" disabled={busy || uncertain} onClick={() => open('reopen_billing')}>Reopen billing evidence</button> : null}</div></details>
    </> : null}
    {message ? <p role="status" className="completion-notice">{message}</p> : null}
  </section>;
}
