import { useEffect, useMemo, useState } from 'react';
import './pulse-ai-private-document-pipeline-workbench.css';

const WORKSPACES = Object.freeze([
  { id: 'readiness', label: 'Pipeline Readiness', description: 'Security, extraction, OCR, embedding, and index gates' },
  { id: 'inventory', label: 'Authorized Inventory', description: 'Permission-filtered SOW, GSD, design, and supporting documents' },
  { id: 'processing', label: 'Processing Preview', description: 'Extraction, citations, chunks, and index projection without persistence' }
]);

const INITIAL_FILTERS = Object.freeze({
  projectCode: '',
  category: '',
  extractionStatus: '',
  limit: '100'
});

function asArray(value) {
  return Array.isArray(value) ? value : [];
}

function title(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function formatDate(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

function formatBytes(value) {
  const size = Number(value ?? 0);
  if (!Number.isFinite(size) || size <= 0) return '0 bytes';
  const units = ['bytes', 'KB', 'MB', 'GB'];
  const index = Math.min(Math.floor(Math.log(size) / Math.log(1024)), units.length - 1);
  const amount = size / (1024 ** index);
  return `${amount.toFixed(index === 0 ? 0 : amount >= 10 ? 1 : 2)} ${units[index]}`;
}

function buildQuery(path, values) {
  const url = new URL(path, window.location.origin);
  Object.entries(values).forEach(([key, value]) => {
    const clean = String(value ?? '').trim();
    if (clean) url.searchParams.set(key, clean);
  });
  return `${url.pathname}${url.search}`;
}

async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(payload.message || `${response.url || 'Request'} returned HTTP ${response.status}.`);
  }
  return payload;
}

async function getJson(path) {
  return readJson(await fetch(path, {
    method: 'GET',
    cache: 'no-store',
    headers: { Accept: 'application/json' }
  }));
}

function Status({ value }) {
  const normalized = String(value || 'unknown');
  const ready = normalized.includes('ready') || normalized.includes('admitted') || normalized.includes('supported');
  const blocked = normalized.includes('blocked') || normalized.includes('failed') || normalized.includes('missing');
  return <span className={`pulse-ai-doc-status ${ready ? 'is-ready' : blocked ? 'is-blocked' : 'is-partial'}`}>{title(normalized)}</span>;
}

function BooleanValue({ value }) {
  return <span className={value ? 'pulse-ai-doc-yes' : 'pulse-ai-doc-no'}>{value ? 'Yes' : 'No'}</span>;
}

function KeyValueGrid({ values }) {
  return (
    <dl className="pulse-ai-doc-key-value-grid">
      {Object.entries(values ?? {}).filter(([, value]) => value !== undefined).map(([key, value]) => (
        <div key={key}>
          <dt>{title(key)}</dt>
          <dd>{typeof value === 'boolean' ? <BooleanValue value={value} /> : String(value ?? 'Not recorded')}</dd>
        </div>
      ))}
    </dl>
  );
}

function ListBlock({ heading, values, empty = 'Nothing recorded.' }) {
  const rows = asArray(values);
  return (
    <section className="pulse-ai-doc-list-block">
      <h5>{heading}</h5>
      {rows.length ? <ul>{rows.map((value, index) => <li key={`${heading}-${index}`}>{String(value)}</li>)}</ul> : <p>{empty}</p>}
    </section>
  );
}

function FullEvidence({ payload }) {
  if (!payload) return null;
  return (
    <details className="pulse-ai-doc-full-evidence">
      <summary>View complete structured evidence</summary>
      <pre>{JSON.stringify(payload, null, 2)}</pre>
    </details>
  );
}

function ReadinessView({ payload }) {
  const readiness = payload?.readiness;
  if (!readiness) return null;
  const stages = asArray(payload?.processingStages);
  const locked = payload?.locked ?? {};
  return (
    <div className="pulse-ai-doc-result-stack">
      <section className="pulse-ai-doc-result-hero">
        <div>
          <p className="pulse-ai-doc-eyebrow">Private pipeline readiness</p>
          <h4>{title(readiness.status)}</h4>
          <p>Readiness is evaluated without returning storage paths, source text, private chunks, embeddings, credentials, or model prompts.</p>
        </div>
        <Status value={readiness.status} />
      </section>

      <KeyValueGrid values={{
        databaseConfigured: readiness.databaseConfigured,
        documentSchemaAvailable: readiness.documentSchemaAvailable,
        storageRootConfigured: readiness.storageRootConfigured,
        storageRootExists: readiness.storageRootExists,
        extractionPreviewEnabled: readiness.extractionPreviewEnabled,
        malwareScanAttested: readiness.malwareScanAttested,
        malwareScannerMode: readiness.malwareScannerMode,
        nativePdfExtractionAvailable: readiness.nativePdfExtractionAvailable,
        nativeOpenXmlExtractionAvailable: readiness.nativeOpenXmlExtractionAvailable,
        nativeTextExtractionAvailable: readiness.nativeTextExtractionAvailable,
        ocrEndpointConfigured: readiness.ocrEndpointConfigured,
        privateEmbeddingEndpointConfigured: readiness.privateEmbeddingEndpointConfigured,
        privateVectorIndexConfigured: readiness.privateVectorIndexConfigured,
        authorizedDocumentCount: readiness.authorizedDocumentCount,
        supportedDocumentCount: readiness.supportedDocumentCount,
        extractionReadyDocumentCount: readiness.extractionReadyDocumentCount,
        generatedAt: formatDate(readiness.generatedAt)
      }} />

      <div className="pulse-ai-doc-two-column">
        <ListBlock heading="Ready capabilities" values={readiness.readyCapabilities} />
        <ListBlock heading="Current blockers" values={readiness.blockers} empty="No blocker was reported." />
      </div>

      <section className="pulse-ai-doc-card">
        <div className="pulse-ai-doc-card-heading">
          <div><h5>Processing stages</h5><p>Each stage remains independently gated and auditable.</p></div>
          <span>{stages.length} stages</span>
        </div>
        <div className="pulse-ai-doc-stage-grid">
          {stages.map((stage) => (
            <article key={stage.stage}>
              <span>{stage.order}</span>
              <div><h6>{title(stage.stage)}</h6><Status value={stage.state} /><p>{stage.detail}</p></div>
            </article>
          ))}
        </div>
      </section>

      <section className="pulse-ai-doc-card">
        <h5>Locked production-changing behavior</h5>
        <KeyValueGrid values={locked} />
      </section>
      <FullEvidence payload={payload} />
    </div>
  );
}

function InventoryView({ payload, onPreview }) {
  const documents = asArray(payload?.documents);
  const summary = payload?.summary ?? {};
  return (
    <div className="pulse-ai-doc-result-stack">
      <section className="pulse-ai-doc-result-hero">
        <div>
          <p className="pulse-ai-doc-eyebrow">Authorized private inventory</p>
          <h4>{documents.length} document{documents.length === 1 ? '' : 's'} in effective-user scope</h4>
          <p>Only active engineering-visible documents from authorized projects are returned. Storage paths and source text remain private.</p>
        </div>
        <Status value={payload?.status} />
      </section>
      <KeyValueGrid values={{
        documentCount: summary.documentCount,
        supportedCount: summary.supportedCount,
        storedFileAvailableCount: summary.storedFileAvailableCount,
        admittedForPreviewCount: summary.admittedForPreviewCount,
        existingContextReadyCount: summary.existingContextReadyCount,
        sowCount: summary.sowCount,
        gsdCount: summary.gsdCount
      }} />
      {documents.length ? (
        <div className="pulse-ai-doc-inventory-grid">
          {documents.map((document) => (
            <article key={document.documentId}>
              <div className="pulse-ai-doc-inventory-topline">
                <strong>{String(document.documentCategory || document.documentType || 'other').toUpperCase()}</strong>
                <Status value={document.productionAdmissionReady ? 'admitted_for_preview' : 'not_admitted'} />
              </div>
              <h5>{document.originalFileName}</h5>
              <p>{document.projectCode} — {document.projectName}</p>
              <small>{document.customerName} · {formatBytes(document.sizeBytes)} · Uploaded {formatDate(document.uploadedAt)}</small>
              <KeyValueGrid values={{
                extension: document.extension,
                extractionStatus: document.extractionStatus,
                engineeringVisible: document.engineeringVisible,
                aiTimesheetContextEnabled: document.aiTimesheetContextEnabled,
                supportedByNativePipeline: document.supportedByNativePipeline,
                storedFileExists: document.storedFileExists,
                storedPathConfined: document.storedPathConfined,
                existingContextSummaryReady: document.existingContextSummaryReady,
                accessScope: document.accessScope
              }} />
              <ListBlock heading="Admission blockers" values={document.blockers} empty="No current admission blocker was reported." />
              <button type="button" onClick={() => onPreview(document.documentId)}>Open processing preview</button>
            </article>
          ))}
        </div>
      ) : <p className="pulse-ai-doc-empty">No authorized document matched the current filters.</p>}
      <FullEvidence payload={payload} />
    </div>
  );
}

function ProcessingView({ payload }) {
  const preview = payload?.preview;
  if (!preview) return null;
  const document = preview.document ?? {};
  const extraction = preview.extraction ?? {};
  const safety = extraction.safety ?? {};
  const chunks = asArray(preview.chunks);
  const indexProjection = asArray(preview.indexProjection);
  return (
    <div className="pulse-ai-doc-result-stack">
      <section className="pulse-ai-doc-result-hero">
        <div>
          <p className="pulse-ai-doc-eyebrow">Private processing preview</p>
          <h4>{document.originalFileName || 'Document'}</h4>
          <p>{document.projectCode} — {document.projectName} · {String(document.documentCategory || 'other').toUpperCase()}</p>
        </div>
        <Status value={preview.status} />
      </section>

      <section className="pulse-ai-doc-card">
        <h5>Safety and admission</h5>
        <KeyValueGrid values={{
          safetyStatus: safety.status,
          extension: safety.extension,
          detectedFormat: safety.detectedFormat,
          extensionAllowed: safety.extensionAllowed,
          signatureMatchesExtension: safety.signatureMatchesExtension,
          sizeWithinLimit: safety.sizeWithinLimit,
          pathConfined: safety.pathConfined,
          isRegularFile: safety.isRegularFile,
          reparsePointDetected: safety.reparsePointDetected,
          macroEnabledFormat: safety.macroEnabledFormat,
          archiveBombRiskDetected: safety.archiveBombRiskDetected,
          malwareScanAttested: safety.malwareScanAttested,
          malwareScannerMode: safety.malwareScannerMode,
          fileSizeBytes: formatBytes(safety.fileSizeBytes),
          sourceSha256: safety.sourceSha256
        }} />
        <div className="pulse-ai-doc-two-column">
          <ListBlock heading="Safety blockers" values={safety.blockers} empty="No safety blocker was reported." />
          <ListBlock heading="Safety warnings" values={safety.warnings} empty="No safety warning was reported." />
        </div>
      </section>

      <section className="pulse-ai-doc-card">
        <h5>Extraction evidence</h5>
        <KeyValueGrid values={{
          status: extraction.status,
          detectedFormat: extraction.detectedFormat,
          extractionMethod: extraction.extractionMethod,
          pageCount: extraction.pageCount,
          sectionCount: extraction.sectionCount,
          characterCount: extraction.characterCount,
          estimatedTokenCount: extraction.estimatedTokenCount,
          ocrRequired: extraction.ocrRequired,
          sourceSha256: extraction.sourceSha256,
          generatedAt: formatDate(extraction.generatedAt)
        }} />
        <div className="pulse-ai-doc-section-table-wrap">
          <table>
            <thead><tr><th>Anchor</th><th>Title</th><th>Page / sheet</th><th>Characters</th><th>Text checksum</th></tr></thead>
            <tbody>
              {asArray(extraction.sections).map((section) => (
                <tr key={`${section.sectionIndex}-${section.anchor}`}>
                  <td><code>{section.anchor}</code></td>
                  <td>{section.title}</td>
                  <td>{section.pageNumber ? `Page ${section.pageNumber}` : section.sheetName || 'Section'}</td>
                  <td>{section.characterCount}</td>
                  <td><code>{section.textSha256}</code></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="pulse-ai-doc-card">
        <div className="pulse-ai-doc-card-heading">
          <div><h5>Citation-preserving chunks</h5><p>Only metadata, anchors, sizes, and checksums are displayed. Private chunk text is not returned.</p></div>
          <span>{chunks.length} chunks</span>
        </div>
        <div className="pulse-ai-doc-chunk-grid">
          {chunks.slice(0, 30).map((chunk) => (
            <article key={chunk.chunkId}>
              <code>{chunk.chunkId}</code>
              <h6>{chunk.title}</h6>
              <p>{chunk.anchor} · {chunk.pageNumber ? `Page ${chunk.pageNumber}` : chunk.sheetName || 'Section'}</p>
              <small>{chunk.characterCount} characters · ~{chunk.estimatedTokenCount} tokens</small>
              <small>Text SHA-256: {chunk.textSha256}</small>
            </article>
          ))}
        </div>
        {chunks.length > 30 ? <p className="pulse-ai-doc-empty">Showing the first 30 chunk evidence records. Complete metadata is available below.</p> : null}
      </section>

      <section className="pulse-ai-doc-card">
        <div className="pulse-ai-doc-card-heading">
          <div><h5>Permission-scoped index projection</h5><p>Vector generation and index writes remain locked.</p></div>
          <span>{indexProjection.length} records</span>
        </div>
        <div className="pulse-ai-doc-section-table-wrap">
          <table>
            <thead><tr><th>Chunk</th><th>Project</th><th>Classification</th><th>Citation</th><th>Embedding</th><th>Index</th></tr></thead>
            <tbody>
              {indexProjection.slice(0, 50).map((record) => (
                <tr key={record.chunkId}>
                  <td><code>{record.chunkId}</code></td>
                  <td>{record.projectCode}</td>
                  <td>{title(record.classification)}</td>
                  <td>{record.citationAnchor}</td>
                  <td>{title(record.embeddingStatus)}</td>
                  <td>{title(record.indexStatus)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <div className="pulse-ai-doc-two-column">
        <ListBlock heading="Version-authority questions" values={preview.versionAuthorityQuestions} empty="No version-authority conflict was reported." />
        <ListBlock heading="Production blockers" values={preview.productionBlockers} empty="No production blocker was reported." />
      </div>
      <FullEvidence payload={payload} />
    </div>
  );
}

export default function PulseAiPrivateDocumentPipelineWorkbench() {
  const [activeWorkspace, setActiveWorkspace] = useState('readiness');
  const [readiness, setReadiness] = useState(null);
  const [inventory, setInventory] = useState(null);
  const [processing, setProcessing] = useState(null);
  const [filters, setFilters] = useState({ ...INITIAL_FILTERS });
  const [loading, setLoading] = useState('');
  const [error, setError] = useState('');

  const workspace = useMemo(
    () => WORKSPACES.find((item) => item.id === activeWorkspace) ?? WORKSPACES[0],
    [activeWorkspace]
  );

  async function loadReadiness() {
    setLoading('readiness'); setError('');
    try {
      const payload = await getJson('/api/celar-ai/v1/documents/pipeline/readiness');
      setReadiness(payload);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Private document pipeline readiness could not be loaded.');
    } finally {
      setLoading('');
    }
  }

  async function loadInventory(event) {
    event?.preventDefault();
    setLoading('inventory'); setError('');
    try {
      const payload = await getJson(buildQuery('/api/celar-ai/v1/documents/inventory', filters));
      setInventory(payload);
      setActiveWorkspace('inventory');
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Authorized document inventory could not be loaded.');
    } finally {
      setLoading('');
    }
  }

  async function loadProcessing(documentId) {
    setLoading('processing'); setError('');
    try {
      const payload = await getJson(`/api/celar-ai/v1/documents/${encodeURIComponent(documentId)}/processing-preview`);
      setProcessing(payload);
      setActiveWorkspace('processing');
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Private document processing preview could not be loaded.');
    } finally {
      setLoading('');
    }
  }

  useEffect(() => {
    void loadReadiness();
  }, []);

  function updateFilter(field, value) {
    setFilters((current) => ({ ...current, [field]: value }));
  }

  return (
    <section className="pulse-ai-doc-workbench" data-pulse-ai-private-document-pipeline="v1">
      <header className="pulse-ai-doc-header">
        <div>
          <p className="pulse-ai-doc-eyebrow">Module 011 · Private knowledge pipeline</p>
          <h2>Private Document Processing & Permission-Aware Index</h2>
          <p>
            Inspect the secure path from an authorized SOW, GSD, design, spreadsheet, or supporting document to private extraction, citations, chunks, and an index projection. This phase performs no database, embedding, vector-index, OCR, provider, or deployment mutation.
          </p>
        </div>
        <div className="pulse-ai-doc-header-actions">
          <button type="button" onClick={loadReadiness} disabled={loading === 'readiness'}>
            {loading === 'readiness' ? 'Checking…' : 'Refresh readiness'}
          </button>
          <button type="button" onClick={() => loadInventory()} disabled={loading === 'inventory'}>
            {loading === 'inventory' ? 'Loading…' : 'Load authorized inventory'}
          </button>
        </div>
      </header>

      <div className="pulse-ai-doc-privacy-banner">
        <strong>Private by default</strong>
        <span>Raw source text, chunks, storage paths, embeddings, and model prompts are not returned to this browser and are never sent to Claude or OpenAI by these endpoints.</span>
      </div>

      <form className="pulse-ai-doc-filters" onSubmit={loadInventory}>
        <label>Project code<input value={filters.projectCode} onChange={(event) => updateFilter('projectCode', event.target.value)} placeholder="Optional exact project code" /></label>
        <label>Document category<select value={filters.category} onChange={(event) => updateFilter('category', event.target.value)}><option value="">All authorized categories</option><option value="sow">SOW</option><option value="gsd">GSD</option><option value="architecture">Architecture</option><option value="design">Design</option><option value="order">Order</option><option value="quote">Quote</option><option value="proposal">Proposal</option><option value="other">Other</option></select></label>
        <label>Extraction status<input value={filters.extractionStatus} onChange={(event) => updateFilter('extractionStatus', event.target.value)} placeholder="Optional status" /></label>
        <label>Maximum records<select value={filters.limit} onChange={(event) => updateFilter('limit', event.target.value)}><option>25</option><option>50</option><option>100</option><option>250</option><option>500</option></select></label>
        <button type="submit" disabled={loading === 'inventory'}>{loading === 'inventory' ? 'Loading…' : 'Apply inventory filters'}</button>
      </form>

      <nav className="pulse-ai-doc-tabs" aria-label="Private document pipeline workspaces">
        {WORKSPACES.map((item) => (
          <button type="button" key={item.id} className={activeWorkspace === item.id ? 'is-active' : ''} onClick={() => setActiveWorkspace(item.id)}>
            <strong>{item.label}</strong><span>{item.description}</span>
          </button>
        ))}
      </nav>

      <section className="pulse-ai-doc-panel">
        <div className="pulse-ai-doc-panel-heading"><p className="pulse-ai-doc-eyebrow">Current workspace</p><h3>{workspace.label}</h3><p>{workspace.description}</p></div>
        {error ? <div className="pulse-ai-doc-error" role="alert">{error}</div> : null}
        {loading ? <div className="pulse-ai-doc-loading" role="status">Loading private pipeline evidence…</div> : null}
        {activeWorkspace === 'readiness' ? <ReadinessView payload={readiness} /> : null}
        {activeWorkspace === 'inventory' ? <InventoryView payload={inventory} onPreview={loadProcessing} /> : null}
        {activeWorkspace === 'processing' ? (processing ? <ProcessingView payload={processing} /> : <p className="pulse-ai-doc-empty">Select an authorized document from the inventory to create a processing preview.</p>) : null}
      </section>
    </section>
  );
}
