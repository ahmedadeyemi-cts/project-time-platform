import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './pulse-ai-center.css';
import './projectpulse-module-standard.css';

const PULSE_AI_TABS = Object.freeze([
  { id: 'overview', label: 'Overview', description: 'Model projects and governed architecture' },
  { id: 'knowledge', label: 'Knowledge & RAG', description: 'Permission-aware knowledge sources' },
  { id: 'datasets', label: 'Datasets', description: 'Reviewed and immutable training examples' },
  { id: 'training', label: 'Training', description: 'External LoRA and QLoRA job orchestration' },
  { id: 'evaluations', label: 'Evaluations', description: 'Quality, security, and permission gates' },
  { id: 'registry', label: 'Model Registry', description: 'Versioned model and adapter records' },
  { id: 'deployments', label: 'Deployments', description: 'Controlled test and production promotion' },
  { id: 'governance', label: 'Governance', description: 'Permissions, audit, and operating rules' }
]);

const FEATURE_TARGETS = Object.freeze([
  {
    code: 'timesheet_description',
    name: 'Timesheet description assistance',
    owner: 'Module 001',
    treatment: 'Live ProjectPulse context plus governed model behavior'
  },
  {
    code: 'sow_gsd_planning',
    name: 'SOW and GSD planning',
    owner: 'Modules 025 and 066',
    treatment: 'Approved templates through RAG; human review remains required'
  },
  {
    code: 'help_assistant',
    name: 'ProjectPulse help assistant',
    owner: 'ProjectPulse Help',
    treatment: 'User guide, module documentation, and permission-aware retrieval'
  },
  {
    code: 'closeout_communication',
    name: 'Closeout communication drafting',
    owner: 'Modules 040 and 041',
    treatment: 'Project-scoped context with explicit approval before delivery'
  },
  {
    code: 'project_flowhive_plan',
    name: 'Project FlowHive planning',
    owner: 'Module 066',
    treatment: 'Approved project context; no autonomous project mutation'
  }
]);

const KNOWLEDGE_SOURCES = Object.freeze([
  {
    name: 'ProjectPulse module documentation',
    scope: 'Approved repository documentation',
    classification: 'Internal',
    access: 'Module permission inherited',
    state: 'Planned'
  },
  {
    name: 'ProjectPulse complete user guide',
    scope: 'Module 999 published guidance',
    classification: 'Internal',
    access: 'Authenticated users',
    state: 'Planned'
  },
  {
    name: 'Role and permission definitions',
    scope: 'Modules 012 and 037 current policy evidence',
    classification: 'Restricted',
    access: 'Current effective user only',
    state: 'Planned'
  },
  {
    name: 'Approved SOW and delivery templates',
    scope: 'Reviewed commercial and engineering templates',
    classification: 'Confidential',
    access: 'Owning role and project scope',
    state: 'Planned'
  }
]);

const DATASET_CHECKS = Object.freeze([
  'Required conversational or prompt-completion schema is valid',
  'Passwords, API keys, tokens, and connection strings are absent',
  'Personal and customer information is removed or explicitly approved',
  'Duplicate and contradictory examples are identified',
  'Every expected answer was reviewed by an authorized person',
  'A validation set and held-out evaluation set remain separate',
  'The dataset version receives an immutable checksum before training'
]);

const TRAINING_STAGES = Object.freeze([
  { state: 'Draft', owner: 'Model project owner', rule: 'Business objective and expected behavior are documented.' },
  { state: 'Data review', owner: 'Dataset reviewer', rule: 'Training examples pass privacy, secret, and quality checks.' },
  { state: 'Approved', owner: 'Authorized approver', rule: 'The exact immutable dataset version is authorized.' },
  { state: 'Queued', owner: 'Training backend', rule: 'ProjectPulse records the external job identifier and configuration.' },
  { state: 'Training', owner: 'External GPU environment', rule: 'LoRA or QLoRA runs outside the ProjectPulse web/API process.' },
  { state: 'Evaluating', owner: 'Evaluation runner', rule: 'The candidate is compared with the base and current active models.' },
  { state: 'Awaiting approval', owner: 'Model approver', rule: 'Promotion remains blocked until required gates pass.' },
  { state: 'Staged', owner: 'Deployment operator', rule: 'The model is available only in an approved non-production environment.' },
  { state: 'Active', owner: 'Module 064 router', rule: 'Only approved features may route to the registered model.' },
  { state: 'Retired', owner: 'Model owner', rule: 'History, evaluation evidence, and artifact references remain preserved.' }
]);

const EVALUATION_GATES = Object.freeze([
  { gate: 'Task correctness', requirement: 'Meets the approved answer or scoring rubric', blocksPromotion: true },
  { gate: 'Unsupported claims', requirement: 'Does not invent records, permissions, or completed actions', blocksPromotion: true },
  { gate: 'Permission isolation', requirement: 'Never returns content outside the caller’s authorized scope', blocksPromotion: true },
  { gate: 'Structured output', requirement: 'Matches required JSON or business response schema', blocksPromotion: true },
  { gate: 'Safety and refusal', requirement: 'Stops on prohibited or policy-refused requests without unsafe failover', blocksPromotion: true },
  { gate: 'Latency and cost', requirement: 'Remains within the feature’s approved operating threshold', blocksPromotion: false }
]);

const MODEL_REGISTRY_FIELDS = Object.freeze([
  'Model name and semantic version',
  'Base model, publisher, and license',
  'Training method and immutable dataset version',
  'Adapter or model artifact location and checksum',
  'Tokenizer, context length, and chat template',
  'Evaluation suite, results, and approval decision',
  'Environment registrations and feature assignments',
  'Created, approved, activated, retired, and rollback history'
]);

const CAPABILITIES = Object.freeze([
  ['View Pulse AI', 'See non-secret projects, model status, and approved evidence.'],
  ['Manage Knowledge Sources', 'Register and refresh permission-aware RAG sources.'],
  ['Create Training Datasets', 'Build draft examples without approving them.'],
  ['Approve Training Datasets', 'Authorize an exact immutable dataset version.'],
  ['Start or Cancel Training', 'Control approved external training jobs.'],
  ['Run Evaluations', 'Execute governed model and permission test suites.'],
  ['Approve Model Versions', 'Approve a candidate after all required gates pass.'],
  ['Deploy to Test', 'Register a model in an approved test environment.'],
  ['Promote to Production', 'Activate an approved version through controlled routing.'],
  ['View AI Audit', 'Review the complete model lifecycle history.']
]);

const INITIAL_PROJECTS = Object.freeze([
  {
    id: 'pulse-ai-help-foundation',
    name: 'ProjectPulse Help Assistant',
    objective: 'Answer product and module questions from approved documentation without revealing restricted records.',
    strategy: 'RAG first',
    ownerModule: '999',
    baseModel: 'Not selected',
    status: 'Foundation only',
    sessionOnly: false
  },
  {
    id: 'pulse-ai-permission-explainer',
    name: 'Permission Explanation Assistant',
    objective: 'Explain current role and module permissions in plain language while the application remains the authorization authority.',
    strategy: 'RAG + evaluated prompt behavior',
    ownerModule: '012 / 037',
    baseModel: 'Not selected',
    status: 'Foundation only',
    sessionOnly: false
  }
]);

const EMPTY_PROJECT = Object.freeze({
  name: '',
  objective: '',
  ownerModule: '',
  baseModel: '',
  strategy: 'RAG first',
  classification: 'Internal'
});

function titleFrom(value) {
  return String(value || 'not checked')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function formatDate(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Not recorded' : parsed.toLocaleString();
}

function providerTone(provider, health) {
  if (!provider?.enabled) return 'inactive';
  if (!provider?.configured) return 'inactive';
  if (['available', 'ready'].includes(health?.status) || ['available', 'ready'].includes(health?.probeStatus)) return 'healthy';
  if (health?.probeStatus === 'checking') return 'checking';
  return 'degraded';
}

async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload.message || `Request returned HTTP ${response.status}.`);
    error.status = response.status;
    throw error;
  }
  return payload;
}

function LockedButton({ children }) {
  return (
    <button type="button" className="pulse-ai-locked-action" disabled title="This action is intentionally locked in the source-only foundation.">
      <span aria-hidden="true">🔒</span>
      {children}
    </button>
  );
}

function SectionHeading({ eyebrow, title, copy, action }) {
  return (
    <div className="pulse-ai-section-heading">
      <div>
        <p className="pulse-ai-eyebrow">{eyebrow}</p>
        <h2>{title}</h2>
        {copy ? <p>{copy}</p> : null}
      </div>
      {action ?? null}
    </div>
  );
}

export default function PulseAiCenter() {
  const [activeTab, setActiveTab] = useState('overview');
  const [providerState, setProviderState] = useState({ loading: true, payload: null, error: '', restricted: false });
  const [projects, setProjects] = useState([...INITIAL_PROJECTS]);
  const [draft, setDraft] = useState({ ...EMPTY_PROJECT });
  const [notice, setNotice] = useState('');

  const loadProviderStatus = useCallback(async () => {
    setProviderState((current) => ({ ...current, loading: true, error: '', restricted: false }));
    try {
      const payload = await readJson(await fetch('/api/ai-configuration', {
        method: 'GET',
        cache: 'no-store',
        headers: { Accept: 'application/json' }
      }));
      setProviderState({ loading: false, payload, error: '', restricted: false });
    } catch (error) {
      const restricted = error?.status === 401 || error?.status === 403;
      setProviderState({
        loading: false,
        payload: null,
        error: restricted
          ? 'Detailed provider configuration remains restricted to authorized Module 064 administrators.'
          : (error instanceof Error ? error.message : 'Provider status could not be loaded.'),
        restricted
      });
    }
  }, []);

  useEffect(() => {
    void loadProviderStatus();
  }, [loadProviderStatus]);

  const configuration = providerState.payload?.configuration;
  const providers = configuration?.providers ?? [];
  const healthByProvider = useMemo(
    () => new Map((providerState.payload?.health ?? []).map((item) => [item.provider, item])),
    [providerState.payload]
  );
  const remoteProviders = useMemo(
    () => providers.filter((provider) => provider.code !== 'local_template'),
    [providers]
  );
  const availableProviderCount = remoteProviders.filter((provider) => {
    const health = healthByProvider.get(provider.code);
    return provider.enabled && provider.configured && ['available', 'ready'].includes(health?.status || health?.probeStatus);
  }).length;
  const activeTabDefinition = PULSE_AI_TABS.find((tab) => tab.id === activeTab) ?? PULSE_AI_TABS[0];

  function updateDraft(field, value) {
    setDraft((current) => ({ ...current, [field]: value }));
  }

  function addProject(event) {
    event.preventDefault();
    const name = draft.name.trim();
    const objective = draft.objective.trim();
    if (!name || !objective) {
      setNotice('Enter a project name and business objective before adding the session draft.');
      return;
    }

    setProjects((current) => [
      ...current,
      {
        id: `pulse-ai-session-${Date.now()}`,
        name,
        objective,
        ownerModule: draft.ownerModule.trim() || 'Not assigned',
        baseModel: draft.baseModel.trim() || 'Not selected',
        strategy: draft.strategy,
        classification: draft.classification,
        status: 'Session draft — not persisted',
        sessionOnly: true
      }
    ]);
    setDraft({ ...EMPTY_PROJECT });
    setNotice('Session draft added. It will disappear when this page is refreshed because persistence is intentionally locked.');
  }

  function removeSessionDraft(id) {
    setProjects((current) => current.filter((project) => project.id !== id || !project.sessionOnly));
    setNotice('Session draft removed. No database record was changed.');
  }

  return (
    <main
      className="pulse-ai-center projectpulse-module-standard"
      data-module="011"
      data-module-name="Pulse AI"
      data-route="work-task-builder"
      data-source-phase="read-only-foundation"
    >
      <header className="pulse-ai-hero">
        <div className="pulse-ai-hero-brand">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div>
            <p className="pulse-ai-eyebrow">Module 011 · ProjectPulse AI lifecycle control plane</p>
            <h1>Pulse AI</h1>
            <p>
              Prepare knowledge, datasets, evaluations, and model versions inside ProjectPulse while Module 064 remains the governed provider and inference gateway.
            </p>
          </div>
        </div>
        <div className="pulse-ai-hero-actions">
          <a className="pulse-ai-secondary-action" href="#ai-provider-configuration">Open Module 064</a>
          <button type="button" className="pulse-ai-primary-action" onClick={loadProviderStatus} disabled={providerState.loading}>
            {providerState.loading ? 'Checking provider boundary…' : 'Refresh provider status'}
          </button>
        </div>
      </header>

      <section className="pulse-ai-foundation-banner" aria-label="Foundation safeguards">
        <div>
          <strong>Source-only foundation</strong>
          <span>No training, persistence, external GPU job, model deployment, feature-route mutation, or Azure change is enabled.</span>
        </div>
        <span className="pulse-ai-status pulse-ai-status--locked">Execution locked</span>
      </section>

      {notice ? <div className="pulse-ai-notice" role="status">{notice}</div> : null}

      <section className="pulse-ai-metrics" aria-label="Pulse AI summary">
        <article>
          <span>Model projects</span>
          <strong>{projects.length}</strong>
          <small>{projects.filter((project) => project.sessionOnly).length} session-only draft(s)</small>
        </article>
        <article>
          <span>Planned knowledge sources</span>
          <strong>{KNOWLEDGE_SOURCES.length}</strong>
          <small>Permission-aware indexing required</small>
        </article>
        <article>
          <span>Remote providers available</span>
          <strong>{providerState.restricted ? 'Restricted' : `${availableProviderCount}/${remoteProviders.length || 0}`}</strong>
          <small>Read from Module 064 only</small>
        </article>
        <article>
          <span>Production promotions</span>
          <strong>0</strong>
          <small>Human approval and deployment controls required</small>
        </article>
      </section>

      <section className="pulse-ai-provider-strip" aria-label="Module 064 provider boundary">
        <div className="pulse-ai-provider-strip-heading">
          <div>
            <p className="pulse-ai-eyebrow">Shared runtime boundary</p>
            <h2>Module 064 provider visibility</h2>
          </div>
          <span>{providerState.payload?.generatedAt ? `Updated ${formatDate(providerState.payload.generatedAt)}` : 'Non-secret status only'}</span>
        </div>

        {providerState.loading ? <p className="pulse-ai-inline-state">Loading governed provider status…</p> : null}
        {providerState.error ? (
          <div className={`pulse-ai-inline-state ${providerState.restricted ? '' : 'pulse-ai-inline-state--error'}`}>
            {providerState.error}
          </div>
        ) : null}
        {!providerState.loading && !providerState.error && providers.length === 0 ? (
          <p className="pulse-ai-inline-state">Module 064 returned no provider records.</p>
        ) : null}

        {providers.length > 0 ? (
          <div className="pulse-ai-provider-grid">
            {providers.map((provider) => {
              const health = healthByProvider.get(provider.code) ?? {};
              const tone = providerTone(provider, health);
              return (
                <article key={provider.code}>
                  <div>
                    <strong>{provider.displayName || titleFrom(provider.code)}</strong>
                    <span className={`pulse-ai-status pulse-ai-status--${tone}`}>
                      {titleFrom(health.probeStatus || health.status || (provider.configured ? 'not checked' : 'not configured'))}
                    </span>
                  </div>
                  <p>{provider.model || 'No active model'}</p>
                  <small>{provider.enabled ? 'Enabled' : 'Disabled'} · {provider.configured ? 'Configured' : 'Not configured'}</small>
                </article>
              );
            })}
          </div>
        ) : null}
      </section>

      <div className="pulse-ai-workspace">
        <nav className="pulse-ai-tabs" aria-label="Pulse AI workspaces">
          {PULSE_AI_TABS.map((tab) => (
            <button
              type="button"
              key={tab.id}
              className={activeTab === tab.id ? 'is-active' : ''}
              aria-current={activeTab === tab.id ? 'page' : undefined}
              onClick={() => setActiveTab(tab.id)}
            >
              <strong>{tab.label}</strong>
              <span>{tab.description}</span>
            </button>
          ))}
        </nav>

        <section className="pulse-ai-tab-panel" aria-labelledby={`pulse-ai-tab-${activeTab}`}>
          <div className="pulse-ai-current-tab">
            <p className="pulse-ai-eyebrow">Pulse AI workspace</p>
            <h2 id={`pulse-ai-tab-${activeTab}`}>{activeTabDefinition.label}</h2>
            <p>{activeTabDefinition.description}</p>
          </div>

          {activeTab === 'overview' ? (
            <div className="pulse-ai-tab-stack">
              <SectionHeading
                eyebrow="Operating model"
                title="ProjectPulse controls the lifecycle; specialized compute does the training"
                copy="Pulse AI prepares and governs model work. Module 064 controls inference routing. Future GPU training runs outside the ProjectPulse API process."
              />
              <div className="pulse-ai-architecture-grid">
                <article>
                  <span>1</span>
                  <h3>Authorized ProjectPulse data</h3>
                  <p>Retrieve only records and documents the current effective user is permitted to access.</p>
                </article>
                <article>
                  <span>2</span>
                  <h3>Pulse AI governance</h3>
                  <p>Version knowledge sources, datasets, evaluations, approvals, model records, and audit evidence.</p>
                </article>
                <article>
                  <span>3</span>
                  <h3>External training compute</h3>
                  <p>Submit an approved immutable dataset to a future managed or self-hosted GPU training backend.</p>
                </article>
                <article>
                  <span>4</span>
                  <h3>Module 064 routing</h3>
                  <p>Register approved endpoints and route each feature through the governed shared provider boundary.</p>
                </article>
              </div>

              <SectionHeading
                eyebrow="Model projects"
                title="Create a scoped AI initiative"
                copy="This foundation stores new entries only in React memory so the workflow can be reviewed without a database migration."
              />
              <div className="pulse-ai-project-layout">
                <div className="pulse-ai-project-list">
                  {projects.map((project) => (
                    <article key={project.id}>
                      <div className="pulse-ai-project-title-row">
                        <div>
                          <h3>{project.name}</h3>
                          <span>{project.status}</span>
                        </div>
                        {project.sessionOnly ? (
                          <button type="button" onClick={() => removeSessionDraft(project.id)}>Remove draft</button>
                        ) : null}
                      </div>
                      <p>{project.objective}</p>
                      <dl>
                        <div><dt>Strategy</dt><dd>{project.strategy}</dd></div>
                        <div><dt>Owner module</dt><dd>{project.ownerModule}</dd></div>
                        <div><dt>Base model</dt><dd>{project.baseModel}</dd></div>
                        {project.classification ? <div><dt>Classification</dt><dd>{project.classification}</dd></div> : null}
                      </dl>
                    </article>
                  ))}
                </div>

                <form className="pulse-ai-project-form" onSubmit={addProject}>
                  <div>
                    <p className="pulse-ai-eyebrow">Session-only designer</p>
                    <h3>Draft a model project</h3>
                    <p>No record leaves the browser and nothing is persisted.</p>
                  </div>
                  <label>
                    Project name
                    <input value={draft.name} onChange={(event) => updateDraft('name', event.target.value)} placeholder="ProjectPulse Status Assistant" required />
                  </label>
                  <label>
                    Business objective
                    <textarea value={draft.objective} onChange={(event) => updateDraft('objective', event.target.value)} rows={4} placeholder="Describe the narrow task and the expected result." required />
                  </label>
                  <div className="pulse-ai-field-grid">
                    <label>
                      Owner module
                      <input value={draft.ownerModule} onChange={(event) => updateDraft('ownerModule', event.target.value)} placeholder="066" />
                    </label>
                    <label>
                      Candidate base model
                      <input value={draft.baseModel} onChange={(event) => updateDraft('baseModel', event.target.value)} placeholder="Not selected" />
                    </label>
                  </div>
                  <div className="pulse-ai-field-grid">
                    <label>
                      Strategy
                      <select value={draft.strategy} onChange={(event) => updateDraft('strategy', event.target.value)}>
                        <option>RAG first</option>
                        <option>Prompt and evaluation only</option>
                        <option>RAG + LoRA fine-tuning</option>
                        <option>LoRA fine-tuning</option>
                        <option>QLoRA fine-tuning</option>
                        <option>Distillation candidate</option>
                      </select>
                    </label>
                    <label>
                      Data classification
                      <select value={draft.classification} onChange={(event) => updateDraft('classification', event.target.value)}>
                        <option>Internal</option>
                        <option>Confidential</option>
                        <option>Restricted</option>
                      </select>
                    </label>
                  </div>
                  <button type="submit" className="pulse-ai-primary-action">Add session draft</button>
                </form>
              </div>

              <SectionHeading
                eyebrow="Feature consumers"
                title="Planned Module 064 routes"
                copy="Each ProjectPulse use case remains independently governed and can be routed, evaluated, or rolled back without replacing every AI feature at once."
              />
              <div className="pulse-ai-feature-grid">
                {FEATURE_TARGETS.map((feature) => (
                  <article key={feature.code}>
                    <code>{feature.code}</code>
                    <h3>{feature.name}</h3>
                    <span>{feature.owner}</span>
                    <p>{feature.treatment}</p>
                  </article>
                ))}
              </div>
            </div>
          ) : null}

          {activeTab === 'knowledge' ? (
            <div className="pulse-ai-tab-stack">
              <SectionHeading
                eyebrow="Retrieval-augmented generation"
                title="Keep changing facts outside the model weights"
                copy="Project status, permissions, deployments, users, customer records, and other live facts should be retrieved after authorization instead of being permanently trained into a model."
                action={<LockedButton>Add knowledge source</LockedButton>}
              />
              <div className="pulse-ai-table-wrap">
                <table>
                  <thead><tr><th>Source</th><th>Scope</th><th>Classification</th><th>Access rule</th><th>State</th></tr></thead>
                  <tbody>
                    {KNOWLEDGE_SOURCES.map((source) => (
                      <tr key={source.name}>
                        <th scope="row">{source.name}</th>
                        <td>{source.scope}</td>
                        <td>{source.classification}</td>
                        <td>{source.access}</td>
                        <td><span className="pulse-ai-status pulse-ai-status--planned">{source.state}</span></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="pulse-ai-rule-grid">
                <article><h3>Authorization first</h3><p>Retrieve only content already authorized for the current user, role, project, and module.</p></article>
                <article><h3>Sanitize before indexing</h3><p>Remove secrets, unnecessary personal data, and unapproved customer information before chunking or embedding.</p></article>
                <article><h3>Preserve source evidence</h3><p>Store the source version, checksum, classification, owner, and indexed-at timestamp for every collection.</p></article>
                <article><h3>Revoke cleanly</h3><p>Removing access to a source must also remove it from retrieval results without retraining the model.</p></article>
              </div>
            </div>
          ) : null}

          {activeTab === 'datasets' ? (
            <div className="pulse-ai-tab-stack">
              <SectionHeading
                eyebrow="Training data"
                title="Build small, reviewed, purpose-specific datasets"
                copy="Pulse AI will treat dataset versions as immutable approval artifacts rather than continuously learning from every user conversation."
                action={<LockedButton>Import dataset</LockedButton>}
              />
              <div className="pulse-ai-dataset-example">
                <div>
                  <p className="pulse-ai-eyebrow">Example conversational record</p>
                  <h3>Permission interpretation</h3>
                </div>
                <pre>{`{"messages":[
  {"role":"system","content":"You are the ProjectPulse permissions assistant."},
  {"role":"user","content":"What does No Access mean for Module 001?"},
  {"role":"assistant","content":"The role must not see Module 001 and direct access must be denied."}
]}`}</pre>
              </div>
              <div className="pulse-ai-checklist">
                {DATASET_CHECKS.map((check, index) => (
                  <div key={check}><span>{index + 1}</span><p>{check}</p></div>
                ))}
              </div>
              <div className="pulse-ai-split-card">
                <article><strong>Training set</strong><p>Examples used to teach the desired behavior.</p></article>
                <article><strong>Validation set</strong><p>Examples used during tuning to identify overfitting and configuration problems.</p></article>
                <article><strong>Held-out evaluation set</strong><p>Examples never used for training decisions and reserved for final comparison.</p></article>
              </div>
            </div>
          ) : null}

          {activeTab === 'training' ? (
            <div className="pulse-ai-tab-stack">
              <SectionHeading
                eyebrow="External compute boundary"
                title="ProjectPulse will submit and monitor jobs, not train inside the API process"
                copy="A future training adapter can target an approved managed service, GPU virtual machine, Kubernetes job, or provider fine-tuning API."
                action={<LockedButton>Submit training job</LockedButton>}
              />
              <div className="pulse-ai-training-flow">
                {TRAINING_STAGES.map((stage, index) => (
                  <article key={stage.state}>
                    <span>{index + 1}</span>
                    <div><h3>{stage.state}</h3><small>{stage.owner}</small><p>{stage.rule}</p></div>
                  </article>
                ))}
              </div>
              <div className="pulse-ai-rule-grid">
                <article><h3>Supported future methods</h3><p>Supervised fine-tuning, LoRA, QLoRA, and evaluated distillation workflows.</p></article>
                <article><h3>Immutable input</h3><p>The job references an approved dataset version and cannot silently switch records while running.</p></article>
                <article><h3>Sanitized logs</h3><p>Training progress may be visible, but secrets and raw restricted records may not be written to browser or audit logs.</p></article>
                <article><h3>Artifact references</h3><p>Large adapters and models belong in approved object storage or a model registry; ProjectPulse stores metadata and checksums.</p></article>
              </div>
            </div>
          ) : null}

          {activeTab === 'evaluations' ? (
            <div className="pulse-ai-tab-stack">
              <SectionHeading
                eyebrow="Promotion gate"
                title="A completed training job is not an approved model"
                copy="Every candidate must be compared with the base model and current active model using a frozen test suite."
                action={<LockedButton>Run evaluation suite</LockedButton>}
              />
              <div className="pulse-ai-table-wrap">
                <table>
                  <thead><tr><th>Gate</th><th>Requirement</th><th>Blocks promotion</th><th>Current state</th></tr></thead>
                  <tbody>
                    {EVALUATION_GATES.map((item) => (
                      <tr key={item.gate}>
                        <th scope="row">{item.gate}</th>
                        <td>{item.requirement}</td>
                        <td>{item.blocksPromotion ? 'Yes' : 'Threshold-based'}</td>
                        <td><span className="pulse-ai-status pulse-ai-status--planned">Not run</span></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="pulse-ai-evaluation-example">
                <div><span>Input</span><p>Role: Inside Sales · Module: 001 · Permission: No Access</p></div>
                <div><span>Required result</span><p>Module 001 is hidden and direct access is denied.</p></div>
                <div><span>Promotion-blocking failure</span><p>The model states that the user may view their own Module 001 records.</p></div>
              </div>
            </div>
          ) : null}

          {activeTab === 'registry' ? (
            <div className="pulse-ai-tab-stack">
              <SectionHeading
                eyebrow="Model evidence"
                title="Register every candidate and active version"
                copy="The registry creates a durable chain from business objective through dataset, training, evaluation, approval, deployment, and retirement."
                action={<LockedButton>Register model artifact</LockedButton>}
              />
              <div className="pulse-ai-registry-card">
                <div className="pulse-ai-registry-identity">
                  <span className="pulse-ai-status pulse-ai-status--planned">No artifact registered</span>
                  <h3>projectpulse-assistant</h3>
                  <p>Version will be assigned only after a training or approved external-model registration workflow exists.</p>
                </div>
                <div className="pulse-ai-registry-fields">
                  {MODEL_REGISTRY_FIELDS.map((field) => <div key={field}><span aria-hidden="true">✓</span>{field}</div>)}
                </div>
              </div>
              <div className="pulse-ai-rule-grid">
                <article><h3>License evidence</h3><p>Commercial use, redistribution, fine-tuning, and output restrictions must be reviewed for every base model.</p></article>
                <article><h3>Reproducibility</h3><p>Record the exact base model revision, parameters, code version, dataset hash, random seed, and compute image.</p></article>
                <article><h3>Retirement without erasure</h3><p>Inactive versions disappear from new routing but remain available for audit and rollback evidence.</p></article>
                <article><h3>No model files in PostgreSQL</h3><p>Store artifact URIs, checksums, classification, retention, and approval metadata rather than multi-gigabyte binaries.</p></article>
              </div>
            </div>
          ) : null}

          {activeTab === 'deployments' ? (
            <div className="pulse-ai-tab-stack">
              <SectionHeading
                eyebrow="Controlled activation"
                title="Promote through development, test, and production gates"
                copy="Pulse AI will register approved endpoints with Module 064. It will not bypass the shared provider router or directly modify production resources."
              />
              <div className="pulse-ai-environment-grid">
                <article><span>Development</span><h3>Not configured</h3><p>Future endpoint experimentation with synthetic or approved non-production data.</p><LockedButton>Deploy development</LockedButton></article>
                <article><span>Test</span><h3>Not configured</h3><p>Evaluation, security testing, smoke checks, and feature-level comparison.</p><LockedButton>Promote to test</LockedButton></article>
                <article><span>Production</span><h3>Not configured</h3><p>Human-approved Module 064 registration, canary routing, monitoring, and rollback.</p><LockedButton>Promote to production</LockedButton></article>
              </div>
              <div className="pulse-ai-deployment-rules">
                <h3>Required before activation</h3>
                <ol>
                  <li>Model version and artifact checksum are registered.</li>
                  <li>All blocking evaluation gates pass.</li>
                  <li>Dataset, model, and production approvers are recorded.</li>
                  <li>The endpoint passes health, authorization, and information-leakage tests.</li>
                  <li>Module 064 accepts the model and its feature-specific route.</li>
                  <li>A tested rollback target remains available.</li>
                </ol>
              </div>
            </div>
          ) : null}

          {activeTab === 'governance' ? (
            <div className="pulse-ai-tab-stack">
              <SectionHeading
                eyebrow="Modules 012 and 037"
                title="Use capability-based access with separation of duties"
                copy="Super Administrator receives Full Control. Every other role receives only explicitly assigned capabilities, and No Access hides the module and denies its APIs."
              />
              <div className="pulse-ai-table-wrap">
                <table>
                  <thead><tr><th>Capability</th><th>Meaning</th><th>Foundation state</th></tr></thead>
                  <tbody>
                    {CAPABILITIES.map(([capability, meaning]) => (
                      <tr key={capability}><th scope="row">{capability}</th><td>{meaning}</td><td><span className="pulse-ai-status pulse-ai-status--planned">Contract planned</span></td></tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="pulse-ai-separation-grid">
                <article><strong>Dataset creator</strong><span>cannot approve the same dataset</span></article>
                <article><strong>Training operator</strong><span>cannot approve production promotion alone</span></article>
                <article><strong>Production approver</strong><span>cannot retrieve provider secret values</span></article>
              </div>
              <div className="pulse-ai-governance-rules">
                <h3>Non-negotiable rules</h3>
                <ul>
                  <li>The ProjectPulse backend—not the model—enforces authorization.</li>
                  <li>No model receives records outside the current effective user’s permission scope.</li>
                  <li>Provider keys remain write-only inside Module 064 and are never displayed here.</li>
                  <li>Conversations are not automatically converted into training data.</li>
                  <li>Fine-tuning teaches behavior and terminology; changing business facts remain live or retrieved.</li>
                  <li>Training, deployment, and production routing require separately authorized implementation phases.</li>
                </ul>
              </div>
            </div>
          ) : null}
        </section>
      </div>
    </main>
  );
}
