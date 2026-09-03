import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import USSignalLogo from '../enterprise/USSignalLogo.jsx';
import './sow-gsd-workspace.css';

const PHASE_FIELDS = [
  ['detailedActivities', 'Detailed activities'],
  ['technicalTasks', 'Technical tasks / configuration'],
  ['deliverables', 'Deliverables'],
  ['usSignalResponsibilities', 'US Signal responsibilities'],
  ['customerResponsibilities', 'Customer responsibilities'],
  ['prerequisites', 'Prerequisites'],
  ['dependencies', 'Dependencies'],
  ['assumptions', 'Assumptions'],
  ['openQuestions', 'Open questions'],
  ['acceptanceCriteria', 'Acceptance criteria'],
  ['validationSteps', 'Validation steps'],
  ['risks', 'Risks / considerations']
];

async function requestJson(url, options = {}) {
  const response = await fetch(url, {
    credentials: 'include',
    ...options,
    headers: {
      Accept: 'application/json',
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...(options.headers || {})
    }
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload?.message || `Request failed with status ${response.status}.`);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload;
}

const GENERATION_POLL_INTERVAL_MS = 2000;
const GENERATION_POLL_ATTEMPTS = 180;

async function waitForDetailedScopeGeneration(engagementId, generationId, onProgress) {
  for (let attempt = 1; attempt <= GENERATION_POLL_ATTEMPTS; attempt += 1) {
    const payload = await requestJson(`/api/module025/sow-gsd/${engagementId}/generations/${generationId}`);
    if (payload?.terminal === true) {
      if (payload?.status === 'module025_detailed_scope_generated') return payload;
      const error = new Error(payload?.message || 'Detailed scope generation did not complete. The saved draft was preserved.');
      error.payload = payload;
      throw error;
    }

    onProgress?.(payload);
    if (attempt < GENERATION_POLL_ATTEMPTS) {
      await new Promise((resolve) => window.setTimeout(resolve, GENERATION_POLL_INTERVAL_MS));
    }
  }

  throw new Error('Detailed scope generation is still running. It remains safely queued; select Generate detailed scope again to resume checking its status.');
}

function toLines(value) {
  return Array.isArray(value) ? value.join('\n') : '';
}

function fromLines(value) {
  return String(value || '')
    .split(/\r?\n/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function formatTime(value) {
  if (!value) return 'Not yet';
  try {
    return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
  } catch {
    return value;
  }
}

function commercialLabel(value) {
  return value === 'fixed' ? 'Fixed Price' : 'Time & Materials';
}

function statusLabel(value) {
  if (value === 'review_ready') return 'Ready for review';
  if (value === 'confirmed') return 'Confirmed';
  if (value === 'archived') return 'Archived';
  return 'Draft';
}

function StatusPill({ value }) {
  return <span className={`m025-status-pill m025-status-pill--${value || 'draft'}`}>{statusLabel(value)}</span>;
}

function Metric({ label, value, detail }) {
  return (
    <article className="m025-metric">
      <span>{label}</span>
      <strong>{value}</strong>
      {detail ? <small>{detail}</small> : null}
    </article>
  );
}

function Button({ children, kind = 'secondary', ...props }) {
  return (
    <button type="button" className={`m025-button m025-button--${kind}`} {...props}>
      {children}
    </button>
  );
}

function Field({ label, hint, children, className = '' }) {
  return (
    <label className={`m025-field ${className}`.trim()}>
      <span>{label}</span>
      {hint ? <small>{hint}</small> : null}
      {children}
    </label>
  );
}

function Notice({ tone = 'info', title, children }) {
  return (
    <div className={`m025-notice m025-notice--${tone}`}>
      <strong>{title}</strong>
      <div>{children}</div>
    </div>
  );
}

function WorkList({ rows, selectedId, onSelect, emptyLabel }) {
  if (!rows.length) {
    return (
      <div className="m025-empty">
        <strong>{emptyLabel}</strong>
        <p>No SOW/GSD records match the current view.</p>
      </div>
    );
  }
  return (
    <div className="m025-work-list" role="list">
      {rows.map((row) => (
        <button
          type="button"
          role="listitem"
          key={row.engagementId}
          className={selectedId === row.engagementId ? 'm025-work-card is-selected' : 'm025-work-card'}
          onClick={() => onSelect(row.engagementId)}
        >
          <div className="m025-work-card__top">
            <span className="m025-record-id">{row.engagementNumber}</span>
            <StatusPill value={row.status} />
          </div>
          <strong>{row.customerName || 'Customer not selected'}</strong>
          <span>{commercialLabel(row.commercialModel)} · {row.finalHours ?? 0} reviewed hour(s)</span>
          <small>SA: {row.ownerDisplayName || 'Unassigned'} · Updated {formatTime(row.updatedAt)}</small>
        </button>
      ))}
    </div>
  );
}

function PhaseEditor({ phase, readOnly, onChange }) {
  const variance = Number(phase?.finalHours || 0) - Number(phase?.suggestedHours || 0);
  return (
    <article className="m025-phase-card">
      <header>
        <div>
          <span className="m025-phase-sequence">{String(phase?.sortOrder || '').padStart(2, '0')}</span>
          <div>
            <h3>{phase?.label || phase?.phaseCode}</h3>
            <p>Detailed execution scope and reviewed level of effort.</p>
          </div>
        </div>
        <div className="m025-phase-hours">
          <div><span>AI suggested</span><strong>{Number(phase?.suggestedHours || 0).toFixed(2)}h</strong></div>
          <Field label="SA final hours">
            <input
              type="number"
              min="0"
              step="0.25"
              value={phase?.finalHours ?? 0}
              disabled={readOnly}
              onChange={(event) => onChange('finalHours', Number(event.target.value || 0))}
            />
          </Field>
          <div><span>Variance</span><strong>{variance >= 0 ? '+' : ''}{variance.toFixed(2)}h</strong></div>
        </div>
      </header>

      <Field label="Phase objective" hint="Describe the expected outcome and what completion of this phase means.">
        <textarea
          rows={4}
          value={phase?.objective || ''}
          disabled={readOnly}
          onChange={(event) => onChange('objective', event.target.value)}
        />
      </Field>

      <Field label="Level-of-effort rationale" hint="Explain what the phase hours cover and what must be validated before they are treated as final.">
        <textarea
          rows={3}
          value={phase?.loeRationale || ''}
          disabled={readOnly}
          onChange={(event) => onChange('loeRationale', event.target.value)}
        />
      </Field>

      <details className="m025-phase-details">
        <summary>Review detailed phase content</summary>
        <div className="m025-phase-detail-grid">
          {PHASE_FIELDS.map(([key, label]) => (
            <Field key={key} label={label} hint="One detailed item per line.">
              <textarea
                rows={5}
                value={toLines(phase?.[key])}
                disabled={readOnly}
                onChange={(event) => onChange(key, fromLines(event.target.value))}
              />
            </Field>
          ))}
        </div>
      </details>
    </article>
  );
}

export default function SowGsdWorkspace() {
  const [bootstrap, setBootstrap] = useState(null);
  const [bootError, setBootError] = useState('');
  const [activeTab, setActiveTab] = useState('active');
  const [ownerUserId, setOwnerUserId] = useState('');
  const [search, setSearch] = useState('');
  const [rows, setRows] = useState([]);
  const [listLoading, setListLoading] = useState(false);
  const [selectedId, setSelectedId] = useState('');
  const [engagement, setEngagement] = useState(null);
  const [access, setAccess] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [saveState, setSaveState] = useState({ state: 'idle', message: '', at: null });
  const [actionState, setActionState] = useState({ busy: '', message: '', error: '' });
  const dirtyRef = useRef(false);
  const saveInFlight = useRef(false);

  const loadBootstrap = useCallback(async () => {
    try {
      const payload = await requestJson('/api/module025/sow-gsd/bootstrap');
      setBootstrap(payload);
      setOwnerUserId(payload?.currentUser?.userId || '');
      setBootError('');
    } catch (error) {
      setBootError(error?.message || 'Module 025 could not be initialized.');
    }
  }, []);

  useEffect(() => {
    void loadBootstrap();
  }, [loadBootstrap]);

  const loadList = useCallback(async () => {
    if (!ownerUserId) return;
    setListLoading(true);
    try {
      const query = new URLSearchParams({ state: activeTab, ownerUserId });
      if (search.trim()) query.set('search', search.trim());
      const payload = await requestJson(`/api/module025/sow-gsd?${query.toString()}`);
      const nextRows = Array.isArray(payload?.engagements) ? payload.engagements : [];
      setRows(nextRows);
      if (selectedId && !nextRows.some((row) => row.engagementId === selectedId)) {
        setSelectedId('');
        setEngagement(null);
        setAccess(null);
      }
    } catch (error) {
      setActionState({ busy: '', message: '', error: error?.message || 'The SOW/GSD work list could not be loaded.' });
    } finally {
      setListLoading(false);
    }
  }, [activeTab, ownerUserId, search, selectedId]);

  useEffect(() => {
    const timer = window.setTimeout(() => void loadList(), search ? 250 : 0);
    return () => window.clearTimeout(timer);
  }, [loadList, search]);

  const openEngagement = useCallback(async (engagementId) => {
    if (!engagementId) return;
    setDetailLoading(true);
    setActionState({ busy: '', message: '', error: '' });
    try {
      const payload = await requestJson(`/api/module025/sow-gsd/${engagementId}`);
      setSelectedId(engagementId);
      setEngagement(payload?.engagement || null);
      setAccess(payload?.access || null);
      dirtyRef.current = false;
      setDirty(false);
      setSaveState({ state: 'idle', message: '', at: payload?.engagement?.updatedAt || null });
    } catch (error) {
      setActionState({ busy: '', message: '', error: error?.message || 'The selected SOW/GSD could not be opened.' });
    } finally {
      setDetailLoading(false);
    }
  }, []);

  const markChanged = useCallback((updater) => {
    setEngagement((current) => {
      if (!current) return current;
      const next = typeof updater === 'function' ? updater(current) : { ...current, ...updater };
      return next;
    });
    dirtyRef.current = true;
    setDirty(true);
    setSaveState({ state: 'pending', message: 'Unsaved changes', at: null });
  }, []);

  const saveNow = useCallback(async () => {
    if (!engagement || !dirtyRef.current || !access?.canEdit || saveInFlight.current) return;
    if (engagement.status === 'confirmed' || engagement.status === 'archived' || !engagement.isActive) return;
    saveInFlight.current = true;
    setSaveState({ state: 'saving', message: 'Saving…', at: null });
    try {
      const payload = await requestJson(`/api/module025/sow-gsd/${engagement.engagementId}`, {
        method: 'PUT',
        body: JSON.stringify({
          expectedRevision: engagement.revision,
          customerId: engagement.customerEntryMode === 'directory' ? engagement.customerId : null,
          customerName: engagement.customerName,
          customerEntryMode: engagement.customerEntryMode,
          commercialModel: engagement.commercialModel,
          customerProgram: engagement.customerProgram,
          accountExecutiveUserId: engagement.accountExecutiveUserId || null,
          resaleUserId: engagement.resaleUserId || null,
          serviceOverview: engagement.serviceOverview,
          phases: (engagement.phases || []).map((phase) => ({
            phaseCode: phase.phaseCode,
            finalHours: Number(phase.finalHours || 0),
            objective: phase.objective,
            detailedActivities: phase.detailedActivities,
            technicalTasks: phase.technicalTasks,
            deliverables: phase.deliverables,
            customerResponsibilities: phase.customerResponsibilities,
            usSignalResponsibilities: phase.usSignalResponsibilities,
            prerequisites: phase.prerequisites,
            dependencies: phase.dependencies,
            assumptions: phase.assumptions,
            openQuestions: phase.openQuestions,
            acceptanceCriteria: phase.acceptanceCriteria,
            validationSteps: phase.validationSteps,
            risks: phase.risks,
            loeRationale: phase.loeRationale
          }))
        })
      });
      const saved = payload?.engagement?.engagement;
      if (saved) setEngagement(saved);
      dirtyRef.current = false;
      setDirty(false);
      setSaveState({ state: 'saved', message: payload?.requiresRegeneration ? 'Saved · regenerate scope' : 'Saved', at: new Date().toISOString() });
      void loadList();
    } catch (error) {
      if (error?.status === 409) {
        setSaveState({ state: 'error', message: 'This record changed elsewhere. Reload before continuing.', at: null });
      } else {
        setSaveState({ state: 'error', message: error?.message || 'Autosave failed.', at: null });
      }
    } finally {
      saveInFlight.current = false;
    }
  }, [engagement, access, loadList]);

  useEffect(() => {
    if (!dirty || !access?.canEdit) return undefined;
    const timer = window.setTimeout(() => void saveNow(), bootstrap?.autosave?.recommendedDebounceMilliseconds || 900);
    return () => window.clearTimeout(timer);
  }, [dirty, access, saveNow, bootstrap]);

  const createEngagement = async () => {
    if (!bootstrap?.access?.canCreate || actionState.busy) return;
    setActionState({ busy: 'create', message: '', error: '' });
    try {
      const payload = await requestJson('/api/module025/sow-gsd', {
        method: 'POST',
        body: JSON.stringify({
          customerEntryMode: 'directory',
          commercialModel: 'time_and_materials',
          customerProgram: 'standard',
          serviceOverview: ''
        })
      });
      const created = payload?.engagement;
      setActiveTab('active');
      setOwnerUserId(created?.ownerUserId || bootstrap?.currentUser?.userId || ownerUserId);
      await loadList();
      if (created?.engagementId) await openEngagement(created.engagementId);
      setActionState({ busy: '', message: `Created ${created?.engagementNumber || 'new SOW/GSD'}.`, error: '' });
    } catch (error) {
      setActionState({ busy: '', message: '', error: error?.message || 'A new SOW/GSD could not be created.' });
    }
  };

  const runAction = async (action, successMessage) => {
    if (!engagement || actionState.busy) return;
    if (dirtyRef.current) await saveNow();
    setActionState({ busy: action, message: '', error: '' });
    try {
      let payload = await requestJson(`/api/module025/sow-gsd/${engagement.engagementId}/${action}`, { method: 'POST' });
      if (action === 'generate') {
        if (payload?.status !== 'module025_detailed_scope_generation_queued' || !payload?.generationId) {
          throw new Error('Detailed scope generation did not return a durable queue identifier. The saved draft was preserved.');
        }
        setActionState({ busy: action, message: payload?.message || 'Detailed scope generation is queued.', error: '' });
        payload = await waitForDetailedScopeGeneration(
          engagement.engagementId,
          payload.generationId,
          (progress) => setActionState({
            busy: action,
            message: progress?.message || 'Celar AI is preparing the detailed P/D/I/V/R review draft.',
            error: ''
          })
        );
      }
      await openEngagement(engagement.engagementId);
      await loadList();
      setActionState({ busy: '', message: payload?.message || successMessage, error: '' });
    } catch (error) {
      setActionState({ busy: '', message: '', error: error?.message || `${action} could not be completed.` });
    }
  };

  const archiveSelected = async () => {
    if (!engagement) return;
    setActionState({ busy: 'archive', message: '', error: '' });
    try {
      await requestJson(`/api/module025/sow-gsd/${engagement.engagementId}/archive`, { method: 'POST' });
      setSelectedId('');
      setEngagement(null);
      setAccess(null);
      await loadList();
      setActionState({ busy: '', message: 'SOW/GSD moved to Archived.', error: '' });
    } catch (error) {
      setActionState({ busy: '', message: '', error: error?.message || 'The SOW/GSD could not be archived.' });
    }
  };

  const updateTopLevel = (key, value) => markChanged((current) => ({ ...current, [key]: value }));
  const updatePhase = (phaseCode, key, value) => markChanged((current) => ({
    ...current,
    phases: (current.phases || []).map((phase) => phase.phaseCode === phaseCode ? { ...phase, [key]: value } : phase)
  }));

  const selectedCustomerValue = engagement?.customerEntryMode === 'manual'
    ? '__manual__'
    : (engagement?.customerId || '');
  const reviewedHours = useMemo(
    () => (engagement?.phases || []).reduce((sum, phase) => sum + Number(phase.finalHours || 0), 0),
    [engagement]
  );
  const suggestedHours = useMemo(
    () => (engagement?.phases || []).reduce((sum, phase) => sum + Number(phase.suggestedHours || 0), 0),
    [engagement]
  );
  const readOnly = !access?.canEdit || engagement?.status === 'confirmed' || engagement?.status === 'archived' || !engagement?.isActive;
  const isSpecialGsd = engagement?.customerProgram === 'toyota' || engagement?.customerProgram === 'hyundai';
  const warnings = Array.isArray(engagement?.aiMetadata?.warnings) ? engagement.aiMetadata.warnings : [];
  const missingEvidence = Array.isArray(engagement?.aiMetadata?.missingEvidence) ? engagement.aiMetadata.missingEvidence : [];

  if (bootError) {
    return (
      <section className="m025-workspace m025-workspace--error">
        <USSignalLogo size="large" />
        <Notice tone="critical" title="Module 025 is unavailable"><p>{bootError}</p></Notice>
      </section>
    );
  }

  if (!bootstrap) {
    return <section className="m025-workspace m025-workspace--loading">Loading SOW &amp; GSD Workspace…</section>;
  }

  return (
    <section className="m025-workspace" data-module025-sow-gsd-workspace="true">
      <header className="m025-header">
        <div className="m025-header__identity">
          <USSignalLogo size="large" />
          <div>
            <p className="m025-eyebrow"><span>Module 025</span><i /> <span>Sales &amp; Opportunities</span></p>
            <h1>SOW &amp; GSD Workspace</h1>
            <p>Create, review, autosave, confirm, archive, and export detailed Statements of Work and General Solution Designs from one governed record.</p>
          </div>
        </div>
        <div className="m025-header__actions">
          <Button kind="primary" onClick={createEngagement} disabled={!bootstrap?.access?.canCreate || Boolean(actionState.busy)}>
            {actionState.busy === 'create' ? 'Creating…' : 'New SOW / GSD'}
          </Button>
        </div>
      </header>

      <section className="m025-metrics" aria-label="Module 025 summary">
        <Metric label="Your role" value={bootstrap?.access?.isSolutionArchitect ? 'Solution Architect' : bootstrap?.access?.isManager ? 'Manager' : 'Administrator'} detail={bootstrap?.access?.managerScopeReadOnly ? 'Direct-report visibility is read-only' : 'Governed workspace access'} />
        <Metric label="Active records" value={activeTab === 'active' ? rows.length : '—'} detail="Searchable by immutable SOW/GSD ID" />
        <Metric label="Autosave" value="On" detail="Optimistic revision protection" />
        <Metric label="AI scope" value="P / D / I / V / R" detail="Suggested LOE remains editable" />
      </section>

      <nav className="m025-tabs" aria-label="SOW and GSD views">
        <button type="button" className={activeTab === 'active' ? 'is-active' : ''} onClick={() => setActiveTab('active')}>Active SOW / GSD</button>
        <button type="button" className={activeTab === 'archived' ? 'is-active' : ''} onClick={() => setActiveTab('archived')}>Archived</button>
      </nav>

      <section className="m025-filters">
        <Field label="Solution Architect">
          <select value={ownerUserId} onChange={(event) => setOwnerUserId(event.target.value)}>
            {(bootstrap?.solutionArchitects || []).map((person) => (
              <option key={person.userId} value={person.userId}>{person.displayName}{person.userId === bootstrap?.currentUser?.userId ? ' (You)' : ''}</option>
            ))}
          </select>
        </Field>
        <Field label="Search" hint="Customer, Service Overview, or immutable SOW/GSD ID">
          <input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="SOW-2026-000123 or customer…" />
        </Field>
        <div className="m025-filter-status">{listLoading ? 'Refreshing…' : `${rows.length} record(s)`}</div>
      </section>

      {actionState.error ? <Notice tone="critical" title="Action needs attention"><p>{actionState.error}</p></Notice> : null}
      {actionState.message ? <Notice tone={actionState.busy ? 'info' : 'success'} title={actionState.busy ? 'In progress' : 'Completed'}><p>{actionState.message}</p></Notice> : null}

      <div className="m025-layout">
        <aside className="m025-list-panel">
          <div className="m025-panel-title">
            <div><span>{activeTab === 'archived' ? 'ARCHIVE' : 'ACTIVE WORK'}</span><h2>{activeTab === 'archived' ? 'Archived packages' : 'SOW / GSD work queue'}</h2></div>
          </div>
          <WorkList
            rows={rows}
            selectedId={selectedId}
            onSelect={openEngagement}
            emptyLabel={activeTab === 'archived' ? 'No archived SOW/GSD packages' : 'No active SOW/GSD packages'}
          />
        </aside>

        <main className="m025-editor-panel">
          {!engagement ? (
            <div className="m025-empty m025-empty--editor">
              <strong>{detailLoading ? 'Opening SOW/GSD…' : 'Select a SOW/GSD package'}</strong>
              <p>Choose a record from the work queue, or create a new one to begin.</p>
            </div>
          ) : (
            <>
              <header className="m025-editor-header">
                <div>
                  <div className="m025-editor-header__meta">
                    <span className="m025-record-id">{engagement.engagementNumber}</span>
                    <StatusPill value={engagement.status} />
                    {access?.readOnlyManagerView ? <span className="m025-read-only">Manager view · read only</span> : null}
                  </div>
                  <h2>{engagement.customerName || 'New SOW / GSD'}</h2>
                  <p>Owned by {engagement.ownerDisplayName} · Revision {engagement.revision}</p>
                </div>
                <div className={`m025-save-state m025-save-state--${saveState.state}`}>
                  <span>{saveState.state === 'saving' ? '●' : saveState.state === 'error' ? '!' : '✓'}</span>
                  <div><strong>{saveState.message || (dirty ? 'Unsaved changes' : 'Saved')}</strong><small>{saveState.at ? formatTime(saveState.at) : 'Autosave enabled'}</small></div>
                </div>
              </header>

              {access?.isViewAs ? <Notice tone="warning" title="Administrator View-As is read-only"><p>Exit View-As before editing, generating, confirming, or archiving this SOW/GSD.</p></Notice> : null}
              {isSpecialGsd ? <Notice tone="info" title="Toyota / Hyundai GSD profile selected"><p>GSD output will use the <strong>HAEA Staff Aug GSD KUS UVO Telematics 1</strong> profile.</p></Notice> : null}

              <section className="m025-section">
                <div className="m025-section-heading">
                  <div><span>01</span><h2>Engagement setup</h2></div>
                  <p>Commercial and ownership metadata flows into both the SOW and GSD.</p>
                </div>
                <div className="m025-form-grid m025-form-grid--3">
                  <Field label="Customer" hint="Use the canonical customer directory or choose Customer not listed.">
                    <select
                      value={selectedCustomerValue}
                      disabled={readOnly}
                      onChange={(event) => {
                        if (event.target.value === '__manual__') {
                          markChanged((current) => ({ ...current, customerEntryMode: 'manual', customerId: null, customerName: '' }));
                          return;
                        }
                        const customer = (bootstrap?.customers || []).find((item) => item.customerId === event.target.value);
                        markChanged((current) => ({ ...current, customerEntryMode: 'directory', customerId: event.target.value || null, customerName: customer?.customerName || '' }));
                      }}
                    >
                      <option value="">Select customer…</option>
                      {(bootstrap?.customers || []).map((customer) => <option key={customer.customerId} value={customer.customerId}>{customer.customerName}</option>)}
                      <option value="__manual__">Customer not listed — enter manually</option>
                    </select>
                  </Field>

                  {engagement.customerEntryMode === 'manual' ? (
                    <Field label="Manual customer name" hint="Stored on this immutable SOW/GSD record without creating a directory customer.">
                      <input value={engagement.customerName || ''} disabled={readOnly} onChange={(event) => updateTopLevel('customerName', event.target.value)} />
                    </Field>
                  ) : null}

                  <Field label="Commercial model" hint="The selected model is written into the SOW and GSD.">
                    <select value={engagement.commercialModel || 'time_and_materials'} disabled={readOnly} onChange={(event) => updateTopLevel('commercialModel', event.target.value)}>
                      {(bootstrap?.commercialModels || []).map((item) => <option key={item.key} value={item.key}>{item.label}</option>)}
                    </select>
                  </Field>

                  <Field label="Customer program" hint="Toyota and Hyundai automatically select the HAEA GSD profile.">
                    <select value={engagement.customerProgram || 'standard'} disabled={readOnly} onChange={(event) => updateTopLevel('customerProgram', event.target.value)}>
                      {(bootstrap?.customerPrograms || []).map((item) => <option key={item.key} value={item.key}>{item.label}</option>)}
                    </select>
                  </Field>

                  <Field label="Account Executive">
                    <select value={engagement.accountExecutiveUserId || ''} disabled={readOnly} onChange={(event) => updateTopLevel('accountExecutiveUserId', event.target.value || null)}>
                      <option value="">Select Account Executive…</option>
                      {(bootstrap?.accountExecutives || []).map((person) => <option key={person.userId} value={person.userId}>{person.displayName}</option>)}
                    </select>
                  </Field>

                  <Field label="Inside Sales Representative">
                    <select value={engagement.resaleUserId || ''} disabled={readOnly} onChange={(event) => updateTopLevel('resaleUserId', event.target.value || null)}>
                      <option value="">Select Inside Sales Representative…</option>
                      {(bootstrap?.insideSalesRepresentatives || bootstrap?.resalePeople || []).map((person) => <option key={person.userId} value={person.userId}>{person.displayName}</option>)}
                    </select>
                  </Field>
                </div>
              </section>

              <section className="m025-section">
                <div className="m025-section-heading m025-section-heading--action">
                  <div><span>02</span><h2>Service Overview &amp; AI scope</h2></div>
                  <Button
                    kind="primary"
                    disabled={readOnly || actionState.busy === 'generate' || (engagement.serviceOverview || '').trim().length < 20}
                    onClick={() => runAction('generate', 'Detailed P/D/I/V/R scope generated and ready for review.')}
                  >
                    {actionState.busy === 'generate' ? 'Generating detailed scope…' : engagement.lastGeneratedAt ? 'Regenerate detailed scope' : 'Generate detailed scope'}
                  </Button>
                </div>
                <Field label="Service Overview" hint="Describe the work in enough detail for Celar AI to produce specific technical execution steps. Unsupported facts are returned as assumptions/open questions rather than invented.">
                  <textarea
                    className="m025-service-overview"
                    rows={10}
                    value={engagement.serviceOverview || ''}
                    disabled={readOnly}
                    onChange={(event) => updateTopLevel('serviceOverview', event.target.value)}
                    placeholder="Describe the requested services, platforms, expected outcome, known quantities/versions, locations, constraints, integrations, customer responsibilities, and any known acceptance requirements…"
                  />
                </Field>
                <div className="m025-ai-meta">
                  <span>Last generated: <strong>{formatTime(engagement.lastGeneratedAt)}</strong></span>
                  <span>Confidence: <strong>{engagement?.aiMetadata?.confidence != null ? `${Math.round(Number(engagement.aiMetadata.confidence) * 100)}%` : 'Not generated'}</strong></span>
                  <span>AI suggested: <strong>{suggestedHours.toFixed(2)}h</strong></span>
                  <span>SA reviewed: <strong>{reviewedHours.toFixed(2)}h</strong></span>
                </div>
                {warnings.length ? <Notice tone="warning" title="Generation warnings"><ul>{warnings.map((item, index) => <li key={`${item}-${index}`}>{item}</li>)}</ul></Notice> : null}
                {missingEvidence.length ? <Notice tone="warning" title="Information still needed"><ul>{missingEvidence.map((item, index) => <li key={`${item}-${index}`}>{item}</li>)}</ul></Notice> : null}
              </section>

              <section className="m025-section">
                <div className="m025-section-heading">
                  <div><span>03</span><h2>Plan · Design · Implement · Validate · Release</h2></div>
                  <p>AI suggestions are a starting point. The Solution Architect owns the final scope and effort.</p>
                </div>
                <div className="m025-phase-stack">
                  {(engagement.phases || []).map((phase) => (
                    <PhaseEditor key={phase.phaseCode} phase={phase} readOnly={readOnly} onChange={(key, value) => updatePhase(phase.phaseCode, key, value)} />
                  ))}
                </div>
              </section>

              <section className="m025-section m025-review-section">
                <div className="m025-section-heading">
                  <div><span>04</span><h2>Review, confirm &amp; export</h2></div>
                  <p>Confirmation freezes the reviewed package for download. Reopen it explicitly if another revision is needed.</p>
                </div>
                <div className="m025-review-grid">
                  <Metric label="AI suggested LOE" value={`${suggestedHours.toFixed(2)}h`} detail="Preserved for future estimate analysis" />
                  <Metric label="SA final LOE" value={`${reviewedHours.toFixed(2)}h`} detail={`${(reviewedHours - suggestedHours) >= 0 ? '+' : ''}${(reviewedHours - suggestedHours).toFixed(2)}h vs AI suggestion`} />
                  <Metric label="Commercial model" value={commercialLabel(engagement.commercialModel)} detail="Written into SOW and GSD" />
                  <Metric label="GSD profile" value={isSpecialGsd ? 'HAEA / KUS UVO' : 'Standard'} detail={isSpecialGsd ? 'Toyota / Hyundai profile' : 'Standard delivery profile'} />
                </div>

                <div className="m025-review-actions">
                  {engagement.status === 'confirmed' ? (
                    <>
                      <Button onClick={() => runAction('reopen', 'SOW/GSD reopened for editing.') } disabled={!access?.canEdit || Boolean(actionState.busy)}>Reopen for editing</Button>
                      <a className="m025-button m025-button--primary" href={`/api/module025/sow-gsd/${engagement.engagementId}/sow.docx`}>Download SOW (.docx)</a>
                      <a className="m025-button m025-button--primary" href={`/api/module025/sow-gsd/${engagement.engagementId}/gsd.xlsx`}>Download GSD (.xlsx)</a>
                    </>
                  ) : engagement.status !== 'archived' ? (
                    <Button kind="primary" onClick={() => runAction('confirm', 'SOW/GSD confirmed and ready for download.')} disabled={readOnly || Boolean(actionState.busy) || !engagement.lastGeneratedAt || reviewedHours <= 0}>
                      {actionState.busy === 'confirm' ? 'Confirming…' : 'Confirm reviewed SOW / GSD'}
                    </Button>
                  ) : null}

                  {engagement.status === 'archived' ? (
                    <Button kind="primary" onClick={() => runAction('unarchive', 'SOW/GSD returned to Active.')} disabled={!access?.canArchive || Boolean(actionState.busy)}>Return to Active</Button>
                  ) : (
                    <Button kind="danger" onClick={archiveSelected} disabled={!access?.canArchive || Boolean(actionState.busy)}>Archive SOW / GSD</Button>
                  )}
                </div>
              </section>
            </>
          )}
        </main>
      </div>
    </section>
  );
}
