import { useEffect, useMemo, useState } from 'react';
import {
  TOYOTA_HYUNDAI_PIPELINE_EVENTS,
  TOYOTA_HYUNDAI_PIPELINE_PROJECTS,
  TOYOTA_HYUNDAI_SNAPSHOT_METADATA
} from './toyota-hyundai-pipeline-snapshot.js';
import './project-register-center.css';

const PAGE_SIZES = Object.freeze([10, 15, 25]);

function normalize(value) {
  return String(value ?? '').trim().toLowerCase();
}

function labelize(value) {
  const text = String(value ?? '').trim();
  if (!text) return 'Not set';
  return text
    .replaceAll('_', ' ')
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function money(value) {
  const amount = Number(value ?? 0);
  return Number.isFinite(amount)
    ? new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 2 }).format(amount)
    : '$0.00';
}

function dateOnly(value) {
  const text = String(value ?? '').trim();
  return text || 'Not set';
}

function unique(items, selector) {
  return [...new Set(items.map(selector).filter(Boolean))]
    .sort((left, right) => String(left).localeCompare(String(right)));
}

function xmlEscape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&apos;');
}

function spreadsheetCell(value, type = 'String') {
  const clean = type === 'Number' ? Number(value || 0) : value;
  return `<Cell><Data ss:Type="${type}">${xmlEscape(clean)}</Data></Cell>`;
}

function worksheetXml(name, headers, rows) {
  const headerCells = headers.map((header) => spreadsheetCell(header)).join('');
  const body = rows.map((row) => (
    `<Row>${row.map((cell) => spreadsheetCell(cell?.value, cell?.type || 'String')).join('')}</Row>`
  )).join('');
  return `<Worksheet ss:Name="${xmlEscape(name)}"><Table><Row ss:StyleID="Header">${headerCells}</Row>${body}</Table></Worksheet>`;
}

function buildSpreadsheetWorkbook(projects, events) {
  const now = new Date().toISOString();
  const active = projects.filter((project) => project.lifecycle === 'active');
  const historical = projects.filter((project) => project.lifecycle === 'historical');
  const projectIds = new Set(projects.map((project) => project.sourceProjectCode));
  const scopedEvents = events.filter((event) => projectIds.has(event.sourceProjectCode));
  const projectRows = (items) => items.map((project) => [
    { value: project.pipelineEntryId },
    { value: project.sourceProjectCode },
    { value: project.customer },
    { value: project.businessUnit },
    { value: project.owner },
    { value: project.projectName },
    { value: project.quoteText },
    { value: project.estimatedValueRaw },
    { value: project.estimatedValueAmount, type: 'Number' },
    { value: project.updateDate },
    { value: project.nextReviewDate },
    { value: project.latestNotes },
    { value: project.status },
    { value: project.eventCount, type: 'Number' },
    { value: project.firstSeen },
    { value: project.lastImported }
  ]);

  const quoteRows = projects.flatMap((project) => (
    project.quoteNumbers.length
      ? project.quoteNumbers.map((quoteNumber) => [
          { value: project.pipelineEntryId },
          { value: project.sourceProjectCode },
          { value: project.customer },
          { value: quoteNumber },
          { value: project.quoteText },
          { value: 'Pending governed Module 026 SELL match' }
        ])
      : [[
          { value: project.pipelineEntryId },
          { value: project.sourceProjectCode },
          { value: project.customer },
          { value: '' },
          { value: project.quoteText },
          { value: 'No quote number parsed' }
        ]]
  ));

  const eventRows = scopedEvents.map((event) => [
    { value: event.eventId },
    { value: event.sourceProjectCode },
    { value: event.customer },
    { value: event.businessUnit },
    { value: event.owner },
    { value: event.projectName },
    { value: event.quoteText },
    { value: event.updateDate },
    { value: event.nextReviewDate },
    { value: event.notes },
    { value: event.importedOn },
    { value: event.sourceSheet },
    { value: event.sourceRow, type: 'Number' }
  ]);

  const summaryRows = [
    [{ value: 'Report' }, { value: 'US Signal Toyota & Hyundai Pipelines' }],
    [{ value: 'Exported at (UTC)' }, { value: now }],
    [{ value: 'Source as of' }, { value: TOYOTA_HYUNDAI_SNAPSHOT_METADATA.sourceAsOf }],
    [{ value: 'Filtered projects' }, { value: projects.length, type: 'Number' }],
    [{ value: 'Active projects' }, { value: active.length, type: 'Number' }],
    [{ value: 'Archived / closed projects' }, { value: historical.length, type: 'Number' }],
    [{ value: 'Logs / audit events' }, { value: scopedEvents.length, type: 'Number' }],
    [{ value: 'Snapshot contract' }, { value: TOYOTA_HYUNDAI_SNAPSHOT_METADATA.contractVersion }],
    [{ value: 'Snapshot SHA-256' }, { value: TOYOTA_HYUNDAI_SNAPSHOT_METADATA.snapshotId }]
  ];

  const evidenceRows = [
    ...TOYOTA_HYUNDAI_SNAPSHOT_METADATA.sourceFiles.map((fileName) => [
      { value: 'Source workbook' },
      { value: fileName },
      { value: 'Reviewed workbook snapshot' }
    ]),
    ...TOYOTA_HYUNDAI_SNAPSHOT_METADATA.excludedRecords.map((record) => [
      { value: 'Excluded source record' },
      { value: record.sourceProjectCode },
      { value: record.reason }
    ])
  ];

  return `<?xml version="1.0"?>
<?mso-application progid="Excel.Sheet"?>
<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet"
 xmlns:o="urn:schemas-microsoft-com:office:office"
 xmlns:x="urn:schemas-microsoft-com:office:excel"
 xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
 <Styles>
  <Style ss:ID="Default" ss:Name="Normal"><Alignment ss:Vertical="Top" ss:WrapText="1"/><Font ss:FontName="Aptos" ss:Size="10"/></Style>
  <Style ss:ID="Header"><Font ss:Bold="1" ss:Color="#FFFFFF"/><Interior ss:Color="#003B5C" ss:Pattern="Solid"/><Alignment ss:Vertical="Center" ss:WrapText="1"/></Style>
 </Styles>
 ${worksheetXml('Summary', ['Field', 'Value'], summaryRows)}
 ${worksheetXml('Active Projects', ['Immutable ID', 'Project ID', 'Customer', 'Business Unit', 'USS Owner', 'Project Name', 'Quote(s)', 'Estimated Value', 'Estimated Value Numeric', 'Update Date', 'Next Review Date', 'Latest Notes', 'Status', 'History Events', 'First Seen', 'Last Imported'], projectRows(active))}
 ${worksheetXml('Archived and Closed', ['Immutable ID', 'Project ID', 'Customer', 'Business Unit', 'USS Owner', 'Project Name', 'Quote(s)', 'Estimated Value', 'Estimated Value Numeric', 'Update Date', 'Next Review Date', 'Latest Notes', 'Status', 'History Events', 'First Seen', 'Last Imported'], projectRows(historical))}
 ${worksheetXml('Logs and Audit', ['Event ID', 'Project ID', 'Customer', 'Business Unit', 'USS Owner', 'Project Name', 'Quote(s)', 'Update Date', 'Next Review Date', 'Notes', 'Imported On', 'Source Sheet', 'Source Row'], eventRows)}
 ${worksheetXml('Quotes and SELL', ['Immutable ID', 'Project ID', 'Customer', 'Parsed Quote', 'Original Quote Text', 'SELL Match Status'], quoteRows)}
 ${worksheetXml('Export Evidence', ['Evidence Type', 'Identifier', 'Details'], evidenceRows)}
</Workbook>`;
}

function downloadTextFile(content, fileName, type) {
  const blob = new Blob([content], { type });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function Pagination({ page, pageCount, pageSize, total, onPage, onPageSize }) {
  if (!total) return null;
  const start = ((page - 1) * pageSize) + 1;
  const end = Math.min(page * pageSize, total);
  return (
    <nav className="project-register-pagination" aria-label="Toyota and Hyundai pipeline pagination">
      <span>Showing {start}–{end} of {total}</span>
      <label>
        Rows
        <select value={pageSize} onChange={(event) => onPageSize(Number(event.target.value))}>
          {PAGE_SIZES.map((size) => <option value={size} key={size}>{size}</option>)}
        </select>
      </label>
      <button type="button" onClick={() => onPage(Math.max(1, page - 1))} disabled={page <= 1}>Previous</button>
      <span>Page {page} of {pageCount}</span>
      <button type="button" onClick={() => onPage(Math.min(pageCount, page + 1))} disabled={page >= pageCount}>Next</button>
    </nav>
  );
}

export default function ProjectRegisterCenter({ legacyRoute = false }) {
  const projects = TOYOTA_HYUNDAI_PIPELINE_PROJECTS;
  const events = TOYOTA_HYUNDAI_PIPELINE_EVENTS;
  const [searchTerm, setSearchTerm] = useState('');
  const [lifecycle, setLifecycle] = useState('active');
  const [customer, setCustomer] = useState('all');
  const [status, setStatus] = useState('all');
  const [owner, setOwner] = useState('all');
  const [pageSize, setPageSize] = useState(10);
  const [page, setPage] = useState(1);
  const [selectedProject, setSelectedProject] = useState(null);

  useEffect(() => {
    if (legacyRoute && typeof window !== 'undefined' && window.location.hash !== '#toyota-hyundai-pipelines') {
      window.history.replaceState(window.history.state, '', '#toyota-hyundai-pipelines');
    }
  }, [legacyRoute]);

  useEffect(() => {
    setPage(1);
  }, [searchTerm, lifecycle, customer, status, owner, pageSize]);

  const summary = useMemo(() => ({
    total: projects.length,
    active: projects.filter((project) => project.lifecycle === 'active').length,
    historical: projects.filter((project) => project.lifecycle === 'historical').length,
    events: events.length
  }), [events.length, projects]);

  const customerOptions = useMemo(() => unique(projects, (project) => project.customer), [projects]);
  const statusOptions = useMemo(() => unique(projects, (project) => project.status), [projects]);
  const ownerOptions = useMemo(() => unique(projects, (project) => project.owner), [projects]);

  const filteredProjects = useMemo(() => {
    const search = normalize(searchTerm);
    return projects.filter((project) => {
      if (lifecycle !== 'all' && project.lifecycle !== lifecycle) return false;
      if (customer !== 'all' && project.customer !== customer) return false;
      if (status !== 'all' && project.status !== status) return false;
      if (owner !== 'all' && project.owner !== owner) return false;
      if (!search) return true;
      return [
        project.sourceProjectCode,
        project.customer,
        project.businessUnit,
        project.owner,
        project.projectName,
        project.quoteText,
        project.latestNotes,
        project.status,
        ...project.quoteNumbers
      ].join(' ').toLowerCase().includes(search);
    });
  }, [customer, lifecycle, owner, projects, searchTerm, status]);

  const filteredProjectIds = useMemo(
    () => new Set(filteredProjects.map((project) => project.sourceProjectCode)),
    [filteredProjects]
  );
  const filteredEvents = useMemo(
    () => events.filter((event) => filteredProjectIds.has(event.sourceProjectCode)),
    [events, filteredProjectIds]
  );
  const filteredValue = useMemo(
    () => filteredProjects.reduce((total, project) => total + Number(project.estimatedValueAmount || 0), 0),
    [filteredProjects]
  );
  const pageCount = Math.max(1, Math.ceil(filteredProjects.length / pageSize));
  const currentPage = Math.min(page, pageCount);
  const visibleProjects = filteredProjects.slice((currentPage - 1) * pageSize, currentPage * pageSize);
  const selectedEvents = useMemo(() => (
    selectedProject
      ? events
          .filter((event) => event.sourceProjectCode === selectedProject.sourceProjectCode)
          .sort((left, right) => String(right.updateDate).localeCompare(String(left.updateDate)))
      : []
  ), [events, selectedProject]);

  function exportExcel() {
    const date = new Date().toISOString().slice(0, 10);
    const workbook = buildSpreadsheetWorkbook(filteredProjects, filteredEvents);
    downloadTextFile(
      workbook,
      `US-Signal-Toyota-Hyundai-Pipelines-${date}.xls`,
      'application/vnd.ms-excel;charset=utf-8'
    );
  }

  function printPdf() {
    window.print();
  }

  return (
    <section
      className="project-register-center projectpulse-module-standard"
      data-module="006"
      data-module-name="Toyota & Hyundai Pipelines"
      data-canonical-route="toyota-hyundai-pipelines"
      data-project-register-contract="reviewed-workbook-snapshot-v1"
    >
      <header className="project-register-hero">
        <div>
          <p className="eyebrow">MODULE 006 · GOVERNED TOYOTA & HYUNDAI PIPELINE</p>
          <h2>Toyota &amp; Hyundai Pipelines</h2>
          <p>
            Review the Toyota and Hyundai delivery pipeline supplied in the approved Beck workbooks. Ordinary ProjectPulse projects are intentionally excluded from this workspace.
          </p>
          <small>
            Source snapshot as of {TOYOTA_HYUNDAI_SNAPSHOT_METADATA.sourceAsOf} · {summary.events} historical updates preserved · contract {TOYOTA_HYUNDAI_SNAPSHOT_METADATA.contractVersion}
          </small>
        </div>
        <div className="project-register-hero-actions">
          <button type="button" className="secondary-action" onClick={exportExcel}>Export Excel</button>
          <button type="button" className="secondary-action" onClick={printPdf}>Print / Save PDF</button>
          <a className="primary-action project-register-link-button" href="#work-register">Open Module 055C</a>
        </div>
      </header>

      {legacyRoute ? (
        <div className="project-register-banner warning">
          A compatibility Module 006 link was used. The canonical route is <code>#toyota-hyundai-pipelines</code>.
        </div>
      ) : null}

      <div className="project-register-banner">
        This release uses a reviewed source-controlled workbook snapshot so the Test workspace displays the intended Toyota and Hyundai records immediately. Database-backed imports, row-level approval decisions, and immutable export evidence remain a separate governed phase.
      </div>

      <div className="project-register-summary" aria-label="Toyota & Hyundai Pipelines summary">
        <article><span>Total projects</span><strong>{summary.total}</strong><small>{filteredProjects.length} match the current filters</small></article>
        <article><span>Active</span><strong>{summary.active}</strong><small>Current workbook records</small></article>
        <article><span>Archived / closed</span><strong>{summary.historical}</strong><small>Historical workbook records</small></article>
        <article><span>Filtered estimated value</span><strong>{money(filteredValue)}</strong><small>{filteredEvents.length} matching log events</small></article>
      </div>

      <div className="project-register-toolbar">
        <label className="wide">
          Search
          <input
            type="search"
            value={searchTerm}
            onChange={(event) => setSearchTerm(event.target.value)}
            placeholder="Project ID, project, customer, owner, quote, or note…"
          />
        </label>
        <label>
          Register view
          <select value={lifecycle} onChange={(event) => setLifecycle(event.target.value)}>
            <option value="active">Active</option>
            <option value="historical">Archived / historical</option>
            <option value="all">All Toyota & Hyundai records</option>
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
            {statusOptions.map((value) => <option value={value} key={value}>{labelize(value)}</option>)}
          </select>
        </label>
        <label>
          USS owner
          <select value={owner} onChange={(event) => setOwner(event.target.value)}>
            <option value="all">All USS owners</option>
            {ownerOptions.map((value) => <option value={value} key={value}>{value}</option>)}
          </select>
        </label>
      </div>

      <Pagination
        page={currentPage}
        pageCount={pageCount}
        pageSize={pageSize}
        total={filteredProjects.length}
        onPage={setPage}
        onPageSize={setPageSize}
      />

      <div className="project-register-table-wrap">
        <table className="project-register-table">
          <thead>
            <tr>
              <th>Project</th>
              <th>Customer / Business Unit</th>
              <th>Status</th>
              <th>USS Owner</th>
              <th>Dates</th>
              <th>Quote / Estimated Value</th>
              <th>Latest Update</th>
              <th>History</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            {visibleProjects.map((project) => (
              <tr key={project.pipelineEntryId} data-pipeline-entry-id={project.pipelineEntryId}>
                <td>
                  <strong>{project.sourceProjectCode}</strong>
                  <small>{project.projectName || 'Unnamed project'}</small>
                  <small className="project-register-immutable-id">Immutable ID: {project.pipelineEntryId}</small>
                </td>
                <td>
                  <strong>{project.customer}</strong>
                  <small>{project.businessUnit || 'Business unit not set'}</small>
                </td>
                <td>
                  <span className={`project-register-state ${project.lifecycle}`}>
                    {project.lifecycle === 'active' ? 'Active' : 'Historical'}
                  </span>
                  <small>{labelize(project.status)}</small>
                </td>
                <td><strong>{project.owner || 'Not assigned'}</strong></td>
                <td>
                  <small>Updated: {dateOnly(project.updateDate)}</small>
                  <small>Next review: {dateOnly(project.nextReviewDate)}</small>
                  <small>First seen: {dateOnly(project.firstSeen)}</small>
                </td>
                <td>
                  <small>Quote(s): {project.quoteText || 'Not set'}</small>
                  <small>Estimate: {project.estimatedValueRaw || 'Not set'}</small>
                </td>
                <td><p className="project-register-latest-note">{project.latestNotes || 'No current note supplied.'}</p></td>
                <td>
                  <strong>{project.eventCount}</strong>
                  <small>preserved update(s)</small>
                </td>
                <td>
                  <button type="button" className="project-register-row-action" onClick={() => setSelectedProject(project)}>
                    View history
                  </button>
                  <a href="#work-register" className="project-register-inline-link">Manage tasks in 055C</a>
                </td>
              </tr>
            ))}
            {!visibleProjects.length ? (
              <tr><td colSpan="9" className="project-register-empty-cell">No Toyota or Hyundai records match the current filters.</td></tr>
            ) : null}
          </tbody>
        </table>
      </div>

      <Pagination
        page={currentPage}
        pageCount={pageCount}
        pageSize={pageSize}
        total={filteredProjects.length}
        onPage={setPage}
        onPageSize={setPageSize}
      />

      <div className="project-register-governance-grid">
        <article>
          <span>Scope control</span>
          <strong>Toyota and Hyundai only</strong>
          <p>The Turion row and the two ambiguous “No Updates” archived placeholders are excluded and listed in export evidence for administrator review.</p>
        </article>
        <article>
          <span>Task authority</span>
          <strong>Module 055C remains authoritative</strong>
          <p>Assigned Project Managers add and maintain tasks in Module 055C after an administrator maps the workbook pipeline record to its authoritative ProjectPulse project.</p>
        </article>
      </div>

      {selectedProject ? (
        <div className="project-register-drawer-backdrop" role="presentation" onMouseDown={(event) => {
          if (event.target === event.currentTarget) setSelectedProject(null);
        }}>
          <aside className="project-register-drawer" role="dialog" aria-modal="true" aria-label="Toyota & Hyundai Pipelines history">
            <header>
              <div>
                <p className="eyebrow">TOYOTA & HYUNDAI PIPELINES HISTORY</p>
                <h3>{selectedProject.projectName}</h3>
                <p>{selectedProject.customer} · {selectedProject.sourceProjectCode}</p>
              </div>
              <button type="button" className="secondary-action" onClick={() => setSelectedProject(null)}>Close</button>
            </header>

            <section className="project-register-detail-summary">
              <article><span>Status</span><strong>{labelize(selectedProject.status)}</strong></article>
              <article><span>USS Owner</span><strong>{selectedProject.owner || 'Not assigned'}</strong></article>
              <article><span>Quote(s)</span><strong>{selectedProject.quoteText || 'Not set'}</strong></article>
              <article><span>History</span><strong>{selectedEvents.length} events</strong></article>
            </section>

            <div className="project-register-readonly-notice">
              Immutable pipeline ID: <code>{selectedProject.pipelineEntryId}</code>. Workbook history is read-only in this source snapshot. Project and task mutations remain in Module 055C.
            </div>

            <section className="project-register-detail-section">
              <h4>Latest register note</h4>
              <p>{selectedProject.latestNotes || 'No current note supplied.'}</p>
            </section>

            <section className="project-register-detail-section">
              <h4>Historical updates and audit context</h4>
              <div className="project-register-timeline">
                {selectedEvents.map((event) => (
                  <article key={event.eventId}>
                    <header>
                      <strong>{dateOnly(event.updateDate)}</strong>
                      <span>{event.owner || 'Owner not set'} · {event.sourceSheet} row {event.sourceRow}</span>
                    </header>
                    <p>{event.notes || 'No note supplied.'}</p>
                    <small>
                      Next review: {dateOnly(event.nextReviewDate)} · Quote(s): {event.quoteText || 'Not set'} · Imported: {dateOnly(event.importedOn)}
                    </small>
                  </article>
                ))}
              </div>
            </section>

            <footer>
              <a className="primary-action project-register-link-button" href="#work-register">Open Module 055C task management</a>
              <button type="button" className="secondary-action" onClick={() => setSelectedProject(null)}>Close detail</button>
            </footer>
          </aside>
        </div>
      ) : null}
    </section>
  );
}
