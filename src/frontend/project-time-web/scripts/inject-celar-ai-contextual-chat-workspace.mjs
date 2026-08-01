import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const helpPath = path.join(root, 'src', 'HelpAssistant.jsx');
let content = fs.readFileSync(helpPath, 'utf8');

function replaceRequired(before, after, label) {
  if (content.includes(after)) return;
  if (!content.includes(before)) {
    throw new Error(`CELAR_AI_CONTEXTUAL_CHAT_MISSING_ANCHOR=${label}`);
  }
  content = content.replace(before, after);
}

replaceRequired(
  `import './pulse-ai-system-chat.css';`,
  `import './pulse-ai-system-chat.css';\nimport './celar-ai-contextual-chat.css';`,
  'contextual_css_import');

replaceRequired(
  `const QUICK_QUESTIONS = Object.freeze([\n  'What APIs are running on the system?',\n  'Troubleshoot the current platform and show me the strongest evidence.',\n  'Explain Celar AI and everything it can do.',\n  'Design a future enhancement for Pulse using the current architecture.',\n  'What is unhealthy, unavailable, unauthorized, or missing right now?',\n  'How do Modules 013, 016, 078, and 998 work together for troubleshooting?'\n]);`,
  `const QUICK_QUESTIONS = Object.freeze([\n  'What is my team working on right now, based on authorized Pulse records?',\n  'How do I create a project in Pulse?',\n  'How do I upload a SOW or GSD and make it available to Celar AI?',\n  'What APIs are running on the system?',\n  'Troubleshoot the current platform and show me the strongest evidence.',\n  'Explain Celar AI and everything it can do.',\n  'Design a future enhancement for Pulse using the current architecture.'\n]);`,
  'quick_questions');

replaceRequired(
  `  text: 'Ask any question about Pulse. I can explain modules and workflows, discover the APIs registered in the running application, use authorized read-only troubleshooting evidence, analyze projects and private documents, explain reports and financials, and prepare detailed future-enhancement blueprints. Completed conversations remain available after closing or refreshing this page.'\n});\n\nfunction asArray(value) {`,
  `  text: 'Ask any question about Pulse. This opens as a fresh chat: previous conversations remain in your History, but they are not automatically inserted into this conversation. I can explain how to use the platform, summarize authorized work and assignments, discover running APIs, troubleshoot the system, analyze projects and documents, explain reports and financials, and prepare detailed future-enhancement blueprints.'\n});\n\nconst CELAR_AI_CHAT_SIZE_KEY = 'celarAiChatSize';\nconst CELAR_AI_CHAT_SIZES = Object.freeze(['compact', 'standard', 'wide', 'fullscreen']);\n\nfunction initialChatSize() {\n  try {\n    const saved = window.localStorage.getItem(CELAR_AI_CHAT_SIZE_KEY);\n    return CELAR_AI_CHAT_SIZES.includes(saved) ? saved : 'standard';\n  } catch {\n    return 'standard';\n  }\n}\n\nfunction asArray(value) {`,
  'fresh_welcome_and_size_contract');

replaceRequired(
  `  const [sending, setSending] = useState(false);\n  const inputRef = useRef(null);`,
  `  const [sending, setSending] = useState(false);\n  const [chatSize, setChatSize] = useState(initialChatSize);\n  const [isMinimized, setIsMinimized] = useState(false);\n  const [historyOpen, setHistoryOpen] = useState(false);\n  const inputRef = useRef(null);`,
  'window_state');

replaceRequired(
  `  async function createConversation(mode = 'system_help') {`,
  `  async function createConversation(mode = 'system_help', resetMessages = true) {`,
  'create_conversation_signature');

replaceRequired(
  `    setActiveConversationId(conversation.conversationId);\n    setMessages([WELCOME_MESSAGE]);\n    await refreshConversationList(conversation.conversationId);`,
  `    setActiveConversationId(conversation.conversationId);\n    if (resetMessages) setMessages([WELCOME_MESSAGE]);\n    await refreshConversationList(conversation.conversationId);`,
  'create_conversation_message_reset');

replaceRequired(
  `  async function hydrate() {\n    if (hydrated) return;\n    setHistoryLoading(true);\n    try {\n      const selected = await refreshConversationList();\n      if (selected) {\n        await loadConversation(selected);\n      } else {\n        await createConversation();\n      }\n    } catch {\n      setMessages([WELCOME_MESSAGE]);\n    } finally {\n      setHydrated(true);\n      setHistoryLoading(false);\n    }\n  }`,
  `  async function hydrate() {\n    if (hydrated) return;\n    setHistoryLoading(true);\n    try {\n      await refreshConversationList();\n      setActiveConversationId('');\n      setMessages([WELCOME_MESSAGE]);\n      followLatestRef.current = true;\n    } catch {\n      setActiveConversationId('');\n      setMessages([WELCOME_MESSAGE]);\n    } finally {\n      setHydrated(true);\n      setHistoryLoading(false);\n    }\n  }\n\n  function beginFreshConversation() {\n    setActiveConversationId('');\n    setMessages([WELCOME_MESSAGE]);\n    setQuestion('');\n    setHistoryOpen(false);\n    followLatestRef.current = true;\n    window.setTimeout(() => inputRef.current?.focus(), 40);\n  }`,
  'fresh_hydration_policy');

replaceRequired(
  `  useEffect(() => {\n    if (!isOpen || !followLatestRef.current) return;`,
  `  useEffect(() => {\n    try {\n      window.localStorage.setItem(CELAR_AI_CHAT_SIZE_KEY, chatSize);\n    } catch {\n      // Window preference is optional and contains no conversation content.\n    }\n  }, [chatSize]);\n\n  useEffect(() => {\n    if (!isOpen || !followLatestRef.current) return;`,
  'size_preference_effect');

replaceRequired(
  `          conversationId = await createConversation();`,
  `          conversationId = await createConversation('system_help', false);`,
  'first_message_conversation_creation');

replaceRequired(
  `      await loadConversation(id);`,
  `      if (!id) {\n        beginFreshConversation();\n        return;\n      }\n      await loadConversation(id);\n      setHistoryOpen(false);`,
  'explicit_history_selection');

replaceRequired(
  `      <button type="button" className="help-launcher" onClick={() => setIsOpen((current) => !current)}>\n        Ask Celar AI\n      </button>`,
  `      <button type="button" className="help-launcher" onClick={() => {\n        setIsOpen((current) => !current);\n        setIsMinimized(false);\n      }}>\n        Ask Celar AI\n      </button>`,
  'launcher_restore');

replaceRequired(
  `        <aside className="help-panel pulse-ai-help-panel pulse-ai-system-chat" aria-label="Celar AI system intelligence assistant">`,
  `        <aside\n          className={\`help-panel pulse-ai-help-panel pulse-ai-system-chat celar-ai-contextual-chat is-size-\${chatSize}\${isMinimized ? ' is-minimized' : ''}\`}\n          aria-label="Celar AI system intelligence assistant"\n          data-context-policy="current-conversation-only"\n          data-history-policy="retained-not-auto-injected"\n        >`,
  'contextual_panel_class');

replaceRequired(
  `          <div className="help-header">\n            <div>\n              <strong>Celar AI Help & Search</strong>\n              <span>Detailed answers · live APIs · troubleshooting · future enhancements</span>\n            </div>\n            <button type="button" aria-label="Close Celar AI" onClick={() => setIsOpen(false)}>×</button>\n          </div>`,
  `          <div className="help-header">\n            <div>\n              <strong>Celar AI Help & Search</strong>\n              <span>Platform guidance · authorized people/work answers · APIs · troubleshooting</span>\n            </div>\n            <div className="celar-ai-chat-window-controls" aria-label="Celar AI window controls">\n              <button type="button" data-size="compact" className={chatSize === 'compact' ? 'is-active' : ''} aria-label="Compact chat" title="Compact" onClick={() => { setChatSize('compact'); setIsMinimized(false); }}>C</button>\n              <button type="button" data-size="standard" className={chatSize === 'standard' ? 'is-active' : ''} aria-label="Standard chat" title="Standard" onClick={() => { setChatSize('standard'); setIsMinimized(false); }}>S</button>\n              <button type="button" data-size="wide" className={chatSize === 'wide' ? 'is-active' : ''} aria-label="Wide chat" title="Wide" onClick={() => { setChatSize('wide'); setIsMinimized(false); }}>W</button>\n              <button type="button" data-size="fullscreen" className={chatSize === 'fullscreen' ? 'is-active' : ''} aria-label="Fullscreen chat" title="Fullscreen" onClick={() => { setChatSize('fullscreen'); setIsMinimized(false); }}>□</button>\n              <button type="button" aria-label={isMinimized ? 'Restore Celar AI' : 'Minimize Celar AI'} title={isMinimized ? 'Restore' : 'Minimize'} onClick={() => setIsMinimized((current) => !current)}>{isMinimized ? '▣' : '—'}</button>\n              <button type="button" className="celar-ai-chat-close" aria-label="Close Celar AI" title="Close" onClick={() => setIsOpen(false)}>×</button>\n            </div>\n          </div>\n\n          <div className="celar-ai-context-bar" role="note">\n            <div>\n              <strong>{activeConversationId ? 'Current conversation only' : 'Fresh chat — no previous conversation context'}</strong>\n              <span>{activeConversationId ? 'This selected thread is retained for you. Other conversations are not merged into it.' : 'History remains available, but the most recent chat is not opened or injected automatically.'}</span>\n            </div>\n            <button type="button" onClick={() => setHistoryOpen((current) => !current)} aria-expanded={historyOpen}>\n              {historyOpen ? 'Hide history' : \`History (\${conversations.length})\`}\n            </button>\n          </div>`,
  'header_controls_and_context_bar');

replaceRequired(
  `          <div className="pulse-ai-conversation-toolbar">`,
  `          <div className={\`pulse-ai-conversation-toolbar\${historyOpen ? ' is-open' : ''}\`}>`,
  'history_drawer_class');

replaceRequired(
  `                {!activeConversationId ? <option value="">Current session</option> : null}`,
  `                <option value="">Fresh chat — no previous context</option>`,
  'fresh_option');

replaceRequired(
  `            <button type="button" onClick={() => void createConversation()} disabled={historyLoading || sending}>New conversation</button>`,
  `            <button type="button" onClick={beginFreshConversation} disabled={historyLoading || sending}>New chat</button>`,
  'new_chat_action');

replaceRequired(
  `          {/* GROUP_7_HELP_GOVERNANCE_PANEL_START */}\n          <HelpGovernancePanel />\n          {/* GROUP_7_HELP_GOVERNANCE_PANEL_END */}`,
  `          {/* GROUP_7_HELP_GOVERNANCE_PANEL_START */}\n          <details className="celar-ai-chat-governance">\n            <summary>Privacy, scope, and answer-detail controls</summary>\n            <HelpGovernancePanel />\n          </details>\n          {/* GROUP_7_HELP_GOVERNANCE_PANEL_END */}`,
  'collapsed_governance');

replaceRequired(
  `          </form>\n        </aside>`,
  `          </form>\n          <span className="celar-ai-chat-resize-note" aria-hidden="true">Drag corner to resize</span>\n        </aside>`,
  'resize_note');

fs.writeFileSync(helpPath, content, 'utf8');

console.log('CELAR_AI_CONTEXTUAL_CHAT_INJECTED=YES');
console.log('CELAR_AI_CONTEXTUAL_CHAT_DEFAULT_SIZE=standard');
console.log('CELAR_AI_CONTEXTUAL_CHAT_FRESH_THREAD_DEFAULT=YES');
console.log('CELAR_AI_CONTEXTUAL_CHAT_HISTORY_AUTO_INJECTED=NO');
console.log('CELAR_AI_CONTEXTUAL_CHAT_USER_RESIZABLE=YES');
console.log('CELAR_AI_CONTEXTUAL_CHAT_ENTER_SENDS=YES');
