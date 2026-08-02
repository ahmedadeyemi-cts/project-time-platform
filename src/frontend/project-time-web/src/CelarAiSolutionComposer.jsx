import { useMemo, useState } from 'react';
import './celar-ai-solution-composer.css';

const MODES = Object.freeze([
  ['project_plan', 'Project plan', 'Create a cited WBS, milestones, dependencies, roles, risks, and open questions.'],
  ['project_timeline', 'High-level timeline', 'Sequence the private project-plan draft into reviewable business-day timing.'],
  ['project_diagram', 'Project diagram', 'Generate a visual flow from the project scope, tasks, dependencies, and milestones.'],
  ['sow_draft', 'SOW draft', 'Create a comprehensive, non-binding Statement of Work draft for review.'],
  ['timesheet_description', 'Timesheet description', 'Draft an accurate description from the Engineer note and authorized project evidence.']
]);

function asArray(value) { return Array.isArray(value) ? value : []; }
function formatDate(value) {
  if (!value) return 'Not recorded';
  const date = new Date(`${value}T12:00:00`);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleDateString();
}
function formatPercent(value) {
  const number = Number(value);
  return Number.isFinite(number) ? `${Math.round(number * 100)}%` : 'Not recorded';
}
async function postJson(path, body) {
  const response = await fetch(path, {
    method: 'POST',
    cache: 'no-store',
    headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.message || payload?.result?.confidenceExplanation || `Request returned HTTP ${response.status}.`);
  return payload;
}

function ListSection({ title, values, ordered = false, open = false }) {
  const rows = asArray(values).filter(Boolean);
  if (!rows.length) return null;
  const List = ordered ? 'ol' : 'ul';
  return (
    <details className="celar-ai-composer-section" open={open}>
      <summary><span>{title}</span><small>{rows.length}</small></summary>
      <List>{rows.map((value, index) => <li key={`${title}-${index}`}>{String(value)}</li>)}</List>
    </details>
  );
}

function DetailedAnswer({ answer }) {
  if (!answer) return null;
  return (
    <section className="celar-ai-composer-answer">
      <h4>{answer.directConclusion}</h4>
      <p>{answer.executiveSummary}</p>
      <ListSection title="Scope and filters" values={answer.scopeAndFilters} open />
      <ListSection title="Detailed analysis" values={answer.detailedAnalysis} open />
      <ListSection title="Source evidence" values={answer.sourceEvidence} />
      <ListSection title="Calculations" values={answer.calculations} />
      <ListSection title="Known, unknown, stale, and unavailable values" values={answer.knownUnknownAndStaleValues} />
      <ListSection title="Assumptions" values={answer.assumptions} />
      <ListSection title="Conflicts" values={answer.conflicts} />
      <ListSection title="Limitations" values={answer.limitations} />
      <ListSection title="Risks and implications" values={answer.risksAndImplications} />
      <ListSection title="Recommended actions" values={answer.recommendedActions} ordered open />
    </section>
  );
}

function SowDraft({ sow }) {
  if (!sow) return null;
  return (
    <section className="celar-ai-sow-draft">
      <div className="celar-ai-composer-result-heading">
        <div><small>Non-binding review draft</small><h4>{sow.title}</h4></div>
        <span>{sow.reviewRequired ? 'Review required' : 'Review state not recorded'}</span>
      </div>
      <p>{sow.executiveSummary}</p>
      <ListSection title="Objectives" values={sow.objectives} open />
      <ListSection title="In scope" values={sow.inScope} open />
      <ListSection title="Out of scope" values={sow.outOfScope} />
      <ListSection title="Deliverables" values={sow.deliverables} open />
      <ListSection title="Customer responsibilities" values={sow.customerResponsibilities} />
      <ListSection title="US Signal responsibilities" values={sow.usSignalResponsibilities} />
      <ListSection title="Assumptions" values={sow.assumptions} />
      <ListSection title="Dependencies" values={sow.dependencies} />
      <ListSection title="Acceptance criteria" values={sow.acceptanceCriteria} />
      <ListSection title="Timeline and milestones" values={sow.timelineAndMilestones} />
      <ListSection title="Risks" values={sow.risks} />
      <ListSection title="Open questions" values={sow.openQuestions} open />
      <div className="celar-ai-composer-warning">This draft is not contractually binding. Commercial, legal, technical, security, delivery, and customer approval remain required.</div>
    </section>
  );
}

function PlanTable({ plan }) {
  const tasks = asArray(plan?.tasks);
  if (!tasks.length) return null;
  return (
    <section className="celar-ai-plan-table">
      <div className="celar-ai-composer-result-heading"><div><small>FlowHive review draft</small><h4>{plan.objective}</h4></div><span>{tasks.length} tasks</span></div>
      <div className="celar-ai-composer-table-wrap">
        <table>
          <thead><tr><th>WBS</th><th>Task</th><th>Description</th><th>Duration</th><th>Predecessors</th><th>Roles</th><th>Evidence</th></tr></thead>
          <tbody>
            {tasks.map((task, index) => (
              <tr key={`${task.wbs}-${index}`}>
                <td><code>{task.wbs}</code></td>
                <td><strong>{task.name}</strong>{task.isAssumption ? <small>Assumption</small> : null}</td>
                <td>{task.description}</td>
                <td>{task.estimatedDurationDays} day(s)</td>
                <td>{asArray(task.predecessors).join(', ') || 'None'}</td>
                <td>{asArray(task.requiredRoles).join(', ') || 'Not assigned'}</td>
                <td>{asArray(task.citationIds).map((id) => `[${id}]`).join(' ') || 'Assumption'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <ListSection title="Dependencies" values={plan.dependencies} />
      <ListSection title="Milestones" values={asArray(plan.milestones).map((milestone) => `${milestone.name}: ${milestone.description} (${milestone.proposedTiming})`)} />
      <ListSection title="Assumptions" values={plan.assumptions} />
      <ListSection title="Risks" values={plan.risks} />
      <ListSection title="Out of scope" values={plan.outOfScopeItems} />
      <ListSection title="Open questions" values={plan.openQuestions} open />
      <ListSection title="Conflicts" values={plan.conflicts} />
    </section>
  );
}

function Timeline({ items }) {
  const rows = asArray(items);
  if (!rows.length) return null;
  const start = Math.min(...rows.map((item) => new Date(`${item.startDate}T12:00:00`).getTime()));
  const end = Math.max(...rows.map((item) => new Date(`${item.endDate}T12:00:00`).getTime()));
  const span = Math.max(1, end - start);
  return (
    <section className="celar-ai-timeline">
      <div className="celar-ai-composer-result-heading"><div><small>High-level business-day draft</small><h4>Project timeline</h4></div><span>{formatDate(rows[0].startDate)} — {formatDate(rows.at(-1).endDate)}</span></div>
      <div className="celar-ai-timeline-grid">
        {rows.map((item) => {
          const itemStart = new Date(`${item.startDate}T12:00:00`).getTime();
          const itemEnd = new Date(`${item.endDate}T12:00:00`).getTime();
          const left = ((itemStart - start) / span) * 100;
          const width = Math.max(4, ((itemEnd - itemStart + 86400000) / (span + 86400000)) * 100);
          return (
            <article key={item.id}>
              <div><strong>{item.wbs} · {item.name}</strong><small>{formatDate(item.startDate)} — {formatDate(item.endDate)} · {item.durationBusinessDays} business day(s)</small></div>
              <div className="celar-ai-timeline-track" aria-label={`${item.name}: ${formatDate(item.startDate)} through ${formatDate(item.endDate)}`}><span style={{ left: `${left}%`, width: `${Math.min(width, 100 - left)}%` }} /></div>
              <p>{item.description}</p>
            </article>
          );
        })}
      </div>
      <div className="celar-ai-composer-warning">The authoritative baseline must be recalculated in FlowHive with approved calendars, holidays, resource capacity, dependencies, and customer constraints.</div>
    </section>
  );
}

function layoutDiagram(diagram) {
  const nodes = asArray(diagram?.nodes);
  const positions = new Map();
  const columns = Math.min(3, Math.max(1, nodes.length));
  nodes.forEach((node, index) => positions.set(node.id, { x: 55 + (index % columns) * 285, y: 65 + Math.floor(index / columns) * 135, width: 225, height: 78 }));
  const rows = Math.max(1, Math.ceil(nodes.length / columns));
  return { nodes, positions, width: 55 + columns * 285, height: 85 + rows * 135 };
}

function Diagram({ diagram }) {
  const layout = useMemo(() => layoutDiagram(diagram), [diagram]);
  if (!diagram || !layout.nodes.length) return null;
  const edges = asArray(diagram.edges);

  function downloadSvg() {
    const lines = [
      `<svg xmlns="http://www.w3.org/2000/svg" width="${layout.width}" height="${layout.height}" viewBox="0 0 ${layout.width} ${layout.height}">`,
      '<rect width="100%" height="100%" fill="white"/>',
      `<text x="24" y="30" font-family="Arial" font-size="18" font-weight="700" fill="#072d59">${escapeXml(diagram.title)}</text>`,
      '<defs><marker id="a" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto"><path d="M0,0 L0,6 L9,3 z" fill="#397ebd"/></marker></defs>'
    ];
    edges.forEach((edge) => {
      const from = layout.positions.get(edge.from); const to = layout.positions.get(edge.to);
      if (!from || !to) return;
      lines.push(`<line x1="${from.x + from.width}" y1="${from.y + from.height / 2}" x2="${to.x}" y2="${to.y + to.height / 2}" stroke="#397ebd" stroke-width="2" marker-end="url(#a)"/>`);
    });
    layout.nodes.forEach((node) => {
      const box = layout.positions.get(node.id);
      lines.push(`<rect x="${box.x}" y="${box.y}" width="${box.width}" height="${box.height}" rx="10" fill="${node.kind === 'milestone' ? '#fff7e8' : '#eef7ff'}" stroke="${node.kind === 'milestone' ? '#c77a1c' : '#397ebd'}"/>`);
      lines.push(`<text x="${box.x + 12}" y="${box.y + 28}" font-family="Arial" font-size="13" font-weight="700" fill="#072d59">${escapeXml(node.label).slice(0, 42)}</text>`);
      lines.push(`<text x="${box.x + 12}" y="${box.y + 52}" font-family="Arial" font-size="10" fill="#52677a">${escapeXml(node.subtitle).slice(0, 55)}</text>`);
    });
    lines.push(`<text x="24" y="${layout.height - 18}" font-family="Arial" font-size="10" fill="#52677a">Created by Dr. Ahmed Adeyemi · Celar AI review draft · not a customer commitment</text></svg>`);
    const blob = new Blob([lines.join('')], { type: 'image/svg+xml' });
    const url = URL.createObjectURL(blob); const anchor = document.createElement('a');
    anchor.href = url; anchor.download = 'celar-ai-project-diagram.svg'; anchor.click(); URL.revokeObjectURL(url);
  }

  return (
    <section className="celar-ai-generated-diagram">
      <div className="celar-ai-composer-result-heading"><div><small>{diagram.diagramType} · private review artifact</small><h4>{diagram.title}</h4></div><button type="button" onClick={downloadSvg}>Download SVG</button></div>
      <p>{diagram.description}</p>
      <div className="celar-ai-generated-diagram-scroll" tabIndex={0}>
        <svg viewBox={`0 0 ${layout.width} ${layout.height}`} role="img" aria-label={diagram.accessibilitySummary}>
          <defs><marker id="composer-arrow" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto"><path d="M0,0 L0,6 L9,3 z" /></marker></defs>
          {edges.map((edge, index) => {
            const from = layout.positions.get(edge.from); const to = layout.positions.get(edge.to);
            if (!from || !to) return null;
            return <g key={`${edge.from}-${edge.to}-${index}`}><line x1={from.x + from.width} y1={from.y + from.height / 2} x2={to.x} y2={to.y + to.height / 2} /><text x={(from.x + from.width + to.x) / 2} y={(from.y + to.y) / 2 + 28}>{edge.label}</text></g>;
          })}
          {layout.nodes.map((node) => { const box = layout.positions.get(node.id); return <g key={node.id} className={`is-${node.kind}`}><rect x={box.x} y={box.y} width={box.width} height={box.height} rx="11" /><text className="node-title" x={box.x + 12} y={box.y + 28}>{node.label.slice(0, 38)}</text><text className="node-subtitle" x={box.x + 12} y={box.y + 53}>{node.subtitle.slice(0, 48)}</text>{node.isAssumption ? <text className="node-assumption" x={box.x + box.width - 10} y={box.y + 17} textAnchor="end">Assumption</text> : null}</g>; })}
          <text className="diagram-credit" x="24" y={layout.height - 18}>Created by Dr. Ahmed Adeyemi · review draft · not a customer commitment</text>
        </svg>
      </div>
      <details><summary>Mermaid source</summary><pre>{diagram.mermaidSource}</pre></details>
    </section>
  );
}

function Citations({ citations }) {
  const rows = asArray(citations);
  if (!rows.length) return null;
  return <details className="celar-ai-citations"><summary>Private source citations <small>{rows.length}</small></summary><div>{rows.map((citation) => <article key={`${citation.citationId}-${citation.documentId}`}><strong>[{citation.citationId}] {citation.originalFileName}</strong><span>{citation.documentCategory} · {citation.documentVersion}</span><p>{citation.citationAnchor}{citation.pageNumber ? ` · page ${citation.pageNumber}` : ''}{citation.sheetName ? ` · ${citation.sheetName}` : ''}</p><small>Processed {new Date(citation.processedAt).toLocaleString()} · relevance {formatPercent(citation.relevanceScore)}</small></article>)}</div></details>;
}

function ExternalAssistance({ assistance }) {
  if (!assistance) return null;
  return <details className="celar-ai-external-assistance"><summary>Sanitized generic reasoning assistance</summary><p>{assistance.warning}</p>{assistance.content ? <pre>{assistance.content}</pre> : null}<ListSection title="Blocked reasons" values={assistance.blockedReasons} /><small>Provider: {assistance.provider || 'none'} · provider called: {assistance.providerCalled ? 'yes' : 'no'} · authorized: {assistance.authorized ? 'yes' : 'no'}</small></details>;
}

function escapeXml(value) { return String(value ?? '').replace(/[<>&"']/g, (character) => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&apos;' }[character])); }

export default function CelarAiSolutionComposer() {
  const [form, setForm] = useState({ mode: 'project_plan', projectCode: '', projectName: '', startDate: '', requestedOutcome: '', diagramType: 'flowchart', workDate: '', timeType: 'normal', rowType: 'project', rowLabel: '', taskCode: '', taskName: '', categoryCode: '', engineerNote: '', allowSanitizedExternalFallback: false });
  const [result, setResult] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const selectedMode = MODES.find(([code]) => code === form.mode) ?? MODES[0];

  function set(field, value) { setForm((current) => ({ ...current, [field]: value })); }
  async function submit(event) {
    event.preventDefault(); setLoading(true); setError(''); setResult(null);
    try {
      const payload = await postJson('/api/celar-ai/v1/compose', {
        ...form,
        startDate: form.startDate || null,
        workDate: form.workDate || null,
        detailLevel: 'comprehensive'
      });
      setResult(payload.result);
    } catch (requestError) { setError(requestError instanceof Error ? requestError.message : 'Celar AI could not compose this draft.'); }
    finally { setLoading(false); }
  }

  return (
    <section className="celar-ai-solution-composer" aria-labelledby="celar-ai-composer-title">
      <div className="celar-ai-composer-heading"><div><p>Private enterprise solution composer</p><h2 id="celar-ai-composer-title">Generate a detailed review artifact</h2><span>Use authorized private project evidence to draft Timesheet descriptions, SOWs, plans, high-level timelines, and diagrams. Nothing is saved, submitted, published, assigned, baselined, or committed.</span></div></div>
      <div className="celar-ai-composer-layout">
        <form onSubmit={submit}>
          <div className="celar-ai-composer-mode-grid">
            {MODES.map(([code, label, description]) => <button type="button" key={code} className={form.mode === code ? 'is-active' : ''} onClick={() => set('mode', code)}><strong>{label}</strong><span>{description}</span></button>)}
          </div>
          <div className="celar-ai-composer-field-grid">
            <label>Project code<input value={form.projectCode} onChange={(event) => set('projectCode', event.target.value)} placeholder="Project code" /></label>
            <label>Project name<input value={form.projectName} onChange={(event) => set('projectName', event.target.value)} placeholder="Authorized project name" /></label>
          </div>
          {form.mode === 'timesheet_description' ? (
            <>
              <div className="celar-ai-composer-field-grid"><label>Work date<input type="date" value={form.workDate} onChange={(event) => set('workDate', event.target.value)} /></label><label>Time type<select value={form.timeType} onChange={(event) => set('timeType', event.target.value)}><option value="normal">Normal</option><option value="afterhours">Afterhours</option></select></label></div>
              <div className="celar-ai-composer-field-grid"><label>Task code<input value={form.taskCode} onChange={(event) => set('taskCode', event.target.value)} /></label><label>Task or request name<input value={form.taskName} onChange={(event) => set('taskName', event.target.value)} /></label></div>
              <label>Engineer’s factual rough note<textarea value={form.engineerNote} onChange={(event) => set('engineerNote', event.target.value)} rows={4} placeholder="Describe the work actually performed. The SOW or GSD cannot prove work occurred." /></label>
            </>
          ) : (
            <>
              <div className="celar-ai-composer-field-grid"><label>Proposed start date<input type="date" value={form.startDate} onChange={(event) => set('startDate', event.target.value)} /></label><label>Diagram type<select value={form.diagramType} onChange={(event) => set('diagramType', event.target.value)}><option value="flowchart">Flowchart</option><option value="timeline">Timeline</option><option value="dependency">Dependency</option><option value="swimlane">Swimlane</option></select></label></div>
              <label>Requested outcome and PM instructions<textarea value={form.requestedOutcome} onChange={(event) => set('requestedOutcome', event.target.value)} rows={5} placeholder="Describe the outcome, planning horizon, assumptions to test, or questions the PM and Engineering team need the draft to address." /></label>
              <label className="celar-ai-external-checkbox"><input type="checkbox" checked={form.allowSanitizedExternalFallback} onChange={(event) => set('allowSanitizedExternalFallback', event.target.checked)} /><span><strong>Allow generic sanitized fallback when private evidence is insufficient</strong><small>No document text, customer/project identity, people records, financial values, credentials, or internal architecture details may leave the private boundary. Runtime policy must also allow it.</small></span></label>
            </>
          )}
          <div className="celar-ai-composer-submit"><div><strong>{selectedMode[1]}</strong><span>{selectedMode[2]}</span></div><button type="submit" disabled={loading || (!form.projectCode.trim() && !form.projectName.trim())}>{loading ? 'Composing privately…' : 'Generate review draft'}</button></div>
        </form>

        <div className="celar-ai-composer-output" aria-live="polite">
          {!result && !error ? <div className="celar-ai-composer-empty"><strong>Ready for an authorized project</strong><p>Select a solution mode, identify the project, and describe the desired outcome. Celar AI will report missing evidence rather than fabricate project facts.</p></div> : null}
          {error ? <div className="celar-ai-composer-error"><strong>Draft did not complete</strong><p>{error}</p></div> : null}
          {result ? (
            <>
              <div className="celar-ai-composer-status"><span>{String(result.status).replaceAll('_', ' ')}</span><span>Confidence {formatPercent(result.confidence)}</span><span>Coverage {formatPercent(result.coverageScore)}</span><span>{result.primaryExecutionPath}</span></div>
              <DetailedAnswer answer={result.detailedAnswer} />
              <SowDraft sow={result.sowDraft} />
              <PlanTable plan={result.flowHivePlan} />
              <Timeline items={result.timeline} />
              <Diagram diagram={result.diagram} />
              <Citations citations={result.citations} />
              <ExternalAssistance assistance={result.externalAssistance} />
              <ListSection title="Missing evidence" values={result.missingEvidence} open />
              <ListSection title="Conflicts" values={result.conflicts} />
              <ListSection title="Warnings and review controls" values={result.warnings} open />
              <div className="celar-ai-composer-footer"><span>Data as of {new Date(result.dataAsOf).toLocaleString()}</span><span>Correlation <code>{result.correlationId}</code></span><span>{result.confidenceExplanation}</span></div>
            </>
          ) : null}
        </div>
      </div>
    </section>
  );
}
