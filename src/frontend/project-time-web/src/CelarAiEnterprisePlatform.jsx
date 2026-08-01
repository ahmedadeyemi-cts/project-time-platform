import { useEffect, useState } from 'react';
import CelarAiArchitectureOverview from './CelarAiArchitectureOverview.jsx';
import CelarAiSolutionComposer from './CelarAiSolutionComposer.jsx';
import './celar-ai-enterprise-platform.css';

const CAPABILITIES = Object.freeze([
  ['Ask & Search', 'Ask detailed questions about modules, workflows, documents, APIs, troubleshooting, reports, financials, and future enhancements.', '011 / 999'],
  ['People & Work', 'Explain authorized assignments, planned workload, task status, capacity, approvals, and FlowHive evidence without inferring surveillance.', '001 / 002 / 019 / 066 / 070'],
  ['Timesheet Intelligence', 'Draft a factual description from the Engineer note, selected work item, and authorized SOW, GSD, design, and project evidence.', '001'],
  ['SOW Composer', 'Prepare a comprehensive, non-binding SOW draft with scope, exclusions, responsibilities, deliverables, acceptance, assumptions, dependencies, and risks.', '025'],
  ['PM Planning & Scheduling', 'Create a cited WBS, milestones, dependencies, roles, high-level business-day timeline, risks, assumptions, and open questions.', '066'],
  ['Project Diagrams', 'Turn a private project-plan draft into an accessible flow, dependency, timeline, or swimlane-style visual with review gates.', '011 / 066'],
  ['Reports & Financials', 'Explain deterministic financial, utilization, capacity, billing, invoice, contract, and reporting results without inventing values.', '003 / 030 / 039 / 042 / 055B / 060'],
  ['API & Troubleshooting', 'Discover APIs from the running ASP.NET endpoint registry and correlate authorized operational, release, SLO, defect, and diagnostic evidence.', '013 / 016 / 076 / 077 / 078 / 998'],
  ['Knowledge & RAG', 'Process, classify, cite, index, retrieve, and revoke permission-scoped project and operating knowledge inside the private boundary.', '011 / 019'],
  ['Training & Evaluation', 'Govern reviewed datasets, LoRA/QLoRA jobs, frozen evaluations, model registry, Test promotion, rollback, and feedback review.', '011 / 064']
]);

function titleFrom(value) {
  return String(value ?? '').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}
async function getJson(path) {
  const response = await fetch(path, { method: 'GET', cache: 'no-store', headers: { Accept: 'application/json' } });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.message || `Request returned HTTP ${response.status}.`);
  return payload;
}

function ReadinessCard({ label, value, detail, tone = 'neutral' }) {
  return <article className={`celar-ai-platform-readiness-card is-${tone}`}><span>{label}</span><strong>{value}</strong><small>{detail}</small></article>;
}

export default function CelarAiEnterprisePlatform() {
  const [state, setState] = useState({ loading: true, payload: null, error: '' });

  async function load() {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const payload = await getJson('/api/celar-ai/v1/platform/readiness');
      setState({ loading: false, payload, error: '' });
    } catch (error) {
      setState({ loading: false, payload: null, error: error instanceof Error ? error.message : 'Celar AI readiness could not be loaded.' });
    }
  }

  useEffect(() => { void load(); }, []);

  const readiness = state.payload?.readiness ?? {};
  const privateRag = readiness.privateRag ?? {};
  const external = readiness.externalFallback ?? {};
  const capabilities = Array.isArray(readiness.capabilities) ? readiness.capabilities : [];

  return (
    <section className="celar-ai-enterprise-platform" aria-label="Celar AI enterprise platform">
      <header className="celar-ai-platform-hero">
        <div>
          <p>Module 011 · US Signal Solution Provider operational intelligence</p>
          <h1>Celar AI Enterprise Platform</h1>
          <span>
            A private-first, permission-aware AI platform for Help and Search, people and work intelligence,
            Timesheet descriptions, SOW drafting, project planning, schedules, diagrams, reports, financial insight,
            API discovery, troubleshooting, training, evaluation, and governed model routing.
          </span>
        </div>
        <div className="celar-ai-platform-actions">
          <button type="button" onClick={load} disabled={state.loading}>{state.loading ? 'Checking readiness…' : 'Refresh readiness'}</button>
          <a href="#ai-provider-configuration">Open Module 064</a>
        </div>
      </header>

      <div className="celar-ai-platform-identity-bar">
        <div><strong>Created by Dr. Ahmed Adeyemi</strong><span>Manager of Professional Services · Speed of light. Speed of delivery.</span></div>
        <div><span>Platform</span><strong>Pulse</strong></div>
        <div><span>Intelligence</span><strong>Celar AI</strong></div>
        <div><span>External gateway</span><strong>Module 064</strong></div>
      </div>

      {state.error ? <div className="celar-ai-platform-error">{state.error} The architecture and review composer remain visible; private execution requires the corresponding runtime services and permissions.</div> : null}

      <section className="celar-ai-platform-readiness" aria-label="Celar AI platform readiness">
        <ReadinessCard label="Platform interface" value={state.loading ? 'Checking' : titleFrom(readiness.status || 'source available')} detail={readiness.contractVersion || 'Celar AI enterprise platform contract'} tone={state.error ? 'warning' : 'ready'} />
        <ReadinessCard label="Private RAG" value={titleFrom(privateRag.status || 'not checked')} detail={privateRag.inferenceConfigured ? 'Private inference configured' : 'Private inference may require configuration'} tone={privateRag.status === 'private_rag_ready' ? 'ready' : 'warning'} />
        <ReadinessCard label="Sanitized external fallback" value={external.enabled ? 'Enabled by policy' : 'Disabled by default'} detail="Generic problem only through Module 064; raw internal context prohibited" tone={external.enabled ? 'warning' : 'ready'} />
        <ReadinessCard label="Supported solution modes" value={String(asArray(readiness.supportedModes).length || 5)} detail="Timesheet · SOW · Plan · Timeline · Diagram" tone="ready" />
      </section>

      <CelarAiArchitectureOverview />

      <section className="celar-ai-platform-capabilities" aria-labelledby="celar-ai-capabilities-title">
        <div className="celar-ai-platform-section-heading"><div><p>Enterprise capability map</p><h2 id="celar-ai-capabilities-title">One governed intelligence layer across Pulse</h2><span>Celar AI explains, drafts, visualizes, and troubleshoots. Owning modules remain the source of truth and consequential actions remain human controlled.</span></div></div>
        <div>
          {CAPABILITIES.map(([name, description, owner], index) => {
            const runtime = capabilities.find((item) => String(item.code).replaceAll('_', ' ').toLowerCase().includes(name.split(' ')[0].toLowerCase()));
            return <article key={name}><span>{String(index + 1).padStart(2, '0')}</span><h3>{name}</h3><p>{description}</p><footer><strong>{owner}</strong><small>{runtime ? titleFrom(runtime.state) : 'Governed capability'}</small></footer></article>;
          })}
        </div>
      </section>

      <CelarAiSolutionComposer />

      <section className="celar-ai-platform-controls" aria-label="Celar AI enterprise controls">
        <article><strong>Private by default</strong><p>SOW, GSD, customer, project, employee, rate, financial, architecture, prompt, chunk, and vector content stays inside the approved private boundary.</p></article>
        <article><strong>Confidence before fallback</strong><p>Celar AI first retrieves authorized evidence and runs governed tools. Low confidence triggers clarification, more retrieval, or an optional generic sanitized reasoning capsule—not automatic data export.</p></article>
        <article><strong>Human review</strong><p>Celar AI cannot submit time, publish a SOW, baseline a plan, assign resources, commit dates, change financials, grant permissions, deploy software, or promote a model through this interface.</p></article>
        <article><strong>Source-grounded output</strong><p>Every project-specific draft reports its evidence, missing sources, conflicts, coverage, confidence, data-as-of time, and review requirements.</p></article>
      </section>
    </section>
  );
}

function asArray(value) { return Array.isArray(value) ? value : []; }
