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

  return token
    ? {
        Authorization: `Bearer ${token}`,
        'X-ProjectPulse-Session': token,
        'X-Project-Pulse-Session': token,
        'X-Session-Token': token
      }
    : {};
}

async function readJson(path, authSession) {
  const response = await fetch(path, {
    method: 'GET',
    credentials: 'include',
    headers: requestHeaders(authSession)
  });

  const payload = await response.json().catch(() => null);

  if (!response.ok) {
    throw new Error(
      payload?.message
      ?? `System Architecture request returned HTTP ${response.status}.`
    );
  }

  return payload;
}

function titleCase(value) {
  return String(value ?? 'unknown')
    .replace(/_/g, ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function statusTone(value) {
  const normalized = String(value ?? '').toLowerCase();

  if (['healthy', 'live', 'active'].includes(normalized)) return 'healthy';
  if (['delegated', 'governed', 'runtime_managed'].includes(normalized)) return 'governed';
  if (['degraded', 'warning', 'unavailable'].includes(normalized)) return 'attention';
  return 'neutral';
}

function formatTimestamp(value) {
  if (!value) return 'Not observed';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Not observed' : date.toLocaleString();
}

export default function SystemArchitectureCenter({ authSession }) {
  const [state, setState] = useState({
    loading: true,
    overview: null,
    dependencies: null,
    error: ''
  });

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));

    try {
      const [overview, dependencies] = await Promise.all([
        readJson('/api/system-architecture/overview', authSession),
        readJson('/api/system-architecture/dependency-status', authSession)
      ]);

      setState({ loading: false, overview, dependencies, error: '' });
    } catch (error) {
      setState((current) => ({
        ...current,
        loading: false,
        error: error?.message ?? 'System Architecture is temporarily unavailable.'
      }));
    }
  }, [authSession]);

  useEffect(() => {
    void load();
  }, [load]);

  const layerGroups = useMemo(() => {
    const layers = [...(state.overview?.layers ?? [])]
      .sort((left, right) => Number(left.order) - Number(right.order));
    const nodes = state.overview?.nodes ?? [];

    return layers.map((layer) => ({
      ...layer,
      nodes: nodes.filter((node) => node.layer === layer.id)
    }));
  }, [state.overview]);

  const nodeNames = useMemo(() => {
    return new Map(
      (state.overview?.nodes ?? []).map((node) => [node.id, node.name])
    );
  }, [state.overview]);

  const summary = useMemo(() => ({
    layers: state.overview?.layers?.length ?? 0,
    nodes: state.overview?.nodes?.length ?? 0,
    connections: state.overview?.connections?.length ?? 0,
    dependencies: state.dependencies?.dependencies?.length ?? 0
  }), [state.overview, state.dependencies]);

  const observedAt = state.dependencies?.observedAt ?? state.overview?.generatedAt;

  return (
    <section
      id="system-architecture"
      className="panel system-architecture-center projectpulse-module-standard"
      data-module="068"
      data-brand="us-signal"
      data-mode="read-only"
      data-contract-version={state.overview?.contractVersion ?? '2026-07-19.1'}
      aria-labelledby="system-architecture-title"
    >
      <header className="system-architecture-hero">
        <img
          className="projectpulse-module-standard__logo"
          src={usSignalLogoDataUrl}
          alt="US Signal"
        />
        <div>
          <p className="eyebrow">Module 068 · Administrator read-only</p>
          <h1 id="system-architecture-title">System Architecture &amp; Dependency Map</h1>
          <p>
            Versioned visibility into ProjectPulse components, data movement,
            authentication boundaries, integrations, environments, and the
            operational centers that own live health.
          </p>
        </div>

        <div className="system-architecture-hero-actions">
          <span className="system-architecture-version">
            Contract {state.overview?.contractVersion ?? 'loading'}
          </span>
          <button type="button" className="secondary-action" onClick={load} disabled={state.loading}>
            {state.loading ? 'Refreshing…' : 'Refresh map'}
          </button>
        </div>
      </header>

      {state.error ? (
        <div className="system-architecture-banner error" role="alert">
          <strong>Architecture map unavailable</strong>
          <span>{state.error}</span>
        </div>
      ) : null}

      {state.overview?.access?.isViewAs ? (
        <div className="system-architecture-banner governed">
          View-As is active. Backend access remains based on the actual
          administrator session and is not transferred to the viewed user.
        </div>
      ) : null}

      <div className="system-architecture-summary" aria-label="Architecture summary">
        <article>
          <span>Logical layers</span>
          <strong>{summary.layers}</strong>
          <small>Experience through operations</small>
        </article>
        <article>
          <span>Components</span>
          <strong>{summary.nodes}</strong>
          <small>Role-safe logical nodes</small>
        </article>
        <article>
          <span>Communication paths</span>
          <strong>{summary.connections}</strong>
          <small>Classified data flows</small>
        </article>
        <article>
          <span>Dependencies</span>
          <strong>{summary.dependencies}</strong>
          <small>Direct and delegated status</small>
        </article>
      </div>

      <section className="system-architecture-panel">
        <div className="system-architecture-heading">
          <div>
            <p className="eyebrow">Logical component map</p>
            <h2>How ProjectPulse communicates</h2>
          </div>
          <div className="system-architecture-observation">
            <span>{titleCase(state.overview?.scope?.environment)}</span>
            <small>Observed {formatTimestamp(observedAt)}</small>
          </div>
        </div>

        <div className="system-architecture-layers" role="list">
          {layerGroups.map((layer) => (
            <article className="system-architecture-layer" key={layer.id} role="listitem">
              <header>
                <span>{String(layer.order).padStart(2, '0')}</span>
                <div>
                  <h3>{layer.name}</h3>
                  <p>{layer.description}</p>
                </div>
              </header>

              <div className="system-architecture-node-grid">
                {layer.nodes.map((node) => (
                  <section className={`system-architecture-node kind-${node.kind}`} key={node.id}>
                    <div className="system-architecture-node-title">
                      <strong>{node.name}</strong>
                      <span>{titleCase(node.kind)}</span>
                    </div>
                    <p>{node.description}</p>
                    <ul>
                      {(node.responsibilities ?? []).map((responsibility) => (
                        <li key={responsibility}>{responsibility}</li>
                      ))}
                    </ul>
                  </section>
                ))}
              </div>
            </article>
          ))}
        </div>
      </section>

      <section className="system-architecture-panel">
        <div className="system-architecture-heading">
          <div>
            <p className="eyebrow">Data and communication</p>
            <h2>Versioned connection registry</h2>
          </div>
          <span>{summary.connections} governed paths</span>
        </div>

        <div className="system-architecture-table-wrap">
          <table className="system-architecture-table">
            <thead>
              <tr>
                <th>From</th>
                <th>To</th>
                <th>Protocol</th>
                <th>Data / purpose</th>
                <th>Classification</th>
              </tr>
            </thead>
            <tbody>
              {(state.overview?.connections ?? []).map((connection) => (
                <tr key={`${connection.from}-${connection.to}-${connection.protocol}`}>
                  <td>{nodeNames.get(connection.from) ?? connection.from}</td>
                  <td>{nodeNames.get(connection.to) ?? connection.to}</td>
                  <td>{connection.protocol}</td>
                  <td>{connection.data}</td>
                  <td>
                    <span className="system-architecture-classification">
                      {titleCase(connection.classification)}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <div className="system-architecture-two-column">
        <section className="system-architecture-panel">
          <div className="system-architecture-heading">
            <div>
              <p className="eyebrow">Security</p>
              <h2>Trust boundaries</h2>
            </div>
          </div>

          <div className="system-architecture-card-list">
            {(state.overview?.trustBoundaries ?? []).map((boundary) => (
              <article key={boundary.id}>
                <strong>{boundary.name}</strong>
                <p>{boundary.control}</p>
                <small>
                  {(boundary.nodeIds ?? []).map((id) => nodeNames.get(id) ?? id).join(' · ')}
                </small>
              </article>
            ))}
          </div>
        </section>

        <section className="system-architecture-panel">
          <div className="system-architecture-heading">
            <div>
              <p className="eyebrow">Release flow</p>
              <h2>Environment communication</h2>
            </div>
          </div>

          <ol className="system-architecture-environments">
            {(state.overview?.environments ?? []).map((environment) => (
              <li key={environment.id}>
                <span>{environment.order}</span>
                <div>
                  <strong>{environment.name}</strong>
                  <p>{environment.configuration}</p>
                  <small>{environment.gate}</small>
                </div>
              </li>
            ))}
          </ol>
        </section>
      </div>

      <section className="system-architecture-panel">
        <div className="system-architecture-heading">
          <div>
            <p className="eyebrow">Dependency ownership</p>
            <h2>Safe status registry</h2>
            <p>
              Module 068 checks session and database access directly. Existing
              operational modules remain authoritative for every delegated status.
            </p>
          </div>
        </div>

        <div className="system-architecture-dependencies">
          {(state.dependencies?.dependencies ?? []).map((dependency) => (
            <article key={dependency.id}>
              <div>
                <span className={`system-architecture-status ${statusTone(dependency.state)}`}>
                  {titleCase(dependency.state)}
                </span>
                <strong>{dependency.name}</strong>
              </div>
              <p>{dependency.evidence}</p>
              <small>{titleCase(dependency.observation)}</small>
              {dependency.href ? (
                <a href={dependency.href}>Open live status</a>
              ) : null}
            </article>
          ))}
        </div>
      </section>

      <section className="system-architecture-panel">
        <div className="system-architecture-heading">
          <div>
            <p className="eyebrow">Operational handoff</p>
            <h2>Live health and evidence centers</h2>
          </div>
        </div>

        <div className="system-architecture-status-links">
          {(state.overview?.statusLinks ?? []).map((link) => (
            <a href={link.href} key={link.id}>
              <span>{link.owner}</span>
              <strong>{link.name}</strong>
              <small>{link.apiPath}</small>
            </a>
          ))}
        </div>
      </section>

      <section className="system-architecture-panel system-architecture-guardrails">
        <div>
          <p className="eyebrow">Contract boundary</p>
          <h2>No secrets. No discovery scan. No mutations.</h2>
        </div>
        <ul>
          {(state.overview?.guardrails ?? []).map((guardrail) => (
            <li key={guardrail}>{guardrail}</li>
          ))}
        </ul>
      </section>
    </section>
  );
}
