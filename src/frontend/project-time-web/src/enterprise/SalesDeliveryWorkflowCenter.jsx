import { useEffect, useMemo, useState } from 'react';
import './sales-delivery-workflow-center.css';
import SowGsdWorkspace from './SowGsdWorkspace';

function token() {
  try { return JSON.parse(localStorage.getItem('projectPulseAuthSession') || 'null')?.sessionToken || ''; } catch { return ''; }
}
function headers(json = false) { const value = token(); return { ...(value ? { 'X-ProjectPulse-Session': value, Authorization: `Bearer ${value}` } : {}), ...(json ? { 'Content-Type': 'application/json' } : {}) }; }
async function request(path, options = {}) {
  const response = await fetch(path, { credentials: 'include', ...options, headers: { ...headers(options.body && !(options.body instanceof FormData)), ...(options.headers || {}) } });
  const text = await response.text(); let body = null; try { body = text ? JSON.parse(text) : null; } catch { body = { message: text }; }
  if (!response.ok) throw new Error(body?.message || body?.status || `${path} returned HTTP ${response.status}.`);
  return body;
}
const words = (value) => String(value ?? '').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
const EMPTY_INTAKE = { clientName: '', opportunityReference: '', requestTitle: '', requestDescription: '', priority: 'normal' };
const uploadKey = (item) => `${item.file.name}:${item.file.size}:${item.file.lastModified}:${item.type}`;

function IntakeUploader({ signed = false }) {
  const storageKey = `projectPulseIntakeDraft:${signed ? 'signed' : 'sales'}`;
  const [draftPackage, setDraftPackage] = useState(() => {
    try { return JSON.parse(sessionStorage.getItem(storageKey) || 'null'); } catch { return null; }
  });
  const [form, setForm] = useState(() => draftPackage?.form || EMPTY_INTAKE);
  const [files, setFiles] = useState([]);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState(null);
  const acceptedTypes = signed ? ['sow', 'gsd', 'purchase_order', 'other'] : ['proposal', 'sow', 'gsd', 'quote', 'purchase_order', 'other'];

  function retainDraft(next) {
    setDraftPackage(next);
    try {
      if (next) sessionStorage.setItem(storageKey, JSON.stringify(next));
      else sessionStorage.removeItem(storageKey);
    } catch { /* A blocked session store must not prevent the resumable in-memory workflow. */ }
  }

  async function submit(event) {
    event.preventDefault(); setBusy(true); setResult(null);
    let workingPackage = draftPackage;
    try {
      if (!workingPackage) {
        const created = await request('/api/project-intake/requests', { method: 'POST', body: JSON.stringify({ ...form, assignedPmUserId: null, accountExecutiveUserId: null, solutionArchitectUserId: null, intakeSource: signed ? 'signed_sales_handoff' : 'sales_upload', sourceSystem: 'ProjectPulse', sourceDocumentRequired: true, intakeSourceNotes: signed ? 'Signed customer package submitted by Sales for PTC handoff.' : 'Sales opportunity/project documents submitted for delivery intake.', estimatedHours: 0, plannedEngineeringCost: 0, plannedPmCost: 0, plannedTotalProjectCost: 0 }) });
        workingPackage = { id: created.projectIntakeRequestId, requestNumber: created.requestNumber, uploadedFileKeys: [], form: { ...form } };
        retainDraft(workingPackage);
      }
      for (const item of files) {
        const key = uploadKey(item);
        if (workingPackage.uploadedFileKeys.includes(key)) continue;
        const data = new FormData(); data.append('file', item.file); data.append('documentType', item.type); data.append('engineeringVisible', 'true'); data.append('aiTimesheetContextEnabled', ['sow', 'gsd'].includes(item.type) ? 'true' : 'false');
        await request(`/api/project-intake/requests/${workingPackage.id}/documents`, { method: 'POST', body: data });
        workingPackage = { ...workingPackage, uploadedFileKeys: [...workingPackage.uploadedFileKeys, key] };
        retainDraft(workingPackage);
      }
      const handoff = signed
        ? await request(`/api/project-intake/requests/${workingPackage.id}/signed-handoff`, { method: 'POST' })
        : null;
      const documentCount = workingPackage.uploadedFileKeys.length;
      setResult({
        ok: true,
        message: handoff?.message || `${workingPackage.requestNumber} created with ${documentCount} document(s).`,
        detail: handoff ? `${documentCount} document(s) retained · ${handoff.recipientCount} PTC recipient(s) · ${words(handoff.status)}` : '',
        id: workingPackage.id
      });
      retainDraft(null); setForm(EMPTY_INTAKE); setFiles([]);
    } catch (error) {
      setResult({
        ok: false,
        message: error.message,
        detail: workingPackage ? `${workingPackage.requestNumber || workingPackage.id} is retained. Retry resumes this package; it will not create another intake.` : ''
      });
    } finally { setBusy(false); }
  }

  const hasSignedSow = files.some((item) => item.type === 'sow')
    || draftPackage?.uploadedFileKeys?.some((key) => key.endsWith(':sow'));
  const hasPendingOrRetainedFiles = files.length > 0
    || Boolean(draftPackage?.uploadedFileKeys?.length);

  return <div className="sales-delivery-two-column"><form className="sales-delivery-card sales-delivery-form" onSubmit={submit}><div className="sales-delivery-card-heading"><div><span>Guided upload</span><h2>{signed ? 'Submit the signed customer package' : 'Create a sales intake package'}</h2></div><b>Step 1 of 2</b></div>{draftPackage ? <div className="sales-delivery-boundary"><strong>Resuming {draftPackage.requestNumber || draftPackage.id}</strong><span>The intake is retained. Already uploaded documents are locked and skipped; retry continues this package without creating a duplicate.</span></div> : null}<div className="sales-delivery-fields"><label>Customer<input required disabled={Boolean(draftPackage)} value={form.clientName} onChange={(event) => setForm((value) => ({ ...value, clientName: event.target.value }))} /></label><label>Opportunity / project reference<input disabled={Boolean(draftPackage)} value={form.opportunityReference} onChange={(event) => setForm((value) => ({ ...value, opportunityReference: event.target.value }))} /></label><label className="is-wide">Package title<input required disabled={Boolean(draftPackage)} value={form.requestTitle} onChange={(event) => setForm((value) => ({ ...value, requestTitle: event.target.value }))} placeholder={signed ? 'Signed SOW — Customer — Project' : 'Opportunity or project name'} /></label><label className="is-wide">Sales context<textarea required disabled={Boolean(draftPackage)} rows="4" value={form.requestDescription} onChange={(event) => setForm((value) => ({ ...value, requestDescription: event.target.value }))} placeholder="Scope, customer outcome, commitments, known dates, and delivery context" /></label><label>Priority<select disabled={Boolean(draftPackage)} value={form.priority} onChange={(event) => setForm((value) => ({ ...value, priority: event.target.value }))}><option value="normal">Normal</option><option value="high">High</option><option value="urgent">Urgent</option></select></label></div><button className="sales-delivery-primary" type="submit" disabled={busy || !hasPendingOrRetainedFiles || (signed && !hasSignedSow)}>{busy ? 'Saving package…' : draftPackage ? 'Resume package' : signed ? 'Submit & notify PTC' : 'Create intake package'}</button>{signed && hasPendingOrRetainedFiles && !hasSignedSow ? <p className="sales-delivery-inline-note">Classify one selected document as SOW to enable the handoff.</p> : null}{result ? <div className={`sales-delivery-result ${result.ok ? 'is-success' : 'is-error'}`}>{result.message}{result.detail ? <span>{result.detail}</span> : null}{result.ok ? <span>Documents become available to engineering automatically after Module 055D links the intake to the project.</span> : null}</div> : null}</form><section className="sales-delivery-card"><div className="sales-delivery-card-heading"><div><span>Documents</span><h2>Upload customer evidence</h2></div><b>{files.length} selected</b></div><label className="sales-delivery-dropzone"><strong>Choose SOW, GSD, PO, quote, proposal, or supporting files</strong><span>Each file retains its type, source, engineering visibility, and download evidence.</span><input type="file" multiple onChange={(event) => setFiles((current) => [...current, ...[...(event.target.files || [])].map((file, index) => ({ file, type: signed && current.length + index === 0 ? 'sow' : 'other' }))])} /></label><div className="sales-delivery-file-list">{files.map((item, index) => { const uploaded = draftPackage?.uploadedFileKeys?.includes(uploadKey(item)); return <article className={uploaded ? 'is-uploaded' : ''} key={`${item.file.name}-${index}`}><div><strong>{item.file.name}</strong><span>{Math.ceil(item.file.size / 1024)} KB{uploaded ? ' · Uploaded' : ''}</span></div><select disabled={uploaded} value={item.type} onChange={(event) => setFiles((current) => current.map((value, itemIndex) => itemIndex === index ? { ...value, type: event.target.value } : value))}>{acceptedTypes.map((type) => <option value={type} key={type}>{words(type)}</option>)}</select><button disabled={uploaded} type="button" onClick={() => setFiles((current) => current.filter((_, itemIndex) => itemIndex !== index))}>{uploaded ? 'Retained' : 'Remove'}</button></article>; })}{!files.length ? <p>{draftPackage?.uploadedFileKeys?.length ? `${draftPackage.uploadedFileKeys.length} uploaded document(s) are retained. Add another file or resume the handoff.` : 'No documents selected yet.'}</p> : null}</div>{signed ? <div className="sales-delivery-boundary"><strong>Automatic PTC delivery</strong><span>Submitting creates one audited Module 065 dispatch to active PTC recipients. The email includes the intake reference and an authenticated View/Download link for every document. Raw customer files remain access-controlled instead of being duplicated into mailboxes.</span></div> : null}</section></div>;
}

function SowGenerator() {
  return <SowGsdWorkspace />;
}

function AiTimeEntry() {
  const [form, setForm] = useState({ projectCode: '', projectName: '', taskCode: '', taskName: '', workDate: new Date().toISOString().slice(0, 10), engineerNote: '' });
  const [state, setState] = useState({ busy: false, result: null, error: '' });
  async function generate(event) { event.preventDefault(); setState({ busy: true, result: null, error: '' }); try { const result = await request('/api/celar-ai/v1/compose', { method: 'POST', body: JSON.stringify({ mode: 'timesheet_description', ...form, requestedOutcome: form.engineerNote, timeType: 'normal', rowType: 'project_task', rowLabel: `${form.projectCode} ${form.taskCode}`.trim(), allowSanitizedExternalFallback: false }) }); setState({ busy: false, result, error: '' }); } catch (error) { setState({ busy: false, result: null, error: error.message }); } }
  return <div className="sales-delivery-two-column"><form className="sales-delivery-card sales-delivery-form" onSubmit={generate}><div className="sales-delivery-card-heading"><div><span>Module 028</span><h2>Turn work notes into a customer-ready description</h2></div><b>Review only</b></div><div className="sales-delivery-fields"><label>Project code<input required value={form.projectCode} onChange={(event) => setForm((value) => ({ ...value, projectCode: event.target.value }))} /></label><label>Project name<input value={form.projectName} onChange={(event) => setForm((value) => ({ ...value, projectName: event.target.value }))} /></label><label>Task code<input value={form.taskCode} onChange={(event) => setForm((value) => ({ ...value, taskCode: event.target.value }))} /></label><label>Task name<input value={form.taskName} onChange={(event) => setForm((value) => ({ ...value, taskName: event.target.value }))} /></label><label>Work date<input type="date" value={form.workDate} onChange={(event) => setForm((value) => ({ ...value, workDate: event.target.value }))} /></label><label className="is-wide">What did you do?<textarea required rows="6" value={form.engineerNote} onChange={(event) => setForm((value) => ({ ...value, engineerNote: event.target.value }))} /></label></div><button className="sales-delivery-primary" type="submit" disabled={state.busy}>{state.busy ? 'Generating…' : 'Generate description'}</button>{state.error ? <div className="sales-delivery-result is-error">{state.error}</div> : null}</form><section className="sales-delivery-card sales-delivery-output"><div className="sales-delivery-card-heading"><div><span>AI route evidence</span><h2>Suggested description</h2></div></div>{state.result ? <pre>{JSON.stringify(state.result.detailedAnswer || state.result, null, 2)}</pre> : <div className="sales-delivery-empty">The suggestion never submits time. Private project evidence stays on the private path, and the engineer remains the final author.</div>}</section></div>;
}

function UatValidation() {
  const checks = useMemo(() => [{ name: 'API health', path: '/health' }, { name: 'Release identity', path: '/api/version' }, { name: 'Module availability', path: '/api/module-availability' }, { name: 'Notification readiness', path: '/api/enterprise-notifications/runtime/readiness' }], []);
  const [results, setResults] = useState([]); const [busy, setBusy] = useState(false);
  async function run() { setBusy(true); const next = await Promise.all(checks.map(async (check) => { const started = performance.now(); try { const response = await fetch(check.path, { credentials: 'include', headers: headers() }); return { ...check, passed: response.ok, status: response.status, duration: Math.round(performance.now() - started) }; } catch (error) { return { ...check, passed: false, status: 'Network error', duration: Math.round(performance.now() - started) }; } })); setResults(next); setBusy(false); }
  useEffect(() => { void run(); }, []);
  return <section className="sales-delivery-card"><div className="sales-delivery-card-heading"><div><span>Live smoke suite</span><h2>UAT validation workspace</h2><p>Small, bounded checks replace the prior endless preview. Results are current-browser evidence and do not mutate business data.</p></div><button className="sales-delivery-primary" type="button" onClick={run} disabled={busy}>{busy ? 'Running…' : 'Run checks'}</button></div><div className="sales-delivery-uat-grid">{checks.map((check) => { const result = results.find((item) => item.path === check.path); return <article key={check.path}><span className={result?.passed ? 'is-success' : result ? 'is-error' : ''}>{result ? result.passed ? 'Passed' : 'Failed' : 'Pending'}</span><strong>{check.name}</strong><small>{check.path}</small><p>{result ? `HTTP ${result.status} · ${result.duration} ms` : 'Waiting to run'}</p></article>; })}</div><div className="sales-delivery-boundary"><strong>Release UAT remains authoritative</strong><span>Role workflows, database migrations, exact-head CI, signed-in acceptance, rollback, and customer approval still belong to the protected deployment and Audit History—not a browser-only green badge.</span></div></section>;
}

export default function SalesDeliveryWorkflowCenter({ module }) {
  return <section className="sales-delivery-workflow-center" data-module={module}>{module === '024' ? <IntakeUploader /> : null}{module === '025' ? <SowGenerator /> : null}{module === '027' ? <IntakeUploader signed /> : null}{module === '028' ? <AiTimeEntry /> : null}{module === '029' ? <UatValidation /> : null}</section>;
}
