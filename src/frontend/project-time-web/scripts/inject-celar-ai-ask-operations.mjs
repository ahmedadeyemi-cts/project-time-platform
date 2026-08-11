import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = fileURLToPath(new URL('../', import.meta.url));
const helpPath = path.join(webRoot, 'src', 'HelpAssistant.jsx');
let source = fs.readFileSync(helpPath, 'utf8');

function replaceOnce(anchor, replacement, label) {
  const occurrences = source.split(anchor).length - 1;
  if (occurrences !== 1) {
    throw new Error(`CELAR_AI_ASK_OPERATIONS_INJECTOR_${label}=FAILED expected=1 actual=${occurrences}`);
  }
  source = source.replace(anchor, replacement);
}

const operationsImport = "import CelarAiAskOperations from './CelarAiAskOperations.jsx';";
if (!source.includes(operationsImport)) {
  replaceOnce(
    "import { useEffect, useMemo, useRef, useState } from 'react';",
    "import { useEffect, useMemo, useRef, useState } from 'react';\nimport CelarAiAskOperations from './CelarAiAskOperations.jsx';",
    'IMPORT'
  );
}

const helperMarker = 'function isDefectIntakeQuestion(value) {';
if (!source.includes(helperMarker)) {
  const helperAnchor = "const EMPTY_QUESTION_CONTEXT = Object.freeze({ projectCode: '', projectName: '', personOrTeam: '', dateFrom: '', dateTo: '' });\n";
  const helpers = `${helperAnchor}
function isDefectIntakeQuestion(value) {
  return /\\b(?:open|create|report|file|log|raise)\\s+(?:a\\s+)?defect\\b|\\breport\\s+this\\s+issue\\b|\\bthis\\s+is\\s+broken\\b|\\bopen\\s+an\\s+issue\\b/i.test(String(value ?? ''));
}

function isTroubleshootingQuestion(value) {
  return /\\btroubleshoot\\b|\\bdiagnose\\b|\\brun\\s+diagnostics?\\b|\\bwhy\\s+(?:is|did)\\b.*\\b(?:fail|failed|error|unavailable|timeout|broken)\\b/i.test(String(value ?? ''));
}

function operationalEvidenceFromResult(result) {
  return asArray(result?.toolResults).slice(0, 25).map((tool) => ({
    probeCode: String(tool?.toolCode || 'ask_celar_ai_tool'),
    componentCode: String(tool?.moduleCode || tool?.toolCode || 'pulse'),
    displayName: String(tool?.toolName || tool?.toolCode || 'Ask Celar AI evidence'),
    status: tool?.status === 'succeeded' ? 'healthy' : tool?.status === 'failed' ? 'failed' : 'degraded',
    httpStatus: Number.isFinite(Number(tool?.statusCode)) ? Number(tool.statusCode) : null,
    latencyMs: Number.isFinite(Number(tool?.durationMs)) ? Math.round(Number(tool.durationMs)) : null,
    failureCode: String(tool?.diagnosticCode || ''),
    detail: asArray(tool?.evidenceSummary).join(' · ') || 'No additional sanitized evidence summary was returned.',
    source: String(tool?.path || 'ask_celar_ai'),
    observedAt: tool?.observedAt || new Date().toISOString()
  }));
}
`;
  replaceOnce(helperAnchor, helpers, 'HELPERS');
}

const actionMarker = 'celar-ai-answer-operational-actions';
if (!source.includes(actionMarker)) {
  const actionAnchor = '          <NavigationTargets targets={answer.navigationTargets} close={close} />';
  const actionBlock = `          <div className="celar-ai-answer-operational-actions" aria-label="Ask Celar AI operational actions">
            <button
              type="button"
              onClick={() => window.dispatchEvent(new CustomEvent('projectpulse:celar-ai-open-operations', {
                detail: {
                  question: \`Troubleshoot this Ask Celar AI result: \${answer.directConclusion || 'The prior request did not complete.'}\`,
                  correlationId: result?.correlationId || '',
                  diagnosticEvidence: operationalEvidenceFromResult(result),
                  autoRun: true
                }
              }))}
            >
              Troubleshoot with Ask Celar AI
            </button>
            <button
              type="button"
              onClick={() => window.dispatchEvent(new CustomEvent('projectpulse:celar-ai-open-defect-intake', {
                detail: {
                  triggerQuestion: answer.directConclusion || '',
                  suggestedTitle: answer.directConclusion || 'Ask Celar AI reported an operational issue',
                  suggestedDescription: asArray(answer.limitations).concat(asArray(answer.knownUnknownAndStaleValues)).join('\\n'),
                  suggestedCategory: troubleshootingProfile ? 'Bug' : 'Other',
                  suggestedPriority: result?.status === 'failed' || result?.status === 'blocked' ? 'High' : 'Medium',
                  correlationId: result?.correlationId || '',
                  diagnosticEvidence: operationalEvidenceFromResult(result)
                }
              }))}
            >
              Open guided Module 076 defect
            </button>
          </div>
${actionAnchor}`;
  replaceOnce(actionAnchor, actionBlock, 'ANSWER_ACTIONS');
}

const submitMarker = 'projectpulse:celar-ai-open-defect-intake';
const submitAnchor = `    const clean = question.trim();
    if (!clean) return;
    sendingRef.current = true;`;
if (source.includes(submitAnchor)) {
  const submitReplacement = `    const clean = question.trim();
    if (!clean) return;
    if (isDefectIntakeQuestion(clean)) {
      setQuestion('');
      window.dispatchEvent(new CustomEvent('projectpulse:celar-ai-open-defect-intake', {
        detail: {
          conversationId: activeConversationId || null,
          triggerQuestion: clean,
          suggestedTitle: clean,
          affectedModule: '',
          environment: 'test'
        }
      }));
      return;
    }
    if (isTroubleshootingQuestion(clean)) {
      setQuestion('');
      window.dispatchEvent(new CustomEvent('projectpulse:celar-ai-open-operations', {
        detail: {
          question: clean,
          conversationId: activeConversationId || null,
          projectCode: questionContext.projectCode || null,
          projectName: questionContext.projectName || null,
          autoRun: true
        }
      }));
      return;
    }
    sendingRef.current = true;`;
  replaceOnce(submitAnchor, submitReplacement, 'SUBMIT_ROUTING');
} else if (!source.includes("if (isDefectIntakeQuestion(clean))")) {
  throw new Error('CELAR_AI_ASK_OPERATIONS_INJECTOR_SUBMIT_ROUTING=FAILED missing stable anchor');
}

const oldDefectFunction = `  function openDefectTracker() {
    setIsOpen(false);
    const destination = new URL(window.location.href);
    destination.searchParams.set('defectSource', 'help');
    destination.hash = 'defect-tracker';
    window.location.assign(destination.toString());
  }`;
const newDefectFunction = `  function openDefectTracker() {
    window.dispatchEvent(new CustomEvent('projectpulse:celar-ai-open-defect-intake', {
      detail: {
        conversationId: activeConversationId || null,
        triggerQuestion: question.trim(),
        suggestedTitle: question.trim(),
        environment: 'test'
      }
    }));
  }

  function openOperations() {
    window.dispatchEvent(new CustomEvent('projectpulse:celar-ai-open-operations', {
      detail: {
        question: question.trim() || 'Troubleshoot the current platform and show me the strongest evidence.',
        conversationId: activeConversationId || null,
        projectCode: questionContext.projectCode || null,
        projectName: questionContext.projectName || null,
        autoRun: true
      }
    }));
  }

  function openHealthAutomation() {
    window.dispatchEvent(new CustomEvent('projectpulse:celar-ai-open-health-automation'));
  }`;
if (!source.includes('function openOperations()')) {
  replaceOnce(oldDefectFunction, newDefectFunction, 'OPEN_FUNCTIONS');
}

const quickActionAnchor = `            <button type="button" className="help-full-guide-button" onClick={() => openRoute('user-guide')}>Module 999 — System User Guide</button>
            <button type="button" className="help-pulse-ai-button" onClick={() => openRoute('celar-ai')}>Celar AI Workbench</button>
            <button type="button" className="help-report-defect-button" onClick={openDefectTracker}>Report a defect — Module 076</button>`;
const quickActionReplacement = `            <button type="button" className="help-full-guide-button" onClick={() => openRoute('user-guide')}>Module 999 — System User Guide</button>
            <button type="button" className="help-pulse-ai-button" onClick={() => openRoute('celar-ai')}>Celar AI Workbench</button>
            <button type="button" className="help-celar-operations-button" onClick={openOperations}>Troubleshoot with Ask Celar AI</button>
            <button type="button" className="help-celar-health-button" onClick={openHealthAutomation}>Health & automatic defects</button>
            <button type="button" className="help-report-defect-button" onClick={openDefectTracker}>Open guided defect questionnaire</button>`;
if (!source.includes('help-celar-operations-button')) {
  replaceOnce(quickActionAnchor, quickActionReplacement, 'QUICK_ACTIONS');
}

const mountAnchor = `  return (
    <>
      <button type="button" className="help-launcher"`;
const mountReplacement = `  return (
    <>
      <CelarAiAskOperations />
      <button type="button" className="help-launcher"`;
if (!source.includes('<CelarAiAskOperations />')) {
  replaceOnce(mountAnchor, mountReplacement, 'MOUNT');
}

for (const marker of [
  operationsImport,
  helperMarker,
  actionMarker,
  "if (isDefectIntakeQuestion(clean))",
  'function openOperations()',
  'help-celar-health-button',
  '<CelarAiAskOperations />'
]) {
  if (!source.includes(marker)) {
    throw new Error(`CELAR_AI_ASK_OPERATIONS_INJECTOR_MARKER=FAILED marker=${marker}`);
  }
}

fs.writeFileSync(helpPath, source, 'utf8');
console.log('CELAR_AI_ASK_OPERATIONS_PRIMARY_SURFACE=Ask Celar AI');
console.log('CELAR_AI_ASK_OPERATIONS_DURABLE_DEFECT_SYSTEM=Module 076');
console.log('CELAR_AI_ASK_OPERATIONS_DIAGNOSTICS=INJECTED');
console.log('CELAR_AI_ASK_OPERATIONS_QUESTIONNAIRE=INJECTED');
console.log('CELAR_AI_ASK_OPERATIONS_HEALTH_AUTOMATION=INJECTED');
