import { useEffect, useMemo, useState } from 'react';
import { TOYOTA_HYUNDAI_PIPELINE_SNAPSHOT } from './module006-toyota-hyundai-snapshot.js';
import './project-register-center.css';

const PAGE_SIZES = Object.freeze([10, 15, 25]);

function normalize(value) {
  return String(value ?? '').trim().toLowerCase();
}

function dateOnly(value) {
  if (!value) return 'Not set';
  const parsed = new Date(`${String(value).slice(0, 10)}T00:00:00`);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleDateString();
}

function isHistorical(item) {
  return normalize(item?.lifecycle) !== 'active' || normalize(item?.status) !== 'active';
}

function unique(items, selector) {
  return [...new Set(items.map(selector).filter(Boolean))]
    .sort((left, right) => String(left).localeCompare(String(right)));
}

function escapeXml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&apos;');
}

function downloadBlob(blob, fileName) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function buildExcelWorkbook(rows) {
  const columns = [
    ['Immutable Pipeline ID', 'pipelineEntryId'],
    ['Project ID', 'sourceProjectCode'],
    ['Customer', 'customer'],
    ['Business Unit', 'businessUnit'],
    ['USS Owner', 'ussOwner'],
    ['Project Name', 'projectName'],
    ['Quote(s)', 'quotesRaw'],
    ['Estimated Value', 'estimatedValueRaw'],
    ['Update Date', 'updateDate'],
    ['Next Review Date', 'nextReviewDate'],
    ['Latest Notes', 'latestNotes'],
    ['Status', 'status'],
    ['History Events', 'historyCount']
  ];
  const header = columns.map(([label]) => `<Cell ss:StyleID="Header"><Data ss:Type="String">${escapeXml(label)}</Data></Cell>`).join('');
  const body = rows.map((row) => `<Row>${columns.map(([, key]) => `<Cell><Data ss:Type="String">${escapeXml(row[key])}</Data></Cell>`).join('')}</Row>`).join('');
  return `<?xml version="1.0"?><?mso-application progid="Excel.Sheet"?>
<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
<Styles><Style ss:ID="Default"><Alignment ss:Vertical="Top" ss:WrapText="1"/></Style><Style ss:ID="Header"><Font ss:Bold="1" ss:Color="#FFFFFF"/><Interior ss:Color="#003B64" ss:Pattern="Solid"/></Style><Style ss:ID="Title"><Font ss:Bold="1" ss:Size="16" ss:Color="#003B64"/></Style></Styles>
<Worksheet ss:Name="Toyota Hyundai"><Table>
<Row><Cell ss:StyleID="Title"><Data ss:Type="String">US Signal — Toyota &amp; Hyundai Pipelines</Data></Cell></Row>
<Row><Cell><Data ss:Type="String">Exported ${escapeXml(new Date().toISOString())}</Data></Cell></Row>
<Row>${header}</Row>${body}</Table></Worksheet></Workbook>`;
}

export default function ProjectRegisterCenter({ legacyRoute = false }) {
  const projects = TOYOTA_HYUNDAI_PIPELINE_SNAPSHOT;
  const [searchTerm, setSearchTerm] = useState('');
  const [lifecycle, setLifecycle] = useState('active');
  const [customer, setCustomer] = useState('all');
  const [status, setStatus] = useState('all');
  const [owner, setOwner] = useState('all');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [selectedProject, setSelectedProject] = useState(null);

  useEffect(() => {
    if (legacyRoute && typeof window !== 'undefined' && window.location.hash !== '#toyota-hyundai-pipelines') {
      window.history.replaceState(window.history.state, '', '#toyota-hyundai-pipelines');
    }
  }, [legacyRoute]);

  useEffect(() => setPage(1), [searchTerm, lifecycle, customer, status, owner, pageSize]);

  const summary = useMemo(() => projects.reduce((result, item) => {
    result.total += 1;
    if (isHistorical(item)) result.historical += 1;
    else result.active += 1;
    result.historyEvents += Number(item.historyCount || 0);
    return result;
  }, { total: 0, active: 0, historical: 0, historyEvents: 0 }), [projects]);

  const customerOptions = useMemo(() => unique(projects, (item) => item.customer), [projects]);
  const statusOptions = useMemo(() => unique(projects, (item) => item.status), [projects]);
  const ownerOptions = useMemo(() => unique(projects, (item) => item.ussOwner), [projects]);

  const filteredProjects = useMemo(() => {
    const search = normalize(searchTerm);
    return projects.filter((item) => {
      const historical = isHistorical(item);
      if (lifecycle === 'active' && historical) return false;
      if (lifecycle === 'historical' && !historical) return false;
      if (customer !== 'all' && normalize(item.customer) !== normalize(customer)) return false;
      if (status !== 'all' && normalize(item.status) !== normalize(status)) return false;
      if (owner !== 'all' && normalize(item.ussOwner) !== normalize(owner)) return false;
      if (!search) return true;
      return [
        item.pipelineEntryId,
        item.sourceProjectCode,
        item.customer,
        item.businessUnit,
        item.ussOwner,
        item.projectName,
        item.quotesRaw,
        item.estimatedValueRaw,
        item.latestNotes,
        item.status
      ].join(' ').toLowerCase().includes(search);
    });
  }, [customer, lifecycle, owner, projects, searchTerm, status]);

  const pageCount = Math.max(Math.ceil(filteredProjects.length / pageSize), 1);
  const safePage = Math.min(page, pageCount);
  const pageStart = (safePage - 1) * pageSize;
  const visibleProjects = filteredProjects.slice(pageStart, pageStart + pageSize);

  function exportExcel() {
    const workbook = buildExcelWorkbook(filteredProjects);
    downloadBlob(
      new Blob([workbook], { type: 'application/vnd.ms-excel;charset=utf-8' }),
      `US-Signal-Toyota-Hyundai-Pipelines-${new Date().toISOString().slice(0, 10)}.xls`
    );
  }

  return (
    <section
      className="project-register-center projectpulse-module-standard"
      data-module="006"
      data-module-name="Toyota & Hyundai Pipelines"
      data-canonical-route="toyota-hyundai-pipelines"
      data-project-register-contract="reviewed-beck-workbook-snapshot-v1"
    >
      <header className="project-register-hero">
        <div>
          <p className="eyebrow">MODULE 006 · TOYOTA &amp; HYUNDAI DELIVERY PIPELINE</p>
          <h2>Toyota &amp; Hyundai Pipelines</h2>
          <p>
            A bounded, searchable register built from the reviewed Beck active and archived exports. It intentionally excludes ordinary ProjectPulse projects and preserves the workbook project code alongside an immutable pipeline identity.
          </p>
        </div>
        <div className="project-register-hero-actions">
          <button type="button" className="secondary-action" onClick={exportExcel}>Export Excel</button>
          <button type="button" className="secondary-action" onClick={() => window.print()}>Print / Save PDF</button>
          <a className="primary-action project-register-link-button" href="#work-register">Manage linked projects</a>
        </div>
      </header>

      {legacyRoute ? (
        <div className="project-register-banner warning">
          A compatibility Module 006 link was used. The canonical route is <code>#toyota-hyundai-pipelines</code>; <code>#psa-modules</code> and <code>#project-register</code> redirect here.
        </div>
      ) : null}

      <div className="project-register-banner">
        <strong>Workbook boundary:</strong> only Toyota and Hyundai records are included. The unrelated Turion row and ambiguous “No Updates” placeholder rows are excluded from this reviewed snapshot.
      </div>

      <div className="project-register-summary" aria-label="Toyota & Hyundai Pipelines summary">
        <article><span>Total projects</span><strong>{summary.total}</strong><small>{filteredProjects.length} match the current filters</small></article>
        <article><span>Active</span><strong>{summary.active}</strong><small>Current pipeline records</small></article>
        <article><span>Archived / closed</span><strong>{summary.historical}</strong><small>Retained for historical review</small></article>
        <article><span>Workbook log events</span><strong>{summary.historyEvents}</strong><small>Historical events referenced by these records</small></article>
      </div>

      <div className="project-register-toolbar">
        <label className="wide">
          Search
          <input
            type="search"
            value={searchTerm}
            onChange={(event) => setSearchTerm(event.target.value)}
            placeholder="Project ID, customer, business unit, owner, project, quote, or note…"
          />
        </label>
        <label>
          Register view
          <select value={lifecycle} onChange={(event) => setLifecycle(event.target.value)}>
            <option value="active">Active</option>
            <option value="historical">Archived / historical</option>
            <option value="all">All Toyota / Hyundai</option>
          </select>
        </label>
        <label>
          Customer
          <select value={customer} onChange={(event) => setCustomer(event.target.value)}>
            <option value="all">Toyota and Hyundai</option>
            {customerOptions.map((value) => <option value={value} key={value}>{value}</option>)}
          </select>
        </label>
        <label>
          Status
          <select value={status} onChange={(event) => setStatus(event.target.value)}>
            <option value="all">All statuses</option>
            {statusOptions.map((value) => <option value={value} key={value}>{value}</option>)}
          </select>
        </label>
        <label>
          USS Owner
          <select value={owner} onChange={(event) => setOwner(event.target.value)}>
            <option value="all">All owners</option>
            {ownerOptions.map((value) => <option value={value} key={value}>{value}</option>)}
          </select>
        </label>
      </div>

      <div className="project-register-totalbar">
        <span><strong>{filteredProjects.length}</strong> matching projects</span>
        <span><strong>{pageStart + (visibleProjects.length ? 1 : 0)}–{pageStart + visibleProjects.length}</strong> displayed</span>
        <label>Rows per page <select value={pageSize} onChange={(event) => setPageSize(Number(event.target.value))}>{PAGE_SIZES.map((size) => <option value={size} key={size}>{size}</option>)}</select></label>
      </div>

      <div className="project-register-table-wrap" role="region" aria-label="Paginated Toyota and Hyundai pipeline table" tabIndex="0">
        <table className="project-register-table">
          <thead>
            <tr>
              <th>Project ID</th>
              <th>Customer / Business Unit</th>
              <th>Project / Quote</th>
              <th>USS Owner</th>
              <th>Update / Review</th>
              <th>Status</th>
              <th>Latest Notes</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            {visibleProjects.map((item) => (
              <tr key={item.pipelineEntryId} data-pipeline-entry-id={item.pipelineEntryId}>
                <td><strong>{item.sourceProjectCode}</strong><small title={item.pipelineEntryId}>Immutable: {item.pipelineEntryId.slice(0, 8)}…</small></td>
                <td><strong>{item.customer}</strong><small>{item.businessUnit || 'Business unit not set'}</small></td>
                <td><strong>{item.projectName || 'Project name not set'}</strong><small>Quote(s): {item.quotesRaw || 'Not linked'}</small><small>Estimated: {item.estimatedValueRaw || 'Not set'}</small></td>
                <td><strong>{item.ussOwner || 'Not assigned'}</strong><small>{item.historyCount} historical event(s)</small></td>
                <td><small>Updated: {dateOnly(item.updateDate)}</small><small>Next review: {dateOnly(item.nextReviewDate)}</small><small>Imported: {dateOnly(item.lastImported)}</small></td>
                <td><span className={`project-register-state ${isHistorical(item) ? 'historical' : 'active'}`}>{item.status || (isHistorical(item) ? 'Historical' : 'Active')}</span></td>
                <td className="project-register-notes-cell">{item.latestNotes || 'No current note was supplied.'}</td>
                <td><button type="button" className="project-register-row-action" onClick={() => setSelectedProject(item)}>View pipeline detail</button></td>
              </tr>
            ))}
            {!visibleProjects.length ? <tr><td colSpan="8" className="project-register-empty-cell">No Toyota or Hyundai pipeline records match the current filters.</td></tr> : null}
          </tbody>
        </table>
      </div>

      <nav className="project-register-pagination" aria-label="Toyota and Hyundai pipeline pages">
        <button type="button" disabled={safePage <= 1} onClick={() => setPage(1)}>First</button>
        <button type="button" disabled={safePage <= 1} onClick={() => setPage((value) => Math.max(value - 1, 1))}>Previous</button>
        <span>Page <strong>{safePage}</strong> of <strong>{pageCount}</strong></span>
        <button type="button" disabled={safePage >= pageCount} onClick={() => setPage((value) => Math.min(value + 1, pageCount))}>Next</button>
        <button type="button" disabled={safePage >= pageCount} onClick={() => setPage(pageCount)}>Last</button>
      </nav>

      <div className="project-register-governance-grid">
        <article><span>Source evidence</span><strong>Reviewed workbook snapshot</strong><p>The page no longer reads ordinary projects from <code>/api/work-register/overview</code>. A later governed import phase will persist the complete workbook history and reviewer decisions.</p></article>
        <article><span>Task ownership</span><strong>Module 055C remains authoritative</strong><p>Assigned Project Managers manage tasks only after the pipeline record is explicitly linked to a ProjectPulse project. Module 006 does not create a competing task repository.</p></article>
      </div>

      {selectedProject ? (
        <div className="project-register-drawer-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setSelectedProject(null); }}>
          <aside className="project-register-drawer" role="dialog" aria-modal="true" aria-label="Toyota and Hyundai pipeline detail">
            <header><div><p className="eyebrow">TOYOTA &amp; HYUNDAI PIPELINE DETAIL</p><h3>{selectedProject.projectName}</h3><p>{selectedProject.customer} · {selectedProject.sourceProjectCode}</p></div><button type="button" className="secondary-action" onClick={() => setSelectedProject(null)}>Close</button></header>
            <section className="project-register-detail-summary">
              <article><span>Status</span><strong>{selectedProject.status}</strong></article>
              <article><span>USS Owner</span><strong>{selectedProject.ussOwner || 'Not assigned'}</strong></article>
              <article><span>Quote(s)</span><strong>{selectedProject.quotesRaw || 'Not linked'}</strong></article>
              <article><span>History</span><strong>{selectedProject.historyCount} event(s)</strong></article>
            </section>
            <section className="project-register-detail-section"><h4>Immutable identity</h4><dl><div><dt>Pipeline entry ID</dt><dd><code>{selectedProject.pipelineEntryId}</code></dd></div><div><dt>Workbook project ID</dt><dd>{selectedProject.sourceProjectCode}</dd></div><div><dt>First seen</dt><dd>{dateOnly(selectedProject.firstSeen)}</dd></div><div><dt>Last imported</dt><dd>{dateOnly(selectedProject.lastImported)}</dd></div></dl></section>
            <section className="project-register-detail-section"><h4>Latest update</h4><p>{selectedProject.latestNotes || 'No current note was supplied.'}</p><p><strong>Updated:</strong> {dateOnly(selectedProject.updateDate)} · <strong>Next review:</strong> {dateOnly(selectedProject.nextReviewDate)}</p></section>
            <section className="project-register-detail-section"><h4>Historical context</h4><p>This record references {selectedProject.historyCount} append-only workbook log event(s). The full event-by-event import, row fingerprint, actor attribution, and administrator review workflow remain separately evidence-gated and must not be fabricated from ordinary project records.</p></section>
            <footer><a className="primary-action project-register-link-button" href="#work-register">Open Module 055C</a><button type="button" className="secondary-action" onClick={() => setSelectedProject(null)}>Close detail</button></footer>
          </aside>
        </div>
      ) : null}
    </section>
  );
}
