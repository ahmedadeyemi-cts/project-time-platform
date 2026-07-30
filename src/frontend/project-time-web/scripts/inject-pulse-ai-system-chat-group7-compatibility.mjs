import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const helpPath = path.join(webRoot, 'src', 'HelpAssistant.jsx');

function count(source, marker) {
  return source.split(marker).length - 1;
}

function replaceRequired(source, anchor, replacement, label) {
  if (!source.includes(anchor)) throw new Error(`${label} anchor is missing.`);
  return source.replace(anchor, replacement);
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

  source = source.replaceAll('>Complete User Guide</button>', '>Module 999 — System User Guide</button>');
  source = source.replaceAll('Module 999 — Complete User Guide', 'Module 999 — System User Guide');

  if (count(source, governanceImport) !== 1) throw new Error('Pulse AI native governance import must appear once.');
  if (count(source, preferenceImport) !== 1) throw new Error('Pulse AI native preference import must appear once.');
  if (count(source, preferenceCall) !== 1) throw new Error('Pulse AI native preference query must appear once.');
  if (count(source, '<HelpGovernancePanel />') !== 1) throw new Error('Pulse AI native governance panel must mount once.');
  if (count(source, 'data-answer-detail={detailLevel}') !== 1) throw new Error('Pulse AI native answer-detail marker must appear once.');

  fs.writeFileSync(helpPath, source.endsWith('\n') ? source : `${source}\n`, 'utf8');
  console.log('PULSE_AI_NATIVE_SYSTEM_CHAT_GROUP_7_PREPARATION=PASS');
}

prepareNativeSystemChat();
await import(pathToFileURL(path.join(scriptDirectory, 'inject-group-7-ai-help-system-guide.mjs')).href);
console.log('PULSE_AI_NATIVE_SYSTEM_CHAT_GROUP_7_COMPATIBILITY=PASS');
