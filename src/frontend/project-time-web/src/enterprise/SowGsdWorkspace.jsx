import { useEffect, useMemo, useRef, useState } from 'react';
import './sow-gsd-workspace.css';

const PHASES = [
  { key: 'plan', label: 'Plan' },
  { key: 'design', label: 'Design' },
  { key: 'implement', label: 'Implement' },
  { key: 'validate', label: 'Validate' },
  { key: 'release', label: 'Release' }
];

function token() {
  try { return JSON.parse(localStorage.getItem('projectPulseAuthSession') || 'null')?.sessionToken || ''; } catch { return ''; }
}

async function request(path, options = {}) {
  const sessionToken = token();
  const response = await fetch(path, {
    credentials: 'include',
    ...options,
    headers: {
      ...(sessionToken ? { 'X-ProjectPulse-Session': sessionToken, Authorization: `Bearer ${sessionToken}` } : {}),
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...(options.headers || {})
    }
  });
  const contentType = response.headers.get('content-type') || '';
  if (!contentType.includes('application/json')) {
    if (!response.ok) throw new Error(`${path} returned HTTP ${response.status}.`);
    return response;
  }
  const body = await response.json();
  if (!response.ok) {
    const detail = Array.isArray(body?.missing) && body.missing.length ? ` ${body.missing.join(' · ')}` : '';
    throw new Error(`${body?.message || body?.status || `HTTP ${response.status}`}${detail}`);
  }
  return body;
}

const text = (value) => String(value ?? '').trim();
const array = (value) => Array.isArray(value) ? value.map((item) => text(item)).filter(Boolean) : text(value) ? [text(value)] : [];
const number = (value) => Number.isFinite(Number(value)) ? Number(value) : null;
const lines = (value) => array(value).join('\n');
const fromLines = (value) => String(value ?? '').split(/\r?\n/).map((item) => item.replace(/^\s*(?:[-•]|\d+[.)])\s*/, '').trim()).filter(Boolean);
const words = (value) => text(value).replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());

function emptyPhase(label) {
  return { phase: label, objective: '', activities: [], loeRationale: '' };
}

function emptyWorkspace(ownerId = '', canEdit = true) {
  return {
    workspaceId: '',
    reference: 'Assigned after first save',
    ownerSolutionArchitectUserId: ownerId,
    ownerSolutionArchitectName: '',
    customerId: '',
    customerName: '',
    customerSource: 'DIRECTORY',
    opportunityReference: '',
    projectCode: '',
    projectName: '',
    serviceOverview: '',
    contractType: 'T_AND_M',
    accountExecutiveUserId: '',
    accountExecutiveName: '',
    resaleUserId: '',
    resaleName: '',
    oemCustomerType: 'STANDARD',
    gsdTemplateCode: 'STANDARD',
    gsdTemplateLabel: 'Standard GSD',
    status: 'DRAFT',
    aiDraft: {},
    phaseDetails: Object.fromEntries(PHASES.map((phase) => [phase.key, emptyPhase(phase.label)])),
    suggestedHours: Object.fromEntries(PHASES.map((phase) => [phase.key, null])),
    finalHours: Object.fromEntries(PHASES.map((phase) => [phase.key, null])),
    generation: { provider: '', citations: [], warnings: [], missingEvidence: [], confidence: null },
    revisionNumber: null,
    lastAutosavedAt: null,
    reviewConfirmedAt: null,
    archivedAt: null,
    canEdit,
    canDownload: false
  };
}

function hydrateWorkspace(body) {
  return {
    ...emptyWorkspace(body?.ownerSolutionArchitectUserId || ''),
    ...body,
    status: body?.statusCode || body?.status || 'DRAFT',
    customerId: body?.customerId || '',
    accountExecutiveUserId: body?.accountExecutiveUserId || '',
    resaleUserId: body?.resaleUserId || '',
    phaseDetails: PHASES.reduce((result, phase) => {
      result[phase.key] = { ...emptyPhase(phase.label), ...(body?.phaseDetails?.[phase.key] || {}) };
      result[phase.key].activities = Array.isArray(result[phase.key].activities) ? result[phase.key].activities : [];
      return result;
    }, {}),
    suggestedHours: { ...Object.fromEntries(PHASES.map((phase) => [phase.key, null])), ...(body?.suggestedHours || {}) },
    finalHours: { ...Object.fromEntries(PHASES.map((phase) => [phase.key, null])), ...(body?.finalHours || {}) },
    generation: { provider: '', citations: [], warnings: [], missingEvidence: [], confidence: null, ...(body?.generation || {}) }
  };
}

function mapPhase(value) {
  const normalized = text(value).toLowerCase();
  if (normalized.includes('plan')) return 'plan';
  if (normalized.includes('design') || normalized.includes('architect')) return 'design';
  if (normalized.includes('implement') || normalized.includes('deploy') || normalized.includes('migrat') || normalized.includes('configur')) return 'implement';
  if (normalized.includes('validat') || normalized.includes('test') || normalized.includes('acceptance')) return 'validate';
  if (normalized.includes('release') || normalized.includes('handoff') || normalized.includes('closeout') || normalized.includes('transition')) return 'release';
  return '';
}

function normalizeAiDraft(response) {
  const result = response?.result || response || {};
  const draft = result?.sowDraft || response?.sowDraft || {};
  const packages = Array.isArray(draft?.workPackages) ? draft.workPackages : [];
  const phaseDetails = Object.fromEntries(PHASES.map((phase) => [phase.key, emptyPhase(phase.label)]));
  const suggestedHours = Object.fromEntries(PHASES.map((phase) => [phase.key, 0]));
  const unclassified = [];

  packages.forEach((workPackage) => {
    const phaseKey = mapPhase(workPackage?.phase || workPackage?.name || workPackage?.description);
    if (!phaseKey) {
      unclassified.push(text(workPackage?.name || workPackage?.description || workPackage?.wbs || 'Unclassified work package'));
      return;
    }
    const activity = {
      wbs: text(workPackage?.wbs),
      name: text(workPackage?.name || `${words(phaseKey)} work package`),
      description: text(workPackage?.description),
      detailedSteps: array(workPackage?.detailedSteps),
      inputs: array(workPackage?.inputs),
      outputs: array(workPackage?.outputs),
      acceptanceCriteria: array(workPackage?.acceptanceCriteria),
      validationSteps: array(workPackage?.validationSteps),
      customerResponsibilities: array(workPackage?.customerResponsibilities),
      usSignalResponsibilities: array(workPackage?.usSignalResponsibilities),
      prerequisites: array(workPackage?.prerequisites),
      risks: array(workPackage?.risks),
      openQuestions: array(workPackage?.openQuestions),
      requiredRoles: array(workPackage?.requiredRoles),
      predecessors: array(workPackage?.predecessors),
      citationIds: array(workPackage?.citationIds),
      estimatedHours: number(workPackage?.estimatedHours) ?? 0,
      isAssumption: Boolean(workPackage?.isAssumption)
    };
    phaseDetails[phaseKey].activities.push(activity);
    suggestedHours[phaseKey] += activity.estimatedHours;
  });

  PHASES.forEach((phase) => {
    const phaseNode = phaseDetails[phase.key];
    const names = phaseNode.activities.map((activity) => activity.name).filter(Boolean);
    phaseNode.objective = phaseNode.activities.length
      ? `During ${phase.label}, US Signal will execute the reviewed delivery activities identified below${names.length ? `: ${names.join('; ')}` : ''}. The objective is to complete the scoped outputs and satisfy the documented acceptance and validation requirements while preserving unresolved technical or customer inputs as explicit open questions.`
      : '';
    phaseNode.loeRationale = phaseNode.activities.length
      ? `Celar AI suggested ${suggestedHours[phase.key].toFixed(2)} hour(s) for ${phase.label} based on ${phaseNode.activities.length} detailed work package(s)${names.length ? `: ${names.join('; ')}` : ''}. The Solution Architect must review and may change the final hours.`
      : '';
  });

  const warnings = array(result?.warnings || response?.warnings);
  if (unclassified.length) warnings.push(`${unclassified.length} AI work package(s) could not be mapped safely to P/D/I/V/R and were not silently reassigned: ${unclassified.join('; ')}`);

  return {
    draft,
    phaseDetails,
    suggestedHours,
    finalHours: { ...suggestedHours },
    generation: {
      provider: text(result?.selectedTarget || response?.selectedTarget || result?.primaryExecutionPath),
      citations: result?.citations || response?.citations || [],
      warnings,
      missingEvidence: array(result?.missingEvidence || response?.missingEvidence),
      confidence: number(result?.confidence ?? response?.confidence)
    }
  };
}

function detailedGenerationPrompt(workspace) {
  return [
    `Customer: ${workspace.customerName}`,
    workspace.opportunityReference ? `Opportunity: ${workspace.opportunityReference}` : '',
    workspace.projectCode ? `Project / opportunity code: ${workspace.projectCode}` : '',
    `Project: ${workspace.projectName}`,
    `Commercial model: ${workspace.contractType === 'FIXED' ? 'Fixed Price' : 'Time & Materials'}`,
    `GSD template: ${workspace.oemCustomerType === 'STANDARD' ? 'Standard GSD' : 'HAEA Staff Aug GSD KUS UVO Telematics 1'}`,
    '',
    'SERVICE OVERVIEW PROVIDED BY THE SOLUTION ARCHITECT:',
    workspace.serviceOverview,
    '',
    'MODULE 025 AUTHORING REQUIREMENTS:',
    'Create a delivery-ready, source-grounded SOW draft organized into Plan, Design, Implement, Validate, and Release.',
    'Do not return generic phase statements such as "configure solution", "implement system", or "validate environment".',
    'For every distinct work package, state exactly what will be done and provide ordered technical execution steps.',
    'Each work package must include: detailed description; inputs; prerequisites and dependencies; US Signal responsibilities; customer responsibilities; outputs/deliverables; acceptance criteria; validation steps; risks; open questions; required roles; predecessor relationships; citations when evidence exists; and estimated engineering hours.',
    'The description must be detailed enough for a delivery engineer to understand the intended work without inventing missing technical values.',
    'Missing products, models, versions, quantities, licensing, access, interfaces, customer decisions, or technical facts must remain explicit open questions. Never fabricate them.',
    'Use the Service Overview as the commercial/technical authoring basis, preserve verified in-scope and out-of-scope boundaries, and identify assumptions clearly.',
    'Estimate Plan, Design, Implement, Validate, and Release hours from the concrete tasks you generate. The hours must be explainable by those activities, not arbitrary totals.',
    'Include project deliverables, detailed exclusions/out-of-scope, customer involvement/responsibilities, US Signal responsibilities, assumptions, dependencies, risks, and acceptance criteria.',
    'This is a review-only SOW/GSD draft. Do not claim contractual approval, completed work, customer acceptance, or committed dates.'
  ].filter((value) => value !== '').join('\n');
}

function PhaseEditor({ phase, value, suggestedHours, finalHours, onChange, onFinalHoursChange, disabled }) {
  function patchActivity(index, patch) {
    const activities = value.activities.map((activity, activityIndex) => activityIndex === index ? { ...activity, ...patch } : activity);
    onChange({ ...value, activities });
  }
  function addActivity() {
    onChange({
      ...value,
      activities: [...value.activities, {
        wbs: '', name: '', description: '', detailedSteps: [], inputs: [], outputs: [], acceptanceCriteria: [], validationSteps: [],
        customerResponsibilities: [], usSignalResponsibilities: [], prerequisites: [], risks: [], openQuestions: [], requiredRoles: [], predecessors: [], citationIds: [], estimatedHours: 0, isAssumption: false
      }]
    });
  }
  function removeActivity(index) {
    onChange({ ...value, activities: value.activities.filter((_, activityIndex) => activityIndex !== index) });
  }

  return <section className="sow-gsd-phase-card">
    <header>
      <div><span>{phase.label}</span><h3>{phase.label} delivery scope</h3></div>
      <div className="sow-gsd-hours-pair">
        <label>AI suggested<input readOnly value={suggestedHours ?? ''} /></label>
        <label>Final hours<input disabled={disabled} type="number" min="0" step="0.25" value={finalHours ?? ''} onChange={(event) => onFinalHoursChange(event.target.value === '' ? null : Number(event.target.value))} /></label>
      </div>
    </header>
    <label className="sow-gsd-wide-label">Phase objective<textarea disabled={disabled} rows="3" value={value.objective || ''} onChange={(event) => onChange({ ...value, objective: event.target.value })} /></label>
    <label className="sow-gsd-wide-label">LOE rationale<textarea disabled={disabled} rows="3" value={value.loeRationale || ''} onChange={(event) => onChange({ ...value, loeRationale: event.target.value })} placeholder="Explain why this phase requires the reviewed hours." /></label>
    <div className="sow-gsd-activity-list">
      {value.activities.map((activity, index) => <details key={`${phase.key}-${index}`} open={index === 0}>
        <summary><strong>{activity.name || `Activity ${index + 1}`}</strong><span>{number(activity.estimatedHours)?.toFixed(2) || '0.00'} AI hr</span></summary>
        <div className="sow-gsd-activity-editor">
          <label>Activity name<input disabled={disabled} value={activity.name || ''} onChange={(event) => patchActivity(index, { name: event.target.value })} /></label>
          <label>WBS<input disabled={disabled} value={activity.wbs || ''} onChange={(event) => patchActivity(index, { wbs: event.target.value })} /></label>
          <label className="is-wide">Detailed description<textarea disabled={disabled} rows="4" value={activity.description || ''} onChange={(event) => patchActivity(index, { description: event.target.value })} /></label>
          <label className="is-wide">Ordered execution steps<textarea disabled={disabled} rows="6" value={lines(activity.detailedSteps)} onChange={(event) => patchActivity(index, { detailedSteps: fromLines(event.target.value) })} placeholder="One concrete technical step per line" /></label>
          <label>Inputs<textarea disabled={disabled} rows="4" value={lines(activity.inputs)} onChange={(event) => patchActivity(index, { inputs: fromLines(event.target.value) })} /></label>
          <label>Prerequisites / dependencies<textarea disabled={disabled} rows="4" value={lines(activity.prerequisites)} onChange={(event) => patchActivity(index, { prerequisites: fromLines(event.target.value) })} /></label>
          <label>US Signal responsibilities<textarea disabled={disabled} rows="4" value={lines(activity.usSignalResponsibilities)} onChange={(event) => patchActivity(index, { usSignalResponsibilities: fromLines(event.target.value) })} /></label>
          <label>Customer responsibilities<textarea disabled={disabled} rows="4" value={lines(activity.customerResponsibilities)} onChange={(event) => patchActivity(index, { customerResponsibilities: fromLines(event.target.value) })} /></label>
          <label>Outputs / deliverables<textarea disabled={disabled} rows="4" value={lines(activity.outputs)} onChange={(event) => patchActivity(index, { outputs: fromLines(event.target.value) })} /></label>
          <label>Acceptance criteria<textarea disabled={disabled} rows="4" value={lines(activity.acceptanceCriteria)} onChange={(event) => patchActivity(index, { acceptanceCriteria: fromLines(event.target.value) })} /></label>
          <label>Validation steps<textarea disabled={disabled} rows="4" value={lines(activity.validationSteps)} onChange={(event) => patchActivity(index, { validationSteps: fromLines(event.target.value) })} /></label>
          <label>Risks<textarea disabled={disabled} rows="4" value={lines(activity.risks)} onChange={(event) => patchActivity(index, { risks: fromLines(event.target.value) })} /></label>
          <label>Open questions<textarea disabled={disabled} rows="4" value={lines(activity.openQuestions)} onChange={(event) => patchActivity(index, { openQuestions: fromLines(event.target.value) })} /></label>
          <label>AI activity hours<input disabled={disabled} type="number" min="0" step="0.25" value={activity.estimatedHours ?? 0} onChange={(event) => patchActivity(index, { estimatedHours: Number(event.target.value || 0) })} /></label>
          <label>Required roles<textarea disabled={disabled} rows="2" value={lines(activity.requiredRoles)} onChange={(event) => patchActivity(index, { requiredRoles: fromLines(event.target.value) })} /></label>
          <label>Predecessor relationships<textarea disabled={disabled} rows="2" value={lines(activity.predecessors)} onChange={(event) => patchActivity(index, { predecessors: fromLines(event.target.value) })} placeholder="WBS/task dependencies; use None for the first independent activity" /></label>
          <label>Source citation IDs<textarea disabled={disabled} rows="2" value={lines(activity.citationIds)} onChange={(event) => patchActivity(index, { citationIds: fromLines(event.target.value) })} placeholder="Retain Celar AI source citation IDs when evidence exists" /></label>
          <label className="sow-gsd-inline-toggle"><input disabled={disabled} type="checkbox" checked={Boolean(activity.isAssumption)} onChange={(event) => patchActivity(index, { isAssumption: event.target.checked })} />Activity contains an assumption requiring validation</label>
          {!disabled ? <button type="button" className="sow-gsd-danger-link" onClick={() => removeActivity(index)}>Remove activity</button> : null}
        </div>
      </details>)}
      {!value.activities.length ? <div className="sow-gsd-empty-phase">No detailed {phase.label} activities exist yet. Generate the SOW/GSD or add the missing delivery activity manually before confirmation.</div> : null}
      {!disabled ? <button type="button" className="sow-gsd-secondary" onClick={addActivity}>+ Add {phase.label} activity</button> : null}
    </div>
  </section>;
}

function confirmationGaps(workspace) {
  const gaps = [];
  if (!workspace.customerName) gaps.push('Customer');
  if (!workspace.accountExecutiveUserId) gaps.push('Account Executive');
  if (!workspace.resaleUserId) gaps.push('Resale person');
  if (!workspace.serviceOverview.trim()) gaps.push('Service Overview');
  PHASES.forEach((phase) => {
    const phaseNode = workspace.phaseDetails?.[phase.key] || emptyPhase(phase.label);
    const final = number(workspace.finalHours?.[phase.key]);
    if (final === null || final <= 0) gaps.push(`${phase.label} final hours greater than zero`);
    if (text(phaseNode.objective).length < 40) gaps.push(`${phase.label} phase objective`);
    if (text(phaseNode.loeRationale).length < 40) gaps.push(`${phase.label} LOE rationale`);
    const activities = phaseNode.activities || [];
    if (!activities.length) {
      gaps.push(`${phase.label} detailed activities`);
      return;
    }
    activities.forEach((activity, index) => {
      const prefix = `${phase.label} activity ${index + 1}`;
      if (text(activity.name).length < 5) gaps.push(`${prefix} name`);
      if (text(activity.description).length < 80) gaps.push(`${prefix} detailed description`);
      const detailedSteps = array(activity.detailedSteps);
      if (detailedSteps.length < 2 || detailedSteps.join(' ').length < 120) gaps.push(`${prefix} ordered execution steps`);
      if (!array(activity.inputs).length) gaps.push(`${prefix} inputs`);
      if (!array(activity.prerequisites).length) gaps.push(`${prefix} prerequisites/dependencies`);
      if (!array(activity.usSignalResponsibilities).length) gaps.push(`${prefix} US Signal responsibilities`);
      if (!array(activity.customerResponsibilities).length) gaps.push(`${prefix} customer responsibilities`);
      if (!array(activity.outputs).length) gaps.push(`${prefix} outputs/deliverables`);
      if (!array(activity.acceptanceCriteria).length) gaps.push(`${prefix} acceptance criteria`);
      if (!array(activity.validationSteps).length) gaps.push(`${prefix} validation steps`);
      if (!array(activity.risks).length) gaps.push(`${prefix} risks / explicit none identified`);
      if (!array(activity.openQuestions).length) gaps.push(`${prefix} open questions / explicit none identified`);
      if (!array(activity.requiredRoles).length) gaps.push(`${prefix} required roles`);
      if ((number(activity.estimatedHours) ?? 0) <= 0) gaps.push(`${prefix} estimated hours`);
    });
  });
  return gaps;
}

export default function SowGsdWorkspace() {
  const [options, setOptions] = useState({ loading: true, customers: [], accountExecutives: [], resalePeople: [], solutionArchitects: [], access: {} });
  const [tab, setTab] = useState('active');
  const [list, setList] = useState([]);
  const [listBusy, setListBusy] = useState(false);
  const [search, setSearch] = useState('');
  const [ownerFilter, setOwnerFilter] = useState('');
  const [workspace, setWorkspace] = useState(emptyWorkspace());
  const [manualCustomer, setManualCustomer] = useState(false);
  const [busy, setBusy] = useState('');
  const [message, setMessage] = useState(null);
  const [saveState, setSaveState] = useState('Not saved yet');
  const lastSavedSignature = useRef('');
  const autosaveTimer = useRef(null);
  const saveInFlight = useRef(false);

  useEffect(() => {
    let active = true;
    request('/api/sow-gsd/v1/options').then((body) => {
      if (!active) return;
      setOptions({ loading: false, ...body });
      setOwnerFilter(body?.access?.canSelectSolutionArchitect ? '' : body?.access?.effectiveUserId || '');
      setWorkspace((current) => current.workspaceId ? current : emptyWorkspace(body?.access?.effectiveUserId || '', Boolean(body?.access?.canCreate)));
    }).catch((error) => {
      if (active) { setOptions((current) => ({ ...current, loading: false })); setMessage({ error: true, text: error.message }); }
    });
    return () => { active = false; };
  }, []);

  const loadList = async (nextTab = tab, nextOwner = ownerFilter, nextSearch = search) => {
    setListBusy(true);
    try {
      const query = new URLSearchParams();
      query.set('status', nextTab === 'archived' ? 'archived' : 'active');
      if (nextOwner) query.set('ownerUserId', nextOwner);
      if (nextSearch.trim()) query.set('search', nextSearch.trim());
      const body = await request(`/api/sow-gsd/v1/workspaces?${query.toString()}`);
      setList(body?.workspaces || []);
    } catch (error) {
      setMessage({ error: true, text: error.message });
    } finally { setListBusy(false); }
  };

  useEffect(() => {
    if (options.loading || tab === 'editor') return;
    void loadList();
  }, [tab, ownerFilter, options.loading]);

  function applyWorkspace(next) {
    const hydrated = hydrateWorkspace(next);
    setWorkspace(hydrated);
    setManualCustomer(hydrated.customerSource === 'MANUAL' || !hydrated.customerId);
    lastSavedSignature.current = JSON.stringify(payloadFor(hydrated, false));
    setSaveState(hydrated.lastAutosavedAt ? `Saved ${new Date(hydrated.lastAutosavedAt).toLocaleTimeString()}` : 'Saved');
  }

  function payloadFor(source, includeRevision = true, extra = {}) {
    return {
      ownerSolutionArchitectUserId: source.ownerSolutionArchitectUserId || null,
      customerId: source.customerSource === 'MANUAL' ? null : source.customerId || null,
      customerName: source.customerName,
      opportunityReference: source.opportunityReference,
      projectCode: source.projectCode,
      projectName: source.projectName,
      serviceOverview: source.serviceOverview,
      contractType: source.contractType,
      accountExecutiveUserId: source.accountExecutiveUserId || null,
      resaleUserId: source.resaleUserId || null,
      oemCustomerType: source.oemCustomerType,
      status: source.status === 'READY_FOR_REVIEW' ? 'READY_FOR_REVIEW' : 'DRAFT',
      aiDraft: source.aiDraft || {},
      phaseDetails: source.phaseDetails || {},
      suggestedPlanHours: source.suggestedHours?.plan,
      suggestedDesignHours: source.suggestedHours?.design,
      suggestedImplementHours: source.suggestedHours?.implement,
      suggestedValidateHours: source.suggestedHours?.validate,
      suggestedReleaseHours: source.suggestedHours?.release,
      finalPlanHours: source.finalHours?.plan,
      finalDesignHours: source.finalHours?.design,
      finalImplementHours: source.finalHours?.implement,
      finalValidateHours: source.finalHours?.validate,
      finalReleaseHours: source.finalHours?.release,
      generationProvider: source.generation?.provider,
      generationCitations: source.generation?.citations || [],
      generationWarnings: source.generation?.warnings || [],
      generationMissingEvidence: source.generation?.missingEvidence || [],
      generationConfidence: source.generation?.confidence,
      ...(includeRevision ? { expectedRevision: source.revisionNumber } : {}),
      ...extra
    };
  }

  async function persist(source = workspace, { silent = false, generationCompleted = false } = {}) {
    if (!source.workspaceId || !source.canEdit || saveInFlight.current) return source;
    saveInFlight.current = true;
    setSaveState('Saving…');
    try {
      const body = await request(`/api/sow-gsd/v1/workspaces/${source.workspaceId}`, {
        method: 'PUT',
        body: JSON.stringify(payloadFor(source, true, { generationCompleted }))
      });
      const hydrated = hydrateWorkspace(body);
      setWorkspace(hydrated);
      lastSavedSignature.current = JSON.stringify(payloadFor(hydrated, false));
      setSaveState(`Saved ${new Date().toLocaleTimeString()}`);
      if (!silent) setMessage({ error: false, text: 'SOW/GSD draft saved.' });
      return hydrated;
    } catch (error) {
      setSaveState('Save failed');
      setMessage({ error: true, text: error.message });
      return source;
    } finally { saveInFlight.current = false; }
  }

  useEffect(() => {
    if (!workspace.workspaceId || !workspace.canEdit || workspace.status === 'ARCHIVED' || busy) return;
    const signature = JSON.stringify(payloadFor(workspace, false));
    if (signature === lastSavedSignature.current) return;
    setSaveState('Unsaved changes');
    clearTimeout(autosaveTimer.current);
    autosaveTimer.current = setTimeout(() => { void persist(workspace, { silent: true }); }, 1400);
    return () => clearTimeout(autosaveTimer.current);
  }, [workspace, busy]);

  useEffect(() => {
    if (workspace.workspaceId) return;
    try { localStorage.setItem('module025UnsavedDraft', JSON.stringify(workspace)); } catch { /* local recovery is best effort */ }
  }, [workspace]);

  function newWorkspace() {
    let recovered = null;
    try { recovered = JSON.parse(localStorage.getItem('module025UnsavedDraft') || 'null'); } catch { recovered = null; }
    const base = recovered?.projectName || recovered?.serviceOverview
      ? { ...emptyWorkspace(options?.access?.effectiveUserId || '', Boolean(options?.access?.canCreate)), ...recovered, workspaceId: '', reference: 'Assigned after first save', revisionNumber: null, canEdit: Boolean(options?.access?.canCreate) }
      : emptyWorkspace(options?.access?.effectiveUserId || '', Boolean(options?.access?.canCreate));
    setWorkspace(base);
    setManualCustomer(base.customerSource === 'MANUAL');
    setTab('editor');
    setMessage(null);
    setSaveState('Not saved yet');
  }

  async function createDraft() {
    setBusy('create'); setMessage(null);
    try {
      const body = await request('/api/sow-gsd/v1/workspaces', { method: 'POST', body: JSON.stringify(payloadFor(workspace, false)) });
      applyWorkspace(body);
      try { localStorage.removeItem('module025UnsavedDraft'); } catch { /* no-op */ }
      setMessage({ error: false, text: `Created ${body.reference}. Autosave is now active.` });
    } catch (error) { setMessage({ error: true, text: error.message }); }
    finally { setBusy(''); }
  }

  async function openWorkspace(id) {
    setBusy('open'); setMessage(null);
    try {
      const body = await request(`/api/sow-gsd/v1/workspaces/${id}`);
      applyWorkspace(body);
      setTab('editor');
    } catch (error) { setMessage({ error: true, text: error.message }); }
    finally { setBusy(''); }
  }

  async function generateDetailedScope() {
    if (!workspace.workspaceId) return;
    setBusy('generate'); setMessage(null);
    const saved = await persist(workspace, { silent: true });
    try {
      const response = await request('/api/sow-gsd-planning/ai/generate', {
        method: 'POST',
        body: JSON.stringify({
          customerName: saved.customerName,
          customerId: saved.customerId || null,
          opportunityReference: saved.opportunityReference,
          projectCode: saved.projectCode,
          projectName: saved.projectName,
          requestedOutcome: detailedGenerationPrompt(saved),
          detailLevel: 'comprehensive',
          allowSanitizedExternalFallback: true,
          mode: 'sow_draft'
        })
      });
      const normalized = normalizeAiDraft(response);
      const next = {
        ...saved,
        aiDraft: normalized.draft,
        phaseDetails: normalized.phaseDetails,
        suggestedHours: normalized.suggestedHours,
        finalHours: normalized.finalHours,
        generation: normalized.generation,
        status: 'DRAFT'
      };
      setWorkspace(next);
      const persisted = await persist(next, { generationCompleted: true, silent: true });
      const gaps = confirmationGaps(persisted);
      setMessage({
        error: gaps.length > 0,
        text: gaps.length
          ? `Detailed AI draft generated and saved, but review is still required: ${gaps.join(' · ')}`
          : 'Detailed SOW scope and P/D/I/V/R LOE were generated and saved. Review every activity and final hour before confirmation.'
      });
    } catch (error) { setMessage({ error: true, text: error.message }); }
    finally { setBusy(''); }
  }

  async function confirmWorkspace() {
    setBusy('confirm'); setMessage(null);
    const saved = await persist(workspace, { silent: true });
    try {
      const body = await request(`/api/sow-gsd/v1/workspaces/${saved.workspaceId}/confirm`, {
        method: 'POST', body: JSON.stringify({ expectedRevision: saved.revisionNumber, reason: 'Solution Architect reviewed SOW/GSD content and final LOE.' })
      });
      applyWorkspace(body);
      setMessage({ error: false, text: 'SOW/GSD confirmed. Final Word SOW and Excel GSD downloads are now enabled.' });
    } catch (error) { setMessage({ error: true, text: error.message }); }
    finally { setBusy(''); }
  }

  async function downloadDocument(kind) {
    if (!workspace.workspaceId || !workspace.canDownload) return;
    setBusy(`download-${kind}`); setMessage(null);
    try {
      const response = await request(`/api/sow-gsd/v1/workspaces/${workspace.workspaceId}/${kind === 'sow' ? 'sow.docx' : 'gsd.xlsx'}`);
      const blob = await response.blob();
      const disposition = response.headers.get('content-disposition') || '';
      const match = disposition.match(/filename\*?=(?:UTF-8''|\")?([^\";]+)/i);
      const fallback = `${workspace.reference || 'Module025'}-${kind.toUpperCase()}.${kind === 'sow' ? 'docx' : 'xlsx'}`;
      const fileName = decodeURIComponent((match?.[1] || fallback).replace(/^\"|\"$/g, ''));
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url; anchor.download = fileName; document.body.appendChild(anchor); anchor.click(); anchor.remove();
      URL.revokeObjectURL(url);
    } catch (error) { setMessage({ error: true, text: error.message }); }
    finally { setBusy(''); }
  }

  async function changeArchive(archive) {
    setBusy(archive ? 'archive' : 'restore'); setMessage(null);
    try {
      const body = await request(`/api/sow-gsd/v1/workspaces/${workspace.workspaceId}/${archive ? 'archive' : 'restore'}`, {
        method: 'POST', body: JSON.stringify({ expectedRevision: workspace.revisionNumber, reason: archive ? 'No longer active.' : 'Returned to active authoring.' })
      });
      applyWorkspace(body);
      setMessage({ error: false, text: archive ? 'SOW/GSD archived and removed from the Active list.' : 'SOW/GSD restored to Active drafts.' });
    } catch (error) { setMessage({ error: true, text: error.message }); }
    finally { setBusy(''); }
  }

  const templateLabel = workspace.oemCustomerType === 'STANDARD' ? 'Standard GSD' : 'HAEA Staff Aug GSD KUS UVO Telematics 1';
  const gaps = useMemo(() => confirmationGaps(workspace), [workspace]);
  const totalSuggested = PHASES.reduce((sum, phase) => sum + Number(workspace.suggestedHours?.[phase.key] || 0), 0);
  const totalFinal = PHASES.reduce((sum, phase) => sum + Number(workspace.finalHours?.[phase.key] || 0), 0);
  const hasUnsavedChanges = Boolean(workspace.workspaceId) && JSON.stringify(payloadFor(workspace, false)) !== lastSavedSignature.current;
  const disabled = !workspace.canEdit || workspace.status === 'ARCHIVED';

  return <div className="sow-gsd-workspace">
    <header className="sow-gsd-hero">
      <div><span>Module 025 · SOW + GSD Workspace</span><h1>Scope once. Build both documents. Keep the record.</h1><p>Create delivery-ready SOW scope, generate explainable P/D/I/V/R effort, review every detail, and retain one immutable reference across the SOW and GSD.</p></div>
      <div className="sow-gsd-hero-actions">{options?.access?.canCreate ? <button type="button" className="sales-delivery-primary" onClick={newWorkspace}>+ New SOW / GSD</button> : <span className="sow-gsd-readonly-note">Manager view · read only</span>}</div>
    </header>

    <nav className="sow-gsd-tabs" aria-label="SOW/GSD workspace tabs">
      <button className={tab === 'active' ? 'is-active' : ''} onClick={() => setTab('active')}>Active</button>
      <button className={tab === 'archived' ? 'is-active' : ''} onClick={() => setTab('archived')}>Archived</button>
      <button className={tab === 'editor' ? 'is-active' : ''} onClick={() => setTab('editor')}>Create / Edit</button>
    </nav>

    {message ? <div className={`sow-gsd-message ${message.error ? 'is-error' : 'is-success'}`}>{message.text}</div> : null}

    {tab !== 'editor' ? <section className="sow-gsd-list-card">
      <div className="sow-gsd-list-toolbar">
        <div><span>{tab === 'archived' ? 'Inactive records' : 'Current work'}</span><h2>{tab === 'archived' ? 'Archived SOW/GSDs' : 'Active SOW/GSDs'}</h2></div>
        <div className="sow-gsd-list-filters">
          {options?.access?.canSelectSolutionArchitect ? <label>Solution Architect<select value={ownerFilter} onChange={(event) => setOwnerFilter(event.target.value)}><option value="">All visible Solution Architects</option>{(options.solutionArchitects || []).map((person) => <option key={person.userId} value={person.userId}>{person.displayName}</option>)}</select></label> : null}
          <label>Search<input value={search} onChange={(event) => setSearch(event.target.value)} onKeyDown={(event) => { if (event.key === 'Enter') void loadList(tab, ownerFilter, search); }} placeholder="Reference, customer, project…" /></label>
          <button type="button" className="sow-gsd-secondary" onClick={() => loadList(tab, ownerFilter, search)}>{listBusy ? 'Loading…' : 'Search'}</button>
        </div>
      </div>
      <div className="sow-gsd-record-list">
        {list.map((item) => <button type="button" className="sow-gsd-record" key={item.workspaceId} onClick={() => openWorkspace(item.workspaceId)}>
          <div><strong>{item.reference}</strong><span>{item.customerName} · {item.projectName}</span></div>
          <div><b>{words(item.status)}</b><span>{item.ownerSolutionArchitectName}</span></div>
          <div><b>{item.contractType === 'FIXED' ? 'Fixed Price' : 'T&M'}</b><span>{Number(item.totalFinalHours || 0).toFixed(2)} final hr</span></div>
          <div><span>Updated</span><b>{new Date(item.updatedAt).toLocaleDateString()}</b></div>
        </button>)}
        {!listBusy && !list.length ? <div className="sow-gsd-empty-list">No {tab === 'archived' ? 'archived' : 'active'} SOW/GSD records match this view.</div> : null}
      </div>
    </section> : null}

    {tab === 'editor' ? <div className="sow-gsd-editor">
      <section className="sow-gsd-status-strip">
        <div><span>Immutable record ID</span><strong>{workspace.reference}</strong></div>
        <div><span>Status</span><strong>{words(workspace.status)}</strong></div>
        <div><span>Autosave</span><strong>{saveState}</strong></div>
        <div><span>GSD template</span><strong>{templateLabel}</strong></div>
        <div><span>LOE</span><strong>{totalSuggested.toFixed(2)} AI / {totalFinal.toFixed(2)} final hr</strong></div>
      </section>

      <section className="sow-gsd-card">
        <div className="sow-gsd-card-heading"><div><span>1 · Engagement setup</span><h2>Customer, commercial model, people, and Service Overview</h2><p>The Service Overview is the authoring basis Celar AI expands into the detailed delivery scope.</p></div>{workspace.workspaceId && workspace.canEdit ? <button type="button" className="sow-gsd-secondary" onClick={() => persist(workspace)}>Save now</button> : null}</div>
        <div className="sow-gsd-grid">
          <label className="sow-gsd-inline-toggle"><input disabled={disabled} type="checkbox" checked={manualCustomer} onChange={(event) => { const checked = event.target.checked; setManualCustomer(checked); setWorkspace((current) => ({ ...current, customerSource: checked ? 'MANUAL' : 'DIRECTORY', customerId: checked ? '' : current.customerId, customerName: checked ? current.customerName : '' })); }} />Customer is not in the directory</label>
          {!manualCustomer ? <label>Customer<select disabled={disabled || options.loading} value={workspace.customerId || ''} onChange={(event) => { const customer = options.customers.find((item) => item.clientId === event.target.value); setWorkspace((current) => ({ ...current, customerId: event.target.value, customerName: customer?.clientName || '', customerSource: 'DIRECTORY' })); }}><option value="">Select customer…</option>{(options.customers || []).map((customer) => <option key={customer.clientId} value={customer.clientId}>{customer.clientName}{customer.clientCode ? ` · ${customer.clientCode}` : ''}</option>)}</select></label> : <label>Manual customer name<input disabled={disabled} value={workspace.customerName} onChange={(event) => setWorkspace((current) => ({ ...current, customerName: event.target.value, customerId: '', customerSource: 'MANUAL' }))} /></label>}
          <label>Project / SOW name<input disabled={disabled} value={workspace.projectName} onChange={(event) => setWorkspace((current) => ({ ...current, projectName: event.target.value }))} placeholder="Customer project or engagement name" /></label>
          <label>Project / opportunity code<input disabled={disabled} value={workspace.projectCode || ''} onChange={(event) => setWorkspace((current) => ({ ...current, projectCode: event.target.value }))} /></label>
          <label>Opportunity reference<input disabled={disabled} value={workspace.opportunityReference || ''} onChange={(event) => setWorkspace((current) => ({ ...current, opportunityReference: event.target.value }))} /></label>
          <label>Contract type<select disabled={disabled} value={workspace.contractType} onChange={(event) => setWorkspace((current) => ({ ...current, contractType: event.target.value }))}><option value="T_AND_M">Time & Materials</option><option value="FIXED">Fixed Price</option></select><small>This selection is written into both the SOW and GSD.</small></label>
          <label>Customer / OEM type<select disabled={disabled} value={workspace.oemCustomerType} onChange={(event) => setWorkspace((current) => ({ ...current, oemCustomerType: event.target.value, gsdTemplateCode: event.target.value === 'STANDARD' ? 'STANDARD' : 'HAEA_STAFF_AUG_KUS_UVO' }))}><option value="STANDARD">Standard</option><option value="TOYOTA">Toyota</option><option value="HYUNDAI">Hyundai</option></select><small>{templateLabel}</small></label>
          <label>Account Executive<select disabled={disabled} value={workspace.accountExecutiveUserId || ''} onChange={(event) => setWorkspace((current) => ({ ...current, accountExecutiveUserId: event.target.value }))}><option value="">Select Account Executive…</option>{(options.accountExecutives || []).map((person) => <option key={person.userId} value={person.userId}>{person.displayName}</option>)}</select></label>
          <label>Resale person<select disabled={disabled} value={workspace.resaleUserId || ''} onChange={(event) => setWorkspace((current) => ({ ...current, resaleUserId: event.target.value }))}><option value="">Select Resale person…</option>{(options.resalePeople || []).map((person) => <option key={person.userId} value={person.userId}>{person.displayName}</option>)}</select></label>
          {options?.access?.canSelectSolutionArchitect ? <label>Solution Architect<select disabled={Boolean(workspace.workspaceId) || disabled} value={workspace.ownerSolutionArchitectUserId || ''} onChange={(event) => setWorkspace((current) => ({ ...current, ownerSolutionArchitectUserId: event.target.value }))}><option value="">Select Solution Architect…</option>{(options.solutionArchitects || []).map((person) => <option key={person.userId} value={person.userId}>{person.displayName}</option>)}</select><small>Owner is locked after the immutable record is created.</small></label> : null}
          <label className="is-wide">Service Overview<textarea disabled={disabled} rows="9" value={workspace.serviceOverview} onChange={(event) => setWorkspace((current) => ({ ...current, serviceOverview: event.target.value }))} placeholder="Describe the customer objective, technologies, locations, quantities, known versions, integrations, constraints, deliverables, exclusions, assumptions, access requirements, target outcome, and other verified scope details. Missing facts can remain open questions." /><small>Be specific. Celar AI will use this text to create detailed Plan, Design, Implement, Validate, and Release activities and estimate the phase LOE.</small></label>
        </div>
        {!workspace.workspaceId && workspace.canEdit ? <div className="sow-gsd-create-row"><button type="button" className="sales-delivery-primary" disabled={busy === 'create'} onClick={createDraft}>{busy === 'create' ? 'Creating…' : 'Create draft & enable autosave'}</button><span>The immutable SOW/GSD reference is assigned at this point.</span></div> : null}
      </section>

      <section className="sow-gsd-card">
        <div className="sow-gsd-card-heading"><div><span>2 · Celar AI authoring</span><h2>Generate detailed SOW scope and explainable LOE</h2><p>The generator must describe what will actually be done—not generic phase labels—and preserve missing facts as open questions.</p></div><button type="button" className="sales-delivery-primary" disabled={disabled || !workspace.workspaceId || busy === 'generate'} onClick={generateDetailedScope}>{busy === 'generate' ? 'Generating detailed scope…' : 'Generate detailed SOW + LOE'}</button></div>
        <div className="sow-gsd-quality-rule"><strong>Required output depth</strong><span>Every work package must contain detailed execution steps, inputs, prerequisites/dependencies, US Signal responsibilities, customer responsibilities, outputs/deliverables, acceptance criteria, validation, risks, open questions, required roles, and estimated hours traceable to the work.</span></div>
        {workspace.generation?.warnings?.length ? <div className="sow-gsd-evidence"><strong>Generation warnings</strong>{workspace.generation.warnings.map((warning, index) => <span key={index}>{warning}</span>)}</div> : null}
        {workspace.generation?.missingEvidence?.length ? <div className="sow-gsd-evidence is-warning"><strong>Missing evidence / open input</strong>{workspace.generation.missingEvidence.map((item, index) => <span key={index}>{item}</span>)}</div> : null}
      </section>

      <div className="sow-gsd-phase-stack">
        {PHASES.map((phase) => <PhaseEditor
          key={phase.key}
          phase={phase}
          value={workspace.phaseDetails?.[phase.key] || emptyPhase(phase.label)}
          suggestedHours={workspace.suggestedHours?.[phase.key]}
          finalHours={workspace.finalHours?.[phase.key]}
          disabled={disabled}
          onChange={(nextPhase) => setWorkspace((current) => ({ ...current, phaseDetails: { ...current.phaseDetails, [phase.key]: nextPhase } }))}
          onFinalHoursChange={(nextHours) => setWorkspace((current) => ({ ...current, finalHours: { ...current.finalHours, [phase.key]: nextHours } }))}
        />)}
      </div>

      <section className="sow-gsd-card sow-gsd-review-card">
        <div className="sow-gsd-card-heading"><div><span>3 · Solution Architect review</span><h2>Confirm the SOW/GSD, then download both documents</h2><p>Confirmation is deliberately blocked until the commercial metadata, final hours, and substantive detail for all five phases are complete.</p></div></div>
        <div className="sow-gsd-review-grid">
          <div><strong>Confirmation readiness</strong>{gaps.length ? <>{gaps.map((gap) => <span className="is-gap" key={gap}>Missing: {gap}</span>)}</> : <span className="is-ready">All required review gates are complete.</span>}</div>
          <div><strong>Final LOE</strong>{PHASES.map((phase) => <span key={phase.key}>{phase.label}: {Number(workspace.finalHours?.[phase.key] || 0).toFixed(2)} hr</span>)}<b>Total: {totalFinal.toFixed(2)} hr</b></div>
          <div><strong>Document synchronization</strong><span>Contract: {workspace.contractType === 'FIXED' ? 'Fixed Price' : 'Time & Materials'}</span><span>GSD: {templateLabel}</span><span>AE: {options.accountExecutives?.find((person) => person.userId === workspace.accountExecutiveUserId)?.displayName || workspace.accountExecutiveName || 'Not selected'}</span><span>Resale: {options.resalePeople?.find((person) => person.userId === workspace.resaleUserId)?.displayName || workspace.resaleName || 'Not selected'}</span></div>
        </div>
        <div className="sow-gsd-review-actions">
          {workspace.canEdit && workspace.status !== 'ARCHIVED' ? <button type="button" className="sow-gsd-secondary" onClick={() => setWorkspace((current) => ({ ...current, status: current.status === 'READY_FOR_REVIEW' ? 'DRAFT' : 'READY_FOR_REVIEW' }))}>{workspace.status === 'READY_FOR_REVIEW' ? 'Return to Draft' : 'Mark Ready for Review'}</button> : null}
          {workspace.canEdit && workspace.status !== 'ARCHIVED' ? <button type="button" className="sales-delivery-primary" disabled={Boolean(gaps.length) || busy === 'confirm' || !workspace.workspaceId} onClick={confirmWorkspace}>{busy === 'confirm' ? 'Confirming…' : 'Confirm SOW + GSD'}</button> : null}
          <button type="button" className={`sow-gsd-download ${workspace.canDownload && !hasUnsavedChanges ? '' : 'is-disabled'}`} disabled={!workspace.canDownload || hasUnsavedChanges || busy === 'download-sow'} onClick={() => downloadDocument('sow')}>{busy === 'download-sow' ? 'Preparing SOW…' : 'Download SOW (.docx)'}</button>
          <button type="button" className={`sow-gsd-download ${workspace.canDownload && !hasUnsavedChanges ? '' : 'is-disabled'}`} disabled={!workspace.canDownload || hasUnsavedChanges || busy === 'download-gsd'} onClick={() => downloadDocument('gsd')}>{busy === 'download-gsd' ? 'Preparing GSD…' : 'Download GSD (.xlsx)'}</button>
          {workspace.workspaceId && workspace.canEdit ? <button type="button" className="sow-gsd-archive" disabled={busy === 'archive' || busy === 'restore'} onClick={() => changeArchive(workspace.status !== 'ARCHIVED')}>{workspace.status === 'ARCHIVED' ? 'Restore to Active' : 'Archive / mark inactive'}</button> : null}
        </div>
      </section>
    </div> : null}
  </div>;
}
