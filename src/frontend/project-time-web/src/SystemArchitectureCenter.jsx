import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './system-architecture-center.css';
import './projectpulse-module-standard.css';

function sessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function requestHeaders(authSession) {
  const token = sessionToken(authSession);
  return token ? {
    Authorization: `Bearer ${token}`,
    'X-ProjectPulse-Session': token,
    'X-Project-Pulse-Session': token,
    'X-Session-Token': token
  } : {};
}

async function readJson(path, authSession) {
  const response = await fetch(path, {
    method: 'GET',
    credentials: 'include',
    cache: 'no-store',
    headers: requestHeaders(authSession)
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok) throw new Error(payload?.message ?? `System Architecture request returned HTTP ${response.status}.`);
  return payload;
}

async function readLegacyArchitecture(authSession) {
  const [overview, dependencies] = await Promise.all([
    readJson('/api/system-architecture/overview', authSession),
    readJson('/api/system-architecture/dependency-status', authSession)
  ]);
  return {
    ...overview,
    platform: { provider: 'legacy_runtime', displayName: 'Runtime-managed platform', environment: overview?.scope?.environment, region: 'not_reported', adapter: 'legacy_architecture_contract', workloadKind: 'application' },
    runtime: { releaseSha: overview?.runtimeRevision, applicationVersion: overview?.contractVersion },
    dependencies: dependencies?.dependencies ?? [],
    legend: [],
    externalDataFlows: [],
    moduleApiRelationships: [],
    regions: [],
    redundancy: { observedReplicaCount: 0, status: 'not_reported', replicas: [], message: 'Provider-neutral redundancy evidence is not available from the legacy contract.' },
    apiAppendix: [],
    export: null
  };
}

function title(value) {
  return String(value ?? 'unknown').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function dateTime(value) {
  if (!value) return 'Not observed';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Not observed' : parsed.toLocaleString();
}

function tone(value) {
  const normalized = String(value ?? '').toLowerCase();
  if (['healthy', 'active', 'live', 'configured', 'observed'].includes(normalized)) return 'healthy';
  if (['failed', 'unavailable', 'critical'].includes(normalized)) return 'critical';
  if (['warning', 'degraded', 'not_configured', 'not_observed'].includes(normalized)) return 'warning';
  return 'neutral';
}

function Status({ value }) {
  return <span className={`system-architecture-status ${tone(value)}`}>{title(value)}</span>;
}

export default function SystemArchitectureCenter({ authSession }) {
  const [state, setState] = useState({ loading: true, architecture: null, error: '', fallback: false });
  const [selectedModule, setSelectedModule] = useState('all');

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const architecture = await readJson('/api/platform-operations/architecture', authSession);
      setState({ loading: false, architecture, error: '', fallback: false });
    } catch (primaryError) {
      try {
        const architecture = await readLegacyArchitecture(authSession);
        setState({ loading: false, architecture, error: primaryError?.message ?? '', fallback: true });
      } catch (fallbackError) {
        setState({ loading: false, architecture: null, error: fallbackError?.message ?? 'System Architecture is unavailable.', fallback: false });
      }
    }
  }, [authSession]);

  useEffect(() => {
    void load();
  }, [load]);

  const architecture = state.architecture ?? {};
  const platform = architecture.platform ?? {};
  const runtime = architecture.runtime ?? {};
  const layers = useMemo(() => [...(architecture.layers ?? [])].sort((left, right) => Number(left.order) - Number(right.order)), [architecture.layers]);
  const nodesByLayer = useMemo(() => {
    const result = new Map();
    (architecture.nodes ?? []).forEach((node) => {
      if (!result.has(node.layer)) result.set(node.layer, []);
      result.get(node.layer).push(node);
    });
    return result;
  }, [architecture.nodes]);
  const nodeNames = useMemo(() => new Map((architecture.nodes ?? []).map((node) => [node.id, node.name])), [architecture.nodes]);
  const relationships = architecture.moduleApiRelationships ?? [];
  const visibleRelationships = selectedModule === 'all' ? relationships : relationships.filter((item) => item.moduleCode === selectedModule);
  const apiCount = architecture.apiAppendix?.length ?? relationships.reduce((total, item) => total + Number(item.apiCount ?? 0), 0);

  function exportArchitecture() {
    const path = architecture.export?.html ?? '/api/platform-operations/architecture/export';
    const anchor = document.createElement('a');
    anchor.href = path;
    anchor.download = '';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  }

  return (
    <section id="system-architecture" className="panel system-architecture-center projectpulse-module-standard" data-module="068" data-brand="us-signal" data-mode="read-only" data-contract-version={architecture.contractVersion ?? 'provider-neutral'} aria-labelledby="system-architecture-title">
      <header className="system-architecture-hero">
        <img className="projectpulse-module-standard__logo" src={usSignalLogoDataUrl} alt="US Signal" />
        <div>
          <p className="eyebrow">Module 068 · Live provider-neutral architecture</p>
          <h1 id="system-architecture-title">System Architecture &amp; API Dependency Map</h1>
          <p>
            One shared registry for the hosting platform, browser, web, API, database, storage,
            Microsoft Integration, mail, SELL, Salesforce, ServiceNow, Certinia, GitHub controls,
            module-to-API relationships, regions, replicas, and governed external data flows.
          </p>
        </div>
        <div className="system-architecture-hero-actions">
          <button type="button" className="secondary-action" onClick={load} disabled={state.loading}>{state.loading ? 'Refreshing…' : 'Refresh architecture'}</button>
          <button type="button" className="primary-action" onClick={exportArchitecture} disabled={!architecture.nodes?.length}>Export branded architecture</button>
        </div>
      </header>

      {state.error ? <div className={`system-architecture-banner ${state.fallback ? 'governed' : 'error'}`} role="alert"><strong>{state.fallback ? 'Provider-neutral endpoint unavailable; legacy map shown' : 'Architecture map unavailable'}</strong><span>{state.error}</span></div> : null}

      <section className="system-architecture-runtime-strip">
        <article><span>Provider</span><strong>{platform.displayName ?? 'Loading…'}</strong><small>{title(platform.adapter)}</small></article>
        <article><span>Environment</span><strong>{title(platform.environment)}</strong><small>Generic environment contract</small></article>
        <article><span>Region</span><strong>{platform.region ?? 'Not reported'}</strong><small>{title(platform.workloadKind)}</small></article>
        <article><span>Release SHA</span><strong title={runtime.releaseSha}>{runtime.releaseSha?.slice(0, 14) ?? 'Not recorded'}</strong><small>Version {runtime.applicationVersion ?? 'Not recorded'}</small></article>
        <article><span>Components</span><strong>{architecture.nodes?.length ?? 0}</strong><small>{architecture.connections?.length ?? 0} data flows</small></article>
        <article><span>APIs</span><strong>{apiCount}</strong><small>{relationships.length} module owners</small></article>
      </section>

      <section className="system-architecture-panel architecture-map-panel">
        <div className="system-architecture-heading"><div><p className="eyebrow">Runtime component map</p><h2>Pulse Platform Operations</h2><p>Azure is the active adapter when Azure runtime evidence is present. OpenCloud and future providers use the same contract without changing Modules 013, 016, or 068.</p></div><small>Generated {dateTime(architecture.generatedAt)}</small></div>
        <div className="provider-adapter-map" aria-label="Provider adapter architecture">
          <div className="provider-contract-root"><strong>Pulse Platform Operations</strong><span>Shared provider-neutral contract</span></div>
          <div className="provider-adapter-branches">
            <article className={platform.provider === 'azure' ? 'active' : ''}><strong>Azure adapter</strong><span>{platform.provider === 'azure' ? 'Active now' : 'Available when detected'}</span></article>
            <article className={platform.provider === 'opencloud' ? 'active' : ''}><strong>OpenCloud adapter</strong><span>{platform.provider === 'opencloud' ? 'Active now' : 'Future provider contract'}</span></article>
            <article className={!['azure', 'opencloud'].includes(platform.provider) ? 'active' : ''}><strong>Other provider adapter</strong><span>{!['azure', 'opencloud'].includes(platform.provider) ? title(platform.provider) : 'Extensible contract'}</span></article>
          </div>
        </div>

        <div className="system-architecture-layers">
          {layers.map((layer) => (
            <article className="system-architecture-layer" key={layer.id}>
              <header><span>{String(layer.order).padStart(2, '0')}</span><div><h3>{layer.name}</h3></div></header>
              <div className="system-architecture-node-grid">
                {(nodesByLayer.get(layer.id) ?? []).map((node) => <section className={`system-architecture-node kind-${node.kind}`} key={node.id}><div className="system-architecture-node-title"><strong>{node.name}</strong><span>{title(node.kind)}</span></div><p>{node.description}</p></section>)}
              </div>
            </article>
          ))}
        </div>
      </section>

      <section className="system-architecture-panel">
        <div className="system-architecture-heading"><div><p className="eyebrow">Data movement</p><h2>Connection registry</h2></div><span>{architecture.connections?.length ?? 0} governed paths</span></div>
        <div className="system-architecture-table-wrap"><table className="system-architecture-table"><thead><tr><th>From</th><th>To</th><th>Protocol</th><th>Data / purpose</th><th>Classification</th></tr></thead><tbody>
          {(architecture.connections ?? []).map((connection, index) => <tr key={`${connection.from}-${connection.to}-${index}`}><td>{nodeNames.get(connection.from) ?? connection.from}</td><td>{nodeNames.get(connection.to) ?? connection.to}</td><td>{connection.protocol}</td><td>{connection.data}</td><td><span className="system-architecture-classification">{title(connection.classification)}</span></td></tr>)}
        </tbody></table></div>
      </section>

      <div className="system-architecture-two-column">
        <section className="system-architecture-panel">
          <div className="system-architecture-heading"><div><p className="eyebrow">External systems</p><h2>Integration data flows</h2></div></div>
          <div className="system-architecture-card-list">
            {(architecture.externalDataFlows ?? []).map((flow) => <article key={flow.system}><div><strong>{flow.system}</strong><Status value={flow.status} /></div><p>{flow.data}</p><small>{flow.owner} · {flow.projectPulseComponent}</small></article>)}
            {!architecture.externalDataFlows?.length ? <p>No external data flows were reported.</p> : null}
          </div>
        </section>

        <section className="system-architecture-panel">
          <div className="system-architecture-heading"><div><p className="eyebrow">Regions and redundancy</p><h2>Observed topology</h2></div></div>
          <div className="system-architecture-card-list">
            {(architecture.regions ?? []).map((region, index) => <article key={`${region.region}-${index}`}><strong>{region.region}</strong><p>{title(region.provider)} · {title(region.environment)}</p></article>)}
            <article><div><strong>Redundancy</strong><Status value={architecture.redundancy?.status} /></div><p>{architecture.redundancy?.message}</p><small>{architecture.redundancy?.observedReplicaCount ?? 0} instance(s) observed</small></article>
            {(architecture.redundancy?.replicas ?? []).map((replica) => <article key={replica.name}><div><strong>{replica.name}</strong><Status value={replica.status} /></div><p>{replica.evidence}</p><small>{replica.region}</small></article>)}
          </div>
        </section>
      </div>

      <section className="system-architecture-panel">
        <div className="system-architecture-heading">
          <div><p className="eyebrow">Modules and APIs</p><h2>Module-to-API relationships</h2><p>This appendix is generated from the routes registered in the running API rather than a manually maintained list.</p></div>
          <label className="architecture-module-filter"><span>Module</span><select value={selectedModule} onChange={(event) => setSelectedModule(event.target.value)}><option value="all">All modules</option>{relationships.map((item) => <option value={item.moduleCode} key={item.moduleCode}>Module {item.moduleCode} · {item.moduleName}</option>)}</select></label>
        </div>
        <div className="module-api-relationship-list">
          {visibleRelationships.map((relationship) => (
            <details key={relationship.moduleCode} open={selectedModule !== 'all'}>
              <summary><span>Module {relationship.moduleCode}</span><strong>{relationship.moduleName}</strong><small>{relationship.apiCount} API route(s)</small></summary>
              <div className="system-architecture-table-wrap"><table className="system-architecture-table"><thead><tr><th>Method</th><th>Path</th><th>Purpose</th></tr></thead><tbody>{(relationship.apis ?? []).map((api) => <tr key={api.apiId}><td><code>{api.method}</code></td><td><code>{api.path}</code></td><td>{api.purpose}</td></tr>)}</tbody></table></div>
            </details>
          ))}
        </div>
      </section>

      <section className="system-architecture-panel system-architecture-guardrails">
        <div><p className="eyebrow">Export contract</p><h2>Official branding and complete runtime evidence</h2><p>The HTML export uses the approved US Signal asset, provider, environment, release SHA, architecture legend, API appendix, generation date, and the footer “Created by Ahmed Adeyemi.”</p></div>
        <button type="button" className="primary-action" onClick={exportArchitecture}>Export architecture</button>
      </section>

      <section className="system-architecture-panel">
        <div className="system-architecture-heading"><div><p className="eyebrow">Current operating model</p><h2>Workflow ownership after the latest module changes</h2><p>These boundaries prevent duplicate modules from competing for the same data or action.</p></div><Status value="active" /></div>
        <div className="architecture-workflow-map">{[
          ['Intake → project → engineering', '020 captures and validates intake · 055D creates the canonical project · 055C manages it · 019 gives assigned engineers the linked documents.'],
          ['Notification configuration → delivery → audit', '022 owns cost routing · 023 owns schedule timing · 032 owns delivery operations · 065 owns provider/recipient authority · 008 owns searchable history.'],
          ['Health → diagnosis → remediation', '013 owns live service/API health · 998 owns evidence-backed diagnosis and controlled restart/remediation · 997 owns security incidents and containment.'],
          ['Build → release → architecture', '058 owns CI/CD dispatch and pipeline evidence · 077 owns promotion/rollback governance · 068 reports the current provider-neutral runtime and API map.']
        ].map(([name, detail]) => <article key={name}><strong>{name}</strong><p>{detail}</p></article>)}</div>
      </section>
    </section>
  );
}
