import { useMemo } from 'react';

function label(value) {
  return String(value ?? '').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function money(value, currency = 'USD') {
  const number = Number(value);
  if (!Number.isFinite(number)) return 'Not available';
  return new Intl.NumberFormat(undefined, { style: 'currency', currency, maximumFractionDigits: 2 }).format(number);
}

function date(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(`${String(value).slice(0, 10)}T12:00:00Z`);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleDateString();
}

function LinesEditor({ label: title, value, onChange, rows = 4, placeholder = '' }) {
  return <label>{title}<textarea rows={rows} value={(value || []).join('\n')} placeholder={placeholder} onChange={(event) => onChange(event.target.value.split('\n').map((item) => item.trim()).filter(Boolean))} /></label>;
}

export function FlowHiveSaveBar({ dirty, workingCopy, canManage, busy, onSaveWorkingCopy, onSaveVersion }) {
  return <div className={`flowhive-save-bar ${dirty ? 'dirty' : 'saved'}`} role="status">
    <div><strong>{dirty ? 'Unsaved changes' : 'Working copy saved'}</strong><span>{workingCopy?.updatedAt ? `Last saved ${new Date(workingCopy.updatedAt).toLocaleString()} · revision ${workingCopy.workingRevision}` : 'No working copy has been saved for this project.'}</span></div>
    <div className="flowhive-save-bar-actions">
      <button type="button" className="primary" disabled={!canManage || !dirty || busy} onClick={onSaveWorkingCopy}>{busy === 'working-copy' ? 'Saving…' : 'Save working copy'}</button>
      <button type="button" disabled={!canManage || busy} onClick={onSaveVersion}>{busy === 'save' ? 'Saving version…' : 'Save immutable version'}</button>
    </div>
  </div>;
}

export function FlowHiveEvidenceReadiness({ enterprise, canManage, busy, onPrepare }) {
  const evidence = enterprise?.sowEvidence || [];
  return <section className="flowhive-enterprise-card flowhive-evidence-card">
    <header><div><span>AI Planner evidence</span><h3>SOW and GSD readiness</h3></div><strong className={enterprise?.sowEvidenceSummary?.approvedSowScopeReady ? 'ready' : 'blocked'}>{enterprise?.sowEvidenceSummary?.approvedSowScopeReady ? 'Ready' : 'Action required'}</strong></header>
    <p>{enterprise?.sowEvidenceSummary?.explanation || 'Select a project to inspect private planning evidence.'}</p>
    {!evidence.length ? <div className="flowhive-empty-state">No SOW or GSD candidate is registered for this project. Upload the approved source through the project document workspace.</div> : null}
    <div className="flowhive-evidence-list">{evidence.map((item) => <article key={item.documentId} className={item.readyForAiPlanner ? 'ready' : 'blocked'}>
      <div><strong>{item.originalFileName}</strong><span>{label(item.documentCategory)} · {item.documentVersion || 'No active version'}</span></div>
      <dl><div><dt>Private processing</dt><dd>{label(item.processingStatus)}</dd></div><div><dt>Authority</dt><dd>{label(item.authorityStatus || 'not approved')}</dd></div><div><dt>Index</dt><dd>{label(item.indexStatus || 'not indexed')}</dd></div><div><dt>Citations</dt><dd>{item.citationCount} total · {item.scopeCitationCount} scope</dd></div></dl>
      {item.blockers?.length ? <ul>{item.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}</ul> : <p className="flowhive-ready-message">This source is approved and citation-ready for AI Planner.</p>}
      {!item.readyForAiPlanner && canManage ? <div className="flowhive-evidence-actions">
        <button type="button" disabled={busy} onClick={() => onPrepare(item, false)}>{busy === `evidence-${item.documentId}` ? 'Preparing…' : 'Prepare / queue processing'}</button>
        {item.processingStatus === 'ready' && item.activeVersionId && !['approved', 'canonical'].includes(item.authorityStatus) ? <button type="button" className="primary" disabled={busy} onClick={() => onPrepare(item, true)}>Approve current processed version</button> : null}
      </div> : null}
    </article>)}</div>
  </section>;
}

export function FlowHiveFinancialsPanel({ enterprise, financials, controls, setControls, canManage, busy, onSave }) {
  const project = financials?.project || financials || {};
  const currency = controls.currencyCode || 'USD';
  const summary = [
    ['Contract type', label(controls.contractType || project.contractType || 'unknown')],
    ['Approved budget', money(controls.approvedBudget ?? project.approvedBudget ?? project.contractedValue, currency)],
    ['Expense budget', money(controls.expenseBudget, currency)],
    ['Uploaded expenses', money(project.uploadedExpenses ?? project.expenses?.total, currency)],
    ['Forecast at completion', money(controls.forecastAtCompletion ?? project.forecastedFinalCost, currency)],
    ['Current variance', money(project.currentVariance, currency)],
    ['Planned labor', `${Number(project.plannedHours ?? 0).toLocaleString()} hours`],
    ['Used labor', `${Number(project.usedHours ?? 0).toLocaleString()} hours`],
    ['Budget health', label(project.budgetStatus || 'unknown')],
    ['SELL readiness', label(project.sell?.readinessStatus || 'not available')]
  ];
  return <div className="flowhive-view-panel">
    <div className="flowhive-section-heading"><div><span>PM financial command center</span><h3>Project financials and commercial controls</h3><p>Authoritative actuals come from the project-financial service, Module 005 expenses, time entries, Module 055C metadata, and SELL. Missing values are shown as unavailable rather than estimated.</p></div></div>
    <div className="flowhive-financial-grid">{summary.map(([title, value]) => <article key={title}><span>{title}</span><strong>{value}</strong></article>)}</div>
    <section className="flowhive-enterprise-card">
      <header><div><span>Project Manager controls</span><h3>Commercial model and forecast assumptions</h3></div><span>{canManage ? 'Editable by assigned PM' : 'Read-only'}</span></header>
      <div className="flowhive-control-grid">
        <label>Contract type<select value={controls.contractType || 'unknown'} disabled={!canManage} onChange={(event) => setControls({ ...controls, contractType: event.target.value })}><option value="unknown">Unknown / not confirmed</option><option value="fixed_price">Fixed Price</option><option value="time_and_materials">Time and Materials</option><option value="hybrid">Hybrid</option><option value="internal">Internal</option><option value="not_billable">Not billable</option></select></label>
        <label>Currency<input maxLength="3" value={currency} disabled={!canManage} onChange={(event) => setControls({ ...controls, currencyCode: event.target.value.toUpperCase() })} /></label>
        <label>Approved budget<input type="number" min="0" step="0.01" value={controls.approvedBudget ?? ''} disabled={!canManage} onChange={(event) => setControls({ ...controls, approvedBudget: event.target.value === '' ? null : Number(event.target.value) })} /></label>
        <label>Expense budget<input type="number" min="0" step="0.01" value={controls.expenseBudget ?? ''} disabled={!canManage} onChange={(event) => setControls({ ...controls, expenseBudget: event.target.value === '' ? null : Number(event.target.value) })} /></label>
        <label>Contingency budget<input type="number" min="0" step="0.01" value={controls.contingencyBudget ?? ''} disabled={!canManage} onChange={(event) => setControls({ ...controls, contingencyBudget: event.target.value === '' ? null : Number(event.target.value) })} /></label>
        <label>PM forecast at completion<input type="number" min="0" step="0.01" value={controls.forecastAtCompletion ?? ''} disabled={!canManage} onChange={(event) => setControls({ ...controls, forecastAtCompletion: event.target.value === '' ? null : Number(event.target.value) })} /></label>
        <label>Percent complete method<select value={controls.percentCompleteMethod || 'task_weighted'} disabled={!canManage} onChange={(event) => setControls({ ...controls, percentCompleteMethod: event.target.value })}><option value="task_weighted">Task weighted</option><option value="effort_weighted">Effort weighted</option><option value="manual">PM manual</option><option value="earned_value">Earned value</option></select></label>
        <label>Status report cadence<select value={controls.statusReportCadence || 'weekly'} disabled={!canManage} onChange={(event) => setControls({ ...controls, statusReportCadence: event.target.value })}><option value="weekly">Weekly</option><option value="biweekly">Biweekly</option><option value="monthly">Monthly</option><option value="milestone">By milestone</option><option value="manual">Manual</option></select></label>
      </div>
      <label className="flowhive-full-width">Financial notes<textarea rows="4" value={controls.financialNotes || ''} disabled={!canManage} onChange={(event) => setControls({ ...controls, financialNotes: event.target.value })} placeholder="Document approved assumptions, travel expectations, exclusions, and forecast rationale." /></label>
      <footer><button type="button" className="primary" disabled={!canManage || busy} onClick={onSave}>{busy === 'controls' ? 'Saving…' : 'Save financial controls'}</button></footer>
    </section>
  </div>;
}

export function FlowHiveStatusRaidPanel({ enterprise, draftPlan, statusDraft, setStatusDraft, newRaid, setNewRaid, canEditPlanner, canAdministerPlanner, busy, onCreateRaid, onDeleteRaid, onGenerateSummary, onCreateStatusReport }) {
  const raid = enterprise?.raidItems || [];
  const reports = enterprise?.statusReports || [];
  return <div className="flowhive-view-panel">
    <div className="flowhive-section-heading"><div><span>PMI-aligned delivery controls</span><h3>Status reporting and RAID</h3><p>Track risks, issues, actions, decisions, assumptions, dependencies, and changes. Status reports are immutable snapshots; corrections create a new report.</p></div></div>
    <section className="flowhive-enterprise-card">
      <header><div><span>RAID register</span><h3>Open project controls</h3></div><strong>{raid.filter((item) => !['closed', 'resolved', 'rejected'].includes(item.status)).length} open</strong></header>
      {canEditPlanner ? <div className="flowhive-raid-create">
        <label>Type<select value={newRaid.itemType} onChange={(event) => setNewRaid({ ...newRaid, itemType: event.target.value })}>{['risk', 'issue', 'action', 'decision', 'assumption', 'dependency', 'change'].map((value) => <option key={value} value={value}>{label(value)}</option>)}</select></label>
        <label>Priority<select value={newRaid.priority} onChange={(event) => setNewRaid({ ...newRaid, priority: event.target.value })}>{['low', 'medium', 'high', 'critical'].map((value) => <option key={value} value={value}>{label(value)}</option>)}</select></label>
        <label className="wide">Title<input value={newRaid.title} onChange={(event) => setNewRaid({ ...newRaid, title: event.target.value })} placeholder="Concise project control statement" /></label>
        <label>Due date<input type="date" value={newRaid.dueDate || ''} onChange={(event) => setNewRaid({ ...newRaid, dueDate: event.target.value || null })} /></label>
        <label className="wide">Description<textarea rows="2" value={newRaid.description} onChange={(event) => setNewRaid({ ...newRaid, description: event.target.value })} /></label>
        <label className="wide">Mitigation / action<textarea rows="2" value={newRaid.mitigation} onChange={(event) => setNewRaid({ ...newRaid, mitigation: event.target.value })} /></label>
        <button type="button" className="primary" disabled={busy || newRaid.title.trim().length < 3} onClick={onCreateRaid}>{busy === 'raid-create' ? 'Adding…' : 'Add RAID item'}</button>
      </div> : null}
      <div className="flowhive-raid-table-wrap"><table className="flowhive-raid-table"><thead><tr><th>Type</th><th>Title</th><th>Priority</th><th>Status</th><th>Owner</th><th>Due</th><th>Mitigation</th><th>Action</th></tr></thead><tbody>{raid.map((item) => <tr key={item.raidItemId}><td>{label(item.itemType)}</td><td><strong>{item.title}</strong><small>{item.description}</small></td><td><span className={`flowhive-priority ${item.priority}`}>{label(item.priority)}</span></td><td>{label(item.status)}</td><td>{item.ownerName || 'Unassigned'}</td><td>{date(item.dueDate)}</td><td>{item.mitigation || 'Not recorded'}</td><td>{canEditPlanner ? <button type="button" className="danger-quiet" disabled={busy} onClick={() => onDeleteRaid(item)}>Delete</button> : 'Read-only'}</td></tr>)}</tbody></table></div>
      {!raid.length ? <div className="flowhive-empty-state">No RAID items have been recorded for this project.</div> : null}
    </section>
    <section className="flowhive-enterprise-card">
      <header><div><span>Executive communication</span><h3>Create project status report</h3></div><span>{draftPlan?.projectCode || 'Select project'}</span></header>
      <div className="flowhive-health-grid">{['overallHealth', 'scheduleHealth', 'financialHealth', 'scopeHealth'].map((field) => <label key={field}>{label(field)}<select value={statusDraft[field]} disabled={!canAdministerPlanner} onChange={(event) => setStatusDraft({ ...statusDraft, [field]: event.target.value })}>{(field === 'overallHealth' ? ['green', 'amber', 'red', 'complete', 'not_started'] : ['green', 'amber', 'red', 'unknown']).map((value) => <option key={value} value={value}>{label(value)}</option>)}</select></label>)}</div>
      <label className="flowhive-full-width">Executive summary<textarea rows="6" value={statusDraft.executiveSummary || ''} disabled={!canAdministerPlanner} onChange={(event) => setStatusDraft({ ...statusDraft, executiveSummary: event.target.value, generatedSource: 'pm_edited' })} /></label>
      <div className="flowhive-status-detail-grid"><LinesEditor label="Accomplishments" value={statusDraft.accomplishments} onChange={(value) => setStatusDraft({ ...statusDraft, accomplishments: value })} placeholder="One completed outcome per line" /><LinesEditor label="Next steps" value={statusDraft.nextSteps} onChange={(value) => setStatusDraft({ ...statusDraft, nextSteps: value })} /><LinesEditor label="Decisions needed" value={statusDraft.decisionsNeeded} onChange={(value) => setStatusDraft({ ...statusDraft, decisionsNeeded: value })} /><LinesEditor label="Key risks" value={statusDraft.keyRisks} onChange={(value) => setStatusDraft({ ...statusDraft, keyRisks: value })} /></div>
      <footer><button type="button" disabled={!canAdministerPlanner || busy} onClick={onGenerateSummary}>Refresh AI/status summary</button><button type="button" className="primary" disabled={!canAdministerPlanner || busy || (statusDraft.executiveSummary || '').trim().length < 20} onClick={onCreateStatusReport}>{busy === 'status-report' ? 'Creating…' : 'Create immutable status report'}</button></footer>
    </section>
    <section className="flowhive-enterprise-card"><header><div><span>History</span><h3>Immutable status reports</h3></div><strong>{reports.length}</strong></header>{reports.map((report) => <details key={report.statusReportId}><summary>{date(report.statusDate)} · {label(report.overallHealth)} · {label(report.generatedSource)}</summary><p>{report.executiveSummary}</p></details>)}{!reports.length ? <div className="flowhive-empty-state">No status report has been created.</div> : null}</section>
  </div>;
}

export function FlowHiveCustomerSharingPanel({ enterprise, controls, savedPlans, draftPlan, latestShareUrl, setLatestShareUrl, shareDraft, setShareDraft, canManage, busy, onEnableSharing, onCreateShare, onRevoke }) {
  const shares = enterprise?.customerShares || [];
  const baselined = useMemo(() => savedPlans.filter((plan) => plan.projectId === draftPlan?.projectId && plan.baselineVersion), [savedPlans, draftPlan?.projectId]);
  return <article className={`flowhive-sharing-card ${controls.customerSharingEnabled ? 'enabled' : 'locked'}`}>
    <h4>Reviewed customer sharing</h4>
    <p>Creates an expiring, revocable, read-only link tied to an exact reviewed baseline. Internal notes, citations, assignments, provider data, and financial details are excluded.</p>
    {!controls.customerSharingEnabled ? <button type="button" disabled={!canManage || busy} onClick={onEnableSharing}>Enable customer sharing for this project</button> : <>
      <div className="flowhive-share-controls"><label>Reviewed baseline<select value={shareDraft.planId || ''} onChange={(event) => { const plan = baselined.find((item) => item.planId === event.target.value); setShareDraft({ ...shareDraft, planId: event.target.value, versionNumber: plan?.baselineVersion || null }); }}><option value="">Select baseline</option>{baselined.map((plan) => <option key={plan.planId} value={plan.planId}>{plan.planName} · baseline v{plan.baselineVersion}</option>)}</select></label><label>Expiration<select value={shareDraft.expirationDays} onChange={(event) => setShareDraft({ ...shareDraft, expirationDays: Number(event.target.value) })}><option value="7">7 days</option><option value="14">14 days</option><option value="30">30 days</option><option value="60">60 days</option><option value="90">90 days</option></select></label><label className="wide">Customer note<input value={shareDraft.shareNote || ''} onChange={(event) => setShareDraft({ ...shareDraft, shareNote: event.target.value })} placeholder="Optional customer-facing context" /></label></div>
      <button type="button" className="primary" disabled={!canManage || busy || !shareDraft.planId || !shareDraft.versionNumber} onClick={onCreateShare}>{busy === 'customer-share' ? 'Creating…' : 'Create customer link'}</button>
      {latestShareUrl ? <div className="flowhive-share-result"><strong>New customer link</strong><input readOnly value={latestShareUrl} onFocus={(event) => event.target.select()} /><button type="button" onClick={() => navigator.clipboard?.writeText(latestShareUrl)}>Copy</button><a href={latestShareUrl} target="_blank" rel="noreferrer">Open</a><button type="button" onClick={() => setLatestShareUrl('')}>Dismiss</button></div> : null}
    </>}
    <div className="flowhive-share-history">{shares.map((share) => <div key={share.shareId}><span><strong>Baseline v{share.versionNumber}</strong><small>{share.active ? `Expires ${date(share.expiresAt)}` : `Revoked ${date(share.revokedAt)}`} · {share.accessCount} access(es)</small></span>{share.active && canManage ? <button type="button" className="danger-quiet" disabled={busy} onClick={() => onRevoke(share)}>Revoke</button> : null}</div>)}</div>
  </article>;
}
