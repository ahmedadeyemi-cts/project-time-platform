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
  claude: 'Optional sanitized external reasoning through Module 064.',
  openai: 'Optional sanitized external reasoning through Module 064.',
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
  const [state, setState] = useState({ loading: true, error: '', routes: [], profile: null, consumers: [] });
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
      const [routesPayload, profilePayload, consumersPayload] = await Promise.all([
        readJson(await fetch('/api/ai-configuration/routes', { credentials: 'include', cache: 'no-store' })),
        readJson(await fetch('/api/ai-configuration/private-model', { credentials: 'include', cache: 'no-store' })),
        readJson(await fetch('/api/ai-configuration/consumers', { credentials: 'include', cache: 'no-store' })),
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
        consumers: consumersPayload.consumers ?? [],
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

  return (
    <section className="celar-ai-routing" aria-labelledby="celar-ai-routing-title">
      <header className="celar-ai-routing__header">
        <div>
          <p>Celar AI and Module 064 control plane</p>
          <h2 id="celar-ai-routing-title">Private-first targets and capability routing</h2>
          <span>
            Celar AI is the preferred private target. Claude and OpenAI are optional sanitized stages, and the governed
            local template remains the deterministic final fallback. Target order is configurable; privacy policy is not.
          </span>
        </div>
        <button type="button" onClick={() => load()} disabled={state.loading}>
          {state.loading ? 'Refreshing…' : 'Refresh routing'}
        </button>
      </header>

      {notice ? <div className="celar-ai-routing__notice" role="status">{notice}</div> : null}
      {state.error ? <div className="celar-ai-routing__error" role="alert">{state.error}</div> : null}
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

        <form className="celar-ai-routing__profile-form" onSubmit={savePrivateSettings}>
          <label>
            <span>Private endpoint</span>
            <input
              type="url"
              value={profileForm.endpoint}
              onChange={(event) => setProfileForm((current) => ({ ...current, endpoint: event.target.value }))}
              placeholder={profile?.endpointConfigured ? 'Leave blank to preserve the encrypted endpoint' : 'https://private-host/v1/chat/completions'}
              autoComplete="off"
            />
            <small>The endpoint must use a private IP, loopback, or approved private DNS suffix. The saved value is never returned.</small>
          </label>
          <label>
            <span>Private model or deployment name</span>
            <input
              value={profileForm.model}
              onChange={(event) => setProfileForm((current) => ({ ...current, model: event.target.value }))}
              placeholder="Private model name"
            />
          </label>
          <label>
            <span>Private-host allowlist</span>
            <textarea
              value={profileForm.allowlist}
              onChange={(event) => setProfileForm((current) => ({ ...current, allowlist: event.target.value }))}
              placeholder="One hostname or private DNS suffix per line; leave blank to preserve existing/default policy"
            />
          </label>
          <div className="celar-ai-routing__checks">
            <label><input type="checkbox" checked={profileForm.enabled} onChange={(event) => setProfileForm((current) => ({ ...current, enabled: event.target.checked }))} /> Enable the private Celar AI target</label>
            <label><input type="checkbox" checked={profileForm.requirePrivateModelForDocuments} onChange={(event) => setProfileForm((current) => ({ ...current, requirePrivateModelForDocuments: event.target.checked }))} /> Require private inference for document-grounded answers</label>
          </div>
          <button type="submit" disabled={savingProfile}>{savingProfile ? 'Saving…' : 'Save private-model settings'}</button>
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
            />
            <button type="submit" disabled={savingToken || !profileForm.bearerToken.trim()}>{savingToken ? 'Saving…' : 'Save securely'}</button>
            <button type="button" onClick={testPrivateModel} disabled={testingProfile || !profile?.configured}>{testingProfile ? 'Testing…' : 'Test private model'}</button>
          </div>
          <small>The token is AES-GCM encrypted and cannot be viewed after saving.</small>
        </form>
      </section>

      <section className="celar-ai-routing__routes" aria-labelledby="capability-route-title">
        <div className="celar-ai-routing__subheading">
          <div><p>Capability routing</p><h3 id="capability-route-title">Primary, secondary, tertiary, and final fallback</h3></div>
          <span>Default: Celar AI → Claude → OpenAI → Governed local template</span>
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
                        disabled={position === 3}
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
                  <span>Revision {route.revision ?? 0} · {route.persisted ? 'Persisted' : 'Default policy'}</span>
                  <div>
                    <button type="button" className="is-secondary" onClick={() => resetRoute(route.feature)} disabled={savingRoute === route.feature}>Reset</button>
                    <button type="button" onClick={() => saveRoute(route.feature)} disabled={savingRoute === route.feature || duplicate || !localLast}>
                      {savingRoute === route.feature ? 'Saving…' : 'Save route'}
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
          <li>Claude and OpenAI receive only a policy-approved sanitized generic capsule.</li>
          <li>A safety refusal stops routing; a later provider is not used to bypass it.</li>
          <li>No AI route automatically saves or submits time, publishes a SOW, baselines a plan, sends a closeout message, changes financial data, or deploys software.</li>
        </ul>
      </aside>
    </section>
  );
}
