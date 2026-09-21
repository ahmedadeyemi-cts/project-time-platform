import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import USSignalLogo from '../enterprise/USSignalLogo.jsx';
import { downloadProtected } from './protected-download.js';
import { generationConfidence } from './generation-feedback.js';
import GenerationProgress from './GenerationProgress.jsx';
import useGenerationMonitor from './useGenerationMonitor.js';
import { generationSeconds, generationIsActive } from './generation-progress.js';
import './sow-gsd-workspace.css';
import { withTasks, exportChecks, phaseTaskIssues } from './task-estimates.js';
import PhaseTaskReview from './PhaseTaskReview.jsx';
import OwnershipTransfer from './OwnershipTransfer.jsx';
import TemplateCatalog from './TemplateCatalog.jsx';
import WorkTrackingPanel from './WorkTrackingPanel.jsx';
import { calendarDate, queueFlags, selectQueue } from './work-queue.js';
import './sa-workspace-redesign.css';

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

function meaningfulServiceOverview(value) {
  const text = String(value || '').trim();
  if (text.length < 20 || !/\s/.test(text)) return false;
  return text.split(/\s+/).filter((token) => /[A-Za-z]{3}/.test(token)).length >= 4;
}

function formatDuration(totalSeconds) {
  const seconds = Math.max(0, Number(totalSeconds || 0));
  const whole = Math.floor(seconds);
  const minutes = Math.floor(whole / 60);
  const remainder = whole % 60;
  return `${minutes}m ${String(remainder).padStart(2, '0')}s`;
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

function Button({ children, kind = 'secondary', className = '', ...props }) {
  return (
    <button type="button" className={`m025-button m025-button--${kind} ${className}`.trim()} {...props}>
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

function WorkList({ rows, selectedId, onSelect, emptyLabel, disabled, trackingById = {} }) {
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
          disabled={disabled}
          key={row.engagementId}
          className={selectedId === row.engagementId ? 'm025-work-card is-selected' : 'm025-work-card'}
          onClick={() => onSelect(row.engagementId)}
        >
          <div className="m025-work-card__top">
            <span className="m025-record-id">{row.engagementNumber}</span>
            <StatusPill value={row.status} />
          </div>
          <strong>{row.customerName || 'Customer not selected'}</strong>
          <span>{row.projectName || 'Project name not set'}</span>
          <span>{commercialLabel(row.commercialModel)} · {row.finalHours ?? 0} delivery LOE hour(s)</span>
          <small>{[row.ownerDepartmentName, row.ownerTeamName].filter(Boolean).join(' · ')}</small>
          <small>AE: {row.accountExecutiveName || 'Unassigned'}</small>
          <small>SA: {row.ownerDisplayName || 'Unassigned'} · Updated {formatTime(row.updatedAt)}</small>
          {trackingById[row.engagementId] && <span className="m025-work-card__tracking">
            <span className={`m025-priority m025-priority--${trackingById[row.engagementId].priority || 'normal'}`}>{trackingById[row.engagementId].priority || 'normal'} priority</span>
            <small className={queueFlags(row, trackingById[row.engagementId]).overdue ? 'm025-overdue' : ''}>Target: {calendarDate(trackingById[row.engagementId].targetDate)}{queueFlags(row, trackingById[row.engagementId]).overdue ? ' · Overdue' : ''}</small>
            {trackingById[row.engagementId].blockerReason && <small className="m025-blocked">Blocked: {trackingById[row.engagementId].blockerReason} · {trackingById[row.engagementId].blockerOwnerDisplayName || 'Owner needs review'}</small>}
            {trackingById[row.engagementId].authoringHours != null && <small>SA authoring remaining: {trackingById[row.engagementId].authoringHours}h</small>}
            {trackingById[row.engagementId].workflowIdleDays != null && <small>{trackingById[row.engagementId].workflowIdleDays} day(s) since workflow activity</small>}
          </span>}
        </button>
      ))}
    </div>
  );
}

function PhaseEditor({ phase, proposals, readOnly, onChange }) {
  const tasks = phase?.tasks || [];
  const variance = Number(phase?.finalHours || 0) - Number(phase?.suggestedHours || 0);
  return (
    <article className="m025-phase-card" id={`m025-phase-${phase.phaseCode}`}>
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
              disabled={readOnly || tasks.length > 0}
              onChange={(event) => onChange('finalHours', Number(event.target.value || 0))}
            />
          </Field>
          <div><span>Variance</span><strong>{variance >= 0 ? '+' : ''}{variance.toFixed(2)}h</strong></div>
        </div>
      </header>

      <PhaseTaskReview phase={phase} proposals={proposals} readOnly={readOnly} onChange={onChange} />

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

export default function SowGsdWorkspace({ onOpenRegister, onWorkspaceReady }) {
  const [bootstrap, setBootstrap] = useState(null);
  const [bootError, setBootError] = useState('');
  const [activeTab, setActiveTab] = useState('active');
  const [workspaceView, setWorkspaceView] = useState('my');
  const [listTotal, setListTotal] = useState(0);
  const [listTruncated, setListTruncated] = useState(false);
  const listRequest = useRef(0);
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
  const [transferBusy, setTransferBusy] = useState(false);
  const [trackingDirty, setTrackingDirty] = useState(false);
  const [trackingBusy, setTrackingBusy] = useState(false);
  const [trackingById, setTrackingById] = useState({});
  const [trackingQueueError, setTrackingQueueError] = useState('');
  const [queueFilter, setQueueFilter] = useState('all');
  const [queueSort, setQueueSort] = useState('updated');
  const [generationNow, setGenerationNow] = useState(Date.now());
  const dirtyRef = useRef(false);
  const editVersion = useRef(0);
  const saveInFlight = useRef(false);
  const selectedEngagementRef = useRef('');

  const loadBootstrap = useCallback(async () => {
    try {
      const payload = await requestJson('/api/module025/sow-gsd/bootstrap');
      setBootstrap(payload);
      setOwnerUserId(payload?.currentUser?.userId || '');
      if (payload?.access?.isManager && !payload?.access?.isSolutionArchitect) {
        setWorkspaceView('team'); setOwnerUserId('__team__');
      }
      setBootError('');
    } catch (error) {
      setBootError(error?.message || 'Module 025 could not be initialized.');
    }
  }, []);

  useEffect(() => {
    void loadBootstrap();
  }, [loadBootstrap]);

  useEffect(() => {
    onWorkspaceReady?.(Boolean(bootstrap));
  }, [bootstrap, onWorkspaceReady]);

  const loadList = useCallback(async () => {
    if (!ownerUserId || workspaceView === 'templates') return;
    const requestId = ++listRequest.current;
    setListLoading(true);
    try {
      const query = new URLSearchParams({ state: activeTab, ownerUserId });
      if (search.trim()) query.set('search', search.trim());
      const endpoint = workspaceView === 'team' && ownerUserId === '__team__' ? '/api/module025/sow-gsd/team-work' : '/api/module025/sow-gsd';
      if (ownerUserId === '__team__') query.delete('ownerUserId');
      const payload = await requestJson(`${endpoint}?${query.toString()}`);
      if (requestId !== listRequest.current) return;
      setListTotal(payload.totalCount ?? payload.engagements?.length ?? 0);
      setListTruncated(Boolean(payload.truncated));
      const nextRows = Array.isArray(payload?.engagements) ? payload.engagements : [];
      setRows(nextRows);
      setTrackingById({}); setTrackingQueueError('');
      if (selectedId && !nextRows.some((row) => row.engagementId === selectedId)) {
        selectedEngagementRef.current = '';
        setSelectedId('');
        setEngagement(null);
        setAccess(null);
      }
      if (bootstrap?.capabilities?.workTracking && nextRows.length) {
        try {
          // Keep each read below request-line limits while avoiding a request per record.
          const batches = [];
          for (let offset = 0; offset < nextRows.length; offset += 100)
            batches.push(nextRows.slice(offset, offset + 100).map(row => row.engagementId).join(','));
          const results = await Promise.all(batches.map(ids => requestJson(`/api/module025/sow-gsd/work-tracking?engagementIds=${encodeURIComponent(ids)}`)));
          if (requestId !== listRequest.current) return;
          if (results.some(result => !result.schemaReady)) {
            setTrackingQueueError('Work tracking is awaiting its database update. Queue deadlines and blockers are unavailable.');
            setQueueFilter('all'); setQueueSort('updated');
          }
          else setTrackingById(Object.fromEntries(results.flatMap(result => result.tracking || []).map(item => [item.engagementId, item])));
        } catch (error) {
          if (requestId === listRequest.current) {
            setTrackingQueueError(error.message || 'Queue tracking could not be loaded.');
            setQueueFilter('all'); setQueueSort('updated');
          }
        }
      }
    } catch (error) {
      if (requestId !== listRequest.current) return;
      setRows([]); setListTotal(0); setListTruncated(false);
      setActionState({ busy: '', message: '', error: error?.message || 'The SOW/GSD work list could not be loaded.' });
    } finally {
      if (requestId === listRequest.current) setListLoading(false);
    }
  }, [activeTab, ownerUserId, search, selectedId, workspaceView, bootstrap?.capabilities?.workTracking]);

  useEffect(() => {
    const timer = window.setTimeout(() => void loadList(), search ? 250 : 0);
    return () => window.clearTimeout(timer);
  }, [loadList, search]);

  useEffect(() => {
    if (!dirty && !trackingDirty && !trackingBusy && !transferBusy) return undefined;
    const protect = event => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', protect);
    return () => window.removeEventListener('beforeunload', protect);
  }, [dirty, trackingDirty, trackingBusy, transferBusy]);

  const openEngagement = useCallback(async (engagementId) => {
    if (!engagementId) return;
    selectedEngagementRef.current = engagementId;
    setDetailLoading(true);
    setActionState({ busy: '', message: '', error: '' });
    try {
      const payload = await requestJson(`/api/module025/sow-gsd/${engagementId}`);
      if (selectedEngagementRef.current !== engagementId) return;
      setSelectedId(engagementId);
      editVersion.current += 1;
      setEngagement(payload?.engagement || null);
      setAccess(payload?.access || null);
      dirtyRef.current = false;
      setDirty(false);
      setSaveState({ state: 'idle', message: '', at: payload?.engagement?.updatedAt || null });
    } catch (error) {
      if (selectedEngagementRef.current !== engagementId) return;
      setActionState({ busy: '', message: '', error: error?.message || 'The selected SOW/GSD could not be opened.' });
    } finally {
      if (selectedEngagementRef.current === engagementId) setDetailLoading(false);
    }
  }, []);

  const generationMonitor = useGenerationMonitor({
    engagementId: engagement?.engagementId || '',
    revision: engagement?.revision,
    identityKey: `${bootstrap?.currentUser?.userId || ''}:${Boolean(access?.canEdit)}:${Boolean(access?.isViewAs)}`,
    request: requestJson,
    onComplete: async (payload, recordId) => {
      if (selectedEngagementRef.current !== recordId) return;
      if (payload?.status === 'module025_detailed_scope_generated') {
        // Generation freezes this editor. Never discard an unexpected local edit
        // if another view/extension has nevertheless changed it during polling.
        if (dirtyRef.current) {
          setActionState({ busy: '', message: '', error: 'The new SOW/GSD draft is ready. Save or review your local changes before reloading it.' });
          return;
        }
        await openEngagement(recordId);
        if (selectedEngagementRef.current !== recordId) return;
        void loadList();
        setActionState({ busy: '', message: payload.message || 'SOW and GSD draft generated. Review the tasks and hours before confirmation.', error: '' });
      }
    }
  });
  const generationRunning = generationIsActive(generationMonitor.payload);
  const generationElapsedSeconds = generationSeconds(generationMonitor.payload, generationMonitor.receivedAt, generationNow, generationMonitor.observing);
  const lastGenerationDurationSeconds = generationMonitor.payload?.generationId && generationMonitor.payload?.terminal === true ? generationElapsedSeconds : null;
  useEffect(() => {
    if (!generationRunning || !generationMonitor.observing) return undefined;
    setGenerationNow(Date.now());
    const timer = window.setInterval(() => setGenerationNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [generationRunning, generationMonitor.observing]);

  const markChanged = useCallback((updater) => {
    editVersion.current += 1;
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
    if (!dirtyRef.current) return true;
    if (!engagement || !access?.canEdit || saveInFlight.current) return false;
    if (engagement.status === 'confirmed' || engagement.status === 'archived' || !engagement.isActive) return false;
    saveInFlight.current = true;
    const savingVersion = editVersion.current;
    setSaveState({ state: 'saving', message: 'Saving…', at: null });
    try {
      const payload = await requestJson(`/api/module025/sow-gsd/${engagement.engagementId}`, {
        method: 'PUT',
        body: JSON.stringify({
          expectedRevision: engagement.revision,
          projectName: engagement.projectName || '',
          customerId: engagement.customerEntryMode === 'directory' ? engagement.customerId : null,
          customerName: engagement.customerName,
          customerEntryMode: engagement.customerEntryMode,
          commercialModel: engagement.commercialModel,
          customerProgram: engagement.customerProgram,
          accountExecutiveUserId: engagement.accountExecutiveUserId || null,
          resaleUserId: engagement.resaleUserId || null,
          serviceOverview: engagement.serviceOverview,
          ...(bootstrap?.capabilities?.serviceScope && engagement.serviceScope != null ? { serviceScope: engagement.serviceScope } : {}),
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
            loeRationale: phase.loeRationale,
            tasks: phase.tasks
          }))
        })
      });
      const saved = payload?.engagement?.engagement;
      if (!saved) throw new Error('The saved SOW/GSD could not be verified. Reload before generating scope.');
      if (editVersion.current !== savingVersion) {
        // Preserve edits made while autosave was awaiting the server. Only
        // advance their base revision so the next save can persist those edits.
        setEngagement((current) => current?.engagementId === saved.engagementId
          ? { ...current, revision: saved.revision }
          : current);
        return false;
      }
      setEngagement(saved);
      dirtyRef.current = false;
      setDirty(false);
      setSaveState({ state: 'saved', message: payload?.requiresRegeneration ? 'Saved · regenerate scope' : 'Saved', at: new Date().toISOString() });
      void loadList();
      return true;
    } catch (error) {
      if (error?.status === 409) {
        setSaveState({ state: 'error', message: 'This record changed elsewhere. Reload before continuing.', at: null });
      } else {
        setSaveState({ state: 'error', message: error?.message || 'Autosave failed.', at: null });
      }
      return false;
    } finally {
      saveInFlight.current = false;
    }
  }, [engagement, access, loadList, bootstrap?.capabilities?.serviceScope]);

  useEffect(() => {
    if (!dirty || !access?.canEdit) return undefined;
    const timer = window.setTimeout(() => void saveNow(), bootstrap?.autosave?.recommendedDebounceMilliseconds || 900);
    return () => window.clearTimeout(timer);
  }, [dirty, access, saveNow, bootstrap]);

  const createEngagement = async () => {
    if (!bootstrap?.access?.canCreate || actionState.busy || transferBusy || trackingBusy || trackingDirty) return;
    if (!await saveNow()) return;
    setActionState({ busy: 'create', message: '', error: '' });
    try {
      const payload = await requestJson('/api/module025/sow-gsd', {
        method: 'POST',
        body: JSON.stringify({
          projectName: '',
          customerEntryMode: 'directory',
          commercialModel: 'time_and_materials',
          customerProgram: 'standard',
          serviceOverview: '',
          ...(bootstrap?.capabilities?.serviceScope ? { serviceScope: '' } : {})
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
    if (!engagement || actionState.busy || generationMonitor.busy || transferBusy || trackingBusy || trackingDirty || !access?.canEdit || access?.isViewAs) return;
    const actionRecordId = engagement.engagementId;
    setActionState({ busy: action, message: '', error: '' });
    try {
      if (dirtyRef.current && !await saveNow()) {
        throw new Error('Save the latest Service Scope and overview edits before continuing. If autosave is running, wait for Saved and try again.');
      }
      if (selectedEngagementRef.current !== actionRecordId) return;
      const payload = await requestJson(`/api/module025/sow-gsd/${actionRecordId}/${action}`, { method: 'POST' });
      if (selectedEngagementRef.current !== actionRecordId) return;
      if (action === 'generate') {
        if (payload?.status !== 'module025_detailed_scope_generation_queued' || !payload?.generationId) {
          throw new Error('Detailed scope generation did not return a durable queue identifier. The saved draft was preserved.');
        }
        generationMonitor.track(payload);
        setActionState({ busy: '', message: '', error: '' });
        return;
      }
      await openEngagement(actionRecordId);
      if (selectedEngagementRef.current !== actionRecordId) return;
      await loadList();
      setActionState({ busy: '', message: payload?.message || successMessage, error: '' });
    } catch (error) {
      if (selectedEngagementRef.current !== actionRecordId) return;
      // A dropped POST response may still have created a durable job. Read its
      // status before permitting another attempt; never issue an automatic POST.
      if (action === 'generate') generationMonitor.recheck();
      setActionState({ busy: '', message: '', error: error?.message || `${action} could not be completed.` });
    }
  };

  const archiveSelected = async () => {
    if (!engagement || generationMonitor.busy || actionState.busy || transferBusy || trackingBusy || trackingDirty || !await saveNow()) return;
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

  const deleteDraft = async () => {
    if (!engagement || generationMonitor.busy || engagement.status !== 'draft' || engagement.lastGeneratedAt || actionState.busy || transferBusy || trackingBusy || trackingDirty) return;
    const confirmed = window.confirm(`Delete draft ${engagement.engagementNumber}? This permanently removes the ungenerated draft and cannot be undone.`);
    if (!confirmed) return;
    setActionState({ busy: 'delete', message: '', error: '' });
    try {
      await requestJson(`/api/module025/sow-gsd/${engagement.engagementId}`, { method: 'DELETE' });
      selectedEngagementRef.current = '';
      setSelectedId('');
      setEngagement(null);
      setAccess(null);
      await loadList();
      setActionState({ busy: '', message: 'Ungenerated draft deleted.', error: '' });
    } catch (error) {
      setActionState({ busy: '', message: '', error: error?.message || 'The draft could not be deleted.' });
    }
  };

  const updateTopLevel = (key, value) => markChanged((current) => ({ ...current, [key]: value }));
  const updatePhase = (phaseCode, key, value) => markChanged((current) => ({
    ...current,
    phases: (current.phases || []).map((phase) => phase.phaseCode === phaseCode ? (key === 'tasks' ? withTasks(phase, value) : { ...phase, [key]: value }) : phase)
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
  const operationBusy = transferBusy || trackingBusy || Boolean(actionState.busy);
  const navigationBlocked = dirty || trackingDirty || operationBusy;
  const visibleRows = selectQueue(rows, trackingById, queueFilter, queueSort);
  const queueCounts = rows.reduce((counts, row) => {
    const flags = queueFlags(row, trackingById[row.engagementId]);
    for (const key of ['overdue', 'blocked', 'urgent']) if (flags[key]) counts[key]++;
    return counts;
  }, { overdue: 0, blocked: 0, urgent: 0 });
  const readOnly = transferBusy || trackingBusy || generationMonitor.busy || actionState.busy === 'generate' || !access?.canEdit || access?.isViewAs || engagement?.status === 'confirmed' || engagement?.status === 'archived' || !engagement?.isActive;
  const isSpecialGsd = engagement?.customerProgram === 'toyota' || engagement?.customerProgram === 'hyundai';
  const warnings = Array.isArray(engagement?.aiMetadata?.warnings) ? engagement.aiMetadata.warnings : [];
  const missingEvidence = Array.isArray(engagement?.aiMetadata?.missingEvidence) ? engagement.aiMetadata.missingEvidence : [];
  const generationInputReady = Boolean(String(engagement?.customerName || '').trim())
    && meaningfulServiceOverview(engagement?.serviceScope ?? engagement?.serviceOverview);
  const downloadReady = engagement?.status === 'confirmed' && !dirty && !detailLoading && !actionState.busy && !transferBusy && !trackingBusy;
  const phaseReviewComplete = (engagement?.phases || []).length === 5
    && (engagement?.phases || []).every((phase) => String(phase.objective || '').trim().length > 0);
  const exportReadiness = exportChecks(engagement);
  const missingExportFields = exportReadiness.filter(item => !item.complete);
  const draftDownloadReady = !generationMonitor.busy && Boolean(engagement?.isActive) && !['confirmed', 'archived'].includes(engagement?.status) && !dirty && !detailLoading && !actionState.busy && !transferBusy && !trackingBusy;
  const confirmChecks = engagement ? [
    ...exportReadiness,
    { key: 'generation', label: 'Detailed P/D/I/V/R scope generated', complete: Boolean(engagement.lastGeneratedAt) },
    { key: 'phases', label: 'All five phase objectives reviewed', complete: phaseReviewComplete },
    { key: 'loe', label: 'SA Final LOE is greater than 0 hours', complete: reviewedHours > 0 }
  ] : [];
  const confirmReady = confirmChecks.length > 0 && confirmChecks.every((item) => item.complete);
  const incompleteConfirmChecks = confirmChecks.filter((item) => !item.complete);
  const taskReviewIssues = (engagement?.phases || []).flatMap(phase => phaseTaskIssues(phase).map(message => ({ phaseCode: phase.phaseCode, label: phase.label || phase.phaseCode, message })));
  const currentStage = !engagement ? 0 : engagement.status === 'confirmed' || engagement.status === 'archived' ? 4 : !generationInputReady ? 0 : !engagement.lastGeneratedAt ? 1 : !confirmReady ? 2 : 3;
  const documentReadiness = !engagement
    ? 'Select a SOW/GSD record to review its documents and ConnectWise SELL handoff.'
    : engagement.status === 'confirmed'
      ? 'The confirmed SOW and GSD are ready to download. Review ConnectWise SELL readiness before submitting.'
      : engagement.status === 'archived'
        ? 'This record is archived. Open version history to download any previously retained documents.'
        : !engagement.lastGeneratedAt
          ? 'Generate the detailed scope, review it, then confirm to enable both downloads. Generation failure does not remove this record or any earlier retained versions.'
          : 'Review and confirm the current scope to enable both downloads. Previously retained documents remain available in version history.';

  function confirmReviewed() {
    if (!engagement || actionState.busy || !access?.canEdit) return;
    if (!confirmReady) {
      const missing = incompleteConfirmChecks.map((item) => item.label).join('; ');
      setActionState({ busy: '', message: '', error: `Complete these review requirements before confirmation: ${missing}.` });
      window.requestAnimationFrame(() => document.getElementById('m025-confirm-readiness')?.scrollIntoView({ behavior: 'smooth', block: 'center' }));
      return;
    }
    void runAction('confirm', 'SOW/GSD confirmed and ready for download.');
  }

  function documentFileName(artifact) {
    const number = String(engagement?.engagementNumber || '').replace(/^SOW-/i, '');
    const project = String(engagement?.projectName || 'Project').trim()
      .replace(/[^A-Za-z0-9._-]+/g, '_').replace(/^_+|_+$/g, '') || 'Project';
    return `SOW#${number}_${project}_${artifact.startsWith('draft-') ? 'DRAFT_' : ''}${artifact.endsWith('sow.docx') ? 'SOW.docx' : 'GSD.xlsx'}`;
  }

  async function downloadDocument(artifact) {
    if (artifact.startsWith('draft-') ? !draftDownloadReady : !downloadReady) return;
    setActionState({ busy: 'download', message: '', error: '' });
    try {
      await downloadProtected(`/api/module025/sow-gsd/${engagement.engagementId}/${artifact}`,
        documentFileName(artifact));
      setActionState({ busy: '', message: `${artifact.startsWith('draft-') ? 'Draft ' : ''}${artifact.endsWith('sow.docx') ? 'SOW' : 'GSD'} downloaded.`, error: '' });
    } catch (error) {
      setActionState({ busy: '', message: '', error: error.message });
    }
  }

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
            <p>Prepare customer scope and detailed delivery estimates in one record. Review the proposed work, confirm the package, and complete the sales handoff.</p>
          </div>
        </div>
        <div className="m025-header__actions">
          <Button kind="primary" onClick={createEngagement} disabled={!bootstrap?.access?.canCreate || (operationBusy || trackingDirty)}>
            {actionState.busy === 'create' ? 'Creating…' : 'New SOW / GSD'}
          </Button>
        </div>
      </header>

      <nav className="m025-workspace-views" aria-label="Workspace audience">
        <button type="button" disabled={navigationBlocked} aria-pressed={workspaceView === 'my'} onClick={() => { setWorkspaceView('my'); setOwnerUserId(bootstrap.currentUser.userId); }}>My Work</button>
        {(bootstrap.access.isManager || bootstrap.access.isAdministrator) && <button type="button" disabled={navigationBlocked} aria-pressed={workspaceView === 'team'} onClick={() => { setWorkspaceView('team'); setOwnerUserId('__team__'); }}>Team Work</button>}
        <button type="button" disabled={navigationBlocked} aria-pressed={workspaceView === 'templates'} onClick={() => setWorkspaceView('templates')}>Templates</button>
      </nav>
      {workspaceView === 'templates' && <TemplateCatalog identityKey={`${bootstrap.currentUser.userId}:${Boolean(bootstrap.access.isViewAs)}`} />}
      <div hidden={workspaceView === 'templates'}>
      <section id="m025-documents" className="m025-section m025-document-actions" aria-label="Documents and ConnectWise SELL">
        <div className="m025-section-heading"><div><h2>Documents &amp; ConnectWise SELL handoff</h2></div></div>
        <p id="m025-document-readiness" role="status">{documentReadiness}</p>
        {engagement && !['confirmed', 'archived'].includes(engagement.status) ? <div className="m025-export-readiness" role="status">
          {missingExportFields.length ? <p>Before final confirmation: {missingExportFields.map(item => item.label).join(', ')}.</p> : <p>Export information and task totals are complete.</p>}
          <p>Draft downloads use the last saved information, are marked DRAFT, and do not generate content or create a retained version.</p>
        </div> : null}
        <div className="m025-review-actions" aria-describedby="m025-document-readiness">
          {engagement && !['confirmed', 'archived'].includes(engagement.status) ? <>
            <Button disabled={!draftDownloadReady} onClick={() => downloadDocument('draft-sow.docx')}>Download draft SOW</Button>
            <Button disabled={!draftDownloadReady} onClick={() => downloadDocument('draft-gsd.xlsx')}>Download draft GSD</Button>
          </> : null}
          <Button kind="primary" disabled={!downloadReady} onClick={() => downloadDocument('sow.docx')}>Download SOW (.docx)</Button>
          <Button kind="primary" disabled={!downloadReady} onClick={() => downloadDocument('gsd.xlsx')}>Download GSD (.xlsx)</Button>
          <Button disabled={!engagement || detailLoading || navigationBlocked || !onOpenRegister} onClick={() => onOpenRegister(engagement.engagementId)}>Send to ConnectWise SELL</Button>
          <Button disabled={!engagement || detailLoading || navigationBlocked || !onOpenRegister} onClick={() => onOpenRegister(engagement.engagementId)}>Version history</Button>
        </div>
        <p className="m025-document-help">Send to ConnectWise SELL opens this record’s retained versions and submission readiness. It does not send documents until you confirm an available submission.</p>
      </section>

      <section className="m025-metrics" aria-label="Module 025 summary">
        <Metric label="Your role" value={bootstrap?.access?.isSolutionArchitect ? 'Solution Architect' : bootstrap?.access?.isManager ? 'Manager' : 'Administrator'} detail={bootstrap?.access?.managerScopeReadOnly ? 'Team content is read-only; authorized transfers are recorded' : 'Governed workspace access'} />
        <Metric label="Active records" value={activeTab === 'active' ? listTotal : '—'} detail="Searchable by immutable SOW/GSD ID" />
        <Metric label="Autosave" value="On" detail="Optimistic revision protection" />
        <Metric label="AI scope" value="P / D / I / V / R" detail="Suggested LOE remains editable" />
      </section>

      <nav className="m025-tabs" aria-label="SOW and GSD views">
        <button type="button" disabled={navigationBlocked} className={activeTab === 'active' ? 'is-active' : ''} onClick={() => setActiveTab('active')}>Active SOW / GSD</button>
        <button type="button" disabled={navigationBlocked} className={activeTab === 'archived' ? 'is-active' : ''} onClick={() => setActiveTab('archived')}>Archived</button>
      </nav>

      <section className="m025-filters">
        <Field label="Solution Architect">
          <select value={ownerUserId} disabled={navigationBlocked} onChange={(event) => setOwnerUserId(event.target.value)}>
            {workspaceView === 'team' && <option value="__team__">All authorized team members</option>}
            {(bootstrap?.solutionArchitects || []).filter(person => workspaceView === 'team' || person.userId === bootstrap.currentUser.userId).map((person) => (
              <option key={person.userId} value={person.userId}>{person.displayName}{person.userId === bootstrap?.currentUser?.userId ? ' (You)' : ''}</option>
            ))}
          </select>
        </Field>
        <Field label="Search" hint="Project Name, Customer, Service Overview, or immutable SOW/GSD ID">
          <input value={search} disabled={navigationBlocked} onChange={(event) => setSearch(event.target.value)} placeholder="SOW-2026-000123 or customer…" />
        </Field>
        <div className="m025-filter-status">{listLoading ? 'Refreshing…' : `${rows.length} record(s)`}</div>
      </section>

      {workspaceView === 'team' && <p className="m025-team-help">Visibility follows your current reporting relationships. Delivery LOE is the project estimate, not the SA’s authoring workload. Select a member to focus their queue.</p>}
      {bootstrap.capabilities?.workTracking && <section className="m025-queue-controls" aria-label="Work queue priorities">
        <div className="m025-queue-summary" aria-live="polite">{listLoading ? 'Loading work tracking…' : trackingQueueError ? 'Tracking unavailable' : `Loaded queue: ${queueCounts.overdue} overdue · ${queueCounts.blocked} blocked · ${queueCounts.urgent} urgent`}</div>
        <Field label="Focus queue"><select value={queueFilter} disabled={navigationBlocked || Boolean(trackingQueueError) || listLoading} onChange={event => setQueueFilter(event.target.value)}>
          <option value="all">All loaded work</option><option value="overdue">Overdue</option><option value="blocked">Blocked</option><option value="urgent">Urgent</option>
        </select></Field>
        <Field label="Sort queue"><select value={queueSort} disabled={navigationBlocked || Boolean(trackingQueueError) || listLoading} onChange={event => setQueueSort(event.target.value)}>
          <option value="updated">Recently updated</option><option value="target">Target date</option><option value="priority">Priority</option>
        </select></Field>
        <small>Filters and counts apply to the {rows.length} loaded records. {visibleRows.length} shown.</small>
      </section>}
      {trackingQueueError && <Notice tone="warning" title="Work tracking unavailable"><p>{trackingQueueError}</p></Notice>}
      {trackingDirty && <p className="m025-team-help" role="status">Save or discard your work-tracking changes before switching records or completing another action.</p>}
      {listTruncated && <p role="status">Showing the latest {rows.length} of {listTotal} matching records. Filter by SA or search to narrow the queue.</p>}
      {actionState.error ? <Notice tone="critical" title="Action needs attention"><p>{actionState.error}</p></Notice> : null}
      {actionState.message ? <Notice tone={actionState.busy ? 'info' : 'success'} title={actionState.busy ? 'In progress' : 'Completed'}><p>{actionState.message}</p></Notice> : null}

      <div className="m025-layout">
        <aside className="m025-list-panel">
          <div className="m025-panel-title">
            <div><span>{activeTab === 'archived' ? 'ARCHIVE' : 'ACTIVE WORK'}</span><h2>{activeTab === 'archived' ? 'Archived packages' : 'SOW / GSD work queue'}</h2></div>
          </div>
          <WorkList
            disabled={operationBusy || trackingDirty}
            rows={visibleRows}
            trackingById={trackingById}
            selectedId={selectedId}
            onSelect={async id => { if (!operationBusy && !trackingDirty && await saveNow()) await openEngagement(id); }}
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
                  <h2>{engagement.projectName || engagement.customerName || 'New SOW / GSD'}</h2>
                  <p>{engagement.customerName || 'Customer not selected'} · Owned by {engagement.ownerDisplayName} · Revision {engagement.revision}</p>
                </div>
                <div className={`m025-save-state m025-save-state--${saveState.state}`}>
                  <span>{saveState.state === 'saving' ? '●' : saveState.state === 'error' ? '!' : '✓'}</span>
                  <div><strong>{saveState.message || (dirty ? 'Unsaved changes' : 'Saved')}</strong><small>{saveState.at ? formatTime(saveState.at) : 'Autosave enabled'}</small></div>
                </div>
              </header>

              <nav className="m025-stage-nav" aria-label="SOW and GSD process">
                {['Define request', 'Generate draft', 'Review tasks & hours', 'Confirm package', 'Download & hand off'].map((label, index) => <a key={label} aria-current={currentStage === index ? 'step' : undefined} href={`#${['m025-setup', 'm025-generation', 'm025-task-review', 'm025-review', 'm025-documents'][index]}`} onClick={event => { event.preventDefault(); document.getElementById(['m025-setup', 'm025-generation', 'm025-task-review', 'm025-review', 'm025-documents'][index])?.scrollIntoView({ behavior: 'smooth', block: 'start' }); }}>{index + 1}. {label}</a>)}
              </nav>
              {bootstrap.capabilities?.workTracking && <WorkTrackingPanel engagementId={engagement.engagementId}
                identityKey={`${bootstrap.currentUser.userId}:${Boolean(bootstrap.access.isViewAs)}`} request={requestJson}
                readOnly={Boolean(bootstrap.access.isViewAs) || operationBusy || generationMonitor.busy || detailLoading}
                onDirtyChanged={setTrackingDirty} onBusyChanged={setTrackingBusy} onSaved={() => void loadList()} />}
              <OwnershipTransfer key={engagement.engagementId} engagement={engagement} disabled={navigationBlocked || generationMonitor.busy || saveState.state === 'saving'} request={requestJson}
                notificationsEnabled={bootstrap.capabilities?.handoffNotifications === true}
                onBusyChanged={setTransferBusy} onTransferred={result => {
                selectedEngagementRef.current = ''; setSelectedId(''); setEngagement(null); setAccess(null); dirtyRef.current = false; setDirty(false);
                setActionState({ busy: '', error: '', message: `Ownership transferred to ${result.ownerDisplayName}. The same record and history are retained.${result.notification?.message ? ` ${result.notification.message}` : ''}` }); void loadList();
              }} />
              {access?.isViewAs ? <Notice tone="warning" title="Administrator View-As is read-only"><p>Exit View-As before editing, generating, confirming, or archiving this SOW/GSD.</p></Notice> : null}
              {isSpecialGsd ? <Notice tone="info" title="Toyota / Hyundai GSD profile selected"><p>GSD output will use the <strong>HAEA Staff Aug GSD KUS UVO Telematics 1</strong> profile.</p></Notice> : null}

              <section id="m025-setup" className="m025-section">
                <div className="m025-section-heading">
                  <div><span>01</span><h2>Engagement setup</h2></div>
                  <p>Commercial and ownership metadata flows into both the SOW and GSD.</p>
                </div>
                <div className="m025-form-grid m025-form-grid--3">
                  <Field label="Project Name" hint="Used in SOW/GSD documents, ConnectWise SELL handoff, search, and downloaded filenames.">
                    <input value={engagement.projectName || ''} disabled={readOnly} maxLength={500} onChange={(event) => updateTopLevel('projectName', event.target.value)} placeholder="Enter project name" />
                  </Field>
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

              <section id="m025-generation" className="m025-section">
                <div className="m025-section-heading m025-section-heading--action">
                  <div><span>02</span><h2>Service Scope &amp; generated SOW</h2></div>
                  <div className="m025-generation-control">
                    <Button
                      kind="primary"
                      disabled={readOnly || actionState.busy === 'generate' || !generationInputReady}
                      onClick={() => runAction('generate', 'Detailed P/D/I/V/R scope generated and ready for review.')}
                    >
                      {actionState.busy === 'generate' || generationRunning ? 'Generating detailed scope…' : engagement.lastGeneratedAt ? 'Regenerate detailed scope' : 'Generate detailed scope'}
                    </Button>
                    {generationRunning && generationMonitor.observing ? (
                      <span className="m025-generation-timer" role="timer">Running · {formatDuration(generationElapsedSeconds)}</span>
                    ) : generationRunning ? (
                      <span className="m025-generation-timer">Last verified · {formatDuration(generationElapsedSeconds)}</span>
                    ) : lastGenerationDurationSeconds !== null ? (
                      <span className="m025-generation-timer m025-generation-timer--complete">Last run · {formatDuration(lastGenerationDurationSeconds)}</span>
                    ) : null}
                  </div>
                </div>
                <Field label={engagement.serviceScope != null ? "Service Scope" : "Service Overview (legacy input)"}
                  hint="Enter the complete requested work: current and desired state, technologies, versions, quantities, deliverables, work windows, constraints and exclusions. Every phase uses this same saved input. AI must expand the explanation, not the authorized commitment.">
                  <textarea
                    className="m025-service-overview"
                    rows={10}
                    value={engagement.serviceScope ?? engagement.serviceOverview ?? ''}
                    disabled={readOnly}
                    onChange={(event) => updateTopLevel(engagement.serviceScope != null ? 'serviceScope' : 'serviceOverview', event.target.value)}
                    maxLength={30000}
                    placeholder="Describe the requested services, platforms, expected outcome, known quantities/versions, locations, constraints, integrations, customer responsibilities, and any known acceptance requirements…"
                  />
                </Field>
                {bootstrap?.capabilities?.serviceScope && engagement.serviceScope == null ? <div>
                  <p>This legacy record still uses Service Overview as its input. Adopt Service Scope explicitly to separate the original requirements from the generated narrative.</p>
                  <Button disabled={readOnly} onClick={() => updateTopLevel('serviceScope', engagement.serviceOverview || '')}>Use existing input as Service Scope</Button>
                </div> : null}
                {engagement.serviceScope != null ? <>
                  <p className="m025-generation-input-help">Service Scope is the only author-entered field sent to AI. Private providers receive its complete text; external providers require separate full-text approval in Module 064. It is not sanitized or reduced to keywords. Customer-record fields, pricing and attachments are not included. Do not enter credentials or confidential details in this field.</p>
                  <Field label="Service Overview (generated, editable for review)"
                    hint="AI writes the expanded customer-facing overview from Service Scope. Your edits are preserved during regeneration and do not change the original input.">
                    <textarea rows={10} maxLength={30000} disabled={readOnly}
                      value={engagement.serviceOverview || ''}
                      placeholder="The expanded Service Overview will appear after all five phases complete."
                      onChange={event => updateTopLevel('serviceOverview', event.target.value)} />
                  </Field>
                  {engagement.generatedServiceOverview && engagement.generatedServiceOverview !== engagement.serviceOverview ? <details>
                    <summary>Latest AI overview proposal (your edited overview is preserved)</summary>
                    <p style={{ whiteSpace: 'pre-wrap' }}>{engagement.generatedServiceOverview}</p>
                    <Button disabled={readOnly} onClick={() => updateTopLevel('serviceOverview', engagement.generatedServiceOverview)}>Use this overview proposal</Button>
                  </details> : null}
                </> : null}
                {!generationInputReady ? (
                  <p className="m025-generation-input-help">To generate scope, select a customer and enter a meaningful multi-word Service Scope describing the technical work, expected outcome, and known platform/version details.</p>
                ) : null}
                <GenerationProgress monitor={generationMonitor} now={generationNow}
                  canGenerate={!readOnly && !actionState.busy && !trackingDirty && generationInputReady}
                  dirty={dirty} onResume={() => runAction('generate', 'Detailed scope ready for review.')} />
                <div className="m025-ai-meta">
                  <span>Last generated: <strong>{formatTime(engagement.lastGeneratedAt)}</strong></span>
                  <span>Confidence: <strong>{generationConfidence(engagement)}</strong></span>
                  <span>AI suggested: <strong>{suggestedHours.toFixed(2)}h</strong></span>
                  <span>SA reviewed: <strong>{reviewedHours.toFixed(2)}h</strong></span>
                </div>
                {warnings.length ? <Notice tone="warning" title="Generation warnings"><ul>{warnings.map((item, index) => <li key={`${item}-${index}`}>{item}</li>)}</ul></Notice> : null}
                {missingEvidence.length ? <Notice tone="warning" title="Information still needed"><ul>{missingEvidence.map((item, index) => <li key={`${item}-${index}`}>{item}</li>)}</ul></Notice> : null}
              </section>

              <section id="m025-task-review" className="m025-section">
                <div className="m025-section-heading">
                  <div><span>03</span><h2>Plan · Design · Implement · Validate · Release</h2></div>
                  <p>AI suggestions are a starting point. The Solution Architect owns the final scope and effort.</p>
                </div>
                <div className="m025-phase-stack">
                  {(engagement.phases || []).map((phase) => (
                    <PhaseEditor key={phase.phaseCode} phase={phase} proposals={engagement.sowSections?.taskProposals?.[phase.phaseCode]} readOnly={readOnly} onChange={(key, value) => updatePhase(phase.phaseCode, key, value)} />
                  ))}
                </div>
              </section>

              <section id="m025-review" className="m025-section m025-review-section">
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

                {engagement.status !== 'confirmed' && engagement.status !== 'archived' ? (
                  <div id="m025-confirm-readiness" className={confirmReady ? 'm025-confirm-readiness is-ready' : 'm025-confirm-readiness'}>
                    <div>
                      <strong>{confirmReady ? 'Ready to confirm' : `Review readiness · ${confirmChecks.filter((item) => item.complete).length}/${confirmChecks.length} complete`}</strong>
                      <p>{confirmReady ? 'All required review checks are complete. Confirming will freeze this revision for download and version history.' : 'Complete the remaining checks below before confirming this SOW/GSD.'}</p>
                    </div>
                    <ul>
                      {confirmChecks.map((item) => <li key={item.key} className={item.complete ? 'is-complete' : 'is-incomplete'}>{item.complete ? '✓' : '○'} {item.label}</li>)}
                    </ul>
                    {incompleteConfirmChecks.some(item => item.key === 'task-hours') && <div className="m025-task-blockers"><strong>Go to the item that needs review</strong><ul>{taskReviewIssues.slice(0, 10).map((issue, index) => <li key={`${issue.phaseCode}-${index}`}><button type="button" onClick={() => document.getElementById(`m025-phase-${issue.phaseCode}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' })}>{issue.label}: {issue.message}</button></li>)}</ul>{taskReviewIssues.length > 10 && <p>{taskReviewIssues.length - 10} more items appear in the phase editors.</p>}</div>}
                  </div>
                ) : null}

                <div className="m025-review-actions">
                  {engagement.status === 'confirmed' ? (
                    <>
                      <Button onClick={() => runAction('reopen', 'SOW/GSD reopened for editing.') } disabled={!access?.canEdit || generationMonitor.busy || (operationBusy || trackingDirty)}>Reopen for editing</Button>
                      <p>The confirmed documents are available in Documents &amp; ConnectWise SELL handoff at the top of this workspace.</p>
                    </>
                  ) : engagement.status !== 'archived' ? (
                    <Button className="m025-confirm-button" kind="primary" onClick={confirmReviewed} disabled={!access?.canEdit || generationMonitor.busy || (operationBusy || trackingDirty)}>
                      {actionState.busy === 'confirm' ? 'Confirming…' : confirmReady ? 'Confirm Reviewed SOW / GSD' : 'Review Requirements to Confirm'}
                    </Button>
                  ) : null}

                  {engagement.status === 'draft' && !engagement.lastGeneratedAt ? (
                    <Button kind="danger" onClick={deleteDraft} disabled={!access?.canEdit || generationMonitor.busy || (operationBusy || trackingDirty)}>
                      {actionState.busy === 'delete' ? 'Deleting…' : 'Delete Draft'}
                    </Button>
                  ) : null}

                  {engagement.status === 'archived' ? (
                    <Button kind="primary" onClick={() => runAction('unarchive', 'SOW/GSD returned to Active.')} disabled={!access?.canArchive || generationMonitor.busy || (operationBusy || trackingDirty)}>Return to Active</Button>
                  ) : (
                    <Button kind="danger" onClick={archiveSelected} disabled={!access?.canArchive || generationMonitor.busy || (operationBusy || trackingDirty)}>Archive SOW / GSD</Button>
                  )}
                </div>
              </section>
            </>
          )}
        </main>
      </div>
      </div>
    </section>
  );
}
