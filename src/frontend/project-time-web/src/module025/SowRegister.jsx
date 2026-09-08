import { useEffect, useMemo, useRef, useState } from 'react';
import USSignalLogo from '../enterprise/USSignalLogo.jsx';
import './sow-gsd-workspace.css';
import './sow-register.css';

async function request(url, options = {}) {
  const response = await fetch(url, {
    credentials: 'include', ...options,
    headers: { Accept: 'application/json', ...(options.body ? { 'Content-Type': 'application/json' } : {}), ...(options.headers || {}) }
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload.message || `Request failed (${response.status}).`);
    error.payload = payload;
    throw error;
  }
  return payload;
}

function when(value) {
  if (!value) return 'Not yet';
  try { return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)); }
  catch { return String(value); }
}

function status(value) {
  return ({
    draft: 'Draft', review_ready: 'Ready for SA review', confirmed: 'Confirmed', archived: 'Archived',
    blocked: 'Blocked — not sent', queued: 'Queued — awaiting processing', publishing: 'Publishing — awaiting verification',
    published: 'Verified in SELL', awaiting_sell: 'Awaiting SELL confirmation', sending: 'Email submission in progress',
    provider_accepted: 'Email provider accepted', suppressed: 'Email suppressed — no delivery',
    failed: 'Failed — review required', needs_reconciliation: 'Outcome unknown — reconciliation required'
  })[value] || 'Not submitted';
}

function Metric({ label, value }) {
  return <article className="m025-metric"><span>{label}</span><strong>{value ?? 0}</strong></article>;
}

const reportFields = [
  ['uniqueSowsGenerated', 'Unique SOWs generated'], ['uniqueSowsSent', 'Unique SOWs verified in SELL'],
  ['successfulGenerationRuns', 'Successful generation runs'], ['releasedVersions', 'Retained document versions'],
  ['successfulVersionSubmissions', 'Verified version submissions'], ['blockedSubmissions', 'Blocked requests']
];

export default function SowRegister() {
  const [bootstrap, setBootstrap] = useState(null);
  const [filters, setFilters] = useState({ ownerUserId: '', search: '', fromDate: '', toDate: '' });
  const [page, setPage] = useState(1);
  const [report, setReport] = useState(null);
  const [loading, setLoading] = useState(false);
  const [readError, setReadError] = useState('');
  const [notice, setNotice] = useState(null);
  const [selectedId, setSelectedId] = useState('');
  const [versionPage, setVersionPage] = useState(1);
  const [detail, setDetail] = useState(null);
  const [history, setHistory] = useState(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [actionBusy, setActionBusy] = useState('');
  const [refresh, setRefresh] = useState(0);
  const selection = useRef(selectedId);
  selection.current = selectedId;

  useEffect(() => {
    const controller = new AbortController();
    request('/api/module025/sow-gsd/bootstrap', { signal: controller.signal })
      .then((payload) => {
        setBootstrap(payload);
        setFilters((current) => ({ ...current, ownerUserId: payload.access?.isManager || payload.access?.isAdministrator ? '' : payload.currentUser?.userId || '' }));
      })
      .catch((error) => { if (!controller.signal.aborted) setReadError(error.message); });
    return () => controller.abort();
  }, []);

  const query = useMemo(() => {
    const value = new URLSearchParams({ page: String(page) });
    Object.entries(filters).forEach(([key, item]) => { if (item.trim()) value.set(key, item.trim()); });
    return value.toString();
  }, [filters, page]);

  useEffect(() => {
    if (!bootstrap) return undefined;
    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      setLoading(true);
      try {
        const payload = await request(`/api/module025/sow-register?${query}`, { signal: controller.signal });
        if (!controller.signal.aborted) { setReport(payload); setReadError(''); }
      } catch (error) {
        if (!controller.signal.aborted) setReadError(error.message);
      } finally { if (!controller.signal.aborted) setLoading(false); }
    }, filters.search ? 250 : 0);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [bootstrap, query, refresh, filters.search]);

  useEffect(() => {
    if (!selectedId) { setDetail(null); setHistory(null); return undefined; }
    const controller = new AbortController();
    setDetail((current) => current?.engagementId === selectedId ? current : null);
    setHistory((current) => current?.engagementId === selectedId ? current : null);
    Promise.all([
      request(`/api/module025/sow-gsd/${selectedId}/versions?page=${versionPage}`, { signal: controller.signal }),
      request(`/api/module025/sow-gsd/${selectedId}/history`, { signal: controller.signal })
    ]).then(([versions, events]) => {
      if (!controller.signal.aborted) { setDetail(versions); setHistory(events); }
    }).catch((error) => { if (!controller.signal.aborted) setReadError(error.message); });
    return () => controller.abort();
  }, [selectedId, versionPage, refresh]);

  const running = (detail?.versions || []).some((version) => (version.submissions || []).some((submission) =>
    ['queued', 'publishing'].includes(submission.sellStatus) || ['queued', 'sending'].includes(submission.mailStatus)));
  useEffect(() => {
    if (!running) return undefined;
    const timer = window.setInterval(() => setRefresh((value) => value + 1), 5000);
    return () => window.clearInterval(timer);
  }, [running]);

  const totals = useMemo(() => Object.fromEntries(reportFields.map(([key]) => [key,
    (report?.statistics || []).reduce((sum, row) => sum + Number(row[key] || 0), 0)])), [report]);

  function changeFilter(key, value) {
    setFilters((current) => ({ ...current, [key]: value }));
    setPage(1); setSelectedId(''); setVersionPage(1);
  }

  async function runAction(action, versionId) {
    if (!detail?.canWrite || actionBusy) return;
    const id = detail.engagementId;
    if (action === 'sell' && !window.confirm(`Push ${detail.engagementNumber}'s selected confirmed version to SELL? The assigned Inside Sales Representative, Account Executive and Solution Architect will be notified only after both documents are verified. No additional SOW record will be created.`)) return;
    setActionBusy(action);
    setNotice(null);
    try {
      const payload = await request(`/api/module025/sow-gsd/${id}/${action}`, {
        method: 'POST', body: JSON.stringify({ expectedRevision: detail.revision, ...(versionId ? { versionId } : {}) })
      });
      if (selection.current === id) setNotice({ tone: 'info', text: payload.message || `Version ${payload.version?.versionNumber ?? ''} retained. Repeated downloads will use these same files.` });
    } catch (error) {
      if (selection.current === id) setNotice({ tone: 'warning', text: error.message + (error.payload?.submissionId ? ` Tracking ID: ${error.payload.submissionId}` : '') });
    } finally { setActionBusy(''); setRefresh((value) => value + 1); }
  }

  async function olderHistory() {
    if (!selectedId || !history?.nextBeforeEventId || historyLoading) return;
    const id = selectedId;
    setHistoryLoading(true);
    try {
      const payload = await request(`/api/module025/sow-gsd/${id}/history?beforeEventId=${history.nextBeforeEventId}`);
      setHistory((current) => current?.engagementId === id ? { ...payload, events: [...current.events, ...payload.events] } : current);
    } catch (error) { if (selection.current === id) setReadError(error.message); }
    finally { setHistoryLoading(false); }
  }

  const canRelease = detail?.canWrite && detail?.status === 'confirmed' && detail?.isActive && !detail?.currentContentReleased;
  const csvQuery = new URLSearchParams(query);
  csvQuery.set('format', 'csv');
  csvQuery.delete('page');

  return (
    <section className="m025-workspace m025-register" data-module025-sow-register="true">
      <header className="m025-header">
        <div className="m025-header__identity"><USSignalLogo size="large" /><div>
          <p className="m025-eyebrow">Module 025 · Retained records</p>
          <h1>SOW Register &amp; SELL</h1>
          <p>One SOW identity. Reviewed versions, verified submissions and an append-only history.</p>
        </div></div>
        <div className="m025-header__actions">
          <button type="button" className="m025-button m025-button--secondary" onClick={() => setRefresh((value) => value + 1)} disabled={loading}>Refresh</button>
          {report ? <a className="m025-button m025-button--primary" href={`/api/module025/sow-register?${csvQuery}`}>Export SA report (.csv)</a> : null}
        </div>
      </header>

      <section className="m025-filters m025-register-filters" aria-label="Report filters">
        <label className="m025-field"><span>Solution Architect</span><select value={filters.ownerUserId} onChange={(event) => changeFilter('ownerUserId', event.target.value)}>
          <option value="">All permitted Solution Architects</option>
          {(bootstrap?.solutionArchitects || []).map((person) => <option key={person.userId} value={person.userId}>{person.displayName}</option>)}
        </select></label>
        <label className="m025-field"><span>Customer or SOW number</span><input value={filters.search} onChange={(event) => changeFilter('search', event.target.value)} placeholder="Search retained records" /></label>
        <label className="m025-field"><span>From date (UTC)</span><input type="date" value={filters.fromDate} onChange={(event) => changeFilter('fromDate', event.target.value)} /></label>
        <label className="m025-field"><span>Through date (UTC, inclusive)</span><input type="date" value={filters.toDate} onChange={(event) => changeFilter('toDate', event.target.value)} /></label>
      </section>
      {readError ? <div className="m025-notice m025-notice--critical" role="alert"><strong>Register needs attention</strong><p>{readError}</p></div> : null}
      {notice ? <div className={`m025-notice m025-notice--${notice.tone}`} role="status"><p>{notice.text}</p></div> : null}
      <section className="m025-metrics" aria-label="SA production metrics">{reportFields.map(([key, label]) => <Metric key={key} label={label} value={totals[key]} />)}</section>
      <p className="m025-register-explanation">Downloads do not increase SOW counts. A revised version stays under its original SOW number. Only a verified SELL receipt counts as sent; queued, blocked or uncertain requests do not. Metrics cover all matching records, not only this page.</p>

      <div className="m025-register-table-wrap">
        <table className="m025-register-table"><caption>Solution Architect output for the selected period</caption>
          <thead><tr><th scope="col">Solution Architect</th><th scope="col">Unique generated</th><th scope="col">Unique sent</th><th scope="col">Version submissions</th><th scope="col">Generation runs</th><th scope="col">Failed generations</th></tr></thead>
          <tbody>{(report?.statistics || []).map((row) => <tr key={row.ownerUserId}><th scope="row">{row.ownerDisplayName}</th><td>{row.uniqueSowsGenerated}</td><td>{row.uniqueSowsSent}</td><td>{row.successfulVersionSubmissions}</td><td>{row.successfulGenerationRuns}</td><td>{row.failedGenerationRuns}</td></tr>)}</tbody>
        </table>
      </div>
      <div className="m025-register-table-wrap" aria-busy={loading}>
        <table className="m025-register-table"><caption>{loading ? 'Refreshing records…' : `${report?.totalRecords ?? 0} matching SOW records · ${report?.runtimeEnvironment || 'governed environment'}`}</caption>
          <thead><tr><th scope="col">SOW record</th><th scope="col">Customer</th><th scope="col">Solution Architect</th><th scope="col">Working status</th><th scope="col">Latest version</th><th scope="col">SELL status</th></tr></thead>
          <tbody>{(report?.records || []).map((row) => <tr key={row.engagementId} aria-selected={selectedId === row.engagementId}>
            <th scope="row"><button type="button" className="m025-button m025-button--secondary" onClick={() => { setSelectedId(row.engagementId); setVersionPage(1); setNotice(null); }}>{row.engagementNumber}</button></th>
            <td>{row.customerName || 'Customer not selected'}</td><td>{row.ownerDisplayName}</td><td>{status(row.status)}</td><td>{row.latestVersionNumber ? `v${row.latestVersionNumber}` : 'Not released'}</td><td>{status(row.lastSellStatus)}</td>
          </tr>)}</tbody>
        </table>
        {!loading && report && !report.records?.length ? <p>No SOW records match these filters.</p> : null}
      </div>
      <div className="m025-review-actions"><button type="button" className="m025-button m025-button--secondary" disabled={page === 1 || loading} onClick={() => { setPage(page - 1); setSelectedId(''); }}>Previous records</button><span>Page {page}</span><button type="button" className="m025-button m025-button--secondary" disabled={!report?.hasMore || loading} onClick={() => { setPage(page + 1); setSelectedId(''); }}>Next records</button></div>

      {selectedId && !detail ? <p role="status">Loading retained versions and history…</p> : null}
      {detail ? <section className="m025-section" aria-label="Selected SOW history">
        <div className="m025-section-heading"><div><h2>{detail.engagementNumber} · {detail.customerName}</h2></div><p>SA: {detail.ownerDisplayName} · Working revision {detail.revision} · {status(detail.status)}</p></div>
        <p>Use SOW Authoring to reopen and edit this record. Reconfirmation retains the next changed version; previous versions remain downloadable.</p>
        {!detail.sellReadiness?.ready ? <div className="m025-notice m025-notice--warning" role="status"><strong>Automatic SELL publication is not enabled</strong><p>{detail.sellReadiness?.message}</p><small>{detail.sellReadiness?.diagnosticCode}</small></div> : null}
        {canRelease ? <button type="button" className="m025-button m025-button--primary" disabled={Boolean(actionBusy)} onClick={() => runAction('versions')}>{actionBusy === 'versions' ? 'Retaining files…' : 'Retain confirmed version'}</button> : null}
        {!detail.versions?.length ? <p>This record is tracked. Document versions appear after SA confirmation. Older confirmed records require one explicit retention step; no historical files are invented.</p> : null}
        <div className="m025-register-versions">{(detail.versions || []).map((version) => {
          const submissions = (version.submissions || []).filter((item) => item.environment === detail.runtimeEnvironment);
          const published = submissions.some((item) => item.sellStatus === 'published');
          const eligible = detail.canWrite && detail.status === 'confirmed' && detail.isActive && detail.currentContentReleased && version.versionId === detail.latestVersionId;
          return <article key={version.versionId} className="m025-register-version">
            <header><h3>Version {version.versionNumber}</h3><span>Retained {when(version.createdAt)} · Source revision {version.sourceRevision}</span></header>
            <div className="m025-review-actions">
              <a className="m025-button m025-button--primary" href={`/api/module025/sow-gsd/${detail.engagementId}/versions/${version.versionId}/sow.docx`}>Download SOW v{version.versionNumber}</a>
              <a className="m025-button m025-button--primary" href={`/api/module025/sow-gsd/${detail.engagementId}/versions/${version.versionId}/gsd.xlsx`}>Download GSD v{version.versionNumber}</a>
              <button type="button" className="m025-button m025-button--secondary" disabled={!eligible || !detail.sellReadiness?.ready || published || Boolean(actionBusy)} onClick={() => runAction('sell', version.versionId)}>{published ? 'Already verified in SELL' : actionBusy === 'sell' ? 'Registering submission…' : 'Push to SELL'}</button>
            </div>
            <p>First SOW issuance: {when(version.firstSowServedAt)} · First GSD issuance: {when(version.firstGsdServedAt)}</p>
            <details><summary>File integrity</summary><p className="m025-register-hash">SOW SHA-256: {version.sowSha256}</p><p className="m025-register-hash">GSD SHA-256: {version.gsdSha256}</p></details>
            {submissions.map((submission) => <div key={submission.submissionId} className="m025-register-submission"><strong>{status(submission.sellStatus)}</strong><p>Email: {status(submission.mailStatus)}</p><p>Tracking ID: {submission.submissionId}{submission.sellRecordId ? ` · SELL record: ${submission.sellRecordId}` : ''}</p><small>Requested {when(submission.requestedAt)} · Verified {when(submission.verifiedAt)}{submission.diagnosticCode ? ` · ${submission.diagnosticCode}` : ''}</small></div>)}
          </article>;
        })}</div>
        <div className="m025-review-actions"><button type="button" className="m025-button m025-button--secondary" disabled={versionPage === 1} onClick={() => setVersionPage(versionPage - 1)}>Newer versions</button><span>Version page {versionPage}</span><button type="button" className="m025-button m025-button--secondary" disabled={!detail.hasMore} onClick={() => setVersionPage(versionPage + 1)}>Older versions</button></div>
        <h3>Immutable event history</h3>
        <ol className="m025-register-timeline">{(history?.events || []).map((event) => <li key={event.eventId}><strong>{event.summary}</strong><p>{when(event.createdAt)} · Revision {event.revision} · {event.eventType.replaceAll('_', ' ')}</p><small>Actor: {event.actorDisplayName || event.actorUserId}{event.generationSnapshotSha256 ? ' · Generation snapshot retained' : ''}</small></li>)}</ol>
        {history?.nextBeforeEventId ? <button type="button" className="m025-button m025-button--secondary" disabled={historyLoading} onClick={olderHistory}>{historyLoading ? 'Loading…' : 'Load older events'}</button> : null}
        <p className="m025-register-explanation">{detail.legacyGenerationNotice} File issuance records mean the server returned the file, not that a local save completed. Email-provider acceptance is not confirmation of mailbox delivery.</p>
      </section> : null}
    </section>
  );
}
