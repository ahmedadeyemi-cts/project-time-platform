import { useCallback, useEffect, useState } from 'react';
import './celar-ai-provider-bridge-panel.css';

function title(value) {
  return String(value ?? 'not reported')
    .replaceAll('_', ' ')
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(payload.message || `Celar AI provider readiness returned HTTP ${response.status}.`);
  }
  return payload;
}

export default function CelarAiProviderBridgePanel() {
  const [state, setState] = useState({ loading: true, error: '', payload: null });

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const payload = await readJson(await fetch('/api/celar-ai/v1/provider-bridge/readiness', {
        method: 'GET',
        cache: 'no-store',
        credentials: 'include',
        headers: { Accept: 'application/json' }
      }));
      setState({ loading: false, error: '', payload });
    } catch (error) {
      setState({
        loading: false,
        error: error instanceof Error ? error.message : 'Celar AI provider readiness could not be loaded.',
        payload: null
      });
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const payload = state.payload;
  const privateModel = payload?.privateModel;
  const ready = privateModel?.ready === true;

  return (
    <section className="celar-ai-provider-bridge" aria-labelledby="celar-ai-provider-bridge-title">
      <div className="celar-ai-provider-bridge__heading">
        <div>
          <p>Celar AI orchestration boundary</p>
          <h2 id="celar-ai-provider-bridge-title">Private intelligence and governed provider routing</h2>
          <span>
            Celar AI is the private operational-intelligence layer inside Pulse. Module 064 remains the authority for
            provider credentials, approved models, health, usage, circuit breakers, and sanitized external fallback.
          </span>
        </div>
        <button type="button" onClick={load} disabled={state.loading}>
          {state.loading ? 'Checking…' : 'Refresh Celar AI readiness'}
        </button>
      </div>

      {state.error ? (
        <div className="celar-ai-provider-bridge__state is-error" role="alert">
          <strong>Celar AI readiness is unavailable</strong>
          <span>{state.error}</span>
        </div>
      ) : null}

      {state.loading && !payload ? (
        <div className="celar-ai-provider-bridge__state" role="status">
          Loading the private-model and Module 064 relationship…
        </div>
      ) : null}

      {payload ? (
        <>
          <div className="celar-ai-provider-bridge__summary">
            <article>
              <span>Celar AI role</span>
              <strong>Private orchestrator</strong>
              <small>Not a public vendor provider</small>
            </article>
            <article>
              <span>Private model</span>
              <strong className={ready ? 'is-ready' : 'is-pending'}>{ready ? 'Ready' : 'Not configured'}</strong>
              <small>{privateModel?.model || 'No model selected'}</small>
            </article>
            <article>
              <span>Confidential context</span>
              <strong>{privateModel?.confidentialContextEligible ? 'Private route eligible' : 'Private route unavailable'}</strong>
              <small>Raw internal documents never use public providers</small>
            </article>
            <article>
              <span>External assistance</span>
              <strong>Optional and sanitized</strong>
              <small>Claude or OpenAI only after DLP policy</small>
            </article>
          </div>

          <div className="celar-ai-provider-bridge__architecture">
            <article>
              <h3>Celar AI</h3>
              <p>{payload.architecture?.celarAiRole}</p>
              <ul>
                <li>Retrieves only authorized Pulse evidence.</li>
                <li>Uses deterministic module tools for facts and calculations.</li>
                <li>Prefers the private Celar AI model for restricted context.</li>
                <li>Verifies evidence before displaying an answer or draft.</li>
              </ul>
            </article>
            <div aria-hidden="true">→</div>
            <article>
              <h3>Module 064</h3>
              <p>{payload.architecture?.module064Role}</p>
              <ul>
                <li>Stores provider secrets as write-only values.</li>
                <li>Controls approved models and provider availability.</li>
                <li>Applies timeouts, rate limits, and circuit breakers.</li>
                <li>Stops routing after a safety refusal.</li>
              </ul>
            </article>
          </div>

          <div className="celar-ai-provider-bridge__routes">
            <div className="celar-ai-provider-bridge__subheading">
              <div><p>Feature policy</p><h3>Celar AI routing by business capability</h3></div>
              <span>{title(payload.status)}</span>
            </div>
            <div className="celar-ai-provider-bridge__route-grid">
              {(payload.featureRoutes ?? []).map((route) => (
                <article key={route.feature}>
                  <strong>{title(route.feature)}</strong>
                  <dl>
                    <div><dt>Primary</dt><dd>{title(route.primary)}</dd></div>
                    <div><dt>External</dt><dd>{title(route.external)}</dd></div>
                  </dl>
                </article>
              ))}
            </div>
          </div>

          <div className="celar-ai-provider-bridge__guardrails">
            <strong>Private-first guardrails</strong>
            <ul>{(payload.rules ?? []).map((rule) => <li key={rule}>{rule}</li>)}</ul>
            <small>
              Endpoint values, bearer tokens, API keys, document text, and provider secrets are intentionally not returned to this page.
            </small>
          </div>
        </>
      ) : null}
    </section>
  );
}
