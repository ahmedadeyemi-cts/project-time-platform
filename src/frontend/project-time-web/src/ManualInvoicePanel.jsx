import { useEffect, useRef, useState } from 'react';
import { requestHeaders } from './UnifiedProjectFinancialWorkspace.jsx';
import { EFFECTIVE_ROLE_AUTHORITY_EVENTS } from './effective-role-authority.js';
import { manualCharge } from './manual-invoice-model.mjs';

const money = value => Number(value || 0).toLocaleString(undefined, { style: 'currency', currency: 'USD' });
const today = () => { const now = new Date(); return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`; };
export default function ManualInvoicePanel(props) {
  const [generation, setGeneration] = useState(0);
  useEffect(() => {
    const changed = () => setGeneration(value => value + 1);
    const events = [...new Set([...EFFECTIVE_ROLE_AUTHORITY_EVENTS, 'projectpulse:auth-session-ready', 'projectpulse:auth-session-cleared'])];
    events.forEach(event => window.addEventListener(event, changed));
    return () => events.forEach(event => window.removeEventListener(event, changed));
  }, []);
  return <ManualInvoiceForm key={generation} {...props} />;
}
function ManualInvoiceForm({ projectId, projectName, onSaved, onBasis }) {
  const [state, setState] = useState({ loading: true, data: null, error: '' });
  const [revision, setRevision] = useState(0);
  const [form, setForm] = useState({ invoiceType: 'partial', agreedTotal: '', billToDate: '', previouslyBilledOutsidePulse: '0',
    periodStart: today(), periodEnd: today(), description: '', authorizationReference: '', externalBillingReference: '', reason: '', confirmed: false });
  const [busy, setBusy] = useState(false), [uncertain, setUncertain] = useState(false), [message, setMessage] = useState('');
  const flight = useRef(false), operation = useRef(null), alive = useRef(true), saveAbort = useRef(null);
  const endpoint = `/api/billing/projects/${encodeURIComponent(projectId)}`;
  useEffect(() => { alive.current = true; return () => { alive.current = false; saveAbort.current?.abort(); }; }, []);
  useEffect(() => {
    const abort = new AbortController();
    setState({ loading: true, data: null, error: '' });
    fetch(`${endpoint}/manual`, { credentials: 'include', cache: 'no-store', headers: requestHeaders(), signal: abort.signal })
      .then(async response => {
        const data = await response.json();
        if (!response.ok) throw new Error(data.message || 'Manual billing is not available to this session.');
        if (!data.basis?.fingerprint || data.projectId !== projectId) throw new Error('Billing balance could not be verified.');
        if (!abort.signal.aborted) {
          setState({ loading: false, data, error: '' });
          onBasis?.({ projectId, ...data.basis });
          setForm(value => ({ ...value, previouslyBilledOutsidePulse: String(data.basis.previouslyBilledOutsidePulse), confirmed: false }));
        }
      }).catch(error => { if (!abort.signal.aborted) setState({ loading: false, data: null, error: error.message }); });
    return () => abort.abort();
  }, [endpoint, projectId, revision]);
  const basis = state.data?.basis;
  const charge = basis ? manualCharge(form.agreedTotal, form.billToDate, form.previouslyBilledOutsidePulse, basis.pulseInvoiced, form.invoiceType) : null;
  const blocked = !state.data?.canCreate || basis?.closed || basis?.finalInvoiceExists || basis?.openTransmission;
  function update(key, value) { if (flight.current || uncertain) return; operation.current = null; setForm(current => ({ ...current, [key]: value, ...(key === 'confirmed' ? {} : { confirmed: false }) })); }
  function reload() { if (flight.current) return; operation.current = null; setUncertain(false); setRevision(value => value + 1); }
  async function save(event) {
    event.preventDefault();
    if (flight.current || blocked || !form.confirmed || charge === null) return;
    operation.current ||= { ...form, operationId: crypto.randomUUID(), expectedFingerprint: basis.fingerprint,
      agreedTotal: Number(form.agreedTotal), billToDate: Number(form.invoiceType === 'final' ? form.agreedTotal : form.billToDate),
      previouslyBilledOutsidePulse: Number(form.previouslyBilledOutsidePulse) };
    flight.current = true; setBusy(true); setMessage('');
    const abort = new AbortController(); saveAbort.current = abort;
    const timer = window.setTimeout(() => abort.abort(), 30000);
    try {
      const response = await fetch(`${endpoint}/manual-invoices`, { method: 'POST', credentials: 'include',
        headers: { ...requestHeaders(), 'Content-Type': 'application/json' }, body: JSON.stringify(operation.current), signal: abort.signal });
      const data = await response.json().catch(() => null);
      if (!alive.current) return;
      if (!response.ok) {
        if (response.status >= 500 || !data) throw new Error('unconfirmed');
        setMessage([data.message, ...(data.blockers || [])].filter(Boolean).join(' '));
        setUncertain(false); operation.current = null;
        if ([401, 403, 404, 409].includes(response.status)) setRevision(value => value + 1);
        return;
      }
      if (!['billing_invoice_created', 'already_recorded'].includes(data?.status) || !data.invoice?.header?.invoiceNumber) throw new Error('unconfirmed');
      setMessage(`${data.invoice.header.invoiceNumber} saved for ${money(data.invoice.header.totalAmount)}. Select it in invoice history to download PDF or Excel. Delivery and payment have not been recorded.`);
      setUncertain(false); operation.current = null;
      setForm(current => ({ ...current, confirmed: false, billToDate: '', description: '', reason: '' }));
      setRevision(value => value + 1);
      Promise.resolve().then(() => onSaved?.()).catch(() => {});
    } catch { if (alive.current) { setUncertain(true); setMessage('The save result is not confirmed. Retry this same request, or reload and review invoice history before creating another invoice.'); } }
    finally { window.clearTimeout(timer); flight.current = false; if (alive.current) setBusy(false); }
  }
  return <details className="m042-manual-panel"><summary>Manual partial / full invoice</summary>
    <h2>Manual invoice · {projectName}</h2>
    <p>Create an invoice without Certinia, SELL, or approved time entries. Enter the cumulative amount to bill for the project; prior invoices are deducted automatically. A full invoice covers the remaining agreed balance.</p>
    {state.loading ? <p role="status">Loading prior billing…</p> : state.error ? <p role="alert">{state.error}</p> : <>
      <dl className="m042-reference-summary"><div><dt>Already invoiced in Pulse</dt><dd>{money(basis.pulseInvoiced)}</dd></div><div><dt>Previously recorded outside Pulse</dt><dd>{money(basis.previouslyBilledOutsidePulse)}</dd></div><div><dt>New invoice amount</dt><dd>{charge === null ? 'Enter billing amounts' : money(charge)}</dd></div></dl>
      {basis.manualInvoicesExist ? <p>This project uses manual amount billing. Continue here for later invoices so the same time is not charged again through the time-based path.</p> : null}
      {blocked ? <p role="status">{basis.closed ? 'Reopen this project before additional billing.' : basis.finalInvoiceExists ? 'A final invoice is already recorded. Review the invoice history below.' : basis.openTransmission ? 'Resolve queued or retryable Certinia deliveries before manual reconciliation.' : 'Your current role can view billing but cannot create invoices.'}</p> : <form onSubmit={save}>
        <fieldset disabled={busy || uncertain}><legend>Amounts and authorization (USD)</legend><div className="m042-manual-fields">
          <label>Invoice type<select value={form.invoiceType} onChange={event => update('invoiceType', event.target.value)}><option value="partial">Partial invoice</option><option value="final">Full / final invoice</option></select></label>
          <label>Agreed project total<input type="number" min="0.01" step="0.01" required value={form.agreedTotal} onChange={event => update('agreedTotal', event.target.value)} /></label>
          {form.invoiceType === 'partial' ? <label>Cumulative amount to bill through this invoice<input type="number" min="0.01" step="0.01" required value={form.billToDate} onChange={event => update('billToDate', event.target.value)} /></label> : null}
          <label>Already billed outside Pulse<input type="number" min={basis.previouslyBilledOutsidePulse} step="0.01" required value={form.previouslyBilledOutsidePulse} onChange={event => update('previouslyBilledOutsidePulse', event.target.value)} /><small>Exclude invoices already listed in Pulse. Include all prior external charges for this project.</small></label>
          <label>Billing period start<input type="date" required value={form.periodStart} onChange={event => update('periodStart', event.target.value)} /></label>
          <label>Billing period end<input type="date" max={today()} required value={form.periodEnd} onChange={event => update('periodEnd', event.target.value)} /></label>
          <label>Approved SOW / PO / billing authorization<input required minLength={2} maxLength={500} value={form.authorizationReference} onChange={event => update('authorizationReference', event.target.value)} /></label>
          <label>Prior external invoice references<input required={Number(form.previouslyBilledOutsidePulse) > 0} maxLength={1000} value={form.externalBillingReference} onChange={event => update('externalBillingReference', event.target.value)} /></label>
          <label>Customer-facing description<textarea required minLength={5} maxLength={2000} value={form.description} onChange={event => update('description', event.target.value)} /></label>
          <label>Internal audit reason<textarea required minLength={5} maxLength={500} value={form.reason} onChange={event => update('reason', event.target.value)} /></label>
        </div><label className="m042-manual-confirm"><input type="checkbox" required checked={form.confirmed} onChange={event => update('confirmed', event.target.checked)} />I verified the authorization and all prior billing. Only the new amount shown above should be invoiced.</label></fieldset>
        <p>Time entries remain unchanged. Creating a final invoice does not approve time, confirm payment, or close the project.</p>
        <button type="submit" className="primary-action" disabled={busy || charge === null || !form.confirmed}>{busy ? 'Saving invoice…' : uncertain ? 'Retry same invoice request' : `Create ${form.invoiceType === 'final' ? 'full / final' : 'partial'} invoice`}</button>
      </form>}
    </>}
    <button type="button" className="secondary-action" disabled={busy} onClick={reload}>Reload billing balance</button>
    {message ? <p role="status">{message}</p> : null}
  </details>;
}
