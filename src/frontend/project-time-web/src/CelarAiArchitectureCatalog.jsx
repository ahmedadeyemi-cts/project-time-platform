import { useCallback, useEffect, useMemo, useState } from 'react';
import NativeModuleAdministrationPanel from './NativeModuleAdministrationPanel.jsx';
import './celar-ai-architecture-catalog.css';

const CATALOG_URL = '/api/native-administration/011/document';

const FALLBACK_COMPONENTS = [
  {
    componentId: 'module-011-workspace',
    name: 'Celar AI Module 011 Workspace',
    layer: 'experience',
    architectureState: 'current_live',
    placement: 'pulse_platform',
    technology: 'React and ASP.NET Core',
    versionOrModel: 'Protected Test release',
    purpose: 'The deployed Celar AI experience and governed intelligence control plane.',
    includeInDiagram: true
  },
  {
    componentId: 'internal-data-intelligence',
    name: 'Permission-Scoped Internal Data Intelligence',
    layer: 'application',
    architectureState: 'current_live',
    placement: 'pulse_platform',
    technology: 'Governed APIs and PostgreSQL',
    versionOrModel: 'Migration 080',
    purpose: 'Answers authorized internal-data questions without requiring the deferred document runtime.',
    includeInDiagram: true
  },
  {
    componentId: 'module-064-router',
    name: 'Module 064 Governed Provider Router',
    layer: 'integration',
    architectureState: 'current_live',
    placement: 'module_064_boundary',
    technology: 'Celar AI capability routing',
    versionOrModel: 'Saved per-feature target order',
    purpose: 'Controls approved models, provider order, health, circuit breakers, and sanitized fallback.',
    includeInDiagram: true
  },
  {
    componentId: 'pulse-postgresql',
    name: 'Pulse PostgreSQL Data and Evidence Plane',
    layer: 'data',
    architectureState: 'current_live',
    placement: 'pulse_data_plane',
    technology: 'PostgreSQL',
    versionOrModel: 'Application-managed',
    purpose: 'Stores authoritative business data, catalog revisions, audit evidence, conversations, routing metadata, and private retrieval metadata.',
    includeInDiagram: true
  },
  {
    componentId: 'governed-document-storage',
    name: 'Governed Private Document Storage',
    layer: 'data',
    architectureState: 'current_governed',
    placement: 'pulse_data_plane',
    technology: 'Persistent private content storage',
    versionOrModel: 'Deployment-managed',
    purpose: 'Retains authorized source documents and durable processing evidence inside the approved private boundary.',
    includeInDiagram: true
  },
  {
    componentId: 'private-document-worker',
    name: 'Celar AI Private Document Worker',
    layer: 'application',
    architectureState: 'deferred_opencloud',
    placement: 'pulse_platform',
    technology: 'Containerized background worker',
    versionOrModel: 'Migration 081 not applied',
    purpose: 'Will coordinate scanning, extraction, OCR, indexing, citations, and approved document processing.',
    includeInDiagram: true
  },
  {
    componentId: 'opencloud-runtime-vm',
    name: 'OpenCloud Shared Private Runtime VM',
    layer: 'private_runtime',
    architectureState: 'planned_opencloud',
    placement: 'opencloud_shared_vm',
    technology: 'Linux with Podman or OCI containers',
    versionOrModel: 'Test/UAT target',
    purpose: 'Hosts the three isolated private runtime containers without moving the Pulse application or database.',
    includeInDiagram: true
  },
  {
    componentId: 'ollama',
    name: 'Ollama Private Inference and Embeddings',
    layer: 'private_runtime',
    architectureState: 'planned_opencloud',
    placement: 'opencloud_shared_vm',
    technology: 'Ollama',
    versionOrModel: 'Model to be selected',
    purpose: 'Will provide private inference and embeddings for restricted document-grounded workloads.',
    includeInDiagram: true
  },
  {
    componentId: 'tesseract-5',
    name: 'Tesseract 5 OCR Adapter',
    layer: 'private_runtime',
    architectureState: 'planned_opencloud',
    placement: 'opencloud_shared_vm',
    technology: 'Tesseract OCR',
    versionOrModel: '5.x',
    purpose: 'Will extract text from scanned or image-only documents when native extraction cannot be used.',
    includeInDiagram: true
  },
  {
    componentId: 'clamav',
    name: 'ClamAV Malware Scanning',
    layer: 'security',
    architectureState: 'planned_opencloud',
    placement: 'opencloud_shared_vm',
    technology: 'ClamAV',
    versionOrModel: 'Version and signature policy to be selected',
    purpose: 'Will scan each document before extraction, indexing, retrieval, or model use.',
    includeInDiagram: true
  },
  {
    componentId: 'openai-external',
    name: 'OpenAI Optional External Reasoning',
    layer: 'integration',
    architectureState: 'optional_external',
    placement: 'external_provider',
    technology: 'OpenAI through Module 064',
    versionOrModel: 'Module 064 managed',
    purpose: 'Provides eligible generic reasoning after privacy-safe de-identification when the saved capability route permits it.',
    includeInDiagram: true
  },
  {
    componentId: 'claude-external',
    name: 'Claude Optional External Reasoning',
    layer: 'integration',
    architectureState: 'optional_external',
    placement: 'external_provider',
    technology: 'Claude through Module 064',
    versionOrModel: 'Module 064 managed',
    purpose: 'Provides eligible generic reasoning after privacy-safe de-identification when the saved capability route permits it.',
    includeInDiagram: true
  },
  {
    componentId: 'ollama-gpu-scale',
    name: 'Ollama Production GPU Scale-Out',
    layer: 'operations',
    architectureState: 'future_scale',
    placement: 'future_gpu_compute',
    technology: 'GPU-capable private compute',
    versionOrModel: 'Capacity-driven future state',
    purpose: 'Moves only Ollama inference and embeddings to dedicated GPU-capable compute when production load justifies it.',
    includeInDiagram: true
  }
];

const STATE_DETAILS = {
  current_live: { label: 'Current · deployed', tone: 'current' },
  current_governed: { label: 'Current · governed', tone: 'current' },
  planned_opencloud: { label: 'Planned · OpenCloud', tone: 'planned' },
  deferred_opencloud: { label: 'Deferred · OpenCloud', tone: 'deferred' },
  optional_external: { label: 'Optional · governed', tone: 'optional' },
  future_scale: { label: 'Future scale', tone: 'future' },
  retired: { label: 'Retired', tone: 'retired' }
};

function title(value) {
  return String(value || '')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

async function readJson(response) {
  const text = await response.text();
  if (!text.trim()) return {};
  try { return JSON.parse(text); } catch { return {}; }
}

function stateDetails(component) {
  return STATE_DETAILS[component?.architectureState] || { label: title(component?.architectureState || 'not recorded'), tone: 'future' };
}

function ComponentCard({ component }) {
  const state = stateDetails(component);
  return (
    <article className={`celar-ai-component-card is-${state.tone}`}>
      <div className="celar-ai-component-card__heading">
        <span>{state.label}</span>
        <small>{title(component.layer)}</small>
      </div>
      <h4>{component.name || component.componentId}</h4>
      <p>{component.purpose || 'Purpose has not been recorded.'}</p>
      <dl>
        <div><dt>Service</dt><dd>{component.technology || 'To be selected'}</dd></div>
        <div><dt>Version / model</dt><dd>{component.versionOrModel || 'To be selected'}</dd></div>
        <div><dt>Placement</dt><dd>{title(component.placement)}</dd></div>
        {component.configurationName ? <div><dt>Update later</dt><dd>{component.configurationName}</dd></div> : null}
      </dl>
      {(component.readinessSource || component.notes) ? (
        <details>
          <summary>Architecture and readiness notes</summary>
          {component.readinessSource ? <p><strong>Readiness authority:</strong> {component.readinessSource}</p> : null}
          {component.notes ? <p>{component.notes}</p> : null}
        </details>
      ) : null}
    </article>
  );
}

function TopologyNode({ component, compact = false }) {
  const state = stateDetails(component);
  return (
    <article className={`celar-ai-runtime-node is-${state.tone}${compact ? ' is-compact' : ''}`}>
      <span>{state.label}</span>
      <strong>{component?.name || 'Component not recorded'}</strong>
      <small>{component?.technology || component?.versionOrModel || 'Technology to be selected'}</small>
    </article>
  );
}

function ArchitectureTopology({ components }) {
  const lookup = useMemo(() => new Map(components.map((component) => [component.componentId, component])), [components]);
  const resolve = (id) => lookup.get(id) || FALLBACK_COMPONENTS.find((component) => component.componentId === id);

  return (
    <section className="celar-ai-runtime-topology" aria-labelledby="celar-ai-runtime-topology-title">
      <div className="celar-ai-runtime-topology__heading">
        <div>
          <p>Deployment view · truthful lifecycle states</p>
          <h3 id="celar-ai-runtime-topology-title">Current Pulse platform and planned OpenCloud private runtime</h3>
        </div>
        <span className="celar-ai-runtime-topology__badge">No private-runtime activation performed</span>
      </div>

      <div className="celar-ai-runtime-flow" role="img" aria-label="The current Celar AI platform will connect through a private network to one planned OpenCloud Linux virtual machine containing separate Ollama, Tesseract 5, and ClamAV containers. The private document worker remains deferred until that runtime is validated.">
        <div className="celar-ai-runtime-flow__current">
          <TopologyNode component={resolve('module-011-workspace')} />
          <TopologyNode component={resolve('internal-data-intelligence')} compact />
          <TopologyNode component={resolve('module-064-router')} compact />
        </div>
        <div className="celar-ai-runtime-flow__connector" aria-hidden="true">
          <span>Approved private routing</span>
          <i>→</i>
        </div>
        <div className="celar-ai-runtime-flow__vm">
          <div className="celar-ai-runtime-flow__vm-heading">
            <TopologyNode component={resolve('opencloud-runtime-vm')} />
          </div>
          <div className="celar-ai-runtime-flow__containers">
            <TopologyNode component={resolve('ollama')} compact />
            <TopologyNode component={resolve('tesseract-5')} compact />
            <TopologyNode component={resolve('clamav')} compact />
          </div>
          <p>One VM for Test/UAT · three isolated containers · private ingress only · persistent service volumes</p>
        </div>
      </div>

      <div className="celar-ai-runtime-worker-boundary">
        <TopologyNode component={resolve('private-document-worker')} compact />
        <p>Enable only after private networking, service identities, malware signatures, OCR, inference, embeddings, and a newly processed citation-ready SOW all pass validation. Migration 081 remains absent until then.</p>
      </div>
    </section>
  );
}

export default function CelarAiArchitectureCatalog() {
  const [state, setState] = useState({
    loading: true,
    components: FALLBACK_COMPONENTS,
    revision: 0,
    canManage: false,
    error: '',
    source: 'source-controlled fallback'
  });

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const response = await fetch(CATALOG_URL, { credentials: 'include', cache: 'no-store' });
      const body = await readJson(response);
      if (!response.ok) throw new Error(body.message || body.status || `Architecture catalog returned HTTP ${response.status}.`);
      const components = Array.isArray(body.document?.components) && body.document.components.length
        ? body.document.components
        : FALLBACK_COMPONENTS;
      setState({
        loading: false,
        components,
        revision: Number(body.revision || 0),
        canManage: Boolean(body.access?.canManage) && !body.access?.isViewAs,
        error: '',
        source: body.status === 'native_document_default' ? 'governed Module 011 baseline' : 'saved Module 011 catalog'
      });
    } catch (error) {
      setState({
        loading: false,
        components: FALLBACK_COMPONENTS,
        revision: 0,
        canManage: false,
        error: error instanceof Error ? error.message : 'The saved architecture catalog could not be loaded.',
        source: 'source-controlled fallback'
      });
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const visible = useMemo(
    () => state.components.filter((component) => component?.includeInDiagram !== false && component?.architectureState !== 'retired'),
    [state.components]
  );
  const groups = useMemo(() => ({
    current: visible.filter((component) => ['current_live', 'current_governed'].includes(component.architectureState)),
    opencloud: visible.filter((component) => ['planned_opencloud', 'deferred_opencloud'].includes(component.architectureState)),
    optional: visible.filter((component) => ['optional_external', 'future_scale'].includes(component.architectureState))
  }), [visible]);

  function acceptDocument(document) {
    if (!Array.isArray(document?.components)) return;
    setState((current) => ({
      ...current,
      components: document.components,
      source: 'saved Module 011 catalog',
      error: ''
    }));
  }

  return (
    <section className="celar-ai-architecture-catalog" aria-labelledby="celar-ai-component-catalog-title">
      <div className="celar-ai-architecture-catalog__heading">
        <div>
          <p>Managed architecture register</p>
          <h3 id="celar-ai-component-catalog-title">Explain what exists now and what will be added later</h3>
          <span>Component names and non-secret planning labels are versioned in Module 011. Operational readiness still comes from the owning live service—not from a manually selected architecture state.</span>
        </div>
        <div className="celar-ai-architecture-catalog__meta">
          <strong>{visible.length} components</strong>
          <span>Revision {state.revision} · {state.source}</span>
          <button type="button" onClick={() => void load()} disabled={state.loading}>{state.loading ? 'Refreshing…' : 'Refresh catalog'}</button>
        </div>
      </div>

      {state.error ? <div className="celar-ai-architecture-catalog__notice is-warning">Saved catalog unavailable: {state.error} The source-controlled architecture baseline remains visible.</div> : null}
      <div className="celar-ai-architecture-catalog__notice">
        Recording Ollama, Tesseract 5, ClamAV, a VM name, or a future model here does not deploy or configure it. Planned items remain visually distinct until their owning readiness authority proves them operational.
      </div>

      <ArchitectureTopology components={visible} />

      <div className="celar-ai-component-group">
        <div className="celar-ai-component-group__heading"><h4>Current Pulse foundation</h4><span>{groups.current.length} components</span></div>
        <div className="celar-ai-component-grid">{groups.current.map((component) => <ComponentCard component={component} key={component.componentId} />)}</div>
      </div>

      <div className="celar-ai-component-group">
        <div className="celar-ai-component-group__heading"><h4>Planned or deferred until OpenCloud</h4><span>{groups.opencloud.length} components</span></div>
        <div className="celar-ai-component-grid">{groups.opencloud.map((component) => <ComponentCard component={component} key={component.componentId} />)}</div>
      </div>

      {groups.optional.length ? (
        <details className="celar-ai-component-optional">
          <summary>Optional external reasoning and future scale ({groups.optional.length})</summary>
          <div className="celar-ai-component-grid">{groups.optional.map((component) => <ComponentCard component={component} key={component.componentId} />)}</div>
        </details>
      ) : null}

      {state.canManage ? (
        <details className="celar-ai-architecture-manager">
          <summary>Manage Module 011 component names and architecture records</summary>
          <div className="celar-ai-architecture-manager__boundary">
            <strong>Architecture metadata only</strong>
            <span>Do not enter secrets, credentials, bearer values, connection strings, or private endpoints. Runtime configuration remains deployment-managed.</span>
          </div>
          <NativeModuleAdministrationPanel
            moduleNumber="011"
            eyebrow="Module 011 architecture governance"
            heading="Manage Celar AI component records"
            description="Rename components, record planned service or model labels, update placement and lifecycle intent, and keep an audited revision history. Saving this catalog never activates infrastructure or changes live routing."
            addRecordLabel="Add architecture component"
            onDocumentChanged={acceptDocument}
          />
        </details>
      ) : null}
    </section>
  );
}
