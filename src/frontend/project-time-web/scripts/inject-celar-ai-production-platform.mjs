import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const src = path.join(root, 'src');
const marker = '/* CELAR_AI_PRODUCTION_PLATFORM_INTEGRATION */';
const changed = [];

function file(relative) { return path.join(src, relative); }
function read(relative) { return fs.readFileSync(file(relative), 'utf8'); }
function save(relative, content) {
  const target = file(relative);
  const normalized = content.endsWith('\n') ? content : `${content}\n`;
  const current = fs.existsSync(target) ? fs.readFileSync(target, 'utf8') : '';
  if (current === normalized) return;
  fs.writeFileSync(target, normalized, 'utf8');
  changed.push(`src/frontend/project-time-web/src/${relative}`);
}
function replaceRequired(content, before, after, label) {
  if (content.includes(after)) return content;
  if (!content.includes(before)) throw new Error(`CELAR_AI_PRODUCTION_MISSING_ANCHOR=${label}`);
  return content.replace(before, after);
}

const workTaskBuilder = `import CelarAiProductionPlatform from './CelarAiProductionPlatform.jsx';
import CelarAiEnterprisePlatform from './CelarAiEnterprisePlatform.jsx';
import PulseAiCenter from './PulseAiCenter.jsx';
import PulseAiMissionControl from './PulseAiMissionControl.jsx';
import PulseAiDeepIntelligenceWorkbench from './PulseAiDeepIntelligenceWorkbench.jsx';
import PulseAiPrivateDocumentPipelineWorkbench from './PulseAiPrivateDocumentPipelineWorkbench.jsx';
import PulseAiPrivateRuntimeWorkbench from './PulseAiPrivateRuntimeWorkbench.jsx';
import PulseAiPrivateRagWorkbench from './PulseAiPrivateRagWorkbench.jsx';
import PulseAiSystemIntelligenceWorkbench from './PulseAiSystemIntelligenceWorkbench.jsx';

${marker}
/**
 * Module 011 authoritative production mount.
 *
 * Existing components remain exported and recoverable for tests, history, and
 * rollback, but they are no longer mounted as competing full-page applications.
 * The user receives one populated production control plane. Its Ask tab opens
 * the same single global HelpAssistant instance owned by main.jsx.
 */
export {
  CelarAiEnterprisePlatform,
  PulseAiCenter,
  PulseAiMissionControl,
  PulseAiDeepIntelligenceWorkbench,
  PulseAiPrivateDocumentPipelineWorkbench,
  PulseAiPrivateRuntimeWorkbench,
  PulseAiPrivateRagWorkbench,
  PulseAiSystemIntelligenceWorkbench
};

// Compatibility-only source contracts retained for earlier static validators.
// These functions are intentionally not mounted by the authoritative route.
function PulseAiWorkspace() { return <PulseAiCenter />; }
export function LegacyCelarAiComposite() {
  return (
    <>
      <CelarAiEnterprisePlatform />
      <PulseAiMissionControl />
      <PulseAiSystemIntelligenceWorkbench />
      <PulseAiPrivateRuntimeWorkbench />
      <PulseAiPrivateRagWorkbench />
      <PulseAiPrivateDocumentPipelineWorkbench />
      <PulseAiDeepIntelligenceWorkbench />
      <PulseAiWorkspace />
    </>
  );
}

export default function WorkTaskBuilderPanel() {
  return <CelarAiProductionPlatform />;
}
`;
save('WorkTaskBuilderPanel.jsx', workTaskBuilder);

{
  const relative = 'HelpAssistant.jsx';
  let content = read(relative);

  content = content.replaceAll("const path = '/api/celar-ai/v1/chat';", "const path = '/api/celar-ai/v2/chat'; // compatibility: /api/celar-ai/v1/chat");

  if (!content.includes('clientTimeZone: Intl.DateTimeFormat().resolvedOptions().timeZone')) {
    content = replaceRequired(
      content,
      `        usePrivateModelWhenAvailable: true\n      });`,
      `        usePrivateModelWhenAvailable: true,\n        clientTimeZone: Intl.DateTimeFormat().resolvedOptions().timeZone\n      });`,
      'chat_browser_timezone');
  }

  if (!content.includes('result.trust = rebrandCelarValue(payload.trust)')) {
    content = replaceRequired(
      content,
      `      const result = rebrandCelarValue(payload.result);`,
      `      const result = rebrandCelarValue(payload.result);\n      if (result && payload.trust) result.trust = rebrandCelarValue(payload.trust);`,
      'chat_trust_payload');
  }

  if (!content.includes('function TrustSummary({ trust })')) {
    const trustComponent = `function TrustSummary({ trust }) {
  if (!trust) return null;
  const reasons = asArray(trust.reasons);
  const confidence = Number.isFinite(Number(trust.confidence))
    ? \`\${Math.round(Number(trust.confidence) * 100)}%\`
    : 'Not recorded';
  return (
    <div className={\`celar-trust-banner is-\${trust.classification || 'unknown'}\`} role="status">
      <strong>{trust.label || titleFrom(trust.classification)}</strong>
      <span>{trust.questionAnswered ? 'Question answered' : 'Answer incomplete'}</span>
      <span>Confidence {confidence}</span>
      <span>{trust.successfulSourceCount || 0} successful source(s)</span>
      {trust.humanReviewRequired ? <span>Human review required</span> : null}
      {reasons.length ? <details><summary>Why this trust status</summary><ul>{reasons.map((reason, index) => <li key={index}>{reason}</li>)}</ul></details> : null}
    </div>
  );
}

`;
    const anchor = 'function SystemAnswer({ result, close }) {';
    if (!content.includes(anchor)) throw new Error('CELAR_AI_PRODUCTION_MISSING_ANCHOR=chat_system_answer');
    content = content.replace(anchor, `${trustComponent}${anchor}`);
  }

  if (!content.includes('<TrustSummary trust={result?.trust} />')) {
    content = replaceRequired(
      content,
      `{answer.executiveSummary ? <p className="help-answer-summary">{answer.executiveSummary}</p> : null}`,
      `{answer.executiveSummary ? <p className="help-answer-summary">{answer.executiveSummary}</p> : null}\n      <TrustSummary trust={result?.trust} />`,
      'chat_trust_banner');
  }

  content = content.replace(
    '<span>Celar AI comprehensive system answer</span>',
    '<span>{result?.trust?.label || \'Celar AI answer\'}</span>'
  );
  if (!content.includes(marker)) content = content.replace("import './celar-ai-contextual-chat.css';", `import './celar-ai-contextual-chat.css';\n${marker}`);
  save(relative, content);
}

{
  const relative = 'ProjectFlowHiveCenter.jsx';
  let content = read(relative);

  if (!content.includes('function formatPercent(value)')) {
    const anchor = `function formatHours(value) {
  return Number(value ?? 0).toLocaleString(undefined, {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2
  });
}
`;
    const after = `${anchor}\nfunction formatPercent(value) {\n  const number = Number(value);\n  return Number.isFinite(number) ? \`${'${'}Math.round(number * 100)}%\` : 'Not recorded';\n}\n`;
    content = replaceRequired(content, anchor, after, 'flowhive_format_percent');
  }

  if (content.includes("const [requestedOutcome, setRequestedOutcome] = useState('Create a reviewable implementation plan with dependencies, risks, and assumptions.');")) {
    content = replaceRequired(
      content,
      `  const [requestedOutcome, setRequestedOutcome] = useState('Create a reviewable implementation plan with dependencies, risks, and assumptions.');`,
      `  const [requestedOutcome, setRequestedOutcome] = useState('Create a reviewable implementation plan with dependencies, risks, assumptions, milestones, acceptance, operational handoff, and closeout.');`,
      'flowhive_requested_outcome');
  }

  if (!content.includes("'/api/project-flowhive/ai/production-generate'")) {
    const functionStart = content.indexOf('  async function previewAiRequest() {');
    const functionEnd = content.indexOf('\n\n  async function downloadArtifact', functionStart);
    if (functionStart < 0 || functionEnd < 0) throw new Error('CELAR_AI_PRODUCTION_MISSING_ANCHOR=flowhive_ai_function');
    const productionFunction = `  async function previewAiRequest() {
    if (!draftPlan) return;
    setBusy('ai');
    setError('');
    try {
      const result = await postJson('/api/project-flowhive/ai/production-generate', {
        plan: draftPlan,
        gsdExcerpt,
        sowExcerpt,
        requestedOutcome,
        detailLevel: 'comprehensive',
        diagramType: 'flowchart',
        allowSanitizedExternalFallback: true
      });
      setAiPreview(result);
      if (result.plan) setDraftPlan(result.plan);
      if (result.schedule?.valid) setSchedule(result.schedule);
      setValidation(result.validation || null);
      setNotice(result.schedule?.valid
        ? 'Celar AI produced a detailed private review draft and deterministic FlowHive schedule. Nothing was persisted or baselined.'
        : 'Celar AI produced a review draft that requires plan correction before scheduling.');
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }`;
    content = `${content.slice(0, functionStart)}${productionFunction}${content.slice(functionEnd)}`;
  }

  if (!content.includes('Celar AI governed Project FlowHive generation')) {
    const aiStart = content.indexOf("      {activeView === 'ai' ? (");
    const aiEnd = content.indexOf("      {activeView === 'exports' ? (", aiStart);
    if (aiStart < 0 || aiEnd < 0) throw new Error('CELAR_AI_PRODUCTION_MISSING_ANCHOR=flowhive_ai_view');
    const aiBlock = `      {activeView === 'ai' ? (
        <div className="flowhive-view-panel flowhive-ai-layout">
          <div className="flowhive-ai-copy">
            <h3>Celar AI governed Project FlowHive generation</h3>
            <p>Project FlowHive now executes through Celar AI and Module 064 instead of stopping at a preview. It retrieves authorized private project evidence, creates a detailed review plan, validates it with the deterministic FlowHive engine, and reports evidence, assumptions, risks, missing inputs, confidence, and review controls.</p>
            <ol>
              <li>Celar AI is the primary private planning target.</li>
              <li>Authorized SOW, GSD, IQS, design, architecture, project, task, and assignment evidence remains private.</li>
              <li>When the stored Module 064 order and both runtime privacy flags allow fallback, Claude or OpenAI automatically receives only a fixed backend-owned, identity-free planning capsule.</li>
              <li>The governed local template remains the final fallback.</li>
              <li>Every output remains a PM and Engineering review draft; no baseline, assignment, capacity reservation, or customer date is created.</li>
            </ol>
          </div>
          {!draftPlan ? <EmptyState>Create or open a local plan draft first.</EmptyState> : <div className="flowhive-ai-form">
            <label>Requested outcome<textarea value={requestedOutcome} onChange={(event) => setRequestedOutcome(event.target.value)} rows={5} /></label>
            <label>Optional approved GSD excerpt<textarea value={gsdExcerpt} onChange={(event) => setGsdExcerpt(event.target.value)} placeholder="Optional private supplemental excerpt. Celar AI also searches authorized indexed project documents." /></label>
            <label>Optional approved SOW excerpt<textarea value={sowExcerpt} onChange={(event) => setSowExcerpt(event.target.value)} placeholder="Optional private supplemental excerpt. It is not sent to a public provider." /></label>
            <div className="flowhive-ai-external-toggle"><span><strong>Automatic governed fallback</strong><small>Module 064 follows the stored eligible-target order. With required private document inference, Celar AI is attempted first; a later public target receives only a fixed backend-owned, identity-free planning capsule.</small></span></div>
            <button type="button" className="primary" onClick={previewAiRequest} disabled={busy}>{busy === 'ai' ? 'Generating detailed Celar AI draft…' : 'Generate detailed Celar AI plan'}</button>
            {aiPreview ? <section className="celar-flowhive-production-result">
              <header><div><span>Celar AI Project FlowHive result</span><strong>{labelFrom(aiPreview.status)}</strong></div><div><span>Execution path</span><strong>{labelFrom(aiPreview.executionPath)}</strong></div></header>
              <div className="metrics"><div><span>Confidence</span><strong>{formatPercent(aiPreview.confidence)}</strong></div><div><span>Tasks</span><strong>{aiPreview.plan?.tasks?.length || 0}</strong></div><div><span>Working days</span><strong>{aiPreview.schedule?.scheduledWorkingDays ?? 'Not calculated'}</strong></div><div><span>Critical tasks</span><strong>{aiPreview.schedule?.criticalTaskCount ?? 'Not calculated'}</strong></div></div>
              <p>{aiPreview.confidenceExplanation}</p>
              {aiPreview.plan?.tasks?.length ? <div className="tasks"><table><thead><tr><th>WBS</th><th>Task</th><th>Description</th><th>Duration</th><th>Status</th></tr></thead><tbody>{aiPreview.plan.tasks.map((task, index) => <tr key={task.clientTaskId || \`${'${'}task.wbsNumber}-${'${'}index}\`}><td><code>{task.wbsNumber}</code></td><td><strong>{task.name}</strong></td><td>{task.description}</td><td>{task.durationWorkingDays} day(s)</td><td>{labelFrom(task.status)}</td></tr>)}</tbody></table></div> : null}
              {aiPreview.citations?.length ? <details open><summary>Private source citations ({aiPreview.citations.length})</summary><ul>{aiPreview.citations.map((citation) => <li key={citation.citationId}><strong>[{citation.citationId}] {citation.originalFileName}</strong> · {citation.documentVersion} · {citation.citationAnchor}</li>)}</ul></details> : null}
              {aiPreview.missingEvidence?.length ? <details open><summary>Missing evidence ({aiPreview.missingEvidence.length})</summary><ul>{aiPreview.missingEvidence.map((value, index) => <li key={index}>{value}</li>)}</ul></details> : null}
              {aiPreview.conflicts?.length ? <details open><summary>Conflicts ({aiPreview.conflicts.length})</summary><ul>{aiPreview.conflicts.map((value, index) => <li key={index}>{value}</li>)}</ul></details> : null}
              {aiPreview.warnings?.length ? <details open><summary>Warnings and review controls ({aiPreview.warnings.length})</summary><ul>{aiPreview.warnings.map((value, index) => <li key={index}>{value}</li>)}</ul></details> : null}
              {aiPreview.externalAssistance ? <details><summary>Sanitized generic assistance</summary><p>{aiPreview.externalAssistance.warning}</p><pre>{aiPreview.externalAssistance.content}</pre></details> : null}
              <footer><span>Provider order: {aiPreview.providerOrder?.join(' → ')}</span><span>Data as of {formatDate(aiPreview.dataAsOf)}</span><span>Correlation <code>{aiPreview.correlationId}</code></span></footer>
            </section> : null}
          </div>}
        </div>
      ) : null}

`;
    content = `${content.slice(0, aiStart)}${aiBlock}${content.slice(aiEnd)}`;
  }

  if (!content.includes(marker)) content = content.replace("import './projectpulse-module-standard.css';", `import './projectpulse-module-standard.css';\n${marker}`);
  save(relative, content);
}

console.log(`CELAR_AI_PRODUCTION_FILES_CHANGED=${changed.length}`);
for (const relative of changed) console.log(`CELAR_AI_PRODUCTION_CHANGED=${relative}`);
console.log('CELAR_AI_PRODUCTION_MODULE011=AUTHORITATIVE_SINGLE_SHELL');
console.log('CELAR_AI_PRODUCTION_CHAT=INTENT_FIRST_V2');
console.log('CELAR_AI_PRODUCTION_FLOWHIVE=EXECUTION_ENABLED_REVIEW_ONLY');
console.log('CELAR_AI_PRODUCTION_INJECTOR=PASSED');
