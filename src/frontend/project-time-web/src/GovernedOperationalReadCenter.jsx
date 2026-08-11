import React, { useEffect, useMemo, useState } from 'react';
import logo from '../brand/ussignal.png';
import './governed-operational-read-center.css';

const MODULE_SETUP = {
  '075': { owner: 'Integration Engineering', permissions: 'Integration administrator', adapter: 'Per-source webhook/queue connector with schema registry', route: '#crm-integration', steps: ['Register each source, owner, authentication method, and payload classification.', 'Publish a versioned event contract with validation and idempotency.', 'Run a signed connection test before delivery, retry, replay, or quarantine is enabled.'] },
  '077': { owner: 'Platform Engineering', permissions: 'Administrator or Super Administrator', adapter: 'GitHub Actions OIDC + Azure deployment identity', route: '#cicd-pipeline', steps: ['Verify repository Actions and protected environments.', 'Bind environment-scoped Azure OIDC federation.', 'Run CI, immutable image, health, UAT, and rollback gates.'] },
  '078': { owner: 'Reliability Engineering', permissions: 'Reliability administrator', adapter: 'Provider-neutral OpenTelemetry endpoint', route: '#system-diagnostics', steps: ['Register the API and web service owners.', 'Connect an approved metrics, logs, and traces endpoint.', 'Define SLOs, alert ownership, retention, and escalation.'] },
  '079': { owner: 'Data Governance', permissions: 'Data governance administrator', adapter: 'Optional catalog, legal-hold, and disposition connectors', route: '#audit-history', steps: ['Assign accountable domain owners.', 'Approve classification and retention policies.', 'Connect legal-hold/disposition adapters before destructive actions.'] },
  '080': { owner: 'Customer Delivery', permissions: 'Delivery acceptance administrator', adapter: 'Optional customer sharing and e-signature adapters', route: '#project-workspace', steps: ['Define engagement, milestone, deliverable, and acceptance owners.', 'Set acceptance criteria and authorized decision makers.', 'Configure expiring, revocable sharing before customer access.'] }
};

const format = (value) => typeof value === 'boolean' ? (value ? 'Yes' : 'No') : String(value ?? 'Not recorded');
const words = (value) => String(value ?? '').replaceAll('-', ' ').replaceAll('_', ' ');

export default function GovernedOperationalReadCenter({ module, title, subtitle, basePath, surfaces }) {
  const [data, setData] = useState({});
  const [errors, setErrors] = useState({});
  const [loading, setLoading] = useState(true);
  const [selected, setSelected] = useState(surfaces[0]);
  const setup = MODULE_SETUP[module] ?? { owner: 'Platform Operations', permissions: 'Authorized administrator', adapter: 'Approved owner adapter', route: '#system-architecture', steps: ['Review the live capability and gap.', 'Configure the bounded adapter.', 'Run the connection test and retain evidence.'] };

  useEffect(() => {
    let active = true;
    setLoading(true);
    Promise.allSettled(surfaces.map(async (key) => {
      const response = await fetch(`${basePath}/${key}`, { credentials: 'include' });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw Object.assign(new Error(body.message || `Unable to load ${key}.`), { key });
      return [key, body];
    })).then((results) => {
      if (!active) return;
      const rows = {}; const failures = {};
      results.forEach((result, index) => { const key = surfaces[index]; if (result.status === 'fulfilled') rows[key] = result.value[1]; else failures[key] = result.reason?.message || `Unable to load ${key}.`; });
      setData(rows); setErrors(failures); setLoading(false);
    });
    return () => { active = false; };
  }, [basePath, surfaces]);

  const readyCount = useMemo(() => surfaces.filter((key) => data[key]?.liveData && !errors[key]).length, [data, errors, surfaces]);
  const activeSurface = data[selected];
  const activeError = errors[selected];

  return (
    <section className="governed-operations-center" data-module={module}>
      <header className="governed-operations-hero"><img src={logo} alt="US Signal" /><div><span>Pulse · Module {module}</span><h1>{title}</h1><p>{subtitle}</p></div><div className="governed-operations-score"><strong>{readyCount}/{surfaces.length}</strong><span>live surfaces</span></div></header>
      <aside><strong>Review before execution:</strong> Every section is selectable now. Readiness, owner, permissions, missing adapters, setup steps, and history remain visible even when an external connector is not yet authorized.</aside>
      <div className="governed-operations-layout">
        <nav aria-label={`${title} sections`}>{surfaces.map((key) => { const surface = data[key]; const error = errors[key]; return <button type="button" key={key} className={selected === key ? 'is-active' : ''} onClick={() => setSelected(key)}><span><strong>{surface?.title || words(key)}</strong><small>{surface?.description || (error ? error : 'Open setup and readiness')}</small></span><b className={error ? 'is-error' : surface?.liveData ? 'is-ready' : 'is-setup'}>{error ? 'Error' : surface?.liveData ? 'Live' : 'Setup'}</b></button>; })}</nav>
        <main>
          {loading ? <p role="status">Loading operational information…</p> : null}
          <article className="governed-operations-active"><div className="governed-operations-heading"><div><small>{words(selected)}</small><h2>{activeSurface?.title || words(selected)}</h2></div><span className={activeError ? 'is-error' : activeSurface?.liveData ? 'is-ready' : 'is-setup'}>{activeError ? 'Unavailable' : activeSurface?.liveData ? 'Ready to review' : 'Configuration required'}</span></div>{activeError ? <p role="alert">{activeError}</p> : <><p>{activeSurface?.description || 'This surface is owned but has not recorded live entries yet.'}</p><div className="governed-operations-entries">{activeSurface?.entries?.length ? activeSurface.entries.map((entry, index) => <dl key={index}>{Object.entries(entry).map(([name, value]) => <div key={name}><dt>{words(name)}</dt><dd>{format(value)}</dd></div>)}</dl>) : <p>No live records have been recorded. The platform does not fabricate a configured state.</p>}</div></>}</article>
          <article className="governed-operations-setup"><div className="governed-operations-heading"><div><small>Activation guide</small><h2>Configuration &amp; connection test</h2></div><span className="is-setup">Guarded</span></div><dl><div><dt>Owner</dt><dd>{setup.owner}</dd></div><div><dt>Required access</dt><dd>{setup.permissions}</dd></div><div><dt>Adapter</dt><dd>{setup.adapter}</dd></div></dl><ol>{setup.steps.map((step) => <li key={step}>{step}</li>)}</ol><div className="governed-operations-actions"><a href={setup.route}>Open owning control</a><a href="#audit-history">Review change history</a></div><p className="governed-operations-guardrail">Production-changing actions stay behind preview, separate approval, scoped credentials, audit evidence, verification, and rollback. View-As remains read-only.</p></article>
        </main>
      </div>
    </section>
  );
}
