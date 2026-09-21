import { useCallback, useEffect, useMemo, useState } from 'react';
import './celar-ai-capability-routing-panel.css';

const TARGET_LABELS = {
  deepseek_v4: 'DeepSeek v4',
  celar_ai: 'Celar AI',
  claude: 'Claude',
  openai: 'OpenAI',
  gemini: 'Gemini',
  copilot_studio: 'Microsoft Copilot Studio',
  local_template: 'Governed local template',
};

const TARGET_DESCRIPTIONS = {
  deepseek_v4: 'Self-hosted DGX inference with governed context and a single request slot.',
  celar_ai: 'Private orchestration, governed tools, private RAG, and private inference.',
  claude: 'Eligible external reasoning target; receives only fixed, backend-owned, identity-free capsules.',
  openai: 'Eligible external reasoning target; receives only fixed, backend-owned, identity-free capsules.',
  gemini: 'Optional Google provider; configure and test before enabling.',
  copilot_studio: 'Optional published Microsoft Copilot Studio agent; requires its Direct Line credential.',
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
    targets: [...(route.targets ?? ['deepseek_v4', 'celar_ai', 'claude', 'openai', 'local_template'])],
    revision: route.revision ?? 0,
    sanitizedExternalGenerationApproved: route.sanitizedExternalGenerationApproved === true,
    serviceScopeFullTextApproved: route.serviceScopeFullTextApproved === true,
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
      const results = await Promise.allSettled([
        '/api/ai-configuration/routes', '/api/ai-configuration/private-model',
        '/api/ai-configuration/consumers', '/api/ai-configuration/knowledge-fabric'
      ].map(async (path) => readJson(await fetch(path, { credentials: 'include', cache: 'no-store' }))));
      if (results[0].status === 'rejected') throw results[0].reason;
      const [routesPayload, profilePayload, consumersPayload, knowledgePayload] = results.map((result) => result.status === 'fulfilled' ? result.value : {});
      const failures = results.flatMap((result, index) => result.status === 'rejected' ? [`${['Routing', 'Private runtime', 'Consumer inventory', 'Knowledge fabric'][index]}: ${result.reason?.message || 'Unavailable'}`] : []);
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
        error: failures.join(' · '),
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
    () => ['deepseek_v4', 'celar_ai', 'claude', 'openai', 'gemini', 'copilot_studio', 'local_template'],
    [],
  );

  function setTarget(feature, position, value) {
    setDrafts((current) => {
      const draft = current[feature] ?? { targets: [...targetOptions], revision: 0 };
      const targets = [...draft.targets];
      const previousPosition = targets.indexOf(value);
      if (previousPosition >= 0) targets[previousPosition] = targets[position];
      targets[position] = value;
      return { ...current, [feature]: { ...draft, targets } };
    });
  }

  async function saveRoute(feature) {
    const draft = drafts[feature];
    const route = state.routes.find((item) => item.feature === feature);
    if (!draft) return;
    setSavingRoute(feature);
    setNotice('');
    try {
      const payload = await readJson(await fetch(`/api/ai-configuration/routes/${encodeURIComponent(feature)}`, {
        method: 'PUT',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          targets: draft.targets,
          expectedRevision: draft.revision,
          ...(route?.externalGenerationApprovalEditable ? { sanitizedExternalGenerationApproved: draft.sanitizedExternalGenerationApproved } : {}),
          ...(route?.serviceScopeApprovalEditable ? { serviceScopeFullTextApproved: draft.serviceScopeFullTextApproved } : {}),
        }),
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
  const routeReadOnly = state.controls?.readOnly === true;
  const releasePhase = state.controls?.releasePhase || production?.releasePhase || 'disabled';

  return (
    <section className="celar-ai-routing" aria-labelledby="celar-ai-routing-title">
      <header className="celar-ai-routing__header">
        <div>
          <p>Celar AI and Module 064 control plane</p>
          <h2 id="celar-ai-routing-title">Provider order and generation policy</h2>
          <span>
            Module 064 is the control source for each capability&apos;s provider order. Each route shows its saved effective
            order and policy restrictions. Approved external SOW generation receives only a validated technical capsule;
            raw documents, identities, and commercial values remain private. Other private document requests retain their privacy policy.
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
            : !routeReadOnly ? 'The private runtime profile is supplied by deployment. Capability routing remains editable below; saving a route updates its audited database revision.' : `Active release configuration is deployment-managed and read-only for source ${profile?.configurationSourceCommit || state.controls?.configurationSourceCommit}. Routes, endpoints, models, and credentials require a new protected release manifest, while normal authorized document processing and application writes remain active.`}
        </div>
      ) : null}
      {state.loading && !state.routes.length ? <div className="celar-ai-routing__loading">Loading Celar AI routing and private-model readiness…</div> : null}

      <div className="celar-ai-routing__architecture" aria-label="Available provider roles">
        {targetOptions.map((target) => (
          <article key={target} className={target === 'local_template' ? 'is-local' : ''}>
            <span>{['deepseek_v4', 'celar_ai'].includes(target) ? 'Private provider' : target === 'local_template' ? 'Template fallback' : 'External provider'}</span>
            <strong>{TARGET_LABELS[target]}</strong>
            <small>{TARGET_DESCRIPTIONS[target]}</small>
          </article>
        ))}
      </div>

      <section className="celar-ai-routing__private-summary" aria-label="Celar AI availability">
        <article><span>Celar AI inference</span><strong>{production?.privateModelReady ? 'Available' : 'Unavailable or unverified'}</strong><small>Only fresh server probe evidence establishes availability.</small></article>
        <article><span>Last verified</span><strong>{formatDate(production?.privateTargetAvailability?.verifiedAt)}</strong><small>{production?.privateTargetAvailability?.lastFailureCode || 'No failure code reported'}</small></article>
        <article><span>Document storage and processing</span><strong>{production?.privateDocumentRuntimeReady ? 'Ready' : 'Attention required'}</strong><small>Document readiness is tracked separately from inference.</small></article>
      </section>

      <p>Celar AI usage in this API process: {production?.privateTargetUsage?.successes ?? 0} successful generations · {production?.privateTargetUsage?.failures ?? 0} failures · {production?.privateTargetUsage?.refusals ?? 0} refusals. Input / output tokens: {production?.privateTargetUsage?.inputTokens ?? 'Not reported'} / {production?.privateTargetUsage?.outputTokens ?? 'Not reported'}.</p>
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
          <article><span>Gateway compatibility model</span><strong>{profile?.model || 'Not configured'}</strong><small>Default OpenAI-compatible target; not the complete Oracle inventory</small></article>
          <article><span>Generation portfolio</span><strong>Gemma · Qwen · Llama</strong><small>Structured: Gemma → Qwen → Llama · General: Qwen → Llama → Gemma</small></article>
          <article><span>Embedding model</span><strong>EmbeddingGemma</strong><small>Live installed versions and digests are authoritative in Module 084</small></article>
          <article><span>Endpoint</span><strong>{profile?.endpointConfigured ? 'Configured' : 'Not configured'}</strong><small>Fingerprint: {profile?.endpointHostFingerprint || 'Not recorded'}</small></article>
          <article><span>Authentication</span><strong>{profile?.bearerTokenConfigured ? 'Token configured' : 'No bearer token'}</strong><small>Token value is write-only</small></article>
          <article><span>Revision</span><strong>{profile?.revision ?? 0}</strong><small>Updated {formatDate(profile?.updatedAt)}</small></article>
        </div>

        <div className="celar-ai-routing__production-readiness" role="status" aria-live="polite">
          <header>
            <div>
              <span>End-to-end private runtime</span>
              <strong>{production?.ready ? 'Runtime ready' : 'Attention required'}</strong>
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
              <summary>Review {production.blockers.length} runtime-readiness item{production.blockers.length === 1 ? '' : 's'}</summary>
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
            <small>Source-controlled knowledge, content and context graphs, time, policy, live route traces, private adapters, citations, and freshness</small>
          </header>
          <div className="celar-ai-routing__knowledge-grid">
            <article><span>Knowledge graph</span><strong>{knowledge?.routeGraphReady ? 'Ready' : 'Review required'}</strong><small>{knowledge?.capabilityNodeCount ?? 0} capabilities · {knowledge?.consumerNodeCount ?? 0} consumers · {knowledge?.relationshipCount ?? 0} relationships</small></article>
            <article><span>Content graph</span><strong>{knowledge?.contentGraphReady ? 'Ready' : 'Review required'}</strong><small>{knowledge?.readyDocumentCount ?? 0} documents · {knowledge?.activeVersionCount ?? 0} active versions · {knowledge?.activeChunkCount ?? 0} searchable chunks</small></article>
            <article><span>Temporal context graph</span><strong>{knowledge?.contextGraphReady ? 'Ready' : 'Review required'}</strong><small>{title(knowledge?.freshnessStatus || 'not available')} · as of {formatDate(knowledge?.knowledgeAsOf)}</small></article>
            <article><span>Policy and decision traces</span><strong>{knowledge?.policyGraphReady && knowledge?.decisionTraceReady ? 'Ready' : 'Review required'}</strong><small>{(knowledge?.decisionTraces ?? []).length} governed capability traces · hidden reasoning never returned</small></article>
            <article><span>Private endpoints</span><strong>{knowledge?.privateEndpointsReady ? 'Verified' : 'Review required'}</strong><small>{(knowledge?.endpoints ?? []).filter((item) => item.required && item.status === 'ready').length} of {(knowledge?.endpoints ?? []).filter((item) => item.required).length} required components ready</small></article>
            <article><span>Latest indexed content</span><strong>{formatDate(knowledge?.lastIndexedAt)}</strong><small>Source {knowledge?.sourceCommit ? knowledge.sourceCommit.slice(0, 12) : 'not recorded'} · {knowledge?.embeddedChunkCount ?? 0} embedded · {knowledge?.pendingIndexCount ?? 0} pending</small></article>
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
          {(knowledge?.decisionTraces ?? []).length ? (
            <details>
              <summary>View privacy-safe live routing traces</summary>
              <ul>{knowledge.decisionTraces.map((trace) => (
                <li key={trace.feature}>
                  <strong>{title(trace.feature)}</strong>: {(trace.configuredRoute ?? []).map((target) => TARGET_LABELS[target] || title(target)).join(' → ')} · last target {title(trace.lastTarget)} · {title(trace.lastOutcome)} · {formatDate(trace.evaluatedAt)}
                </li>
              ))}</ul>
            </details>
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
              disabled={routeReadOnly}
            />
            <small>
              {deploymentManaged
                ? 'This protected endpoint is deployment-managed. Its raw URL and credential remain hidden; the configured state and fingerprint above are authoritative.'
                : 'The endpoint must use a private IP, loopback, or approved private DNS suffix. The saved value is never returned.'}
            </small>
          </label>
          <label>
            <span>Private model or deployment name</span>
            <input
              value={profileForm.model}
              onChange={(event) => setProfileForm((current) => ({ ...current, model: event.target.value }))}
              placeholder="Private model name"
              disabled={routeReadOnly}
            />
          </label>
          <label>
            <span>Private-host allowlist</span>
            <textarea
              value={profileForm.allowlist}
              onChange={(event) => setProfileForm((current) => ({ ...current, allowlist: event.target.value }))}
              placeholder="One hostname or private DNS suffix per line; leave blank to preserve existing/default policy"
              disabled={routeReadOnly}
            />
          </label>
          <div className="celar-ai-routing__checks">
            <label><input type="checkbox" checked={profileForm.enabled} disabled={routeReadOnly} onChange={(event) => setProfileForm((current) => ({ ...current, enabled: event.target.checked }))} /> Enable the private Celar AI target</label>
            <label><input type="checkbox" checked={profileForm.requirePrivateModelForDocuments} disabled={routeReadOnly} onChange={(event) => setProfileForm((current) => ({ ...current, requirePrivateModelForDocuments: event.target.checked }))} /> Require private inference for document-grounded answers</label>
          </div>
          <button type="submit" disabled={savingProfile || routeReadOnly}>{routeReadOnly ? 'Deployment-managed' : savingProfile ? 'Saving…' : 'Save private-model settings'}</button>
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
              disabled={routeReadOnly}
            />
            <button type="submit" disabled={routeReadOnly || savingToken || !profileForm.bearerToken.trim()}>{routeReadOnly ? 'Deployment-managed' : savingToken ? 'Saving…' : 'Save securely'}</button>
            <button type="button" onClick={testPrivateModel} disabled={testingProfile || !profile?.configured}>{testingProfile ? 'Testing…' : 'Test private model'}</button>
          </div>
          <small>The token is AES-GCM encrypted and cannot be viewed after saving.</small>
        </form>
      </section>

      <section className="celar-ai-routing__routes" aria-labelledby="capability-route-title">
        <div className="celar-ai-routing__subheading">
          <div><p>Capability routing</p><h3 id="capability-route-title">Provider priority and final fallback</h3></div>
          <span>Set the order here. Unavailable or unsupported targets are skipped; policy blockers are shown with each saved route.</span>
        </div>
        <div className="celar-ai-routing__route-grid">
          {state.routes.map((route) => {
            const draft = drafts[route.feature] ?? routeDraft(route);
            const duplicate = new Set(draft.targets).size !== draft.targets.length;
            const localLast = draft.targets.length >= 5 && draft.targets.at(-1) === 'local_template';
            const sowRoute = route.feature === 'sow_gsd_planning';
            const unsaved = draft.sanitizedExternalGenerationApproved !== (route.sanitizedExternalGenerationApproved === true)
              || draft.serviceScopeFullTextApproved !== (route.serviceScopeFullTextApproved === true)
              || JSON.stringify(draft.targets) !== JSON.stringify(route.targets);
            return (
              <article key={route.feature} className="celar-ai-routing__route-card">
                <header>
                  <div><strong>{route.displayName}</strong><small>Modules {(route.consumerModules ?? []).join(', ')}</small></div>
                  <span>{title(route.contextClassification)}</span>
                </header>
                <div className="celar-ai-routing__route-selects">
                  {draft.targets.map((currentTarget, position) => { const label = currentTarget === 'local_template' ? 'Final fallback' : `Priority ${position + 1}`; return (
                    <label key={label}>
                      <span>{label}</span>
                      <select
                        value={draft.targets[position] || ''}
                        onChange={(event) => setTarget(route.feature, position, event.target.value)}
                        disabled={routeReadOnly || position === draft.targets.length - 1}
                      >
                        {targetOptions.filter((target) => position === draft.targets.length - 1 ? target === 'local_template' : draft.targets.includes(target) && target !== 'local_template').map((target) => <option value={target} key={target}>{TARGET_LABELS[target]}</option>)}
                      </select>
                    </label>
                  ); })}
                </div>
                <div className="celar-ai-routing__checks">{['gemini', 'copilot_studio'].map(target => <label key={target}><input type="checkbox" disabled={routeReadOnly || savingRoute === route.feature} checked={draft.targets.includes(target)} onChange={event => setDrafts(current => ({ ...current, [route.feature]: { ...draft, targets: event.target.checked ? [...draft.targets.slice(0, -1), target, 'local_template'] : draft.targets.filter(value => value !== target) } }))} /> Include {TARGET_LABELS[target]}</label>)}</div>
                <p><strong>External policy:</strong> {title(route.externalContextPolicy)}</p>
                {sowRoute ? (
                  <div className="celar-ai-routing__external-approval">
                    <label>
                      <input
                        type="checkbox"
                        checked={draft.sanitizedExternalGenerationApproved}
                        disabled={routeReadOnly || !route.externalGenerationApprovalEditable || savingRoute === route.feature}
                        onChange={(event) => setDrafts((current) => ({ ...current, [route.feature]: { ...current[route.feature], sanitizedExternalGenerationApproved: event.target.checked } }))}
                      />
                      <span>Authorize paid providers for validated, sanitized SOW/GSD generation</span>
                    </label>
                    <small>Allows eligible external providers in the saved order. Provider charges may apply. Raw documents, identities, and commercial values remain private. Save the route to apply this approval.</small>
                  </div>
                ) : null}
                {sowRoute ? <div className="celar-ai-routing__external-approval">
                  <label><input type="checkbox" checked={draft.serviceScopeFullTextApproved === true}
                    disabled={routeReadOnly || !route.serviceScopeApprovalEditable || savingRoute === route.feature}
                    onChange={event => setDrafts(current => ({ ...current,
                      [route.feature]: { ...current[route.feature], serviceScopeFullTextApproved: event.target.checked } }))} />
                    <span>Allow configured AI providers to process the complete Service Scope for SOW/GSD generation</span>
                  </label>
                  <small>Separate full-text approval for the new Service Scope field. Its complete contents are sent without sanitization, including anything entered there. Customer-record fields, commercial data and attachments are not automatically included. Provider charges may apply. Saving this approval does not alter your provider order.</small>
                  {!route.serviceScopeApprovalEditable && !routeReadOnly ? <small>Service Scope migration 124 must be applied before this approval can be saved.</small> : null}
                </div> : null}
                {route.executionPolicy ? (
                  <section className={`celar-ai-routing__effective-policy is-${route.executionPolicy.status}`} aria-label={`${route.displayName} saved execution policy`}>
                    <strong>Saved execution policy: {title(route.executionPolicy.status)}</strong>
                    <p>{route.executionPolicy.message}</p>
                    <p><strong>Effective order:</strong> {(route.effectiveTargets ?? route.targets ?? []).map((target) => TARGET_LABELS[target] || target).join(' → ')}</p>
                    {(route.executionPolicy.blockers ?? []).length ? <ul>{(route.executionPolicy.blockerDetails ?? route.executionPolicy.blockers.map(title)).map((blocker) => <li key={blocker}>{blocker}</li>)}</ul> : null}
                    {route.executionPolicy.generationMode === 'private_evidence_wbs_with_generic_external_assistance' ? <p>External providers offer generic planning guidance for FlowHive. Detailed WBS generation still uses the private planning runtime.</p> : null}
                    {sowRoute ? <p>A SOW phase completes only with a validated generation result. The local template does not mark a failed phase complete.</p> : null}
                  </section>
                ) : null}
                {unsaved ? <p className="celar-ai-routing__unsaved" role="status">Unsaved changes. The effective order above reflects the saved policy.</p> : null}
                {!localLast ? <p className="is-error">Governed local template must remain final.</p> : null}
                {duplicate ? <p className="is-error">Every route position must be unique.</p> : null}
                <footer>
                  <span>Revision {route.revision ?? 0} · {route.deploymentManaged ? 'Deployment-managed' : route.persisted ? 'Persisted' : 'Default policy'}</span>
                  <div>
                    <button type="button" className="is-secondary" onClick={() => resetRoute(route.feature)} disabled={routeReadOnly || savingRoute === route.feature}>Reset</button>
                    <button type="button" onClick={() => saveRoute(route.feature)} disabled={routeReadOnly || savingRoute === route.feature || duplicate || !localLast}>
                      {routeReadOnly ? 'Read-only' : savingRoute === route.feature ? 'Saving…' : 'Save route'}
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
          <li>Stored SOW/GSD attachments, email, customer records, employee records, contracts, rates and financial fields are not automatically added to Service Scope requests.</li>
          <li>Legacy modes use approved backend-owned capsules. The separate full-text Service Scope mode sends exactly the saved field, without sanitization, only after explicit Module 064 approval. Eligibility never reorders saved provider priorities.</li>
          <li>A safety refusal stops routing; a later provider is not used to bypass it.</li>
          <li>No AI route automatically saves or submits time, publishes a SOW, baselines a plan, sends a closeout message, changes financial data, or deploys software.</li>
        </ul>
      </aside>
    </section>
  );
}
