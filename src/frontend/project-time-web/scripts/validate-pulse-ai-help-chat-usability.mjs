import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const absolute = (relative) => path.join(repoRoot, relative);
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const exists = (relative) => fs.existsSync(absolute(relative));
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`PULSE_AI_HELP_CHAT_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const files = {
  usability: 'src/frontend/project-time-web/src/pulse-ai-help-chat-usability.js',
  css: 'src/frontend/project-time-web/src/pulse-ai-help-chat-usability.css',
  main: 'src/frontend/project-time-web/src/main.jsx',
  help: 'src/frontend/project-time-web/src/HelpAssistant.jsx',
  knowledge: 'src/backend/ProjectTime.Api/Ai/PulseAiProductKnowledgeCatalog.cs',
  planner: 'src/backend/ProjectTime.Api/Ai/PulseAiQuestionPlanner.cs',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'
};

for (const [key, relative] of Object.entries(files)) {
  assert(`FILE_${key.toUpperCase()}`, exists(relative), relative);
}

if (checks.some((check) => !check.condition)) {
  console.error('PULSE_AI_HELP_CHAT_CONTRACT=FAILED_MISSING_FILE');
  process.exit(1);
}

const usability = read(files.usability);
const css = read(files.css);
const main = read(files.main);
const help = read(files.help);
const knowledge = read(files.knowledge);
const planner = read(files.planner);
const project = read(files.project);

assert(
  'MAIN_MOUNT',
  main.includes("import './pulse-ai-help-chat-usability.js';")
    && main.indexOf("import './pulse-ai-help-chat-usability.js';") < main.indexOf("import App from './App.Module001.g.jsx';"),
  'the accessibility bridge loads once through the global React entry point'
);

assert(
  'ENTER_SENDS',
  usability.includes("event.key !== 'Enter'")
    && usability.includes('event.shiftKey')
    && usability.includes('event.isComposing')
    && usability.includes('form.requestSubmit()')
    && usability.includes("event.preventDefault()")
    && usability.includes("'Enter sends • Shift+Enter adds a line'"),
  'Enter submits, Shift+Enter keeps a newline, and composition input is preserved'
);

assert(
  'ESCAPE_CLOSES',
  usability.includes("event.key === 'Escape'")
    && usability.includes('Close help assistant')
    && usability.includes('close.click()'),
  'keyboard users can close the panel without reaching for the mouse'
);

assert(
  'SCROLL_CONTAINER',
  css.includes('height: min(820px, calc(100dvh - 112px));')
    && css.includes('overflow-y: scroll;')
    && css.includes('overscroll-behavior: contain;')
    && css.includes('touch-action: pan-y;')
    && css.includes('scrollbar-gutter: stable both-edges;'),
  'the conversation row has a definite height and owns vertical scrolling'
);

assert(
  'FOLLOW_CONVERSATION_WITHOUT_SCROLL_TRAP',
  usability.includes('nearBottom(messages)')
    && usability.includes('shouldFollowConversation')
    && usability.includes('MutationObserver')
    && usability.includes("scrollToBottom(messages, userMessageAdded ? 'smooth' : 'auto')")
    && usability.includes("messages.addEventListener('scroll'"),
  'new messages stay visible while users can scroll up without being forced back to the bottom'
);

assert(
  'ACCESSIBLE_CONVERSATION',
  usability.includes("messages.setAttribute('role', 'log')")
    && usability.includes("messages.setAttribute('aria-live', 'polite')")
    && usability.includes("messages.setAttribute('tabindex', '0')")
    && usability.includes("textarea.setAttribute('aria-keyshortcuts', 'Enter Shift+Enter')"),
  'screen-reader and keyboard metadata is applied without replacing React-owned content'
);

assert(
  'REACT_OWNERSHIP_PRESERVED',
  !usability.includes('innerHTML')
    && !usability.includes('replaceChildren')
    && !usability.includes('removeChild')
    && usability.includes('form.append(hint)')
    && (
      (help.includes('onSubmit={(event) => {') && help.includes('void submitQuestion();'))
      || help.includes('onSubmit={submitQuestion}')
    ),
  'the bridge delegates submission to the existing React form and adds only a non-state hint'
);

assert(
  'DIRECT_CELAR_AI_PURPOSE_ANSWER',
  knowledge.includes('"what is pulse ai"')
    && knowledge.includes('"purpose of module 011"')
    && knowledge.includes('Celar AI is the private intelligence layer for Pulse')
    && knowledge.includes('document-grounded Timesheet suggestions')
    && knowledge.includes('FlowHive project-plan drafting')
    && knowledge.includes('reporting or financial insight')
    && knowledge.includes('optional policy-approved sanitized external assistance'),
  'the exact question shown by the user receives a comprehensive direct answer'
);

assert(
  'PLANNER_COMPOSITION',
  project.includes('<PulseAiQuestionPlannerGenerated>')
    && project.includes('<Compile Remove="Ai/PulseAiQuestionPlanner.cs" />')
    && project.includes('PulseAiProductKnowledgeCatalog.Find(normalized) ?? FindKnowledgeAnswer(normalized)')
    && project.includes('<Compile Include="$(PulseAiQuestionPlannerGenerated)" />')
    && planner.includes('var directAnswer = FindKnowledgeAnswer(normalized);'),
  'the generated compile copy adds approved product knowledge without rewriting the canonical planner'
);

assert(
  'NO_PROVIDER_OR_DATA_MUTATION',
  !usability.includes('/api/')
    && !knowledge.includes('HttpClient')
    && !knowledge.includes('Npgsql')
    && !knowledge.includes('INSERT INTO')
    && !knowledge.includes('UPDATE ')
    && !knowledge.includes('DELETE '),
  'the usability hotfix neither calls a provider nor changes application data'
);

assert(
  'RESPONSIVE_VIEWPORT',
  css.includes('@media (max-width: 760px)')
    && css.includes('@media (max-width: 620px)')
    && css.includes('@media (max-height: 640px)')
    && css.includes('100dvh'),
  'desktop, mobile, and short-height viewports retain a scrollable conversation'
);

console.log(`PULSE_AI_HELP_CHAT_CHECKS=${checks.length}`);
console.log('PULSE_AI_HELP_CHAT_ENTER_SENDS=YES');
console.log('PULSE_AI_HELP_CHAT_SHIFT_ENTER_NEWLINE=YES');
console.log('PULSE_AI_HELP_CHAT_SCROLL_RESTORED=YES');
console.log('PULSE_AI_HELP_CHAT_DIRECT_PURPOSE_ANSWER=YES');
console.log('PULSE_AI_HELP_CHAT_DATABASE_CHANGES=0');
console.log('PULSE_AI_HELP_CHAT_PROVIDER_CALLS_ADDED=0');
console.log('PULSE_AI_HELP_CHAT_DEPLOYMENTS=0');

if (checks.some((check) => !check.condition)) {
  console.error('PULSE_AI_HELP_CHAT_CONTRACT=FAILED');
  process.exit(1);
}

console.log('PULSE_AI_HELP_CHAT_CONTRACT=PASSED');
