import { useCallback, useEffect, useMemo, useState } from 'react';
import './celar-ai-capability-routing-panel.css';

const TARGET_LABELS = {
  celar_ai: 'Celar AI',
  claude: 'Claude',
  openai: 'OpenAI',
  local_template: 'Governed local template',
};

const TARGET_DESCRIPTIONS = {
  celar_ai: 'Private orchestration, governed tools, private RAG, and private inference.',
  claude: 'Eligible external reasoning target; receives only fixed, backend-owned, identity-free capsules.',
  openai: 'Eligible external reasoning target; receives only fixed, backend-owned, identity-free capsules.',
  local_template: 'Deterministic final fallback that never calls a public provider.',
};

function title(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function formatDate(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Not recorded' : parsed.toLocaleString();
}

async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.message || `Request returned HTTP ${response.status}.`);
  return payload;
}

function routeDraft(route) {
  return {
    targets: [...(route.targets ?? ['celar_ai', 'claude', 'openai', 'local_template'])],
    revision: route.revision ?? 0,
  };
}

export default function CelarAiCapabilityRoutingPanel() {
  const [state, setState] = useState({ loading: true, error: '', routes: [], profile: null, productionReadiness: null, knowledgeFabric: null, consumers: [], controls: null });
  const [drafts, setDrafts] = useState({});
  const [savingRoute, setSavingRoute] = useState('');
  const [notice, setNotice] = useState('');
  const [profileForm, setProfileForm] = useState({
    enabled: false,
    endpoint: '',
    model: '',
    allowlist: '',
    requirePrivateModelForDocuments: true,
    revision: 0,
    bearerToken: '',
  });
  const [savingProfile, setSavingProfile] = useState(false);
  const [savingToken, setSavingToken] = useState(false);
  const [testingProfile, setTestingProfile] = useState(false);

  const load = useCallback(async ({ quiet = false } = {}) => {
    if (!quiet) setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const [routesPayload, profilePayload, consumersPayload, knowledgePayload] = await Promise.all([
        readJson(await fetch('/api/ai-configuration/routes', { credentials: 'include', cache: 'no-store' })),
        readJson(await fetch('/api/ai-configuration/private-model', { credentials: 'include', cache: 'no-store' })),
        readJson(await fetch('/api/ai-configuration/consumers', { credentials: 'include', cache: 'no-store' })),
        readJson(await fetch('/api/ai-configuration/knowledge-fabric', { credentials: 'include', cache: 'no-store' })),
      ]);
      const routes = routesPayload.routes ?? [];
      const profile = profilePayload.profile ?? null;
      setDrafts(Object.fromEntries(routes.map((route) => [route.feature, routeDraft(route)])));
      setProfileForm((current) => ({
        ...current,
        enabled: profile?.enabled === true,
        endpoint: '',
        model: profile?.model && profile.model !== 'Not configured' ? profile.model : '',
        allowlist: '',
        requirePrivateModelForDocuments: profile?.requirePrivateModelForDocuments !== false,
        revision: profile?.revision ?? 0,
        bearerToken: '',
      }));
      setState({
        loading: false,
        error: '',
        routes,
        profile,
        productionReadiness: profilePayload.productionReadiness ?? null,
        knowledgeFabric: knowledgePayload.knowledgeFabric ?? null,
        consumers: consumersPayload.consumers ?? [],
        controls: routesPayload.controls ?? null,
      });
    } catch (error) {
      setState((current) => ({
        ...current,
        loading: false,
        error: error instanceof Error ? error.message : 'Celar AI routing could not be loaded.',
      }));
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const targetOptions = useMemo(
    () => ['celar_ai', 'claude', 'openai', 'local_template'],
    [],
  );

  function setTarget(feature, position, value) {
    setDrafts((current) => {
      const draft = current[feature] ?? { targets: [...targetOptions], revision: 0 };
      const targets = [...draft.targets];
      targets[position] = value;
      return { ...current, [feature]: { ...draft, targets } };
    });
  }

  async function saveRoute(feature) {
    const draft = drafts[feature];
    if (!draft) return;
    setSavingRoute(feature);
    setNotice('');
    try {
      const payload = await readJson(await fetch(`/api/ai-configuration/routes/${encodeURIComponent(feature)}`, {
        method: 'PUT',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ targets: draft.targets, expectedRevision: draft.revision }),
      }));
      setNotice(payload.message || 'Capability route saved.');
      await load({ quiet: true });
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'The capability route could not be saved.');
    } finally {
      setSavingRoute('');
    }
  }

  async function resetRoute(feature) {
    const draft = drafts[feature];
    setSavingRoute(feature);
    setNotice('');
    try {
      const payload = await readJson(await fetch(`/api/ai-configuration/routes/${encodeURIComponent(feature)}/reset`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedRevision: draft?.revision ?? 0 }),
      }));
      setNotice(payload.message || 'Capability route reset.');
      await load({ quiet: true });
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'The capability route could not be reset.');
    } finally {
      setSavingRoute('');
    }
  }

  async function savePrivateSettings(event) {
    event.preventDefault();
    setSavingProfile(true);
    setNotice('');
    try {
      const payload = await readJson(await fetch('/api/ai-configuration/private-model/settings', {
        method: 'PUT',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          enabled: profileForm.enabled,
          endpoint: profileForm.endpoint || null,
          model: profileForm.model || null,
          privateHostAllowlist: profileForm.allowlist
            .split(/[;,\n\r]/)
            .map((value) => value.trim())
            .filter(Boolean),
          requirePrivateModelForDocuments: profileForm.requirePrivateModelForDocuments,
          expectedRevision: profileForm.revision,
        }),
      }));
      setNotice(payload.message || 'Private Celar AI settings saved.');
      await load({ quiet: true });
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'The private model settings could not be saved.');
    } finally {
      setSavingProfile(false);
    }
  }

  async function savePrivateToken(event) {
    event.preventDefault();
    const bearerToken = profileForm.bearerToken.trim();
    if (!bearerToken) return;
    setSavingToken(true);
    setNotice('');
    try {
      const payload = await readJson(await fetch('/api/ai-configuration/private-model/secret', {
        method: 'PUT',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ bearerToken, expectedRevision: profileForm.revision }),
      }));
      setNotice(payload.message || 'Private Celar AI token saved securely.');
      await load({ quiet: true });
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'The private model token could not be saved.');
    } finally {
      setSavingToken(false);
    }
  }

  async function testPrivateModel() {
    setTestingProfile(true);
    setNotice('');
    try {
      const payload = await readJson(await fetch('/api/ai-configuration/private-model/test', {
        method: 'POST',
        credentials: 'include',
      }));
      setNotice(`Private Celar AI test: ${title(payload.status)}. ${payload.diagnosticCode || ''}`.trim());
      await load({ quiet: true });
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'The private model test did not complete.');
    } finally {
      setTestingProfile(false);
    }
  }

  const profile = state.profile;
  const production = state.productionReadiness;
  const knowledge = state.knowledgeFabric;
  const deploymentManaged = state.controls?.deploymentManaged === true || profile?.deploymentManaged === true;
  const releasePhase = state.controls?.releasePhase || production?.releasePhase || 'disabled';

  return (
    <section className="celar-ai-routing" aria-labelledby="celar-ai-routing-title">
      <header className="celar-ai-routing__header">
        <div>
          <p>Celar AI and Module 064 control plane</p>
          <h2 id="celar-ai-routing-title">Private-first targets and capability routing</h2>
          <span>
            The backend follows each capability&apos;s stored priority among eligible targets. When Require private inference
            for document-grounded answers is on, document-grounded requests force private Celar AI first. After private
            failure, Claude and OpenAI keep their stored relative order and receive only fixed, backend-owned, identity-free
            capsules. The governed local template remains the deterministic final fallback.
          </span>
        </div>
        <button type="button" onClick={() => load()} disabled={state.loading}>
          {state.loading ? 'Refreshing…' : 'Refresh routing'}
        </button>
      </header>

      {notice ? <div className="celar-ai-routing__notice" role="status">{notice}</div> : null}
      {state.error ? <div className="celar-ai-routing__error" role="alert">{state.error}</div> : null}
      {deploymentManaged ? (
        <div className="celar-ai-routing__notice" role="status">
          {releasePhase === 'candidate'
            ? `Release candidate configuration is deployment-managed and read-only for source ${profile?.configurationSourceCommit || state.controls?.configurationSourceCommit}. Candidate document processing, audit persistence, and every application mutation are blocked; verification runs only through the combined candidate operation.`
            : `Active release configuration is deployment-managed and read-only for source ${profile?.configurationSourceCommit || state.controls?.configurationSourceCommit}. Routes, endpoints, models, and credentials require a new protected release manifest, while normal authorized document processing and application writes remain active.`}
        </div>
      ) : null}
      {state.loading && !state.routes.length ? <div className="celar-ai-routing__loading">Loading Celar AI routing and private-model readiness…</div> : null}

      <div className="celar-ai-routing__architecture" aria-label="Celar AI routing architecture">
        {targetOptions.map((target, index) => (
          <article key={target} className={target === 'celar_ai' ? 'is-primary' : target === 'local_template' ? 'is-local' : ''}>
            <span>{index === 0 ? 'Default primary' : index === 1 ? 'Default secondary' : index === 2 ? 'Default tertiary' : 'Final fallback'}</span>
            <strong>{TARGET_LABELS[target]}</strong>
            <small>{TARGET_DESCRIPTIONS[target]}</small>
          </article>
        ))}
      </div>

      <section className="celar-ai-routing__private-model" aria-labelledby="private-celar-model-title">
        <div className="celar-ai-routing__subheading">
          <div>
            <p>Private Celar AI target</p>
            <h3 id="private-celar-model-title">Configure the private OpenAI-compatible inference endpoint</h3>
          </div>
          <span className={profile?.ready ? 'is-ready' : 'is-pending'}>
            {profile?.ready ? 'Ready' : profile?.configured ? 'Configured, not ready' : 'Not configured'}
          </span>
        </div>

        <div className="celar-ai-routing__private-summary">
          <article><span>Model</span><strong>{profile?.model || 'Not configured'}</strong></article>
          <article><span>Endpoint</span><strong>{profile?.endpointConfigured ? 'Configured' : 'Not configured'}</strong><small>Fingerprint: {profile?.endpointHostFingerprint || 'Not recorded'}</small></article>
          <article><span>Authentication</span><strong>{profile?.bearerTokenConfigured ? 'Token configured' : 'No bearer token'}</strong><small>Token value is write-only</small></article>
          <article><span>Revision</span><strong>{profile?.revision ?? 0}</strong><small>Updated {formatDate(profile?.updatedAt)}</small></article>
        </div>

        <div className="celar-ai-routing__production-readiness" role="status" aria-live="polite">
          <header>
            <div>
              <span>End-to-end private runtime</span>
              <strong>{production?.ready ? 'Production ready' : 'Configuration required'}</strong>
            </div>
            <small>Endpoint, encrypted secret storage, migrations, persistent files, processing, and SOW readiness</small>
          </header>
          <div>
            <article><span>Migrations 052 / 053 / 061</span><strong>{production?.migrations?.allRequiredApplied ? 'Applied' : 'Required'}</strong></article>
            <article><span>Shared persistent storage</span><strong>{production?.storage?.sharedPersistentWritable ? 'Ready' : 'Required'}</strong></article>
            <article><span>Private document worker</span><strong>{production?.processing?.workerEnabled ? 'Enabled' : 'Disabled'}</strong></article>
            <article><span>Ready SOW / GSD</span><strong>{production?.documents?.readySowDocumentCount ?? 0}</strong></article>
          </div>
          {!production?.ready && (production?.blockers ?? []).length ? (
            <details>
              <summary>Review {production.blockers.length} production-readiness item{production.blockers.length === 1 ? '' : 's'}</summary>
              <ul>{production.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}</ul>
            </details>
          ) : null}
        </div>

        <section className="celar-ai-routing__knowledge-fabric" aria-labelledby="celar-knowledge-fabric-title">
          <header>
            <div>
              <span>Comprehensive knowledge fabric</span>
              <strong id="celar-knowledge-fabric-title">{knowledge?.ready ? 'Connected and current' : 'Connected with readiness items'}</strong>
            </div>
            <small>Source-controlled knowledge, capability graph, content graph, private endpoints, citations, and freshness</small>
          </header>
          <div className="celar-ai-routing__knowledge-grid">
            <article><span>Knowledge graph</span><strong>{knowledge?.routeGraphReady ? 'Ready' : 'Review required'}</strong><small>{knowledge?.capabilityNodeCount ?? 0} capabilities · {knowledge?.consumerNodeCount ?? 0} consumers · {knowledge?.relationshipCount ?? 0} relationships</small></article>
            <article><span>Content graph</span><strong>{knowledge?.contentGraphReady ? 'Ready' : 'Review required'}</strong><small>{knowledge?.readyDocumentCount ?? 0} documents · {knowledge?.activeVersionCount ?? 0} active versions · {knowledge?.activeChunkCount ?? 0} searchable chunks</small></article>
            <article><span>Private endpoints</span><strong>{knowledge?.privateEndpointsReady ? 'Verified' : 'Review required'}</strong><small>{(knowledge?.endpoints ?? []).filter((item) => item.status === 'ready').length} of {(knowledge?.endpoints ?? []).filter((item) => item.required).length} required components ready</small></article>
            <article><span>Latest indexed content</span><strong>{formatDate(knowledge?.lastIndexedAt)}</strong><small>Source {knowledge?.sourceCommit ? knowledge.sourceCommit.slice(0, 12) : 'not recorded'} · {knowledge?.embeddedChunkCount ?? 0} embedded chunks</small></article>
          </div>
          <div className="celar-ai-routing__knowledge-versions">
            <span>Product knowledge: {knowledge?.productKnowledgeVersion || 'not recorded'}</span>
            <span>System knowledge: {knowledge?.systemKnowledgeVersion || 'not recorded'}</span>
            <span>Private runtime: {knowledge?.privateRuntimeVersion || 'not recorded'}</span>
          </div>
          {(knowledge?.endpoints ?? []).length ? (
            <div className="celar-ai-routing__endpoint-matrix" role="list" aria-label="Private endpoint readiness">
              {knowledge.endpoints.map((endpoint) => (
                <span key={endpoint.component} role="listitem" className={endpoint.status === 'ready' || endpoint.status === 'not_required' ? 'is-good' : 'is-bad'}>
                  {title(endpoint.component)}: {title(endpoint.status)}
                </span>
              ))}
            </div>
          ) : null}
          {!knowledge?.ready && (knowledge?.blockers ?? []).length ? (
            <details>
              <summary>Review {knowledge.blockers.length} knowledge-fabric item{knowledge.blockers.length === 1 ? '' : 's'}</summary>
              <ul>{knowledge.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}</ul>
            </details>
          ) : null}
        </section>

        <form className="celar-ai-routing__profile-form" onSubmit={savePrivateSettings}>
          <label>
            <span>Private endpoint</span>
            <input
              type="url"
              value={profileForm.endpoint}
              onChange={(event) => setProfileForm((current) => ({ ...current, endpoint: event.target.value }))}
              placeholder={profile?.endpointConfigured ? 'Leave blank to preserve the encrypted endpoint' : 'https://private-host/v1/chat/completions'}
              autoComplete="off"
              disabled={deploymentManaged}
            />
            <small>The endpoint must use a private IP, loopback, or approved private DNS suffix. The saved value is never returned.</small>
          </label>
          <label>
            <span>Private model or deployment name</span>
            <input
              value={profileForm.model}
              onChange={(event) => setProfileForm((current) => ({ ...current, model: event.target.value }))}
              placeholder="Private model name"
              disabled={deploymentManaged}
            />
          </label>
          <label>
            <span>Private-host allowlist</span>
            <textarea
              value={profileForm.allowlist}
              onChange={(event) => setProfileForm((current) => ({ ...current, allowlist: event.target.value }))}
              placeholder="One hostname or private DNS suffix per line; leave blank to preserve existing/default policy"
              disabled={deploymentManaged}
            />
          </label>
          <div className="celar-ai-routing__checks">
            <label><input type="checkbox" checked={profileForm.enabled} disabled={deploymentManaged} onChange={(event) => setProfileForm((current) => ({ ...current, enabled: event.target.checked }))} /> Enable the private Celar AI target</label>
            <label><input type="checkbox" checked={profileForm.requirePrivateModelForDocuments} disabled={deploymentManaged} onChange={(event) => setProfileForm((current) => ({ ...current, requirePrivateModelForDocuments: event.target.checked }))} /> Require private inference for document-grounded answers</label>
          </div>
          <button type="submit" disabled={savingProfile || deploymentManaged}>{deploymentManaged ? 'Deployment-managed' : savingProfile ? 'Saving…' : 'Save private-model settings'}</button>
        </form>

        <form className="celar-ai-routing__token-form" onSubmit={savePrivateToken}>
          <label htmlFor="celar-private-token">Private bearer token</label>
          <div>
            <input
              id="celar-private-token"
              type="password"
              value={profileForm.bearerToken}
              onChange={(event) => setProfileForm((current) => ({ ...current, bearerToken: event.target.value }))}
              placeholder={profile?.bearerTokenConfigured ? 'Replace the write-only token' : 'Paste token once when required'}
              autoComplete="new-password"
              disabled={deploymentManaged}
            />
            <button type="submit" disabled={deploymentManaged || savingToken || !profileForm.bearerToken.trim()}>{deploymentManaged ? 'Deployment-managed' : savingToken ? 'Saving…' : 'Save securely'}</button>
            <button type="button" onClick={testPrivateModel} disabled={testingProfile || !profile?.configured}>{testingProfile ? 'Testing…' : 'Test private model'}</button>
          </div>
          <small>The token is AES-GCM encrypted and cannot be viewed after saving.</small>
        </form>
      </section>

      <section className="celar-ai-routing__routes" aria-labelledby="capability-route-title">
        <div className="celar-ai-routing__subheading">
          <div><p>Capability routing</p><h3 id="capability-route-title">Primary, secondary, tertiary, and final fallback</h3></div>
          <span>Stored priority among eligible targets. Default: Celar AI → Claude → OpenAI → Governed local template</span>
        </div>
        <div className="celar-ai-routing__route-grid">
          {state.routes.map((route) => {
            const draft = drafts[route.feature] ?? routeDraft(route);
            const duplicate = new Set(draft.targets).size !== draft.targets.length;
            const localLast = draft.targets[3] === 'local_template';
            return (
              <article key={route.feature} className="celar-ai-routing__route-card">
                <header>
                  <div><strong>{route.displayName}</strong><small>Modules {(route.consumerModules ?? []).join(', ')}</small></div>
                  <span>{title(route.contextClassification)}</span>
                </header>
                <div className="celar-ai-routing__route-selects">
                  {['Primary', 'Secondary', 'Tertiary', 'Final fallback'].map((label, position) => (
                    <label key={label}>
                      <span>{label}</span>
                      <select
                        value={draft.targets[position] || ''}
                        onChange={(event) => setTarget(route.feature, position, event.target.value)}
                        disabled={deploymentManaged || position === 3}
                      >
                        {targetOptions.map((target) => <option value={target} key={target}>{TARGET_LABELS[target]}</option>)}
                      </select>
                    </label>
                  ))}
                </div>
                <p><strong>External policy:</strong> {title(route.externalContextPolicy)}</p>
                {!localLast ? <p className="is-error">Governed local template must remain final.</p> : null}
                {duplicate ? <p className="is-error">Every route position must be unique.</p> : null}
                <footer>
                  <span>Revision {route.revision ?? 0} · {route.deploymentManaged ? 'Deployment-managed' : route.persisted ? 'Persisted' : 'Default policy'}</span>
                  <div>
                    <button type="button" className="is-secondary" onClick={() => resetRoute(route.feature)} disabled={deploymentManaged || savingRoute === route.feature}>Reset</button>
                    <button type="button" onClick={() => saveRoute(route.feature)} disabled={deploymentManaged || savingRoute === route.feature || duplicate || !localLast}>
                      {deploymentManaged ? 'Read-only' : savingRoute === route.feature ? 'Saving…' : 'Save route'}
                    </button>
                  </div>
                </footer>
              </article>
            );
          })}
        </div>
      </section>

      <section className="celar-ai-routing__consumers" aria-labelledby="consumer-assurance-title">
        <div className="celar-ai-routing__subheading">
          <div><p>Consumer assurance</p><h3 id="consumer-assurance-title">Confirm every AI component uses Module 064</h3></div>
          <span>Direct public-provider clients are prohibited</span>
        </div>
        <div className="celar-ai-routing__consumer-table" role="table">
          <div role="row" className="is-header"><span>Capability</span><span>Module / entry point</span><span>Central router</span><span>Private boundary</span><span>Last target</span></div>
          {state.consumers.map((consumer) => (
            <div role="row" key={consumer.feature}>
              <span><strong>{title(consumer.feature)}</strong><small>{(consumer.route ?? []).map((target) => TARGET_LABELS[target] || target).join(' → ')}</small></span>
              <span>{consumer.module}<small>{consumer.entryPoint}</small></span>
              <span className={consumer.centralRouterConnected ? 'is-good' : 'is-bad'}>{consumer.centralRouterConnected ? 'Connected' : 'Missing'}</span>
              <span className={consumer.privateContextCompliant && consumer.directProviderFree ? 'is-good' : 'is-bad'}>{consumer.privateContextCompliant && consumer.directProviderFree ? 'Compliant' : 'Review required'}</span>
              <span>{title(consumer.lastTarget || 'not exercised')}<small>{formatDate(consumer.lastExercisedAt)}</small></span>
            </div>
          ))}
        </div>
      </section>

      <aside className="celar-ai-routing__guardrails">
        <strong>Non-editable enterprise guardrails</strong>
        <ul>
          <li>Raw SOW, GSD, IQS, email, customer, project, employee, contract, rate, and financial context never goes directly to a public provider.</li>
          <li>Claude and OpenAI keep their stored relative order after private failure and receive only fixed, backend-owned, identity-free capsules.</li>
          <li>A safety refusal stops routing; a later provider is not used to bypass it.</li>
          <li>No AI route automatically saves or submits time, publishes a SOW, baselines a plan, sends a closeout message, changes financial data, or deploys software.</li>
        </ul>
      </aside>
    </section>
  );
}
