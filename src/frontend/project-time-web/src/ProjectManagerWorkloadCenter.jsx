import { useEffect, useRef, useState } from 'react';
import { FinancialTab, ExpenseTab, readJson, requestHeaders } from './UnifiedProjectFinancialWorkspace.jsx';
import './project-workspace-center.css';
import './project-manager-workload-center.css';

const readable = value => String(value || 'Not recorded').replaceAll('_', ' ');
const numeric = value => value == null || value === '' || !Number.isFinite(Number(value)) ? null : Number(value);
const count = value => numeric(value) == null ? 'Unavailable' : Number(value).toLocaleString(undefined, { maximumFractionDigits: 2 });
const hours = value => numeric(value) == null ? 'Unavailable' : `${count(value)} hrs`;
const empty = { loading: true, data: null, error: '' };
const closed = project => ['closed', 'complete', 'completed', 'done', 'cancelled', 'canceled'].includes(String(project.status || '').toLowerCase());
const tabs = [['overview', 'Project context'], ['team', 'Team & hours'], ['financials', 'Financials'], ['expenses', 'Expenses'], ['documents', 'Documents']];

function identityKey() {
  try {
    const session = JSON.parse(localStorage.getItem('projectPulseAuthSession') || 'null');
    const preview = JSON.parse(localStorage.getItem('projectPulseViewAsUser') || 'null');
    return `${session?.sessionToken || session?.token || session?.accessToken || ''}:${preview?.userId || ''}`;
  } catch { return ''; }
}

export default function ProjectManagerWorkloadCenter() {
  const [identity, setIdentity] = useState(identityKey);
  useEffect(() => {
    const update = () => setIdentity(identityKey());
    const events = ['storage', 'projectpulse:view-as-changed', 'projectpulse:auth-session-ready'];
    events.forEach(event => window.addEventListener(event, update));
    return () => events.forEach(event => window.removeEventListener(event, update));
  }, []);
  return <ProjectManagerWorkspace key={identity} />;
}

function ProjectManagerWorkspace() {
  const [manager, setManager] = useState('');
  const [revision, setRevision] = useState(0);
  const [workload, setWorkload] = useState({ ...empty, key: '' });
  const [managerOptions, setManagerOptions] = useState({ access: {}, users: [] });
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('active');
  const [selectedId, setSelectedId] = useState('');
  const [tab, setTab] = useState('overview');
  const [detail, setDetail] = useState({ ...empty, key: '' });
  const [download, setDownload] = useState({ key: '', id: '', loading: false, message: '' });
  const downloadGeneration = useRef(0);
  const scopeKey = `${manager}:${revision}`;
  const refresh = () => setRevision(value => value + 1);

  useEffect(() => {
    let current = true;
    setWorkload({ ...empty, key: scopeKey });
    const params = new URLSearchParams();
    if (manager) params.set('projectManagerUserId', manager);
    readJson(`/api/project-management/workload?${params}`).then(data => {
      if (!current) return;
      setWorkload({ key: scopeKey, loading: false, data, error: '' });
      setManagerOptions({ access: data.access || {}, users: data.selectableProjectManagers || [] });
    }).catch(() => {
      if (current) setWorkload({ key: scopeKey, loading: false, data: null, error: 'Your project workload could not be loaded. Please retry.' });
    });
    return () => { current = false; };
  }, [scopeKey]);

  const currentWorkload = workload.key === scopeKey ? workload : empty;
  const data = currentWorkload.data;
  const projects = data?.projects || [];
  const matches = projects.filter(project => (status === 'all' || (status === 'closed' ? closed(project) : !closed(project)))
    && `${project.projectCode} ${project.projectName} ${project.clientName} ${project.projectManagerName}`.toLowerCase().includes(search.trim().toLowerCase()));
  const project = matches.find(item => item.projectId === selectedId) || matches[0] || null;
  const selectionKey = `${scopeKey}:${project?.projectId || ''}`;
  const currentDetail = detail.key === selectionKey ? detail : empty;
  const financial = currentDetail.data?.project;
  const missingSources = (currentDetail.data?.sources || []).filter(source => source.status !== 'healthy');
  const sourceUnavailable = key => missingSources.some(source => source.key === key);

  useEffect(() => {
    let current = true;
    setDetail({ ...empty, key: selectionKey });
    if (project) {
      const params = new URLSearchParams({ workspace: 'pm' });
      if (manager) params.set('projectManagerUserId', manager);
      readJson(`/api/project-financials/projects/${encodeURIComponent(project.projectId)}?${params}`).then(result => {
        if (result?.project?.projectId !== project.projectId) throw new Error('Project mismatch');
        if (current) setDetail({ key: selectionKey, loading: false, data: result, error: '' });
      }).catch(() => {
        if (current) setDetail({ key: selectionKey, loading: false, data: null, error: 'Additional project details are unavailable in your current access. You can still review the workload information.' });
      });
    }
    return () => { current = false; };
  }, [selectionKey]);

  useEffect(() => {
    downloadGeneration.current += 1;
    setDownload({ key: selectionKey, id: '', loading: false, message: '' });
    return () => { downloadGeneration.current += 1; };
  }, [selectionKey]);

  async function downloadDocument(document) {
    const operation = ++downloadGeneration.current;
    setDownload({ key: selectionKey, id: document.documentId, loading: true, message: `Downloading ${document.originalFileName}...` });
    try {
      const response = await fetch(document.downloadUrl, { credentials: 'include', headers: requestHeaders() });
      if (!response.ok) throw new Error('Document download is unavailable. Please retry.');
      const blob = await response.blob();
      if (operation !== downloadGeneration.current) return;
      const url = URL.createObjectURL(blob);
      const anchor = window.document.createElement('a');
      anchor.href = url;
      anchor.download = document.originalFileName || 'project-document';
      window.document.body.appendChild(anchor); anchor.click(); anchor.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 30000);
      setDownload({ key: selectionKey, id: document.documentId, loading: false, message: `${document.originalFileName} downloaded.` });
    } catch (error) {
      if (operation === downloadGeneration.current) setDownload({ key: selectionKey, id: document.documentId, loading: false, message: error.message });
    }
  }

  const canSelectManager = managerOptions.access.canSelectProjectManager && managerOptions.users.length > 1;
  return <section className="pm-workload-center project-workspace-center" data-module="018">
    <header className="project-workspace-header"><div><p className="eyebrow">Module 018 · Project Management</p><h2>My PM project workspace</h2><p className="muted">Choose a project. Review delivery, team hours, financials, and documents.</p></div><button type="button" onClick={refresh} disabled={currentWorkload.loading}>Refresh</button></header>
    {canSelectManager && <div className="pm-workload-selector-row"><label>Workload view<select value={manager} onChange={event => { setManager(event.target.value); setSelectedId(''); }}><option value="">{managerOptions.access.canViewAll ? 'All project managers' : 'All PMs on my team'}</option>{managerOptions.users.map(pm => <option value={pm.userId} key={pm.userId}>{pm.displayName}{pm.email ? ` (${pm.email})` : ''}</option>)}</select></label></div>}
    {currentWorkload.loading ? <p role="status">Loading your project workload...</p> : currentWorkload.error ? <div className="workspace-error" role="alert">{currentWorkload.error} <button type="button" onClick={refresh}>Retry</button></div> : <>
      <div className="workspace-picker pm-workspace-picker">
        <label>Find a project<input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search by customer, name, or number" /></label>
        <label>Project status<select value={status} onChange={event => setStatus(event.target.value)}><option value="active">Active projects</option><option value="closed">Closed / cancelled projects</option><option value="all">All projects</option></select></label>
        <label>Select managed project<select value={project?.projectId || ''} onChange={event => setSelectedId(event.target.value)} disabled={!matches.length}>{!matches.length && <option value="">No matching projects</option>}{matches.map(item => <option key={item.projectId} value={item.projectId}>{item.projectCode} · {item.projectName} · {item.clientName}</option>)}</select></label>
      </div>
      <details className="pm-portfolio"><summary>Portfolio overview · {count(data.summary?.totalProjects)} projects · {data.quarter || 'Current quarter'}</summary>
        <div className="workspace-summary-grid"><Metric title="Active this quarter" value={count(data.summary?.activeProjectsThisQuarter)} /><Metric title="Closed this quarter" value={count(data.summary?.closedProjectsThisQuarter)} /><Metric title="Projects in scope" value={count(data.summary?.totalProjects)} /><Metric title="Risk highlights" value={count(data.riskHighlights?.length ?? 0)} detail="Highlights supplied by the workload service" /></div>
        <div className="pm-portfolio-grid"><section><h3>Project status</h3>{(data.statusBreakdown || []).map(item => <p key={item.status}>{readable(item.status)}: <strong>{count(item.count)}</strong></p>)}</section><section><h3>Workload risk highlights</h3>{(data.riskHighlights || []).map((risk, index) => <p key={`${risk.projectId}-${index}`}><strong>{risk.projectCode}</strong> · {risk.projectName}<br />{risk.riskSummary}</p>)}{!data.riskHighlights?.length && <p>No workload risk highlights were returned.</p>}</section></div>
      </details>
      {!project ? <p className="workspace-empty">{projects.length ? 'No projects match your search or status filter.' : 'No projects are currently assigned to this PM scope.'}</p> : <>
        <div className="workspace-selection-heading"><div><p className="eyebrow">{project.projectCode} · {project.clientName}</p><h3>{project.projectName}</h3><p>Project manager: {project.projectManagerName || 'Not assigned'}</p></div><button type="button" className="workspace-primary" onClick={() => setTab('documents')}>Project documents</button></div>
        <div className="workspace-summary-grid"><Metric title="Project allocated" value={hours(project.assignedHours)} detail="Recorded task allocations" /><Metric title="Project logged" value={hours(sourceUnavailable('time_entries') ? null : financial?.usedHours)} detail="Includes time awaiting approval" /><Metric title="Team members" value={count(project.assignedResourceCount)} detail="Recorded assigned resources" /><Metric title="Project tasks" value={count(project.taskCount)} detail="Recorded project tasks" /></div>
        <nav className="workspace-tabs" aria-label="PM project sections">{tabs.map(([id, label]) => <button type="button" key={id} aria-pressed={tab === id} onClick={() => setTab(id)}>{label}</button>)}</nav>
        <section className="workspace-panel workspace-selected-content" aria-label={`${tabs.find(([id]) => id === tab)?.[1]} for ${project.projectCode}`}>
          {tab === 'overview' ? <><h3>Project context</h3><dl className="workspace-context-grid"><Field name="Customer" value={project.clientName} /><Field name="Status" value={readable(project.status)} /><Field name="Project manager" value={project.projectManagerName} /><Field name="Start date" value={project.startDate} /><Field name="End date" value={project.endDate} /><Field name="Open cost alerts" value={count(project.openCostAlertCount)} /></dl>{(data.riskHighlights || []).filter(risk => risk.projectId === project.projectId).map((risk, index) => <p key={index} className="workspace-notice attention">{risk.riskSummary}</p>)}<p className="workspace-notice">Hours show recorded allocations and time entries. Financial estimates and uploaded expenses are available in their respective sections.</p></> : currentDetail.loading ? <p role="status">Loading project details...</p> : currentDetail.error ? <div className="workspace-error" role="alert">{currentDetail.error} <button type="button" onClick={refresh}>Retry</button></div> : <>
            {missingSources.length > 0 && <div className="workspace-notice attention" role="status">Some project details could not be loaded: {missingSources.map(source => source.name || readable(source.key)).join(', ')}. Values may be incomplete. <button type="button" onClick={refresh}>Retry details</button></div>}
            {tab === 'team' && <><h3>Project team &amp; hours</h3><dl className="workspace-context-grid"><Field name="Project Team Coordinator" value={financial?.projectTeamCoordinator?.displayName} /><Field name="Solution Architect" value={financial?.solutionArchitect?.displayName} /><Field name="Account Executive" value={financial?.accountExecutive?.displayName} /></dl>{sourceUnavailable('assignments') || sourceUnavailable('time_entries') ? <p>Team hours are unavailable while assignment or time information is incomplete.</p> : <TeamTable engineers={financial?.engineers || []} />}<h4>ConnectWise SELL</h4><dl className="workspace-context-grid"><Field name="Quote" value={financial?.sell?.sellQuoteNumber} /><Field name="Billing method" value={readable(financial?.sell?.billingMethod)} /><Field name="Rate card" value={financial?.sell?.rateCard?.rateCardName} /></dl></>}
            {tab === 'financials' && (['assignments', 'time_entries', 'project_expenses', 'project_metadata'].some(sourceUnavailable) ? <p>Financial estimates are unavailable while their source information is incomplete.</p> : <div className="pm-financial-detail"><p className="workspace-notice">Labor cost and forecast values are rate-based estimates, not verified internal labor cost or actual invoicing. Review the calculation details below.</p><FinancialTab project={financial} /></div>)}
            {tab === 'expenses' && (sourceUnavailable('project_expenses') ? <p>Expense information could not be loaded.</p> : <div className="pm-financial-detail"><ExpenseTab project={financial} /></div>)}
            {tab === 'documents' && (sourceUnavailable('project_documents') ? <p>Project documents could not be loaded.</p> : <><h3>Project documents</h3><p>SOW, GSD, and supporting files available for this project.</p><ul className="workspace-file-list">{(financial?.documentGroups || []).flatMap(group => group.documents || []).map(document => <li key={document.documentId}><div><strong>{document.originalFileName}</strong><small>{readable(document.documentCategory)}</small></div><button type="button" onClick={() => downloadDocument(document)} disabled={!document.downloadUrl || (download.key === selectionKey && download.loading)}>Download</button></li>)}</ul>{!(financial?.documentGroups || []).some(group => group.documents?.length) && <p className="workspace-empty">No documents are available for this project in your current access.</p>}{download.key === selectionKey && download.message && <p role="status">{download.message}</p>}</>)}
          </>}
        </section>
      </>}
    </>}
  </section>;
}

function Metric({ title, value, detail }) { return <article><span>{title}</span><strong>{value}</strong>{detail && <small>{detail}</small>}</article>; }
function Field({ name, value }) { return <div><dt>{name}</dt><dd>{value || 'Not recorded'}</dd></div>; }
function TeamTable({ engineers }) {
  return !engineers.length ? <p className="workspace-empty">No engineering assignments were returned.</p> : <div className="workspace-table-wrap"><table className="workspace-table"><thead><tr><th>Engineer</th><th>Allocated</th><th>Logged</th><th>Available / over</th><th>Tasks</th></tr></thead><tbody>{engineers.map(engineer => {
    const assigned = numeric(engineer.assignedHours), used = numeric(engineer.usedHours);
    const remaining = assigned == null || used == null ? null : assigned - used;
    return <tr key={engineer.userId}><td><strong>{engineer.displayName}</strong><span>{engineer.email}</span></td><td>{hours(assigned)}</td><td>{hours(used)}</td><td className={remaining < 0 ? 'workspace-hours-overrun' : ''}>{remaining < 0 ? `${hours(Math.abs(remaining))} over` : hours(remaining)}</td><td>{engineer.tasks?.join(' · ') || 'No task names recorded'}</td></tr>;
  })}</tbody></table></div>;
}
