import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '../../..');
const containerContextRoot = path.join(webRoot, 'container-context');
const helpPath = path.join(webRoot, 'src', 'HelpAssistant.jsx');

const containerSourcePaths = [
  'database/migrations/054_pulse_ai_system_intelligence_conversations.sql',
  'database/rollback/054_pulse_ai_system_intelligence_conversations_rollback.sql',
  'docs/modules/module-011-pulse-ai/SYSTEM-INTELLIGENCE-AND-TROUBLESHOOTING.md',
  'src/backend/ProjectTime.Api/Modules/PulseAiSystemIntelligenceModule.cs',
  'tests/test-pulse-ai-system-intelligence-migration-054.sh'
];

function count(source, marker) {
  return source.split(marker).length - 1;
}

function replaceRequired(source, anchor, replacement, label) {
  if (!source.includes(anchor)) throw new Error(`${label} anchor is missing.`);
  return source.replace(anchor, replacement);
}

function ensureContainerSourceMirror(relativePath) {
  const normalized = relativePath.split('/').join(path.sep);
  const target = path.join(repositoryRoot, normalized);
  if (fs.existsSync(target)) {
    console.log(`PULSE_AI_CONTAINER_SOURCE=CANONICAL_PRESENT path=${relativePath}`);
    return;
  }

  const mirror = path.join(containerContextRoot, normalized);
  if (!fs.existsSync(mirror)) {
    throw new Error(`Pulse AI container source mirror is missing: ${relativePath}`);
  }

  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.copyFileSync(mirror, target);
  console.log(`PULSE_AI_CONTAINER_SOURCE=EXACT_MIRROR_HYDRATED path=${relativePath}`);
}

function prepareNativeSystemChat() {
  if (!fs.existsSync(helpPath)) throw new Error('Pulse AI HelpAssistant.jsx is missing.');
  let source = fs.readFileSync(helpPath, 'utf8');
  if (!source.includes("import './pulse-ai-system-chat.css';")) return;

  const governanceImport = "import HelpGovernancePanel from './help/HelpGovernancePanel.jsx';";
  const preferenceImport = "import { applyHelpAnswerPreferences } from './help/help-answer-preferences.js';";
  const importAnchor = "import './help-assistant.css';";
  if (!source.includes(governanceImport)) {
    source = replaceRequired(
      source,
      importAnchor,
      `${importAnchor}\n${governanceImport}\n${preferenceImport}`,
      'Pulse AI Group 7 native imports'
    );
  }

  const preferenceCall = '  const answerPreferences = applyHelpAnswerPreferences(url, question);';
  if (!source.includes(preferenceCall)) {
    source = replaceRequired(
      source,
      "  url.searchParams.set('question', question);",
      "  url.searchParams.set('question', question);\n  const answerPreferences = applyHelpAnswerPreferences(url, question);",
      'Pulse AI Group 7 answer preference query'
    );
  }
  if (!source.includes('return { ...payload, answerPreferences };')) {
    source = replaceRequired(
      source,
      '  return getJson(`${url.pathname}${url.search}`);',
      '  const payload = await getJson(`${url.pathname}${url.search}`);\n  return { ...payload, answerPreferences };',
      'Pulse AI Group 7 answer preference response'
    );
  }

  if (!source.includes('GROUP_7_HELP_ANSWER_DETAIL_START')) {
    source = replaceRequired(
      source,
      'function SystemAnswer({ result, close }) {\n  const answer = result?.answer ?? {};',
      [
        'function SystemAnswer({ result, close }) {',
        '  const answer = result?.answer ?? {};',
        '  /* GROUP_7_HELP_ANSWER_DETAIL_START */',
        "  const detailLevel = result?.detailLevel ?? 'comprehensive';",
        '  /* GROUP_7_HELP_ANSWER_DETAIL_END */'
      ].join('\n'),
      'Pulse AI Group 7 answer detail'
    );
  }
  if (!source.includes('data-answer-detail={detailLevel}')) {
    source = replaceRequired(
      source,
      '    <div className="help-detailed-answer pulse-ai-system-answer">',
      '    <div className="help-detailed-answer pulse-ai-system-answer" data-answer-detail={detailLevel}>',
      'Pulse AI Group 7 detail marker'
    );
  }
  if (!source.includes('Answer detail: {titleFrom(detailLevel)}')) {
    source = replaceRequired(
      source,
      '      <EvidenceBadges result={result} />',
      [
        '      <EvidenceBadges result={result} />',
        '      <div className="help-answer-preference-evidence" role="note">',
        '        <span>Answer detail: {titleFrom(detailLevel)}</span>',
        '        <span>Source: saved profile, per-question command, or comprehensive system default</span>',
        '      </div>'
      ].join('\n'),
      'Pulse AI Group 7 preference evidence'
    );
  }

  if (!source.includes('GROUP_7_HELP_GOVERNANCE_PANEL_START')) {
    const anchor = '          <div\n            ref={messagesRef}';
    source = replaceRequired(
      source,
      anchor,
      [
        '          {/* GROUP_7_HELP_GOVERNANCE_PANEL_START */}',
        '          <HelpGovernancePanel />',
        '          {/* GROUP_7_HELP_GOVERNANCE_PANEL_END */}',
        '',
        anchor
      ].join('\n'),
      'Pulse AI Group 7 governance panel'
    );
  }

  if (!source.includes('function openDefectTracker()')) {
    const routeAnchor = [
      '  function openRoute(route) {',
      '    setIsOpen(false);',
      '    window.location.hash = route;',
      '  }'
    ].join('\n');
    source = replaceRequired(
      source,
      routeAnchor,
      [
        routeAnchor,
        '',
        '  function openDefectTracker() {',
        '    setIsOpen(false);',
        '    const destination = new URL(window.location.href);',
        "    destination.searchParams.set('defectSource', 'help');",
        "    destination.hash = 'defect-tracker';",
        '    window.location.assign(destination.toString());',
        '  }'
      ].join('\n'),
      'Pulse AI Module 076 defect-intake function'
    );
  }

  const genericDefectAction = '<button type="button" className="help-report-defect-button" onClick={() => openRoute(\'defect-tracker\')}>Report a Defect</button>';
  const governedDefectAction = '<button type="button" className="help-report-defect-button" onClick={openDefectTracker}>Report a defect — Module 076</button>';
  if (!source.includes(governedDefectAction)) {
    source = replaceRequired(
      source,
      genericDefectAction,
      governedDefectAction,
      'Pulse AI Module 076 defect-intake action'
    );
  }

  const pulseHelpTitle = '<strong>Pulse AI Help & Search</strong>';
  const celarHelpTitle = '<strong>Celar AI Help & Search</strong>';
  if (!source.includes(pulseHelpTitle) && !source.includes(celarHelpTitle)) {
    const pulseTitle = '<strong>Pulse AI</strong>';
    const celarTitle = '<strong>Celar AI</strong>';
    if (source.includes(pulseTitle)) {
      source = source.replace(pulseTitle, pulseHelpTitle);
    } else if (source.includes(celarTitle)) {
      source = source.replace(celarTitle, celarHelpTitle);
    } else {
      throw new Error('Pulse/Celar AI deep-intelligence Help title anchor is missing.');
    }
  }

  const legacySummary = '<p className="help-answer-summary">The detailed system-intelligence API could not be reached. Pulse prepared a read-only evidence plan, but it did not invent live values.</p>';
  if (!source.includes('Automatic multi-tool execution is not yet enabled')) {
    source = replaceRequired(
      source,
      legacySummary,
      '<p className="help-answer-summary">The detailed system-intelligence API could not be reached. Automatic multi-tool execution is not yet enabled for this compatibility response, so Pulse prepared a read-only evidence plan and did not invent live values.</p>',
      'Pulse AI deep-intelligence compatibility summary'
    );
  }

  source = source.replaceAll('>Complete User Guide</button>', '>Module 999 — System User Guide</button>');
  source = source.replaceAll('Module 999 — Complete User Guide', 'Module 999 — System User Guide');

  if (count(source, governanceImport) !== 1) throw new Error('Pulse AI native governance import must appear once.');
  if (count(source, preferenceImport) !== 1) throw new Error('Pulse AI native preference import must appear once.');
  if (count(source, preferenceCall) !== 1) throw new Error('Pulse AI native preference query must appear once.');
  if (count(source, '<HelpGovernancePanel />') !== 1) throw new Error('Pulse AI native governance panel must mount once.');
  if (count(source, 'data-answer-detail={detailLevel}') !== 1) throw new Error('Pulse AI native answer-detail marker must appear once.');
  if (count(source, "destination.searchParams.set('defectSource', 'help')") !== 1) throw new Error('Pulse AI Module 076 defect source must appear once.');
  if (count(source, "destination.hash = 'defect-tracker'") !== 1) throw new Error('Pulse AI Module 076 route must appear once.');
  if (count(source, 'Report a defect — Module 076') !== 1) throw new Error('Pulse AI Module 076 action must appear once.');
  const brandedHelpTitleCount = count(source, pulseHelpTitle) + count(source, celarHelpTitle);
  if (brandedHelpTitleCount !== 1) throw new Error('Pulse/Celar AI Help & Search compatibility title must appear exactly once.');
  if (count(source, 'Automatic multi-tool execution is not yet enabled') !== 1) throw new Error('Pulse AI deep-intelligence compatibility statement must appear once.');

  fs.writeFileSync(helpPath, source.endsWith('\n') ? source : `${source}\n`, 'utf8');
  console.log('PULSE_AI_NATIVE_SYSTEM_CHAT_GROUP_7_PREPARATION=PASS');
}

await import(pathToFileURL(path.join(scriptDirectory, 'inject-group-6-enterprise-presentation.mjs')).href);
for (const relativePath of containerSourcePaths) ensureContainerSourceMirror(relativePath);
prepareNativeSystemChat();
await import(pathToFileURL(path.join(scriptDirectory, 'inject-group-7-ai-help-system-guide.mjs')).href);
console.log('PULSE_AI_NATIVE_SYSTEM_CHAT_GROUP_7_COMPATIBILITY=PASS');
