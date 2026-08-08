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

function DiagramText({ className, id, x, y, lines, lineHeight = 17, textAnchor = 'middle' }) {
  return (
    <text className={className} id={id} x={x} y={y} textAnchor={textAnchor}>
      {lines.map((line, index) => (
        <tspan x={x} dy={index === 0 ? 0 : lineHeight} key={`${line}-${index}`}>{line}</tspan>
      ))}
    </text>
  );
}

function ComponentNode({
  componentId,
  x,
  y,
  width,
  height,
  tone = 'current',
  state,
  title,
  subtitle = [],
  compact = false
}) {
  const titleLines = Array.isArray(title) ? title : [title];
  const subtitleLines = Array.isArray(subtitle) ? subtitle : [subtitle];
  const titleY = y + (compact ? 43 : 47);
  const subtitleY = y + height - 18 - ((subtitleLines.length - 1) * 14);

  return (
    <g data-component-id={componentId} filter="url(#celar-ai-shadow)">
      <rect className={`diagram-node is-${tone}`} id={`box-${componentId}`} x={x} y={y} width={width} height={height} rx="12" />
      <text className={`diagram-node-state is-${tone}`} id={`state-${componentId}`} x={x + 18} y={y + 22}>{state}</text>
      <DiagramText
        className={`diagram-node-title${compact ? ' is-compact' : ''}`}
        id={`title-${componentId}`}
        x={x + (width / 2)}
        y={titleY}
        lines={titleLines}
        lineHeight={19}
      />
      {subtitleLines.filter(Boolean).length > 0 ? (
        <DiagramText
          className="diagram-node-subtitle"
          id={`subtitle-${componentId}`}
          x={x + (width / 2)}
          y={subtitleY}
          lines={subtitleLines.filter(Boolean)}
          lineHeight={14}
        />
      ) : null}
    </g>
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
        <div className="celar-ai-architecture-canvas" tabIndex={0} aria-label="Scrollable Celar AI component architecture diagram">
          <svg
            viewBox="0 0 1200 1770"
            role="img"
            aria-labelledby="celar-ai-svg-title celar-ai-svg-description"
            preserveAspectRatio="xMidYMid meet"
          >
            <title id="celar-ai-svg-title">Celar AI private-first enterprise architecture</title>
            <desc id="celar-ai-svg-description">
              The diagram shows all thirteen managed Module 011 architecture components and their connections. Pulse users authenticate into the Celar AI Module 011 Workspace. Current internal-data intelligence uses the Pulse PostgreSQL data and evidence plane. Governed document storage connects to the deferred private document worker, which will use an OpenCloud virtual machine containing ClamAV malware scanning, Tesseract 5 OCR, and Ollama private inference and embeddings. Ollama may later scale to dedicated GPU-capable private compute. The Celar AI context fabric evaluates confidence, freshness, and policy. Private evidence can produce a local answer; eligible identity-free assistance routes through Module 064 to optional Claude or OpenAI reasoning. Every result passes source, privacy, citation, and human-review checks.
            </desc>
            <defs>
              <marker id="celar-ai-arrow-current" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto">
                <path className="arrow-current" d="M0,0 L0,6 L9,3 z" />
              </marker>
              <marker id="celar-ai-arrow-planned" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto">
                <path className="arrow-planned" d="M0,0 L0,6 L9,3 z" />
              </marker>
              <marker id="celar-ai-arrow-optional" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto">
                <path className="arrow-optional" d="M0,0 L0,6 L9,3 z" />
              </marker>
              <marker id="celar-ai-arrow-future" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto">
                <path className="arrow-future" d="M0,0 L0,6 L9,3 z" />
              </marker>
              <filter id="celar-ai-shadow" x="-20%" y="-20%" width="140%" height="140%">
                <feDropShadow dx="0" dy="3" stdDeviation="4" floodOpacity="0.14" />
              </filter>
            </defs>

            <rect className="diagram-surface" x="8" y="8" width="1184" height="1754" rx="24" />
            <image href={usSignalLogoDataUrl} x="414" y="20" width="120" height="62" preserveAspectRatio="xMidYMid meet" />
            <text className="diagram-brand" x="550" y="57">Pulse Platform</text>
            <text className="diagram-created" x="1120" y="36" textAnchor="end">Created by Dr. Ahmed Adeyemi</text>

            <g className="diagram-legend" aria-label="Architecture lifecycle legend">
              <line className="diagram-legend-line is-current" x1="88" y1="92" x2="128" y2="92" />
              <text x="138" y="97">Current / deployed</text>
              <line className="diagram-legend-line is-governed" x1="330" y1="92" x2="370" y2="92" />
              <text x="380" y="97">Current / governed</text>
              <line className="diagram-legend-line is-planned" x1="574" y1="92" x2="614" y2="92" />
              <text x="624" y="97">Planned / deferred</text>
              <line className="diagram-legend-line is-optional" x1="818" y1="92" x2="858" y2="92" />
              <text x="868" y="97">Optional external</text>
              <line className="diagram-legend-line is-future" x1="1015" y1="92" x2="1055" y2="92" />
              <text x="1065" y="97">Future scale</text>
            </g>

            <g filter="url(#celar-ai-shadow)">
              <rect className="diagram-node is-auth" id="box-auth-boundary" x="170" y="125" width="860" height="84" rx="12" />
              <text className="diagram-node-state is-current" id="state-auth-boundary" x="190" y="149">CURRENT SECURITY BOUNDARY</text>
              <DiagramText
                className="diagram-node-title"
                id="title-auth-boundary"
                x={600}
                y={170}
                lines={['Authentication · roles · permissions', 'Project · customer · team · record scope']}
                lineHeight={18}
              />
            </g>

            <path className="diagram-line is-current" d="M600 209 L600 235" />
            <ComponentNode
              componentId="module-011-workspace"
              x={260}
              y={235}
              width={680}
              height={105}
              state="CURRENT · DEPLOYED"
              title="Celar AI Module 011 Workspace"
              subtitle={['Governed intelligence experience and architecture control plane']}
            />

            <path className="diagram-line is-current no-arrow" d="M600 340 L600 355" />
            <circle className="diagram-junction is-current" cx="600" cy="355" r="6" />
            <path className="diagram-line is-current" d="M600 355 L310 355 L310 385" />
            <path className="diagram-line is-governed" d="M600 355 L890 355 L890 385" />

            <ComponentNode
              componentId="internal-data-intelligence"
              x={60}
              y={385}
              width={500}
              height={130}
              state="CURRENT · DEPLOYED · MIGRATION 080"
              title={['Permission-Scoped Internal', 'Data Intelligence']}
              subtitle={['Projects · people · time · finance', 'APIs · diagnostics · governed resolvers']}
            />
            <ComponentNode
              componentId="governed-document-storage"
              x={640}
              y={385}
              width={500}
              height={130}
              tone="governed"
              state="CURRENT · GOVERNED STORAGE"
              title={['Governed Private', 'Document Storage']}
              subtitle={['SOW · GSD · approved versions', 'Durable private processing evidence']}
            />

            <path className="diagram-line is-current" d="M310 515 L310 565" />
            <path className="diagram-line is-planned" d="M890 515 L890 565" />
            <ComponentNode
              componentId="pulse-postgresql"
              x={60}
              y={565}
              width={500}
              height={115}
              state="CURRENT · DEPLOYED"
              title={['Pulse PostgreSQL Data', 'and Evidence Plane']}
              subtitle={['Authoritative business data · audit · conversations', 'Routing and retrieval metadata']}
            />
            <ComponentNode
              componentId="private-document-worker"
              x={640}
              y={565}
              width={500}
              height={115}
              tone="deferred"
              state="DEFERRED · MIGRATION 081 NOT APPLIED"
              title="Celar AI Private Document Worker"
              subtitle={['Scan · extract/OCR · chunk · index · cite']}
            />

            <path className="diagram-line is-current" d="M310 680 L310 705 L38 705 L38 995 L360 995 L360 1010" />

            <g data-component-id="opencloud-runtime-vm">
              <rect className="diagram-boundary is-opencloud" id="box-opencloud-runtime-vm" x="70" y="735" width="820" height="220" rx="18" />
              <text className="diagram-boundary-title" id="title-opencloud-runtime-vm" x="95" y="764">OpenCloud Shared Private Runtime VM</text>
              <text className="diagram-boundary-status is-planned" id="state-opencloud-runtime-vm" x="865" y="764" textAnchor="end">PLANNED · NOT OPERATIONAL</text>
              <text className="diagram-boundary-copy" id="subtitle-opencloud-runtime-vm" x="95" y="788">One Linux VM · three isolated Podman / OCI containers · private network only</text>
            </g>

            <path className="diagram-line is-planned" d="M890 680 L890 710 L210 710 L210 805" />

            <ComponentNode
              componentId="clamav"
              x={100}
              y={805}
              width={220}
              height={105}
              tone="planned"
              state="PLANNED · OPENCLOUD"
              title={['ClamAV', 'Malware Scanning']}
              subtitle={['Pre-extraction safety gate']}
              compact
            />
            <ComponentNode
              componentId="tesseract-5"
              x={365}
              y={805}
              width={220}
              height={105}
              tone="planned"
              state="PLANNED · OPENCLOUD"
              title={['Tesseract 5', 'OCR Adapter']}
              subtitle={['Image-only text extraction']}
              compact
            />
            <ComponentNode
              componentId="ollama"
              x={630}
              y={805}
              width={230}
              height={105}
              tone="planned"
              state="PLANNED · OPENCLOUD"
              title={['Ollama Private Inference', 'and Embeddings']}
              subtitle={['Model to be selected']}
              compact
            />

            <path className="diagram-line is-planned" d="M320 857 L365 857" />
            <path className="diagram-line is-planned" d="M585 857 L630 857" />

            <ComponentNode
              componentId="ollama-gpu-scale"
              x={940}
              y={785}
              width={200}
              height={145}
              tone="future"
              state="FUTURE SCALE"
              title={['Ollama Production', 'GPU Scale-Out']}
              subtitle={['Dedicated private compute', 'Capacity-driven', 'future state']}
              compact
            />
            <path className="diagram-line is-future" d="M860 857 L940 857" />

            <path className="diagram-line is-planned" d="M745 910 L745 980 L800 980 L800 1010" />
            <g filter="url(#celar-ai-shadow)">
              <rect className="diagram-node is-context" id="box-context-fabric" x="180" y="1010" width="840" height="100" rx="14" />
              <text className="diagram-node-state is-current" id="state-context-fabric" x="202" y="1035">CURRENT GOVERNED ORCHESTRATION</text>
              <DiagramText
                className="diagram-node-title"
                id="title-context-fabric"
                x={600}
                y={1063}
                lines={['Celar AI intelligence and context fabric']}
              />
              <DiagramText
                className="diagram-node-subtitle"
                id="subtitle-context-fabric"
                x={600}
                y={1088}
                lines={['Scoped evidence · authoritative versions · freshness · policy · deterministic tools']}
              />
            </g>

            <path className="diagram-line is-current" d="M600 1110 L600 1160" />
            <g filter="url(#celar-ai-shadow)">
              <rect className="diagram-node is-confidence" id="box-route-decision" x="260" y="1160" width="680" height="85" rx="12" />
              <text className="diagram-node-state is-current" id="state-route-decision" x="282" y="1185">LIVE ROUTE DECISION</text>
              <DiagramText
                className="diagram-node-title"
                id="title-route-decision"
                x={600}
                y={1213}
                lines={['Confidence · freshness · policy · source eligibility']}
              />
              <DiagramText
                className="diagram-node-subtitle"
                id="subtitle-route-decision"
                x={600}
                y={1234}
                lines={['Only eligible targets follow the saved Module 064 order']}
              />
            </g>

            <path className="diagram-line is-current no-arrow" d="M600 1245 L600 1268" />
            <circle className="diagram-junction is-current" cx="600" cy="1268" r="6" />
            <path className="diagram-line is-governed" d="M600 1268 L290 1268 L290 1310" />
            <path className="diagram-line is-optional" d="M600 1268 L890 1268 L890 1285" />

            <g filter="url(#celar-ai-shadow)">
              <rect className="diagram-node is-sufficient" id="box-private-outcome" x="60" y="1310" width="460" height="90" rx="12" />
              <text className="diagram-node-state is-governed" id="state-private-outcome" x="82" y="1335">PRIVATE EVIDENCE SUFFICIENT</text>
              <DiagramText
                className="diagram-node-title"
                id="title-private-outcome"
                x={290}
                y={1364}
                lines={['Answer locally · no public call']}
              />
              <DiagramText
                className="diagram-node-subtitle"
                id="subtitle-private-outcome"
                x={290}
                y={1385}
                lines={['Private content never leaves the governed boundary']}
              />
            </g>

            <ComponentNode
              componentId="module-064-router"
              x={640}
              y={1285}
              width={500}
              height={115}
              tone="optional"
              state="CURRENT · GOVERNED ROUTER"
              title={['Module 064 Governed', 'Provider Router']}
              subtitle={['Identity-free capsule · saved order · health · circuit breakers']}
            />

            <path className="diagram-line is-optional no-arrow" d="M890 1400 L890 1420" />
            <circle className="diagram-junction is-optional" cx="890" cy="1420" r="6" />
            <path className="diagram-line is-optional" d="M890 1420 L755 1420 L755 1445" />
            <path className="diagram-line is-optional" d="M890 1420 L1025 1420 L1025 1445" />

            <ComponentNode
              componentId="claude-external"
              x={640}
              y={1445}
              width={230}
              height={90}
              tone="optional"
              state="OPTIONAL · GOVERNED"
              title="Claude"
              subtitle={['External reasoning', 'Module 064 managed']}
              compact
            />
            <ComponentNode
              componentId="openai-external"
              x={910}
              y={1445}
              width={230}
              height={90}
              tone="optional"
              state="OPTIONAL · GOVERNED"
              title="OpenAI"
              subtitle={['External reasoning', 'Module 064 managed']}
              compact
            />

            <path className="diagram-line is-governed no-arrow" d="M290 1400 L290 1560 L600 1560" />
            <path className="diagram-line is-optional no-arrow" d="M755 1535 L755 1560 L600 1560" />
            <path className="diagram-line is-optional no-arrow" d="M1025 1535 L1025 1560 L600 1560" />
            <circle className="diagram-junction is-current" cx="600" cy="1560" r="6" />
            <path className="diagram-line is-current" d="M600 1560 L600 1580" />
            <g filter="url(#celar-ai-shadow)">
              <rect className="diagram-node is-verification" id="box-output-gate" x="240" y="1580" width="720" height="75" rx="12" />
              <text className="diagram-node-state is-current" id="state-output-gate" x="262" y="1605">GOVERNED OUTPUT GATE</text>
              <DiagramText
                className="diagram-node-title"
                id="title-output-gate"
                x={600}
                y={1634}
                lines={['Privacy · source checks · citations · human review']}
              />
            </g>
            <path className="diagram-line is-current" d="M600 1655 L600 1685" />
            <rect className="diagram-result" id="box-result" x="230" y="1685" width="740" height="44" rx="10" />
            <text className="diagram-result-text" id="title-result" x="600" y="1713" textAnchor="middle">Cited answer · Timesheet · SOW · Plan · Timeline · Diagram</text>
            <text className="diagram-footer-note" x="600" y="1747" textAnchor="middle">13 managed components shown · architecture state is not runtime health evidence</text>
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
