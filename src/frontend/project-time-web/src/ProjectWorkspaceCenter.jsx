import { useEffect, useMemo, useRef, useState } from 'react';
import './project-workspace-center.css';
import { workOptions, documentsForWork, projectHours, hourExplanation, knownNumber } from './project-workspace-model.js';

function getStoredProjectPulseAuthSession() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return null;

    const parsed = JSON.parse(rawSession);
    if (!parsed?.sessionToken) return null;

    if (parsed?.expiresAt && Date.now() >= Date.parse(parsed.expiresAt)) {
      return null;
    }

    return parsed;
  } catch {
    return null;
  }
}

function getProjectPulseAuthHeaders(viewAsUserId = '') {
  const session = getStoredProjectPulseAuthSession();
  const headers = session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};

  if (viewAsUserId) {
    headers['X-ProjectPulse-View-As-User'] = viewAsUserId;
  }

  return headers;
}

async function fetchJson(path, viewAsUserId = '') {
  const response = await fetch(path, {
    headers: getProjectPulseAuthHeaders(viewAsUserId)
  });

  const body = await response.text();

  if (!response.ok) {
    throw new Error(`${path} returned HTTP ${response.status}${body ? `: ${body.slice(0, 160)}` : ''}`);
  }

  if (!body.trim()) {
    throw new Error(`${path} returned HTTP ${response.status} with an empty response body.`);
  }

  try {
    return JSON.parse(body);
  } catch {
    throw new Error(`${path} returned invalid JSON.`);
  }
}

function readDownloadFileName(response, fallbackName) {
  const disposition = response.headers.get('Content-Disposition') || '';
  const utf8Match = disposition.match(/filename\*=UTF-8''([^;]+)/i);

  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1].replace(/^["']|["']$/g, ''));
    } catch {
      // Continue to the ordinary filename or API-provided fallback.
    }
  }

  const filenameMatch = disposition.match(/filename="?([^";]+)"?/i);
  return filenameMatch?.[1]?.trim() || fallbackName || 'project-document';
}

async function readDownloadError(response) {
  const body = await response.text();

  if (!body.trim()) {
    return `Document download returned HTTP ${response.status}.`;
  }

  try {
    const result = JSON.parse(body);
    return result?.message || result?.status || `Document download returned HTTP ${response.status}.`;
  } catch {
    return body.slice(0, 240);
  }
}

const displayHours = value => value == null ? 'Unavailable' : `${Number(value).toLocaleString(undefined, { maximumFractionDigits: 2 })} hrs`;
const displayAmount = value => knownNumber(value) == null ? 'Not recorded' : Number(value).toLocaleString(undefined, { maximumFractionDigits: 2 });
const readable = value => String(value || 'Not recorded').replaceAll('_', ' ');

export default function ProjectWorkspaceCenter() {
  const [viewAsUsers, setViewAsUsers] = useState([]);
  const [selectedViewAsUserId, setSelectedViewAsUserId] = useState(() => {
    try { return JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null')?.userId || ''; } catch { return ''; }
  });
  useEffect(() => {
    let active = true;
    fetchJson('/api/project-workspace/view-as/users').then(result => { if (active) setViewAsUsers(result.users || []); }).catch(() => {});
    return () => { active = false; };
  }, []);
  return <section className="project-workspace-center" data-module="019">
    {viewAsUsers.length > 0 && <details className="workspace-admin-preview"><summary>Administrator user preview</summary>
      <label>View as <select aria-label="View as" value={selectedViewAsUserId} onChange={event => setSelectedViewAsUserId(event.target.value)}>
        <option value="">My Administrator view</option>
        {viewAsUsers.map(user => <option key={user.userId} value={user.userId}>{user.displayName}</option>)}
      </select></label><p>Read-only preview. Access follows the selected user.</p>
    </details>}
    {selectedViewAsUserId && <p className="workspace-notice">Viewing another user’s assigned workspace. <button type="button" onClick={() => setSelectedViewAsUserId('')}>Exit preview</button></p>}
    <EngineeringProjectWorkspace key={selectedViewAsUserId || 'self'} selectedViewAsUserId={selectedViewAsUserId} />
  </section>;
}

function EngineeringProjectWorkspace({ selectedViewAsUserId }) {
  const [overview, setOverview] = useState({ loading: true, data: null, error: '' });
  const [revision, setRevision] = useState(0);
  const [search, setSearch] = useState('');
  const [selectedKey, setSelectedKey] = useState('');
  const [activeTab, setActiveTab] = useState('tasks');
  const [taskScope, setTaskScope] = useState('mine');
  const [documentSearch, setDocumentSearch] = useState('');
  const [financial, setFinancial] = useState({ key: '', loading: false, data: null, error: '' });
  const [billing, setBilling] = useState({ key: '', loading: false, data: null, error: '' });
  const [documentDownload, setDocumentDownload] = useState({ documentId: '', message: '', error: false });
  const [documentPreview, setDocumentPreview] = useState({ loading: false, item: null, url: '', error: '' });
  const operation = useRef(0);
  const previewTrigger = useRef(null);
  const previewClose = useRef(null);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; operation.current += 1; }; }, []);

  useEffect(() => {
    let current = true;
    setOverview({ loading: true, data: null, error: '' });
    fetchJson('/api/project-workspace/overview', selectedViewAsUserId).then(data => {
      if (current) {
        setOverview({ loading: false, data, error: '' });
        if (revision === 0 && /team|manager|managed|administrator/i.test(data.access?.scope || '')) setTaskScope('team');
      }
    }).catch(() => { if (current) setOverview({ loading: false, data: null, error: 'Your assigned work could not be loaded. Please retry.' }); });
    return () => { current = false; };
  }, [selectedViewAsUserId, revision]);

  const data = overview.data || {};
  const options = useMemo(() => workOptions(data), [overview.data]);
  const matches = options.filter(item => `${item.code} ${item.name} ${item.customer}`.toLowerCase().includes(search.trim().toLowerCase()));
  const work = matches.find(item => item.key === selectedKey) || matches[0] || null;
  const selectionKey = work?.key || '';
  const isProject = work?.kind === 'project';
  const hours = projectHours(data, isProject ? work.id : null, data.access?.userId);
  const projectDocuments = documentsForWork(data.documents, work);
  const filteredDocuments = projectDocuments.filter(document => `${document.originalFileName} ${document.documentCategory}`.toLowerCase().includes(documentSearch.toLowerCase()));
  const relatedRequests = isProject ? (data.resourceRequests || []).filter(request => request.projectId === work.id) : work ? [work.request] : [];
  const visibleTasks = taskScope === 'team' ? hours.tasks : hours.tasks.filter(task => task.userId === data.access?.userId);
  const currentFinancial = financial.key === selectionKey ? financial : { loading: true };
  const currentBilling = billing.key === selectionKey ? billing : { loading: true };

  useEffect(() => {
    operation.current += 1;
    setDocumentDownload({ documentId: '', message: '', error: false });
    setDocumentPreview({ loading: false, item: null, url: '', error: '' });
    setDocumentSearch('');
  }, [selectionKey]);

  useEffect(() => {
    if (activeTab !== 'cost' || !isProject) return;
    let current = true;
    const key = selectionKey;
    setFinancial({ key, loading: true, data: null, error: '' });
    fetchJson(`/api/project-financials/projects/${encodeURIComponent(work.id)}?workspace=engineering`, selectedViewAsUserId)
      .then(result => { if (current) setFinancial({ key, loading: false, data: result, error: '' }); })
      .catch(() => { if (current) setFinancial({ key, loading: false, data: null, error: 'Cost details are unavailable for this project in your current access.' }); });
    // The existing billing endpoint resolves the signed-in actor. Never reuse administrator billing in View-As.
    if (!selectedViewAsUserId && !data.access?.isViewAs) {
      setBilling({ key, loading: true, data: null, error: '' });
      fetchJson(`/api/billing/projects/${encodeURIComponent(work.id)}/invoices`, selectedViewAsUserId)
        .then(result => { if (current) setBilling({ key, loading: false, data: result, error: '' }); })
        .catch(() => { if (current) setBilling({ key, loading: false, data: null, error: 'Invoice history is unavailable in your current access. Ask the project manager or Billing for the billed amount.' }); });
    } else setBilling({ key, loading: false, data: null, error: 'Invoice history is not available in user preview. Sign in as the user to verify billing access.' });
    return () => { current = false; };
  }, [activeTab, selectionKey, selectedViewAsUserId, revision, data.access?.isViewAs]);

  useEffect(() => {
    if (documentPreview.item) previewClose.current?.focus();
  }, [documentPreview.item]);
  useEffect(() => () => { if (documentPreview.url) URL.revokeObjectURL(documentPreview.url); }, [documentPreview.url]);

  async function downloadDocument(workspaceDocument) {
    const request = operation.current;
    setDocumentDownload({ documentId: workspaceDocument.id, message: `Downloading ${workspaceDocument.originalFileName}...`, error: false });
    try {
      if (!workspaceDocument.downloadUrl) throw new Error('This document has no download available.');
      const response = await fetch(workspaceDocument.downloadUrl, { headers: getProjectPulseAuthHeaders(selectedViewAsUserId) });
      if (!response.ok) throw new Error(await readDownloadError(response));
      const blob = await response.blob();
      if (!mounted.current || request !== operation.current) return;
      const blobUrl = URL.createObjectURL(blob);
      const anchor = window.document.createElement('a');
      anchor.href = blobUrl;
      anchor.download = readDownloadFileName(response, workspaceDocument.originalFileName);
      window.document.body.appendChild(anchor); anchor.click(); anchor.remove();
      window.setTimeout(() => URL.revokeObjectURL(blobUrl), 60000);
      setDocumentDownload({ documentId: workspaceDocument.id, message: `${workspaceDocument.originalFileName} downloaded.`, error: false });
    } catch (error) {
      if (mounted.current && request === operation.current) setDocumentDownload({ documentId: workspaceDocument.id, message: error.message || 'Download unavailable.', error: true });
    }
  }

  async function previewDocument(workspaceDocument, event) {
    previewTrigger.current = event.currentTarget;
    const request = ++operation.current;
    setDocumentPreview({ loading: true, item: workspaceDocument, url: '', error: '' });
    try {
      const response = await fetch(workspaceDocument.downloadUrl, { headers: getProjectPulseAuthHeaders(selectedViewAsUserId) });
      if (!response.ok) throw new Error(await readDownloadError(response));
      const blob = await response.blob();
      // Only passive PDF/image previews; HTML and text use a native download.
      if (!/^(application\/pdf|image\/(png|jpeg|gif|webp))(;|$)/i.test(blob.type || workspaceDocument.contentType || '')) throw new Error('Use Download to open this file in its native application.');
      if (!mounted.current || request !== operation.current) return;
      setDocumentPreview({ loading: false, item: workspaceDocument, url: URL.createObjectURL(blob), error: '' });
    } catch (error) {
      if (mounted.current && request === operation.current) setDocumentPreview({ loading: false, item: workspaceDocument, url: '', error: error.message });
    }
  }
  function closePreview() {
    operation.current += 1;
    setDocumentPreview({ loading: false, item: null, url: '', error: '' });
    previewTrigger.current?.focus();
  }

  return <>
    <header className="project-workspace-header"><div><p className="eyebrow">Module 019 · Engineering</p><h2>My project workspace</h2><p className="muted">Choose your work. See the team, track hours, and get the documents you need.</p></div>
      <button type="button" onClick={() => setRevision(value => value + 1)} disabled={overview.loading}>Refresh</button>
    </header>
    {overview.error ? <div className="workspace-error" role="alert">{overview.error} <button type="button" onClick={() => setRevision(value => value + 1)}>Retry</button></div> : null}
    {overview.loading ? <p role="status">Loading your assigned work...</p> : <>
      <div className="workspace-picker">
        <label>Find a project or service request<input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search by customer, name, or number" /></label>
        <label>Select assigned work<select value={selectionKey} onChange={event => setSelectedKey(event.target.value)} disabled={!matches.length}>
          {!matches.length && <option value="">No matching work</option>}
          {matches.map(item => <option key={item.key} value={item.key}>{item.code} · {item.name} · {item.customer}</option>)}
        </select></label>
      </div>
      {!work ? <p className="workspace-empty">{options.length ? 'No work matches your search.' : 'No active projects or service requests are assigned in your current access.'}</p> : <>
        <div className="workspace-selection-heading"><div><p className="eyebrow">{work.code} · {work.customer}</p><h3>{work.name}</h3><p>{isProject ? `Project manager: ${work.project.projectManagerName || 'Not assigned'}` : readable(work.request.status)}</p></div>
          <button type="button" className="workspace-primary" onClick={() => setActiveTab('documents')}>Project documents ({projectDocuments.length})</button>
        </div>
        {isProject ? <>
          <div className="workspace-summary-grid">
            <HourMetric title={hours.myRemaining < 0 ? "My hours over allocation" : "My available hours"} attention={hours.myRemaining < 0} value={hours.myRemaining === null ? null : Math.abs(hours.myRemaining)} detail={hours.mine ? `${displayHours(hours.mine.assigned)} currently allocated to you` : 'No current task allocation for you'} />
            <HourMetric title="Project allocated" value={hours.assigned} detail="Current allocations across the team" />
            <HourMetric title="Project logged" value={hours.logged} detail="Includes time awaiting approval" />
            <HourMetric title={hours.remaining < 0 ? 'Hours over allocation' : 'Project available'} value={hours.remaining === null ? null : Math.abs(hours.remaining)} attention={hours.remaining < 0} detail="Current allocation minus logged time" />
          </div>
          <div className={`workspace-notice ${hours.remaining < 0 ? 'attention' : ''}`}><strong>{hourExplanation(hours)}</strong><p>Available hours are an allocation balance, not an estimate of work left or a billed amount.</p></div>
        </> : <p className="workspace-notice">{displayHours(work.request.requestedHours)} requested. Allocated and logged hours are not yet available for this service request.</p>}
        <nav className="workspace-tabs" aria-label="Project sections">
          {[['tasks', 'Tasks & hours'], ['team', 'Project team'], ['documents', 'Documents'], ['cost', 'Cost & billing'], ['context', 'Project context']].map(([id, title]) => <button type="button" key={id} aria-pressed={activeTab === id} onClick={() => setActiveTab(id)}>{title}</button>)}
        </nav>
        <section className="workspace-panel workspace-selected-content" aria-label={`${readable(activeTab)} for ${work.code}`}>
          {activeTab === 'tasks' && <>
            <div className="workspace-panel-header"><div><h3>Engineering assignments</h3><p>Task hours for {work.code}. Logged time includes entries awaiting approval.</p></div>
              <div className="workspace-toggle"><button type="button" aria-pressed={taskScope === 'mine'} onClick={() => setTaskScope('mine')}>My tasks</button><button type="button" aria-pressed={taskScope === 'team'} onClick={() => setTaskScope('team')}>All project tasks</button></div></div>
            {!visibleTasks.length ? <p className="workspace-empty">{isProject ? taskScope === 'mine' ? 'You have no current task assignments here. Select All project tasks to see the team’s work.' : 'No current task assignments are recorded.' : 'Task assignments will appear when the service request is linked to a project.'}</p> : <div className="workspace-table-wrap"><table className="workspace-table"><thead><tr><th>Task</th><th>Engineer</th><th>Allocated</th><th>Logged</th><th>Available / over</th></tr></thead><tbody>{visibleTasks.map(task => <tr key={task.id}><td><strong>{task.taskName}</strong><span>{task.taskCode}</span></td><td>{task.engineerName}{task.userId === data.access?.userId ? ' (you)' : ''}</td><td>{displayHours(task.assignedHours)}</td><td>{displayHours(task.usedHours)}</td><td className={task.remainingHours < 0 ? 'workspace-hours-overrun' : ''}>{task.remainingHours < 0 ? `${displayHours(Math.abs(task.remainingHours))} over` : displayHours(task.remainingHours)}</td></tr>)}</tbody></table></div>}
          </>}
          {activeTab === 'team' && <>
            <h3>Who is working on this project?</h3><p>Each person’s logged hours contribute to the shared allocation. Dollar cost cannot be inferred from hours alone.</p>
            {isProject && hours.people.length ? <div className="workspace-table-wrap"><table className="workspace-table"><thead><tr><th>Engineer</th><th>Allocated</th><th>Logged</th><th>Share of project hours</th></tr></thead><tbody>{hours.people.map(person => <tr key={person.userId}><td><strong>{person.name}{person.userId === data.access?.userId ? ' (you)' : ''}</strong><span>{person.tasks ? `${person.tasks} current task(s)` : 'No current task allocation'}</span></td><td>{displayHours(person.assigned)}</td><td>{displayHours(person.logged)}</td><td>{hours.logged > 0 && person.logged != null ? `${(person.logged / hours.logged * 100).toFixed(1)}%` : 'Not applicable'}</td></tr>)}</tbody></table></div> : <p className="workspace-empty">{work.request?.assignedEngineers || 'No engineering assignments recorded.'}</p>}
          </>}
          {activeTab === 'documents' && <>
            <div className="workspace-panel-header"><div><h3>Project documents</h3><p>SOW, GSD, and supporting files for {work.code}.</p></div><input type="search" aria-label="Search project documents" placeholder="Search files" value={documentSearch} onChange={event => setDocumentSearch(event.target.value)} /></div>
            {!filteredDocuments.length ? <p className="workspace-empty">{projectDocuments.length ? 'No documents match your search.' : 'No documents are available for this work in your current access.'}</p> : <ul className="workspace-file-list">{filteredDocuments.map(document => <li key={document.id}><div><strong>{document.originalFileName}</strong><small>{readable(document.documentCategory)} · {Math.round((document.sizeBytes || 0) / 1024)} KB</small></div><div className="workspace-document-actions"><button type="button" onClick={event => previewDocument(document, event)}>View</button><button type="button" className="workspace-download-link" onClick={() => downloadDocument(document)} disabled={documentDownload.documentId === document.id && documentDownload.message.startsWith('Downloading') && !documentDownload.error}>Download</button></div></li>)}</ul>}
            {documentDownload.message && <p role="status" className={`workspace-download-status ${documentDownload.error ? 'error' : ''}`}>{documentDownload.message}</p>}
          </>}
          {activeTab === 'cost' && <CostAndBilling isProject={isProject} hours={hours} financial={currentFinancial} billing={currentBilling} onTeam={() => setActiveTab('team')} />}
          {activeTab === 'context' && <>
            <h3>Project context</h3><dl className="workspace-context-grid"><div><dt>Customer</dt><dd>{work.customer}</dd></div><div><dt>Status</dt><dd>{readable(work.project?.status || work.request?.status)}</dd></div>
              {isProject && <><div><dt>Project manager</dt><dd>{work.project.projectManagerName || 'Not assigned'}</dd></div><div><dt>Solution architect</dt><dd>{work.project.solutionArchitectName || 'Not assigned'}</dd></div><div><dt>Account executive</dt><dd>{work.project.accountExecutiveName || work.project.salesExecutiveName || 'Not assigned'}</dd></div><div><dt>Schedule</dt><dd>{work.project.startDate || 'Not scheduled'} to {work.project.endDate || 'Not scheduled'}</dd></div></>}
            </dl>{relatedRequests.length > 0 && <><h4>Related service requests</h4><ul className="workspace-file-list">{relatedRequests.map(request => <li key={request.requestNumber}><div><strong>{request.requestNumber} · {request.requestedFunction}</strong><small>{readable(request.status)} · {displayHours(request.requestedHours)} requested</small></div><span>{request.assignedEngineers || 'No engineers assigned'}</span></li>)}</ul></>}
          </>}
        </section>
      </>}
    </>}
    {documentPreview.item && <div className="workspace-preview-backdrop" onMouseDown={event => { if (event.target === event.currentTarget) closePreview(); }}><section className="workspace-preview-dialog" role="dialog" aria-modal="true" aria-labelledby="workspace-preview-title" onKeyDown={event => {
      if (event.key === 'Escape') closePreview();
      if (event.key === 'Tab') { const elements = [...event.currentTarget.querySelectorAll('button, iframe')]; const first = elements[0]; const last = elements[elements.length - 1]; if (event.shiftKey && window.document.activeElement === first) { event.preventDefault(); last.focus(); } else if (!event.shiftKey && window.document.activeElement === last) { event.preventDefault(); first.focus(); } }
    }}><header><h3 id="workspace-preview-title">{documentPreview.item.originalFileName}</h3><button ref={previewClose} type="button" onClick={closePreview}>Close</button></header>{documentPreview.loading && <p role="status">Preparing preview...</p>}{documentPreview.error && <div className="workspace-preview-state">{documentPreview.error}<button type="button" onClick={() => downloadDocument(documentPreview.item)}>Download file</button></div>}{documentPreview.url && <iframe sandbox="" src={documentPreview.url} title={`Preview ${documentPreview.item.originalFileName}`} />}</section></div>}
  </>;
}

function HourMetric({ title, value, detail, attention }) {
  return <article className={attention ? 'workspace-hours-overrun' : ''}><span>{title}</span><strong>{displayHours(value)}</strong><small>{detail}</small></article>;
}

function CostAndBilling({ isProject, hours, financial, billing, onTeam }) {
  const project = financial.data?.project;
  const commercial = project?.visibility?.commercial === true;
  const estimatesAvailable = commercial && !(financial.data?.sources || []).some(source => source.status !== 'healthy');
  return <>
    <h3>Understand the hours, cost, and billing</h3>
    {isProject ? <div className="workspace-notice"><strong>{hourExplanation(hours)}</strong><p>See who contributed the hours and which tasks exceeded their allocation.</p><button type="button" onClick={onTeam}>View team contribution</button></div> : <p>Project cost and invoice history become available after this request is linked to a project.</p>}
    {isProject && <>
      <h4>Project cost</h4>
      {financial.loading ? <p role="status">Loading cost context...</p> : financial.error ? <p>{financial.error}</p> : !commercial ? <p>Cost amounts are restricted for your project role. Your project manager can explain the financial budget; the team’s hours remain visible above.</p> : <>
        <dl className="workspace-context-grid"><div><dt>Recorded labor budget</dt><dd>{displayAmount(project.laborBudget)}</dd></div><div><dt>Recorded expense budget</dt><dd>{displayAmount(project.expenseBudget)}</dd></div></dl>
        <p>Verified internal labor cost and a current estimate to complete are not supplied in this view.</p>
        <details className="workspace-estimate"><summary>Explain the existing budget flag</summary>
          {!estimatesAvailable ? <p>Some cost information is unavailable. The existing budget flag cannot be verified.</p> : <><p>The existing assessment is <strong>{readable(project.budgetStatus)}</strong>. It compares a rate-based estimate of {displayAmount(project.forecastedFinalCost)} with the recorded budget, giving a forecast variance of {displayAmount(project.currentVariance)}. A negative variance means the estimate exceeds that budget.</p><p>This estimate uses a commercial rate or a rate derived from budget and hours. It is not verified internal labor cost, actual invoicing, or a current forecast of remaining work. Currency is not supplied by this cost source.</p></>}
        </details>
      </>}
      <h4>Recorded invoices</h4>
      {billing.loading ? <p role="status">Loading invoice history...</p> : billing.error ? <p>{billing.error}</p> : !billing.data?.invoices?.length ? <p>No invoice records were returned for this project. Logged hours do not mean the customer has been billed.</p> : <>
        <p>These are invoice records, including their status. They do not confirm delivery or payment. Currency is not supplied by this invoice source.</p>
        <div className="workspace-table-wrap"><table className="workspace-table"><thead><tr><th>Invoice</th><th>Date</th><th>Status</th><th>Amount (source currency)</th></tr></thead><tbody>{billing.data.invoices.map(invoice => <tr key={invoice.billingInvoiceId || invoice.invoiceId || invoice.invoiceNumber}><td>{invoice.invoiceNumber}</td><td>{invoice.invoiceDate || 'Not recorded'}</td><td>{readable(invoice.invoiceStatus)}</td><td>{displayAmount(invoice.totalAmount)}</td></tr>)}</tbody></table></div>
      </>}
    </>}
  </>;
}
