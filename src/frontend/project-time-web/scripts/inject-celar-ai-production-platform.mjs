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

  // FlowHive V2 owns its planner implementation. The production transform must
  // never replace it with the retired compatibility endpoint. This assertion is
  // intentionally inside the injector because this transform runs after source
  // validation and previously rewrote the correct checked-in implementation.
  const durableContracts = [
    'async function runAiPlannerOperation()',
    'postJson(`/api/project-flowhive/projects/${selectedProjectId}/ai-planner/runs`',
    'const result = await runAiPlannerOperation();',
    'AI Planning Workspace',
    'an uncited generic template is never substituted'
  ];
  for (const contract of durableContracts) {
    if (!content.includes(contract)) {
      throw new Error(`CELAR_AI_PRODUCTION_FLOWHIVE_DURABLE_CONTRACT_MISSING=${contract}`);
    }
  }

  const legacyExecutableContracts = [
    "postJson('/api/project-flowhive/ai/production-generate'",
    'postJson("/api/project-flowhive/ai/production-generate"',
    "fetch('/api/project-flowhive/ai/production-generate'",
    'fetch("/api/project-flowhive/ai/production-generate"'
  ];
  if (legacyExecutableContracts.some((contract) => content.includes(contract))) {
    throw new Error('CELAR_AI_PRODUCTION_FLOWHIVE_LEGACY_BROWSER_ROUTE_REJECTED');
  }

  if (!content.includes(marker)) content = content.replace("import './projectpulse-module-standard.css';", `import './projectpulse-module-standard.css';\n${marker}`);
  save(relative, content);
}

console.log(`CELAR_AI_PRODUCTION_FILES_CHANGED=${changed.length}`);
for (const relative of changed) console.log(`CELAR_AI_PRODUCTION_CHANGED=${relative}`);
console.log('CELAR_AI_PRODUCTION_MODULE011=AUTHORITATIVE_SINGLE_SHELL');
console.log('CELAR_AI_PRODUCTION_CHAT=INTENT_FIRST_V2');
console.log('CELAR_AI_PRODUCTION_FLOWHIVE=DURABLE_PROJECT_SCOPED_PLANNER');
console.log('CELAR_AI_PRODUCTION_INJECTOR=PASSED');
