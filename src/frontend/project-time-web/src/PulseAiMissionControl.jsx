import './pulse-ai-mission-control.css';

const PRIMARY_INTELLIGENCE_MODES = Object.freeze([
  Object.freeze({
    code: 'TIMESHEET',
    title: 'Document-grounded timesheet suggestions',
    audience: 'Engineers · Module 001',
    description:
      'When an engineer selects Regular Tasks or Requests / Service Request and chooses Generate AI suggestion, Pulse AI will resolve the project, task, assignment, and request; retrieve the approved SOW, GSD, and engineering-visible project documents; and produce a description that aligns with the documented scope.',
    sources: ['Engineer rough note', 'Selected task or service request', 'Project assignment', 'Approved SOW and GSD', 'Related engineering documents'],
    output: 'A reviewable description only. Pulse AI cannot change hours, save, submit, approve, or create work.'
  }),
  Object.freeze({
    code: 'ASK PROJECTPULSE',
    title: 'System-wide Help and Search',
    audience: 'Every authorized ProjectPulse user',
    description:
      'Users can ask questions about how the application works or about live information already available to their role. Pulse AI will search approved documentation and use permission-filtered read-only tools to answer across modules, projects, users, workflows, operations, reports, and current status.',
    sources: ['Module 999 and approved help', 'Module catalog and operating documentation', 'Current module APIs', 'Role and project scope', 'Audit and status evidence'],
    output: 'A sourced answer with scope, as-of time, assumptions, and clear uncertainty when evidence is incomplete.'
  }),
  Object.freeze({
    code: 'FLOWHIVE',
    title: 'SOW/GSD-driven project planning',
    audience: 'Project Managers and Engineers · Module 066',
    description:
      'Pulse AI will read the approved SOW, GSD, architecture, order, and supporting project documents; combine them with project constraints, calendars, capacity, and templates; and draft a WBS, tasks, dependencies, milestones, risks, assumptions, and proposed timeline.',
    sources: ['Approved SOW and GSD', 'Architecture and order documents', 'Project constraints', 'Resource capacity and calendars', 'Approved planning templates'],
    output: 'A PM-presentable draft that the Engineer can modify. It cannot baseline the plan, assign resources, or commit customer dates.'
  }),
  Object.freeze({
    code: 'INSIGHT',
    title: 'Reports, financials, and deep system insight',
    audience: 'Role-scoped operational, financial, and executive users',
    description:
      'Pulse AI will answer analytical questions across time, utilization, projects, customers, expenses, rates, contracts, billing readiness, invoices, revenue, cost, margin, opportunities, system health, and workflow evidence. Exact values will come from governed calculations and read-only semantic tools—not model guesses.',
    sources: ['Reporting semantic layer', 'Approved financial calculations', 'Permission-filtered module tools', 'Live operational evidence', 'Saved report definitions'],
    output: 'A cited explanation with formula, period, currency, filters, record count, freshness, and any material assumptions.'
  })
]);

const PRIVATE_REASONING_PATH = Object.freeze([
  ['1', 'Authorize first', 'Resolve the actual and effective user, module permission, project scope, and data classification before retrieving anything.'],
  ['2', 'Retrieve inside ProjectPulse', 'Read only approved document versions and live records the user is already allowed to see.'],
  ['3', 'Reason privately first', 'Use deterministic calculations, private retrieval, and a private model as the primary path.'],
  ['4', 'Measure confidence', 'Check source coverage, conflicts, freshness, calculation validity, and unsupported claims.'],
  ['5', 'Sanitize before escalation', 'When external help is allowed, create a minimal reasoning capsule with raw documents, secrets, identities, pricing, and restricted details removed.'],
  ['6', 'Optional Claude/OpenAI reasoning', 'Module 064 sends only the sanitized capsule through an approved enterprise route. Raw SOW and GSD files are not sent.'],
  ['7', 'Verify privately', 'Re-ground the external result against the private SOW, GSD, records, and calculations; reject anything unsupported.'],
  ['8', 'Return evidence', 'Provide a cited answer or draft with source versions, assumptions, conflicts, uncertainty, and as-of time.'],
  ['9', 'Learn under governance', 'Accepted or corrected results become evaluation candidates. They do not automatically train or promote a model.']
]);

const SYSTEM_DATA_SURFACES = Object.freeze([
  {
    title: 'Time, work, and utilization',
    modules: '001, 002, 003, 018, 023, 028, 057',
    examples: 'Missing time, utilization, assignment load, task history, approvals, capacity, and SOW-aligned work.'
  },
  {
    title: 'Projects and delivery',
    modules: '019, 020, 025, 027, 040, 041, 055C, 055D, 066',
    examples: 'Intake, documents, handoff, project readiness, work plans, milestones, risks, closeout, and delivery status.'
  },
  {
    title: 'Commercial and financial',
    modules: '005, 022, 026, 030, 038, 039, 042, 055B, 060, 063',
    examples: 'Expenses, rates, contracts, billing readiness, invoices, revenue, cost, margin, opportunities, and exceptions.'
  },
  {
    title: 'People, permissions, and security',
    modules: '008, 009, 010, 012, 037, 059, 062, 079, 997',
    examples: 'Role explanations, effective access, session evidence, identity, audit history, privacy, and security operations.'
  },
  {
    title: 'Platform and operations',
    modules: '013–017, 058, 064, 067, 068, 071, 072, 075, 077, 078, 998',
    examples: 'Health, dependencies, backup, recovery, replication, integrations, releases, observability, diagnostics, and remediation evidence.'
  },
  {
    title: 'Guidance and system knowledge',
    modules: '011, 029, 076, 080, 999 and all registered modules',
    examples: 'How-to questions, workflow guidance, defects, UAT evidence, acceptance, module purpose, and current operating rules.'
  }
]);

const PRIVATE_PLATFORM_SERVICES = Object.freeze([
  ['Private document pipeline', 'Malware scanning, versioning, local PDF/DOCX extraction, OCR only when necessary, classification, and approval.'],
  ['Private retrieval layer', 'Local embeddings and a permission- plus project-scoped vector index that can remove access immediately.'],
  ['Private reasoning endpoint', 'A self-hosted or private open-weight model for raw internal documents and restricted system context.'],
  ['Read-only tool gateway', 'Approved APIs and a governed semantic layer for live system, reporting, and financial facts—never arbitrary model-generated SQL.'],
  ['DLP and escalation gateway', 'Redaction, policy checks, minimal context packaging, provider allowlists, and audited Module 064 routing.'],
  ['Evaluation and learning system', 'Feedback capture, frozen test suites, approved training datasets, private fine-tuning jobs, model registry, canary, and rollback.']
]);

const NON_NEGOTIABLE_PRIVACY_RULES = Object.freeze([
  'Raw SOW, GSD, architecture, contract, customer, and financial documents remain inside the approved private boundary by default.',
  'External providers receive no document bytes and no unrestricted retrieved context.',
  'Financial questions use deterministic calculations first; the model explains the result but does not invent formulas or values.',
  'Every retrieval and tool call follows the current effective user’s role, module, project, customer, and record scope.',
  'Answers show evidence and uncertainty. A confident tone never replaces missing source support.',
  'Pulse AI may improve from approved feedback, but it cannot rewrite its own policy, retrain itself, or promote itself to production.'
]);

export default function PulseAiMissionControl() {
  return (
    <section
      className="pulse-ai-mission-control"
      aria-labelledby="pulse-ai-authoritative-mission-title"
      data-pulse-ai-authoritative-scope="timesheet-help-search-flowhive-reporting-financials"
      data-pulse-ai-raw-document-boundary="private-projectpulse-runtime-only"
      data-pulse-ai-external-escalation="sanitized-reasoning-capsule-only"
    >
      <header className="pulse-ai-mission-header">
        <div>
          <p className="pulse-ai-mission-eyebrow">Authoritative Module 011 operating mission</p>
          <h2 id="pulse-ai-authoritative-mission-title">One private intelligence layer for the entire ProjectPulse system</h2>
          <p>
            Pulse AI is not limited to model training. Its primary purpose is to understand approved internal documents and live ProjectPulse data so it can support timesheets, Help and Search, FlowHive planning, reports, financial analysis, and future role-authorized use cases.
          </p>
        </div>
        <div className="pulse-ai-mission-badges" aria-label="Pulse AI privacy state">
          <span>Private-first</span>
          <span>Permission-aware</span>
          <span>Read-only foundation</span>
        </div>
      </header>

      <div className="pulse-ai-mission-alert" role="note">
        <strong>Internal-document boundary</strong>
        <p>
          Raw SOW and GSD files stay inside the private ProjectPulse AI environment. Claude or OpenAI can be used only as a governed reasoning fallback with a sanitized, minimal problem capsule unless a separately approved policy explicitly allows more.
        </p>
      </div>

      <section className="pulse-ai-mission-section" aria-labelledby="pulse-ai-primary-modes-title">
        <div className="pulse-ai-mission-section-heading">
          <div>
            <p className="pulse-ai-mission-eyebrow">Primary intelligence modes</p>
            <h3 id="pulse-ai-primary-modes-title">What Pulse AI must do</h3>
          </div>
          <span>All outputs remain governed drafts or sourced answers</span>
        </div>

        <div className="pulse-ai-mission-mode-grid">
          {PRIMARY_INTELLIGENCE_MODES.map((mode) => (
            <article key={mode.code}>
              <div className="pulse-ai-mission-mode-title">
                <span>{mode.code}</span>
                <small>{mode.audience}</small>
              </div>
              <h4>{mode.title}</h4>
              <p>{mode.description}</p>
              <div className="pulse-ai-mission-source-list">
                <strong>Authoritative context</strong>
                <ul>
                  {mode.sources.map((source) => <li key={source}>{source}</li>)}
                </ul>
              </div>
              <div className="pulse-ai-mission-output"><strong>Output boundary:</strong> {mode.output}</div>
            </article>
          ))}
        </div>
      </section>

      <section className="pulse-ai-mission-section" aria-labelledby="pulse-ai-reasoning-path-title">
        <div className="pulse-ai-mission-section-heading">
          <div>
            <p className="pulse-ai-mission-eyebrow">Private-first orchestration</p>
            <h3 id="pulse-ai-reasoning-path-title">How Pulse AI reaches an answer without leaking internal documents</h3>
          </div>
          <span>Local verification remains authoritative</span>
        </div>

        <div className="pulse-ai-private-reasoning-path">
          {PRIVATE_REASONING_PATH.map(([step, title, description]) => (
            <article key={step}>
              <span>{step}</span>
              <div>
                <h4>{title}</h4>
                <p>{description}</p>
              </div>
            </article>
          ))}
        </div>
      </section>

      <section className="pulse-ai-mission-section" aria-labelledby="pulse-ai-system-surface-title">
        <div className="pulse-ai-mission-section-heading">
          <div>
            <p className="pulse-ai-mission-eyebrow">Deep system access—never unrestricted access</p>
            <h3 id="pulse-ai-system-surface-title">Authorized data surfaces for Help, Search, reports, and insight</h3>
          </div>
          <span>Role and record scope apply to every source</span>
        </div>

        <div className="pulse-ai-system-surface-grid">
          {SYSTEM_DATA_SURFACES.map((surface) => (
            <article key={surface.title}>
              <h4>{surface.title}</h4>
              <code>{surface.modules}</code>
              <p>{surface.examples}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="pulse-ai-mission-section" aria-labelledby="pulse-ai-private-platform-title">
        <div className="pulse-ai-mission-section-heading">
          <div>
            <p className="pulse-ai-mission-eyebrow">Platform required for the complete system</p>
            <h3 id="pulse-ai-private-platform-title">Additional private AI services Pulse AI will need</h3>
          </div>
          <span>Provider-neutral architecture</span>
        </div>

        <div className="pulse-ai-private-platform-grid">
          {PRIVATE_PLATFORM_SERVICES.map(([title, description], index) => (
            <article key={title}>
              <span>{String(index + 1).padStart(2, '0')}</span>
              <div><h4>{title}</h4><p>{description}</p></div>
            </article>
          ))}
        </div>
      </section>

      <section className="pulse-ai-mission-section pulse-ai-privacy-contract" aria-labelledby="pulse-ai-privacy-contract-title">
        <div>
          <p className="pulse-ai-mission-eyebrow">Non-negotiable contract</p>
          <h3 id="pulse-ai-privacy-contract-title">Self-sustaining means continuously maintained—not autonomous or uncontrolled</h3>
          <p>
            Pulse AI can automatically index approved changes, refresh stale knowledge, measure answer quality, capture accepted corrections, and prepare training candidates. Humans still approve datasets, training jobs, model versions, feature activation, and production promotion.
          </p>
        </div>
        <ul>
          {NON_NEGOTIABLE_PRIVACY_RULES.map((rule) => <li key={rule}>{rule}</li>)}
        </ul>
      </section>
    </section>
  );
}
