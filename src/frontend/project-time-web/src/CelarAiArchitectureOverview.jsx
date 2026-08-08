import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import CelarAiArchitectureCatalog from './CelarAiArchitectureCatalog.jsx';
import './celar-ai-architecture-overview.css';

function AtAGlance({ icon, title, children, tone }) {
  return (
    <article className={`celar-ai-architecture-glance is-${tone}`}>
      <span aria-hidden="true">{icon}</span>
      <div><h3>{title}</h3><p>{children}</p></div>
    </article>
  );
}

export default function CelarAiArchitectureOverview() {
  return (
    <section className="celar-ai-architecture-overview" aria-labelledby="celar-ai-architecture-title">
      <div className="celar-ai-architecture-heading">
        <div>
          <p>US Signal · Module 011 · Enterprise AI Platform</p>
          <h2 id="celar-ai-architecture-title">Celar AI Architecture Overview</h2>
          <span>
            Celar AI is the unified operational intelligence system for the US Signal Solution Provider division.
            The logical diagram explains the governed target. The managed component register below separates what is deployed now from what remains planned or deferred until OpenCloud.
          </span>
        </div>
        <a href="#system-architecture">Open system architecture</a>
      </div>

      <div className="celar-ai-architecture-layout">
        <div className="celar-ai-architecture-canvas" tabIndex={0} aria-label="Scrollable Celar AI architecture diagram">
          <svg
            viewBox="0 0 1200 820"
            role="img"
            aria-labelledby="celar-ai-svg-title celar-ai-svg-description"
            preserveAspectRatio="xMidYMid meet"
          >
            <title id="celar-ai-svg-title">Celar AI private-first enterprise architecture</title>
            <desc id="celar-ai-svg-description">
              Pulse users authenticate and receive role and record scope. Authorized private documents and governed live-data tools feed the Celar AI context fabric. The target OpenCloud private runtime explicitly includes ClamAV for malware scanning, Tesseract 5 for OCR, and Ollama for private inference and embeddings; all three remain planned and deferred until runtime validation, and migration 081 is not applied. Confidence and freshness assessment follows the saved Module 064 order among eligible targets. Private source content never enters a public route; an eligible external provider can receive only a fixed identity-free capsule. Returned output passes privacy and source checks applicable to its route and remains subject to human review.
            </desc>
            <defs>
              <marker id="celar-ai-arrow" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto">
                <path d="M0,0 L0,6 L9,3 z" />
              </marker>
              <filter id="celar-ai-shadow" x="-20%" y="-20%" width="140%" height="140%">
                <feDropShadow dx="0" dy="3" stdDeviation="4" floodOpacity="0.14" />
              </filter>
            </defs>

            <rect className="diagram-surface" x="8" y="8" width="1184" height="804" rx="24" />
            <image href={usSignalLogoDataUrl} x="415" y="24" width="125" height="68" preserveAspectRatio="xMidYMid meet" />
            <text className="diagram-brand" x="555" y="62">Pulse Platform</text>
            <text className="diagram-created" x="1120" y="36" textAnchor="end">Created by Dr. Ahmed Adeyemi</text>

            <g filter="url(#celar-ai-shadow)">
              <rect className="diagram-node is-auth" x="250" y="105" width="700" height="55" rx="10" />
              <text className="diagram-node-title" x="600" y="138" textAnchor="middle">Authentication · Roles · Permissions · Project / Customer / Record Scope</text>
            </g>

            <path className="diagram-line" d="M600 160 L600 190 M600 190 L300 190 L300 215 M600 190 L900 190 L900 215" />

            <g filter="url(#celar-ai-shadow)">
              <rect className="diagram-node is-private" x="95" y="215" width="410" height="58" rx="10" />
              <text className="diagram-node-title" x="300" y="249" textAnchor="middle">Private content graph and retrieval</text>
              <rect className="diagram-chip is-private" x="115" y="292" width="105" height="42" rx="8" />
              <rect className="diagram-chip is-private" x="247" y="292" width="105" height="42" rx="8" />
              <rect className="diagram-chip is-private" x="379" y="292" width="105" height="42" rx="8" />
              <text className="diagram-chip-text" x="167" y="318" textAnchor="middle">SOW</text>
              <text className="diagram-chip-text" x="299" y="318" textAnchor="middle">GSD</text>
              <text className="diagram-chip-text" x="431" y="318" textAnchor="middle">Versions</text>
            </g>

            <g filter="url(#celar-ai-shadow)">
              <rect className="diagram-node is-tools" x="695" y="215" width="410" height="58" rx="10" />
              <text className="diagram-node-title" x="900" y="249" textAnchor="middle">Governed live-data tools</text>
              {['Projects', 'Time', 'Finance', 'APIs', 'Diagnostics'].map((label, index) => {
                const x = 710 + (index * 78);
                return <g key={label}><rect className="diagram-chip is-tools" x={x} y="292" width="68" height="42" rx="8" /><text className="diagram-chip-text" x={x + 34} y="318" textAnchor="middle">{label}</text></g>;
              })}
            </g>

            <path className="diagram-line" d="M300 273 L300 355 L600 355 M900 273 L900 355 L600 355" />

            <g filter="url(#celar-ai-shadow)">
              <rect className="diagram-node is-intelligence" x="175" y="350" width="850" height="115" rx="12" />
              <text className="diagram-node-title" x="600" y="377" textAnchor="middle">Celar AI private intelligence and context fabric</text>
              <text className="diagram-node-subtitle" x="600" y="397" textAnchor="middle">Managed component states · live readiness gates · authoritative versions · governed lifecycle</text>
              <rect className="diagram-chip is-planned" x="205" y="407" width="230" height="32" rx="8" />
              <rect className="diagram-chip is-planned" x="485" y="407" width="230" height="32" rx="8" />
              <rect className="diagram-chip is-planned" x="765" y="407" width="230" height="32" rx="8" />
              <text className="diagram-chip-text" x="320" y="427" textAnchor="middle">ClamAV · malware scanning</text>
              <text className="diagram-chip-text" x="600" y="427" textAnchor="middle">Tesseract 5 · OCR</text>
              <text className="diagram-chip-text" x="880" y="427" textAnchor="middle">Ollama · private inference + embeddings</text>
              <text className="diagram-node-subtitle is-planned-status" x="600" y="456" textAnchor="middle">Planned / OpenCloud deferred · not operational · migration 081 not applied</text>
            </g>

            <path className="diagram-line" markerEnd="url(#celar-ai-arrow)" d="M600 465 L600 482" />
            <g filter="url(#celar-ai-shadow)">
              <rect className="diagram-node is-confidence" x="335" y="482" width="530" height="56" rx="10" />
              <text className="diagram-node-title" x="600" y="516" textAnchor="middle">Confidence · freshness · policy · live decision trace</text>
            </g>

            <path className="diagram-line" d="M600 538 L600 562 M600 562 L295 562 L295 588 M600 562 L905 562 L905 588" />

            <g filter="url(#celar-ai-shadow)">
              <rect className="diagram-node is-sufficient" x="105" y="588" width="380" height="54" rx="10" />
              <text className="diagram-node-title" x="295" y="621" textAnchor="middle">Private evidence sufficient · no public call</text>
            </g>

            <g filter="url(#celar-ai-shadow)">
              <rect className="diagram-node is-external" x="715" y="588" width="380" height="54" rx="10" />
              <text className="diagram-node-title" x="905" y="621" textAnchor="middle">Eligible route may use identity-free assistance</text>
              <rect className="diagram-chip is-external" x="715" y="659" width="115" height="40" rx="8" />
              <rect className="diagram-chip is-external" x="848" y="659" width="115" height="40" rx="8" />
              <rect className="diagram-chip is-external" x="981" y="659" width="115" height="40" rx="8" />
              <text className="diagram-chip-text" x="772" y="684" textAnchor="middle">Fixed capsule</text>
              <text className="diagram-chip-text" x="905" y="684" textAnchor="middle">Module 064</text>
              <text className="diagram-chip-text" x="1038" y="684" textAnchor="middle">Claude / OpenAI</text>
            </g>

            <path className="diagram-line" d="M295 642 L295 720 L600 720 M905 699 L905 720 L600 720" />
            <g filter="url(#celar-ai-shadow)">
              <rect className="diagram-node is-verification" x="330" y="720" width="540" height="48" rx="10" />
              <text className="diagram-node-title" x="600" y="750" textAnchor="middle">Output safety · applicable source checks · citations · human review</text>
            </g>
            <path className="diagram-line" markerEnd="url(#celar-ai-arrow)" d="M600 768 L600 784" />
            <rect className="diagram-result" x="345" y="780" width="510" height="28" rx="8" />
            <text className="diagram-result-text" x="600" y="800" textAnchor="middle">Detailed cited answer · Timesheet · SOW · Plan · Timeline · Diagram</text>
          </svg>
        </div>

        <aside className="celar-ai-architecture-glance-panel" aria-label="Celar AI architecture at a glance">
          <h3>At a glance</h3>
          <AtAGlance icon="◆" title="Private-first" tone="private">
            Authorized US Signal documents and governed Pulse data are consulted before any optional external reasoning.
          </AtAGlance>
          <AtAGlance icon="◎" title="Permission-aware" tone="permission">
            Every source remains limited by the current effective user, owning module, project, customer, team, and record scope.
          </AtAGlance>
          <AtAGlance icon="⌁" title="Temporal context graph" tone="permission">
            Celar AI links evidence to authoritative versions, freshness, policy revisions, eligibility decisions, and privacy-safe live route traces.
          </AtAGlance>
          <AtAGlance icon="◉" title="Self-monitoring adapters" tone="private">
            Module 011 and Module 064 show private inference, database, scanning, OCR, embedding, storage, and training readiness without exposing endpoints or secrets.
          </AtAGlance>
          <AtAGlance icon="◇" title="Planned OpenCloud runtime" tone="planned">
            ClamAV will scan documents, Tesseract 5 will perform OCR, and Ollama will provide private inference and embeddings. All three remain deferred until validated.
          </AtAGlance>
          <AtAGlance icon="↗" title="Governed external fallback" tone="external">
            Only a fixed identity-free purpose capsule may leave the private boundary through Module 064. Private evidence is never included.
          </AtAGlance>
          <AtAGlance icon="✓" title="Reviewable outcomes" tone="review">
            Generated Timesheet descriptions, SOWs, plans, timelines, and diagrams remain drafts until the owning human workflow approves them.
          </AtAGlance>
        </aside>
      </div>

      <CelarAiArchitectureCatalog />
    </section>
  );
}
