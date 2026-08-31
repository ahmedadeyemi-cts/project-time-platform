import { useEffect, useMemo, useRef, useState } from 'react';
import './SowGsdWorkspace.css';

function token() {
  try { return JSON.parse(localStorage.getItem('projectPulseAuthSession') || 'null')?.sessionToken || ''; } catch { return ''; }
}
function authHeaders(json = false) {
  const value = token();
  return { ...(value ? { 'X-ProjectPulse-Session': value, Authorization: `Bearer ${value}` } : {}), ...(json ? { 'Content-Type': 'application/json' } : {}) };
}
async function request(path, options = {}) {
  const response = await fetch(path, { credentials: 'include', ...options, headers: { ...authHeaders(Boolean(options.body)), ...(options.headers || {}) } });
  const text = await response.text();
  let body = null;
  try { body = text ? JSON.parse(text) : null; } catch { body = { message: text }; }
  if (!response.ok) {
    const detail = Array.isArray(body?.missing) ? ` ${body.missing.join(', ')}` : '';
    throw new Error(`${body?.error || body?.message || body?.status || `${path} returned HTTP ${response.status}.`}${detail}`);
  }
  return body;
}

const PHASES = ['Plan', 'Design', 'Implement', 'Validate', 'Release'];
const EMPTY_PHASES = () => PHASES.map((name) => ({ key: name.toLowerCase(), name, description: '', suggestedHours: 0, hours: 0, activities: [] }));
const EMPTY_DOCUMENT = { deliverablesText: '', exclusionsText: '', clientInvolvementText: '', assumptionsText: '', aiSowDraft: null, evidenceScore: null };

function parseJson(value, fallback) {
  if (!value) return fallback;
  if (typeof value === 'object') return value;
  try { return JSON.parse(value); } catch { return fallback; }
}
function normalizePhaseName(value) {
  const text = String(value || '').toLowerCase();
  if (text.includes('discover') || text === 'plan') return 'Plan';
  if (text.includes('design')) return 'Design';
  if (text.includes('implement')) return 'Implement';
  if (text.includes('validat')) return 'Validate';
  if (text.includes('release') || text.includes('handoff') || text.includes('closeout')) return 'Release';
  return value || '';
}
function normalizeRecord(record) {
  if (!record) return null;
  const scope = parseJson(record.scopeJson, { phases: EMPTY_PHASES() });
  const incoming = Array.isArray(scope?.phases) ? scope.phases : [];
  const phases = PHASES.map((name) => {
    const match = incoming.find((phase) => normalizePhaseName(phase.name || phase.key) === name);
    return {
      key: name.toLowerCase(),
      name,
      description: match?.description || '',
      suggestedHours: Number(match?.suggestedHours || 0),
      hours: Number(match?.hours ?? match?.suggestedHours ?? 0),
      activities: Array.isArray(match?.activities) ? match.activities.map((item) => typeof item === 'string' ? item : item?.text || item?.title || item?.description || '').filter(Boolean) : []
    };
  });
  return { ...record, scope: { ...scope, phases }, document: { ...EMPTY_DOCUMENT, ...parseJson(record.documentJson, {}) } };
}
function toPayload(record) {
  return {
    solutionArchitectUserId: record.solutionArchitectUserId || null,
    solutionArchitectName: record.solutionArchitectName || '',
    customerId: record.customerId || null,
    customerName: record.customerName || '',
    customerIsManual: Boolean(record.customerIsManual),
    opportunityId: record.opportunityId || null,
    projectName: record.projectName || '',
    contractType: record.contractType || 'T&M',
    gsdTemplate: record.gsdTemplate || 'Standard',
    accountExecutiveUserId: record.accountExecutiveUserId || null,
    accountExecutiveName: record.accountExecutiveName || '',
    resaleUserId: record.resaleUserId || null,
    resaleName: record.resaleName || '',
    serviceOverview: record.serviceOverview || '',
    scope: record.scope || { phases: EMPTY_PHASES() },
    document: record.document || EMPTY_DOCUMENT
  };
}
function words(value) { return String(value || '').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase()); }
function stamp(value) { return value ? new Date(value).toLocaleString() : '—'; }
function phaseHours(record) { return (record?.scope?.phases || []).reduce((sum, phase) => sum + Number(phase.hours || 0), 0); }

function groupByDepartment(people) {
  return people.reduce((groups, person) => {
    const key = person.department || 'Other';
    groups[key] = groups[key] || [];
    groups[key].push(person);
    return groups;
  }, {});
}

function PersonSelect({ label, value, people, onChange, required = false, help = '' }) {
  return <label>{label}<select required={required} value={value || ''} onChange={(event) => onChange(event.target.value)}><option value="">Select {label.toLowerCase()}</option>{people.map((person) => <option key={person.userId} value={person.userId}>{person.fullName}{person.jobTitle ? ` · ${person.jobTitle}` : ''}</option>)}</select>{help ? <small>{help}</small> : null}</label>;
}

function SolutionArchitectSelect({ value, people, onChange, disabled = false }) {
  const grouped = groupByDepartment(people);
  return <label>Solution Architect<select disabled={disabled} value={value || ''} onChange={(event) => onChange(event.target.value)}>{Object.entries(grouped).map(([department, members]) => <optgroup key={department} label={department}>{members.map((person) => <option key={person.userId} value={person.userId}>{person.fullName}{person.jobTitle ? ` · ${person.jobTitle}` : ''}</option>)}</optgroup>)}</select><small>Managers see the Solution Architects inside their reporting scope, grouped by department.</small></label>;
}

function RecordsTable({ records, onEdit, onArchive, onRestore, archived = false, busyId }) {
  if (!records.length) return <div className="sgw-empty"><strong>{archived ? 'No archived SOW/GSD records' : 'No active SOW/GSD records'}</strong><span>{archived ? 'Archived records remain searchable and can be restored.' : 'Create a SOW/GSD to start a persistent, autosaved workspace.'}</span></div>;
  return <div className="sgw-table-wrap"><table className="sgw-table"><thead><tr><th>Identifier</th><th>Customer / Project</th><th>Solution Architect</th><th>Commercial</th><th>Status</th><th>Updated</th><th /></tr></thead><tbody>{records.map((record) => <tr key={record.id}><td><strong>{record.recordNumber}</strong></td><td><strong>{record.customerName || 'Customer not selected'}</strong><span>{record.projectName || 'Untitled SOW'}</span></td><td>{record.solutionArchitectName || '—'}</td><td><span>{record.contractType}</span><span>{record.gsdTemplate === 'ToyotaHyundai' ? 'Toyota / Hyundai GSD' : 'Standard GSD'}</span></td><td><span className={`sgw-status is-${String(record.status || '').toLowerCase()}`}>{record.status}</span></td><td>{stamp(record.updatedAtUtc)}</td><td><div className="sgw-row-actions">{archived ? <button type="button" disabled={busyId === record.id} onClick={() => onRestore(record)}>{busyId === record.id ? 'Restoring…' : 'Restore'}</button> : <><button type="button" onClick={() => onEdit(record)}>Open</button><button className="is-quiet" type="button" disabled={busyId === record.id} onClick={() => onArchive(record)}>{busyId === record.id ? 'Archiving…' : 'Archive'}</button></>}</div></td></tr>)}</tbody></table></div>;
}

function PhaseEditor({ phase, onChange }) {
  const activitiesText = (phase.activities || []).join('\n');
  return <article className="sgw-phase"><div className="sgw-phase-head"><div><span>{phase.name}</span><strong>{Number(phase.hours || 0).toFixed(1)} h</strong></div><div className="sgw-hour-pair"><label>AI suggested<input type="number" min="0" step="0.25" value={phase.suggestedHours || 0} readOnly /></label><label>Final hours<input type="number" min="0" step="0.25" value={phase.hours ?? 0} onChange={(event) => onChange({ ...phase, hours: Number(event.target.value || 0) })} /></label></div></div><label>Phase description<textarea rows="3" value={phase.description || ''} onChange={(event) => onChange({ ...phase, description: event.target.value })} placeholder={`What is required during ${phase.name.toLowerCase()}?`} /></label><label>Detailed activities<textarea rows="8" value={activitiesText} onChange={(event) => onChange({ ...phase, activities: event.target.value.split('\n').map((line) => line.trim()).filter(Boolean) })} placeholder="One activity per line" /></label></article>;
}

function Editor({ record, options, customers, onRecordChange, onSaved, onConfirmed, onArchived, onClose }) {
  const [step, setStep] = useState('engagement');
  const [saveState, setSaveState] = useState('saved');
  const [message, setMessage] = useState('');
  const [aiBusy, setAiBusy] = useState(false);
  const [actionBusy, setActionBusy] = useState(false);
  const dirtyRef = useRef(false);
  const saveVersionRef = useRef(0);

  const selectedSa = options.solutionArchitects.find((person) => person.userId === record.solutionArchitectUserId);
  const selectedAe = options.people.find((person) => person.userId === record.accountExecutiveUserId);
  const selectedResale = options.people.find((person) => person.userId === record.resaleUserId);

  function change(patch) {
    dirtyRef.current = true;
    setSaveState('unsaved');
    onRecordChange({ ...record, ...patch });
  }
  function updatePhase(updated) {
    change({ scope: { ...record.scope, phases: record.scope.phases.map((phase) => phase.name === updated.name ? updated : phase) } });
  }

  async function saveNow(snapshot = record) {
    if (!dirtyRef.current) return snapshot;
    const version = ++saveVersionRef.current;
    setSaveState('saving');
    try {
      const saved = normalizeRecord(await request(`/api/sow-gsd-workspace/records/${snapshot.id}`, { method: 'PUT', body: JSON.stringify(toPayload(snapshot)) }));
      if (version === saveVersionRef.current) {
        dirtyRef.current = false;
        setSaveState('saved');
        onSaved(saved);
      }
      return saved;
    } catch (error) {
      if (version === saveVersionRef.current) setSaveState('error');
      setMessage(error.message);
      throw error;
    }
  }

  useEffect(() => {
    if (!dirtyRef.current) return undefined;
    const timer = window.setTimeout(() => { void saveNow(record); }, 1000);
    return () => window.clearTimeout(timer);
  }, [record]);

  async function generateAi() {
    setAiBusy(true); setMessage('');
    try {
      const saved = await saveNow(record);
      const result = await request('/api/sow-gsd-planning/ai/generate', {
        method: 'POST',
        body: JSON.stringify({
          customerName: saved.customerName,
          customerId: saved.customerId || '',
          opportunityReference: saved.recordNumber,
          opportunityId: saved.opportunityId || '',
          projectCode: saved.recordNumber,
          projectName: saved.projectName,
          requestedOutcome: [`Contract type: ${saved.contractType}`, `GSD template: ${saved.gsdTemplate === 'ToyotaHyundai' ? 'Toyota / Hyundai' : 'Standard'}`, 'Services Overview:', saved.serviceOverview].join('\n'),
          detailLevel: 'comprehensive',
          allowSanitizedExternalFallback: true,
          mode: 'sow_draft'
        })
      });
      const ai = result?.result || result || {};
      const packages = ai.workPackages || ai.WorkPackages || [];
      const nextPhases = PHASES.map((name) => {
        const matches = packages.filter((item) => normalizePhaseName(item.phase || item.Phase) === name);
        const previous = saved.scope.phases.find((phase) => phase.name === name) || { name, key: name.toLowerCase(), activities: [] };
        if (!matches.length) return previous;
        const suggestedHours = matches.reduce((sum, item) => sum + Number(item.estimatedHours ?? item.EstimatedHours ?? 0), 0);
        const descriptions = [...new Set(matches.map((item) => item.description || item.Description || item.outcome || item.Outcome || '').filter(Boolean))];
        const activities = matches.map((item) => {
          const title = item.title || item.Title || '';
          const description = item.description || item.Description || '';
          return [title, description].filter(Boolean).join(title && description ? ' — ' : '');
        }).filter(Boolean);
        return { ...previous, description: descriptions.join(' '), activities, suggestedHours, hours: suggestedHours };
      });
      const totalSuggested = nextPhases.reduce((sum, phase) => sum + Number(phase.suggestedHours || 0), 0);
      const assumptions = ai.assumptions || ai.Assumptions || [];
      const next = {
        ...saved,
        scope: { ...saved.scope, phases: nextPhases },
        document: {
          ...saved.document,
          assumptionsText: Array.isArray(assumptions) ? assumptions.join('\n') : (assumptions || saved.document.assumptionsText || ''),
          aiSowDraft: ai.sowDraft || ai.SowDraft || saved.document.aiSowDraft,
          evidenceScore: ai.confidence ?? ai.Confidence ?? result?.confidence ?? null
        }
      };
      dirtyRef.current = true;
      onRecordChange(next);
      setSaveState('unsaved');
      if (!packages.length) setMessage('Celar AI returned a draft but did not return LOE work packages. Existing phase hours were preserved for manual review.');
      else setMessage(`Celar AI proposed ${totalSuggested.toFixed(1)} hours across Plan, Design, Implement, Validate, and Release. Review every phase before confirmation.`);
      setStep('scope');
    } catch (error) { setMessage(error.message); }
    finally { setAiBusy(false); }
  }

  async function confirm() {
    setActionBusy(true); setMessage('');
    try {
      const saved = await saveNow(record);
      const confirmed = normalizeRecord(await request(`/api/sow-gsd-workspace/records/${saved.id}/confirm`, { method: 'POST' }));
      onConfirmed(confirmed);
      setMessage('SOW and GSD confirmed. Final downloads are now enabled.');
      setStep('review');
    } catch (error) { setMessage(error.message); }
    finally { setActionBusy(false); }
  }

  async function archive() {
    setActionBusy(true); setMessage('');
    try {
      await saveNow(record);
      const archived = normalizeRecord(await request(`/api/sow-gsd-workspace/records/${record.id}/archive`, { method: 'POST' }));
      onArchived(archived);
    } catch (error) { setMessage(error.message); }
    finally { setActionBusy(false); }
  }

  async function download(kind) {
    setActionBusy(true); setMessage('');
    try {
      const response = await fetch(`/api/sow-gsd-workspace/records/${record.id}/download/${kind}`, { credentials: 'include', headers: authHeaders() });
      if (!response.ok) {
        const text = await response.text(); let body = {}; try { body = JSON.parse(text); } catch { body = { error: text }; }
        throw new Error(body.error || `Download returned HTTP ${response.status}.`);
      }
      const blob = await response.blob();
      const disposition = response.headers.get('content-disposition') || '';
      const match = disposition.match(/filename\*?=(?:UTF-8''|\")?([^\";]+)/i);
      const fileName = match ? decodeURIComponent(match[1].replaceAll('"', '')) : `${record.recordNumber}-${kind.toUpperCase()}.${kind === 'sow' ? 'docx' : 'xlsx'}`;
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a'); link.href = url; link.download = fileName; document.body.appendChild(link); link.click(); link.remove(); URL.revokeObjectURL(url);
    } catch (error) { setMessage(error.message); }
    finally { setActionBusy(false); }
  }

  const isConfirmed = record.status === 'Confirmed';
  const total = phaseHours(record);
  const customerSelection = record.customerIsManual ? '__manual__' : (record.customerId || '');

  return <div className="sgw-editor"><header className="sgw-editor-header"><div><button type="button" className="sgw-back" onClick={onClose}>← Active SOW/GSD</button><span className="sgw-eyebrow">Module 025 · Persistent SOW/GSD workspace</span><h2>{record.projectName || 'Untitled SOW / GSD'}</h2><div className="sgw-meta"><code>{record.recordNumber}</code><span className={`sgw-status is-${String(record.status).toLowerCase()}`}>{record.status}</span><span>{saveState === 'saving' ? 'Autosaving…' : saveState === 'unsaved' ? 'Unsaved changes' : saveState === 'error' ? 'Autosave issue' : `Saved · ${stamp(record.updatedAtUtc)}`}</span></div></div><div className="sgw-header-actions"><button type="button" className="is-quiet" disabled={actionBusy} onClick={archive}>Archive</button>{isConfirmed ? <><button type="button" disabled={actionBusy} onClick={() => download('sow')}>Download SOW</button><button type="button" className="sgw-primary" disabled={actionBusy} onClick={() => download('gsd')}>Download GSD</button></> : <button type="button" className="sgw-primary" disabled={actionBusy} onClick={confirm}>{actionBusy ? 'Working…' : 'Confirm for download'}</button>}</div></header>

    {isConfirmed ? <div className="sgw-notice is-success"><strong>Confirmed package</strong><span>Both final documents are available. Editing any field will return the package to Draft and require confirmation again.</span></div> : null}
    {message ? <div className="sgw-notice"><strong>Module 025</strong><span>{message}</span></div> : null}

    <nav className="sgw-steps">{[['engagement','1','Engagement'],['overview','2','Services Overview'],['scope','3','Scope & LOE'],['review','4','Review & Export']].map(([key, number, label]) => <button key={key} type="button" className={step === key ? 'is-active' : ''} onClick={() => setStep(key)}><b>{number}</b><span>{label}</span></button>)}</nav>

    {step === 'engagement' ? <section className="sgw-panel"><div className="sgw-section-heading"><div><span>Engagement setup</span><h3>Who, what, and how this work is sold</h3><p>These fields flow into both the SOW and the selected GSD template.</p></div></div><div className="sgw-grid">
      <SolutionArchitectSelect value={record.solutionArchitectUserId} people={options.solutionArchitects} onChange={(id) => { const person = options.solutionArchitects.find((item) => item.userId === id); change({ solutionArchitectUserId: id || null, solutionArchitectName: person?.fullName || '' }); }} />
      <label>Customer<select value={customerSelection} onChange={(event) => { const value = event.target.value; if (value === '__manual__') change({ customerId: null, customerName: '', customerIsManual: true }); else { const customer = customers.find((item) => String(item.clientId) === value); change({ customerId: customer?.clientId || null, customerName: customer?.clientName || '', customerIsManual: false }); } }}><option value="">Select a customer</option>{customers.map((customer) => <option key={customer.clientId || customer.clientName} value={customer.clientId}>{customer.clientName}{customer.clientCode ? ` · ${customer.clientCode}` : ''}</option>)}<option value="__manual__">Customer not listed — enter manually</option></select><small>Use the Customer Directory when possible; manual entry remains available.</small></label>
      {record.customerIsManual ? <label>Manual customer name<input value={record.customerName || ''} onChange={(event) => change({ customerName: event.target.value, customerId: null })} placeholder="Enter customer name" /></label> : null}
      <label>Project / SOW name<input value={record.projectName || ''} onChange={(event) => change({ projectName: event.target.value })} placeholder="Customer initiative or project name" /></label>
      <label>Contract model<select value={record.contractType || 'T&M'} onChange={(event) => change({ contractType: event.target.value })}><option value="T&M">Time & Materials (T&M)</option><option value="Fixed">Fixed</option></select><small>This selection updates the GSD contract type.</small></label>
      <label>Is this for Toyota or Hyundai?<select value={record.gsdTemplate || 'Standard'} onChange={(event) => change({ gsdTemplate: event.target.value })}><option value="Standard">No — use standard GSD</option><option value="ToyotaHyundai">Yes — use HAEA Toyota / Hyundai GSD</option></select><small>The Toyota / Hyundai option uses the HAEA Staff Aug KUS UVO Telematics GSD template.</small></label>
      <PersonSelect label="Account Executive" required value={record.accountExecutiveUserId} people={options.people} onChange={(id) => { const person = options.people.find((item) => item.userId === id); change({ accountExecutiveUserId: id || null, accountExecutiveName: person?.fullName || '' }); }} />
      <PersonSelect label="Resale person" required value={record.resaleUserId} people={options.people} onChange={(id) => { const person = options.people.find((item) => item.userId === id); change({ resaleUserId: id || null, resaleName: person?.fullName || '' }); }} />
    </div><div className="sgw-next"><button type="button" className="sgw-primary" onClick={() => setStep('overview')}>Continue to Services Overview →</button></div></section> : null}

    {step === 'overview' ? <section className="sgw-panel"><div className="sgw-section-heading"><div><span>Services Overview</span><h3>Describe the outcome and scope once</h3><p>Celar AI uses this source text to build detailed Plan, Design, Implement, Validate, and Release activities and estimate LOE.</p></div><button type="button" className="sgw-primary" onClick={generateAi} disabled={aiBusy || !record.serviceOverview.trim()}>{aiBusy ? 'Generating scope & LOE…' : 'Generate P/D/I/V/R + hours'}</button></div><label className="sgw-wide">Service Overview<textarea rows="16" value={record.serviceOverview || ''} onChange={(event) => change({ serviceOverview: event.target.value })} placeholder="Describe the customer's requested outcome, current environment, new configuration, quantities, migration or implementation boundaries, dependencies, third parties, dates, assumptions, acceptance expectations, and anything explicitly out of scope." /><small>This becomes Section 2.1 of the generated SOW. Keep customer-specific facts and measurable quantities here.</small></label><div className="sgw-ai-boundary"><strong>Human-reviewed estimate</strong><span>AI-suggested hours are preserved separately from the final editable hours. The Solution Architect remains responsible for validating scope, assumptions, and LOE before confirmation.</span></div></section> : null}

    {step === 'scope' ? <section className="sgw-panel"><div className="sgw-section-heading"><div><span>Scope & level of effort</span><h3>Review the five delivery phases</h3><p>Suggested hours came from the AI scope analysis; final hours are what the GSD receives.</p></div><div className="sgw-total"><span>Total final LOE</span><strong>{total.toFixed(1)} hours</strong></div></div><div className="sgw-phase-grid">{record.scope.phases.map((phase) => <PhaseEditor key={phase.name} phase={phase} onChange={updatePhase} />)}</div><div className="sgw-next"><button type="button" className="sgw-primary" onClick={() => setStep('review')}>Review final SOW & GSD →</button></div></section> : null}

    {step === 'review' ? <section className="sgw-panel"><div className="sgw-section-heading"><div><span>Review & export</span><h3>Complete contractual sections and confirm</h3><p>The SOW mirrors the approved Services Overview / Services Description / P-D-I-V-R / Deliverables / Exclusions / Client Involvement structure.</p></div><div className="sgw-total"><span>Final LOE</span><strong>{total.toFixed(1)} hours</strong></div></div><div className="sgw-review-grid"><div className="sgw-review-form"><label>Deliverables<textarea rows="5" value={record.document.deliverablesText || ''} onChange={(event) => change({ document: { ...record.document, deliverablesText: event.target.value } })} placeholder="One deliverable per line" /></label><label>Detailed exclusions<textarea rows="6" value={record.document.exclusionsText || ''} onChange={(event) => change({ document: { ...record.document, exclusionsText: event.target.value } })} placeholder="One exclusion per line" /></label><label>Client involvement<textarea rows="6" value={record.document.clientInvolvementText || ''} onChange={(event) => change({ document: { ...record.document, clientInvolvementText: event.target.value } })} placeholder="One client responsibility per line" /></label><label>Assumptions<textarea rows="5" value={record.document.assumptionsText || ''} onChange={(event) => change({ document: { ...record.document, assumptionsText: event.target.value } })} placeholder="One assumption per line" /></label></div><article className="sgw-preview"><span>Generated SOW preview</span><h4>{record.projectName || 'Untitled SOW'}</h4><dl><div><dt>Identifier</dt><dd>{record.recordNumber}</dd></div><div><dt>Customer</dt><dd>{record.customerName || '—'}</dd></div><div><dt>Contract</dt><dd>{record.contractType}</dd></div><div><dt>GSD</dt><dd>{record.gsdTemplate === 'ToyotaHyundai' ? 'Toyota / Hyundai' : 'Standard'}</dd></div><div><dt>SA</dt><dd>{selectedSa?.fullName || record.solutionArchitectName || '—'}</dd></div><div><dt>AE</dt><dd>{selectedAe?.fullName || record.accountExecutiveName || '—'}</dd></div><div><dt>Resale</dt><dd>{selectedResale?.fullName || record.resaleName || '—'}</dd></div></dl><h5>2.1 Services Overview</h5><p>{record.serviceOverview || 'Services Overview not entered.'}</p><h5>2.2 Services Description</h5>{record.scope.phases.map((phase) => <div className="sgw-preview-phase" key={phase.name}><strong>{phase.name} · {Number(phase.hours || 0).toFixed(1)} hours</strong><p>{phase.description || 'Description not entered.'}</p><ul>{phase.activities.map((activity) => <li key={activity}>{activity}</li>)}</ul></div>)}</article></div><div className="sgw-confirm-bar"><div><strong>{isConfirmed ? 'Package is confirmed' : 'Ready for final review?'}</strong><span>{isConfirmed ? 'Download the final SOW and GSD, or edit and reconfirm.' : 'Confirmation locks in the reviewed version for final document download. Autosaved drafts remain available even without download.'}</span></div>{isConfirmed ? <div><button type="button" disabled={actionBusy} onClick={() => download('sow')}>Download SOW</button><button type="button" className="sgw-primary" disabled={actionBusy} onClick={() => download('gsd')}>Download GSD</button></div> : <button type="button" className="sgw-primary" disabled={actionBusy} onClick={confirm}>{actionBusy ? 'Confirming…' : 'Confirm SOW & GSD'}</button>}</div></section> : null}
  </div>;
}

export default function SowGsdWorkspace() {
  const [tab, setTab] = useState('active');
  const [options, setOptions] = useState({ currentUserId: '', canViewTeam: false, solutionArchitects: [], people: [] });
  const [customers, setCustomers] = useState([]);
  const [records, setRecords] = useState([]);
  const [archived, setArchived] = useState([]);
  const [editor, setEditor] = useState(null);
  const [selectedSa, setSelectedSa] = useState('');
  const [search, setSearch] = useState('');
  const [busy, setBusy] = useState(true);
  const [busyId, setBusyId] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    let alive = true;
    (async () => {
      setBusy(true); setError('');
      try {
        const [workspaceOptions, customerOverview] = await Promise.all([
          request('/api/sow-gsd-workspace/options'),
          request('/api/customers/overview').catch(() => ({ customers: [] }))
        ]);
        if (!alive) return;
        setOptions(workspaceOptions || { currentUserId: '', canViewTeam: false, solutionArchitects: [], people: [] });
        setCustomers(customerOverview?.customers || []);
        const [activeRecords, archivedRecords] = await Promise.all([
          request('/api/sow-gsd-workspace/records?state=active'),
          request('/api/sow-gsd-workspace/records?state=archived')
        ]);
        if (!alive) return;
        setRecords((activeRecords || []).map(normalizeRecord));
        setArchived((archivedRecords || []).map(normalizeRecord));
      } catch (failure) { if (alive) setError(failure.message); }
      finally { if (alive) setBusy(false); }
    })();
    return () => { alive = false; };
  }, []);

  useEffect(() => {
    if (busy) return;
    let alive = true;
    const timer = window.setTimeout(async () => {
      try {
        const suffix = selectedSa ? `&solutionArchitectId=${encodeURIComponent(selectedSa)}` : '';
        const [activeRecords, archivedRecords] = await Promise.all([
          request(`/api/sow-gsd-workspace/records?state=active${suffix}`),
          request(`/api/sow-gsd-workspace/records?state=archived${suffix}`)
        ]);
        if (!alive) return;
        setRecords((activeRecords || []).map(normalizeRecord)); setArchived((archivedRecords || []).map(normalizeRecord));
      } catch (failure) { if (alive) setError(failure.message); }
    }, 100);
    return () => { alive = false; window.clearTimeout(timer); };
  }, [selectedSa]);

  const filteredRecords = useMemo(() => {
    const term = search.trim().toLowerCase();
    if (!term) return records;
    return records.filter((record) => [record.recordNumber, record.customerName, record.projectName, record.solutionArchitectName].some((value) => String(value || '').toLowerCase().includes(term)));
  }, [records, search]);
  const filteredArchived = useMemo(() => {
    const term = search.trim().toLowerCase();
    if (!term) return archived;
    return archived.filter((record) => [record.recordNumber, record.customerName, record.projectName, record.solutionArchitectName].some((value) => String(value || '').toLowerCase().includes(term)));
  }, [archived, search]);

  async function createNew() {
    setBusy(true); setError('');
    try {
      const current = options.solutionArchitects.find((person) => person.userId === options.currentUserId) || options.solutionArchitects[0];
      const created = normalizeRecord(await request('/api/sow-gsd-workspace/records', {
        method: 'POST',
        body: JSON.stringify({
          solutionArchitectUserId: current?.userId || options.currentUserId || null,
          solutionArchitectName: current?.fullName || '', customerId: null, customerName: '', customerIsManual: false,
          opportunityId: null, projectName: '', contractType: 'T&M', gsdTemplate: 'Standard',
          accountExecutiveUserId: null, accountExecutiveName: '', resaleUserId: null, resaleName: '', serviceOverview: '',
          scope: { phases: EMPTY_PHASES() }, document: EMPTY_DOCUMENT
        })
      }));
      setEditor(created); setTab('editor'); setRecords((currentRecords) => [created, ...currentRecords]);
    } catch (failure) { setError(failure.message); }
    finally { setBusy(false); }
  }

  async function openRecord(record) {
    setBusyId(record.id); setError('');
    try { const full = normalizeRecord(await request(`/api/sow-gsd-workspace/records/${record.id}`)); setEditor(full); setTab('editor'); }
    catch (failure) { setError(failure.message); }
    finally { setBusyId(''); }
  }
  async function archiveRecord(record) {
    setBusyId(record.id); setError('');
    try { const changed = normalizeRecord(await request(`/api/sow-gsd-workspace/records/${record.id}/archive`, { method: 'POST' })); setRecords((items) => items.filter((item) => item.id !== record.id)); setArchived((items) => [changed, ...items]); }
    catch (failure) { setError(failure.message); }
    finally { setBusyId(''); }
  }
  async function restoreRecord(record) {
    setBusyId(record.id); setError('');
    try { const changed = normalizeRecord(await request(`/api/sow-gsd-workspace/records/${record.id}/restore`, { method: 'POST' })); setArchived((items) => items.filter((item) => item.id !== record.id)); setRecords((items) => [changed, ...items]); }
    catch (failure) { setError(failure.message); }
    finally { setBusyId(''); }
  }
  function syncSaved(saved) {
    setEditor(saved);
    setRecords((items) => [saved, ...items.filter((item) => item.id !== saved.id)]);
  }

  if (tab === 'editor' && editor) return <Editor record={editor} options={options} customers={customers} onRecordChange={setEditor} onSaved={syncSaved} onConfirmed={syncSaved} onArchived={(changed) => { setArchived((items) => [changed, ...items.filter((item) => item.id !== changed.id)]); setRecords((items) => items.filter((item) => item.id !== changed.id)); setEditor(null); setTab('active'); }} onClose={() => { setEditor(null); setTab('active'); }} />;

  return <div className="sgw-shell"><header className="sgw-hero"><div><span className="sgw-eyebrow">Module 025 · SOW / GSD Generator</span><h1>Scope once. Estimate once. Generate both documents.</h1><p>Create persistent SOW/GSD packages with AI-assisted P/D/I/V/R scope, editable LOE, the correct GSD template, autosave, review, confirmation, and searchable immutable identifiers.</p></div><button className="sgw-primary" type="button" onClick={createNew} disabled={busy || !options.currentUserId}>+ New SOW / GSD</button></header>

    <div className="sgw-kpis"><article><span>Active</span><strong>{records.length}</strong><small>Draft + confirmed packages</small></article><article><span>Archived</span><strong>{archived.length}</strong><small>Retained for record keeping</small></article><article><span>Solution Architects</span><strong>{options.solutionArchitects.length}</strong><small>{options.canViewTeam ? 'Your reporting scope' : 'Your workspace'}</small></article><article><span>Templates</span><strong>2</strong><small>Standard · Toyota / Hyundai</small></article></div>

    <section className="sgw-card"><div className="sgw-toolbar"><nav><button type="button" className={tab === 'active' ? 'is-active' : ''} onClick={() => setTab('active')}>Active SOW/GSD <b>{records.length}</b></button><button type="button" className={tab === 'archived' ? 'is-active' : ''} onClick={() => setTab('archived')}>Archived <b>{archived.length}</b></button></nav><div className="sgw-filters">{options.canViewTeam ? <select value={selectedSa} onChange={(event) => setSelectedSa(event.target.value)}><option value="">All Solution Architects in my scope</option>{Object.entries(groupByDepartment(options.solutionArchitects)).map(([department, members]) => <optgroup key={department} label={department}>{members.map((person) => <option key={person.userId} value={person.userId}>{person.fullName}</option>)}</optgroup>)}</select> : null}<input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search ID, customer, project, or SA" /></div></div>{error ? <div className="sgw-notice is-error"><strong>Module 025</strong><span>{error}</span></div> : null}{busy ? <div className="sgw-empty"><strong>Loading SOW/GSD workspace…</strong><span>Retrieving your saved records and reporting scope.</span></div> : tab === 'archived' ? <RecordsTable records={filteredArchived} archived busyId={busyId} onRestore={restoreRecord} /> : <RecordsTable records={filteredRecords} busyId={busyId} onEdit={openRecord} onArchive={archiveRecord} />}</section>

    <section className="sgw-guidance"><article><b>1</b><div><strong>Engagement</strong><span>Customer, SA, AE, Resale, T&M/Fixed, and GSD template.</span></div></article><article><b>2</b><div><strong>Services Overview</strong><span>The architect describes the customer outcome and boundaries once.</span></div></article><article><b>3</b><div><strong>AI scope + LOE</strong><span>Celar AI proposes detailed P/D/I/V/R activities and hours for review.</span></div></article><article><b>4</b><div><strong>Confirm & export</strong><span>Final reviewed content populates the SOW and the selected GSD workbook.</span></div></article></section>
  </div>;
}
