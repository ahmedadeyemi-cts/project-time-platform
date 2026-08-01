import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const sourceRoot = path.join(root, 'src');
const changed = [];

function file(relative) {
  return path.join(sourceRoot, relative);
}

function read(relative) {
  return fs.readFileSync(file(relative), 'utf8');
}

function save(relative, content) {
  const target = file(relative);
  const current = fs.readFileSync(target, 'utf8');
  if (current === content) return;
  fs.writeFileSync(target, content, 'utf8');
  changed.push(`src/frontend/project-time-web/src/${relative}`);
}

function replaceRequired(content, before, after, label) {
  if (content.includes(after)) return content;
  if (!content.includes(before)) {
    throw new Error(`CELAR_AI_REBRAND_MISSING_ANCHOR=${label}`);
  }
  return content.replace(before, after);
}

function rebrandVisibleText(content) {
  return content
    .replaceAll('PULSE AI', 'CELAR AI')
    .replaceAll('Pulse AI', 'Celar AI');
}

const visibleFiles = [
  'App.jsx',
  'App.Module001.g.jsx',
  'HelpAssistant.jsx',
  'PulseAiCenter.jsx',
  'PulseAiMissionControl.jsx',
  'PulseAiDeepIntelligenceWorkbench.jsx',
  'PulseAiPrivateDocumentPipelineWorkbench.jsx',
  'PulseAiPrivateRuntimeWorkbench.jsx',
  'PulseAiPrivateRagWorkbench.jsx',
  'PulseAiSystemIntelligenceWorkbench.jsx',
  'WorkTaskBuilderPanel.jsx',
  'SystemUserGuide.jsx',
  'help/HelpGovernancePanel.jsx'
];

for (const relative of visibleFiles) {
  if (!fs.existsSync(file(relative))) continue;
  save(relative, rebrandVisibleText(read(relative)));
}

{
  const relative = 'HelpAssistant.jsx';
  let content = read(relative);

  const helpers = `function rebrandCelarString(value) {
  return String(value ?? '')
    .replaceAll('PULSE AI', 'CELAR AI')
    .replaceAll('Pulse AI', 'Celar AI');
}

function rebrandCelarValue(value) {
  if (typeof value === 'string') return rebrandCelarString(value);
  if (Array.isArray(value)) return value.map(rebrandCelarValue);
  if (!value || typeof value !== 'object') return value;
  return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, rebrandCelarValue(item)]));
}

`;
  if (!content.includes('function rebrandCelarValue(value)')) {
    const anchor = `function titleFrom(value) {`;
    if (!content.includes(anchor)) throw new Error('CELAR_AI_REBRAND_MISSING_ANCHOR=help_rebrand_helpers');
    content = content.replace(anchor, `${helpers}${anchor}`);
  }

  content = replaceRequired(
    content,
    `  const payload = await getJson('/api/pulse-ai/v1/system/conversations?limit=100');
    const rows = asArray(payload.conversations);`,
    `  const payload = await getJson('/api/pulse-ai/v1/system/conversations?limit=100');
    const rows = asArray(payload.conversations).map((conversation) => ({
      ...conversation,
      title: rebrandCelarString(conversation.title)
    }));`,
    'help_conversation_titles');

  content = replaceRequired(
    content,
    `  const structured = message.structuredResponse && typeof message.structuredResponse === 'object'
    ? message.structuredResponse
    : null;`,
    `  const structured = message.structuredResponse && typeof message.structuredResponse === 'object'
    ? rebrandCelarValue(message.structuredResponse)
    : null;`,
    'help_history_structured_response');

  content = replaceRequired(
    content,
    `    text: message.text,
    error: message.status === 'failed' && !structured?.answer ? message.text : '',`,
    `    text: rebrandCelarString(message.text),
    error: message.status === 'failed' && !structured?.answer ? rebrandCelarString(message.text) : '',`,
    'help_history_message_text');

  content = replaceRequired(
    content,
    `  const plan = payload?.plan ?? {};`,
    `  const plan = rebrandCelarValue(payload?.plan ?? {});`,
    'help_legacy_response_rebrand');

  content = replaceRequired(
    content,
    `      const path = conversationId
        ? \`/api/pulse-ai/v1/system/conversations/\${encodeURIComponent(conversationId)}/messages\`
        : '/api/pulse-ai/v1/system/questions';`,
    `      const path = '/api/celar-ai/v1/chat';`,
    'help_celar_chat_route');

  content = replaceRequired(
    content,
    `      const result = payload.result;`,
    `      const result = rebrandCelarValue(payload.result);`,
    'help_answer_rebrand');

  content = content.replaceAll(`openRoute('work-task-builder')`, `openRoute('celar-ai')`);
  content = content.replaceAll(`['#work-task-builder', '#service-control', '#user-guide']`, `['#celar-ai', '#service-control', '#user-guide']`);
  save(relative, content);
}

{
  const relative = 'PulseAiSystemIntelligenceWorkbench.jsx';
  let content = read(relative);
  const helpers = `function rebrandCelarString(value) { return String(value ?? '').replaceAll('PULSE AI', 'CELAR AI').replaceAll('Pulse AI', 'Celar AI'); }
function rebrandCelarValue(value) {
  if (typeof value === 'string') return rebrandCelarString(value);
  if (Array.isArray(value)) return value.map(rebrandCelarValue);
  if (!value || typeof value !== 'object') return value;
  return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, rebrandCelarValue(item)]));
}
`;
  if (!content.includes('function rebrandCelarValue(value)')) {
    const anchor = `function asArray(value) { return Array.isArray(value) ? value : []; }`;
    if (!content.includes(anchor)) throw new Error('CELAR_AI_REBRAND_MISSING_ANCHOR=workbench_rebrand_helpers');
    content = content.replace(anchor, `${anchor}\n${helpers}`);
  }
  content = content.replaceAll(`'/api/pulse-ai/v1/system/questions'`, `'/api/celar-ai/v1/chat'`);
  content = replaceRequired(
    content,
    `      setResult(payload.result);`,
    `      setResult(rebrandCelarValue(payload.result));`,
    'workbench_answer_rebrand');
  content = replaceRequired(
    content,
    `    try { setConversationDetail(await getJson(\`/api/pulse-ai/v1/system/conversations/\${encodeURIComponent(id)}\`)); }`,
    `    try { setConversationDetail(rebrandCelarValue(await getJson(\`/api/pulse-ai/v1/system/conversations/\${encodeURIComponent(id)}\`))); }`,
    'workbench_history_rebrand');
  save(relative, content);
}

{
  const relative = 'AiProviderConfigurationCenter.jsx';
  let content = read(relative);
  const importLine = `import CelarAiProviderBridgePanel from './CelarAiProviderBridgePanel.jsx';\n`;
  if (!content.includes(importLine.trim())) {
    content = replaceRequired(
      content,
      `import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';\n`,
      `import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';\n${importLine}`,
      'provider_bridge_import');
  }
  content = replaceRequired(
    content,
    `            ProjectPulse checks provider health automatically and routes each AI request once through Claude,
            then OpenAI, then the governed local fallback. A safety refusal never triggers another provider.`,
    `            Celar AI uses Module 064 as the governed provider gateway. Module 064 checks provider health automatically,
            controls approved models and feature routes, and preserves the private-first boundary. Claude and OpenAI remain
            optional sanitized fallbacks, and a safety refusal never triggers another provider.`,
    'provider_header_copy');

  const featureSection = `          <section className="ai-provider-center__section">
            <div className="ai-provider-center__section-heading">
              <div><p className="ai-provider-center__eyebrow">Feature routing</p><h2>One governed route per AI capability</h2></div>`;
  if (!content.includes('<CelarAiProviderBridgePanel />')) {
    content = replaceRequired(
      content,
      featureSection,
      `          <CelarAiProviderBridgePanel />\n\n${featureSection}`,
      'provider_bridge_mount');
  }
  save(relative, content);
}

console.log(`CELAR_AI_RUNTIME_REBRAND_FILES_CHANGED=${changed.length}`);
for (const relative of changed) console.log(`CELAR_AI_RUNTIME_REBRAND_CHANGED=${relative}`);
console.log('CELAR_AI_RUNTIME_REBRAND_VISIBLE_NAME=Celar AI');
console.log('CELAR_AI_RUNTIME_REBRAND_TECHNICAL_COMPATIBILITY=Pulse AI');
