import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = fileURLToPath(new URL('../', import.meta.url));
const helpPath = path.join(webRoot, 'src', 'HelpAssistant.jsx');
let source = fs.readFileSync(helpPath, 'utf8');

function replaceOnce(anchor, replacement, label) {
  const count = source.split(anchor).length - 1;
  if (count !== 1) {
    throw new Error(`CELAR_AI_SERVER_INTENT_${label}=FAILED expected=1 actual=${count}`);
  }
  source = source.replace(anchor, replacement);
}

if (!source.includes('async function resolveOperationalIntent(value)')) {
  const anchor = `function isTroubleshootingQuestion(value) {
  return /\\btroubleshoot\\b|\\bdiagnose\\b|\\brun\\s+diagnostics?\\b|\\bwhy\\s+(?:is|did)\\b.*\\b(?:fail|failed|error|unavailable|timeout|broken)\\b/i.test(String(value ?? ''));
}
`;
  const helper = `${anchor}
async function resolveOperationalIntent(value) {
  try {
    const response = await fetch('/api/celar-ai/v1/operations/intent', {
      method: 'POST',
      cache: 'no-store',
      credentials: 'same-origin',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify({ question: String(value ?? '') })
    });
    if (!response.ok) return null;
    const payload = await response.json();
    return payload?.decision ?? null;
  } catch {
    return null;
  }
}
`;
  replaceOnce(anchor, helper, 'HELPER');
}

if (!source.includes('const operationalDecision = await resolveOperationalIntent(clean);')) {
  replaceOnce(
    `    if (isDefectIntakeQuestion(clean)) {`,
    `    const operationalDecision = await resolveOperationalIntent(clean);
    if (operationalDecision?.actionKind === 'open_health_automation') {
      setQuestion('');
      window.dispatchEvent(new CustomEvent('projectpulse:celar-ai-open-health-automation'));
      return;
    }
    if (operationalDecision?.actionKind === 'open_defect_lifecycle') {
      setQuestion('');
      window.dispatchEvent(new CustomEvent('projectpulse:celar-ai-open-operations', {
        detail: { question: clean, autoRun: false, requestedView: 'defect_lifecycle' }
      }));
      return;
    }
    if (operationalDecision?.actionKind === 'open_defect_questionnaire'
        || (!operationalDecision && isDefectIntakeQuestion(clean))) {`,
    'DEFECT_ROUTE'
  );
  replaceOnce(
    `    if (isTroubleshootingQuestion(clean)) {`,
    `    if (operationalDecision?.actionKind === 'run_read_only_diagnostics'
        || (!operationalDecision && isTroubleshootingQuestion(clean))) {`,
    'TROUBLESHOOT_ROUTE'
  );
}

for (const marker of [
  'async function resolveOperationalIntent(value)',
  "operationalDecision?.actionKind === 'open_defect_questionnaire'",
  "operationalDecision?.actionKind === 'run_read_only_diagnostics'",
  "operationalDecision?.actionKind === 'open_health_automation'",
  "operationalDecision?.actionKind === 'open_defect_lifecycle'"
]) {
  if (!source.includes(marker)) {
    throw new Error(`CELAR_AI_SERVER_INTENT_MARKER=FAILED marker=${marker}`);
  }
}

fs.writeFileSync(helpPath, source, 'utf8');
console.log('CELAR_AI_SERVER_INTENT_ROUTER=AUTHORITATIVE');
console.log('CELAR_AI_BROWSER_INTENT_REGEX=FALLBACK_ONLY');
