import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const helpPath = path.join(root, 'src', 'HelpAssistant.jsx');
let content = fs.readFileSync(helpPath, 'utf8');

function replaceRequired(before, after, label) {
  if (content.includes(after)) return;
  if (!content.includes(before)) throw new Error(`CELAR_AI_ENTERPRISE_CHAT_MISSING_ANCHOR=${label}`);
  content = content.replace(before, after);
}

replaceRequired(
  `  const [historyOpen, setHistoryOpen] = useState(false);\n  const inputRef = useRef(null);`,
  `  const [historyOpen, setHistoryOpen] = useState(false);\n  const [contextOpen, setContextOpen] = useState(false);\n  const [questionContext, setQuestionContext] = useState({ projectCode: '', projectName: '', personOrTeam: '', dateFrom: '', dateTo: '' });\n  const inputRef = useRef(null);`,
  'context_state');

replaceRequired(
  `  function beginFreshConversation() {\n    setActiveConversationId('');\n    setMessages([WELCOME_MESSAGE]);\n    setQuestion('');\n    setHistoryOpen(false);`,
  `  function beginFreshConversation() {\n    setActiveConversationId('');\n    setMessages([WELCOME_MESSAGE]);\n    setQuestion('');\n    setQuestionContext({ projectCode: '', projectName: '', personOrTeam: '', dateFrom: '', dateTo: '' });\n    setHistoryOpen(false);\n    setContextOpen(false);`,
  'fresh_context_reset');

// questionWithContext is the durable idempotency marker. Do not require the
// original path/explicitContext adjacency on later passes because other owned
// injectors may add compatible content around the same request construction.
if (!content.includes(`const questionWithContext = explicitContext.length`)) {
  replaceRequired(
    `      const path = '/api/celar-ai/v1/chat';\n      const payload = await postJson(path, {\n        conversationId: conversationId || null,\n        question: clean,`,
    `      const path = '/api/celar-ai/v1/chat';\n      const explicitContext = [\n        questionContext.projectCode ? \`Project code: \${questionContext.projectCode}\` : '',\n        questionContext.projectName ? \`Project name: \${questionContext.projectName}\` : '',\n        questionContext.personOrTeam ? \`Person or team: \${questionContext.personOrTeam}\` : '',\n        questionContext.dateFrom ? \`Date from: \${questionContext.dateFrom}\` : '',\n        questionContext.dateTo ? \`Date to: \${questionContext.dateTo}\` : ''\n      ].filter(Boolean);\n      const questionWithContext = explicitContext.length\n        ? \`\${clean}\\n\\nExplicit current-question context:\\n- \${explicitContext.join('\\n- ')}\`\n        : clean;\n      const payload = await postJson(path, {\n        conversationId: conversationId || null,\n        question: questionWithContext,\n        projectCode: questionContext.projectCode || null,\n        projectName: questionContext.projectName || null,`,
    'question_context_payload');
}

if (!content.includes(`className="celar-ai-question-context-toggle"`)) {
  replaceRequired(
    `          <div className={\`pulse-ai-conversation-toolbar\${historyOpen ? ' is-open' : ''}\`}>`,
    `          <div className="celar-ai-question-context-toggle">\n            <button type="button" onClick={() => setContextOpen((current) => !current)} aria-expanded={contextOpen}>\n              {contextOpen ? 'Hide question context' : 'Add project, person/team, or date context'}\n            </button>\n            <span>Context applies only to the current question and selected thread. It is not copied from another conversation.</span>\n          </div>\n\n          <div className={\`celar-ai-question-context\${contextOpen ? ' is-open' : ''}\`} aria-hidden={!contextOpen}>\n            <label>Project code<input value={questionContext.projectCode} onChange={(event) => setQuestionContext((current) => ({ ...current, projectCode: event.target.value }))} placeholder="Optional" /></label>\n            <label>Project name<input value={questionContext.projectName} onChange={(event) => setQuestionContext((current) => ({ ...current, projectName: event.target.value }))} placeholder="Optional" /></label>\n            <label>Person or team<input value={questionContext.personOrTeam} onChange={(event) => setQuestionContext((current) => ({ ...current, personOrTeam: event.target.value }))} placeholder="Authorized scope only" /></label>\n            <label>Date from<input type="date" value={questionContext.dateFrom} onChange={(event) => setQuestionContext((current) => ({ ...current, dateFrom: event.target.value }))} /></label>\n            <label>Date to<input type="date" value={questionContext.dateTo} onChange={(event) => setQuestionContext((current) => ({ ...current, dateTo: event.target.value }))} /></label>\n            <button type="button" onClick={() => setQuestionContext({ projectCode: '', projectName: '', personOrTeam: '', dateFrom: '', dateTo: '' })}>Clear context</button>\n          </div>\n\n          <div className={\`pulse-ai-conversation-toolbar\${historyOpen ? ' is-open' : ''}\`}>`,
    'question_context_ui');
}

fs.writeFileSync(helpPath, content, 'utf8');
console.log('CELAR_AI_ENTERPRISE_CHAT_CONTEXT=INJECTED');
console.log('CELAR_AI_ENTERPRISE_CHAT_CONTEXT_AUTO_CARRIED=NO');
console.log('CELAR_AI_ENTERPRISE_CHAT_CONTEXT_PROJECT=SUPPORTED');
console.log('CELAR_AI_ENTERPRISE_CHAT_CONTEXT_PEOPLE=SUPPORTED');
console.log('CELAR_AI_ENTERPRISE_CHAT_CONTEXT_DATE_RANGE=SUPPORTED');

await import('./inject-celar-ai-capability-routing.mjs');
