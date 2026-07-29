import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const sourceRoot = path.join(webRoot, 'src');

function read(name) {
  const filePath = path.join(sourceRoot, name);
  if (!fs.existsSync(filePath)) throw new Error(`Group 7 target is missing: ${name}`);
  return { filePath, source: fs.readFileSync(filePath, 'utf8') };
}

function write(filePath, source) {
  fs.writeFileSync(filePath, source.endsWith('\n') ? source : `${source}\n`, 'utf8');
}

function count(source, marker) {
  return source.split(marker).length - 1;
}

function replaceRequired(source, anchor, replacement, label) {
  if (!source.includes(anchor)) throw new Error(`${label} anchor is missing.`);
  return source.replace(anchor, replacement);
}

function installApp() {
  const target = read('App.jsx');
  let source = target.source;
  const importLine = "import AiProviderReadinessController from './ai/AiProviderReadinessController.jsx';";
  const importAnchor = "import EnterpriseModulePresentation from './enterprise/EnterpriseModulePresentation.jsx';";
  if (!source.includes(importLine)) {
    if (!source.includes(importAnchor)) throw new Error('Group 7 requires the Group 6 enterprise presentation import.');
    source = source.replace(importAnchor, `${importAnchor}\n${importLine}`);
  }

  const markerStart = 'GROUP_7_AI_PROVIDER_READINESS_CONTROLLER_START';
  const markerEnd = 'GROUP_7_AI_PROVIDER_READINESS_CONTROLLER_END';
  if (!source.includes(markerStart)) {
    const anchor = '      {/* GROUP_6_ENTERPRISE_PRESENTATION_END */}';
    if (!source.includes(anchor)) throw new Error('Group 7 requires the generated Group 6 presentation block.');
    source = source.replace(anchor, [
      anchor,
      `      {/* ${markerStart} */}`,
      '      <AiProviderReadinessController authSession={authSession} />',
      `      {/* ${markerEnd} */}`
    ].join('\n'));
  }

  if (count(source, importLine) !== 1) throw new Error('Group 7 App controller import must appear once.');
  if (count(source, markerStart) !== 1 || count(source, markerEnd) !== 1) throw new Error('Group 7 App controller markers must appear once.');
  if (count(source, '<AiProviderReadinessController authSession={authSession} />') !== 1) throw new Error('Group 7 readiness controller must mount once.');
  write(target.filePath, source);
}

function installProviderPanel() {
  const target = read('AiProviderConfigurationCenter.jsx');
  let source = target.source;
  const importLine = "import AiProviderReadinessPanel from './ai/AiProviderReadinessPanel.jsx';";
  const importAnchor = "import './projectpulse-module-standard.css';";
  if (!source.includes(importLine)) {
    if (!source.includes(importAnchor)) throw new Error('Group 7 Module 064 import anchor is missing.');
    source = source.replace(importAnchor, `${importAnchor}\n${importLine}`);
  }

  const markerStart = 'GROUP_7_MODULE_064_READINESS_PANEL_START';
  const markerEnd = 'GROUP_7_MODULE_064_READINESS_PANEL_END';
  if (!source.includes(markerStart)) {
    const anchor = '      <div className="ai-provider-center__automatic-health" role="status">';
    if (!source.includes(anchor)) throw new Error('Group 7 Module 064 readiness mount anchor is missing.');
    source = source.replace(anchor, [
      `      {/* ${markerStart} */}`,
      '      <AiProviderReadinessPanel />',
      `      {/* ${markerEnd} */}`,
      anchor
    ].join('\n'));
  }

  if (count(source, importLine) !== 1) throw new Error('Group 7 Module 064 panel import must appear once.');
  if (count(source, '<AiProviderReadinessPanel />') !== 1) throw new Error('Group 7 Module 064 panel must mount once.');
  write(target.filePath, source);
}

function installHelp() {
  const target = read('HelpAssistant.jsx');
  let source = target.source;
  const governanceImport = "import HelpGovernancePanel from './help/HelpGovernancePanel.jsx';";
  const preferenceImport = "import { applyHelpAnswerPreferences } from './help/help-answer-preferences.js';";
  const importAnchor = "import './help-assistant.css';";
  if (!source.includes(governanceImport)) {
    if (!source.includes(importAnchor)) throw new Error('Group 7 Help import anchor is missing.');
    source = source.replace(importAnchor, `${importAnchor}\n${governanceImport}\n${preferenceImport}`);
  }

  const preferenceCall = '  const answerPreferences = applyHelpAnswerPreferences(url, question);';
  if (!source.includes(preferenceCall)) {
    const anchor = "  url.searchParams.set('question', question);";
    source = replaceRequired(source, anchor, `${anchor}\n${preferenceCall}`, 'Group 7 Help query');
  }

  if (!source.includes('return { ...payload, answerPreferences };')) {
    const anchor = '  return payload;\n}\n\nfunction navigateTo';
    source = replaceRequired(
      source,
      anchor,
      '  return { ...payload, answerPreferences };\n}\n\nfunction navigateTo',
      'Group 7 Help answer-preference response'
    );
  }

  if (!source.includes('GROUP_7_HELP_ANSWER_DETAIL_START')) {
    const anchor = '  const answerContract = payload?.answerContract ?? {};';
    source = replaceRequired(source, anchor, [
      anchor,
      '  /* GROUP_7_HELP_ANSWER_DETAIL_START */',
      "  const answerPreferences = payload?.answerPreferences ?? { detailLevel: 'standard' };",
      "  const detailLevel = answerPreferences.detailLevel ?? 'standard';",
      "  const conciseAnswer = detailLevel === 'concise';",
      "  const executiveAnswer = detailLevel === 'executive';",
      "  const expandedAnswer = ['detailed', 'highly_detailed', 'technical', 'step_by_step'].includes(detailLevel);",
      "  const technicalAnswer = ['highly_detailed', 'technical'].includes(detailLevel);",
      '  /* GROUP_7_HELP_ANSWER_DETAIL_END */'
    ].join('\n'), 'Group 7 Help answer-detail state');
  }

  if (!source.includes('data-answer-detail={detailLevel}')) {
    source = replaceRequired(
      source,
      '    <div className="help-detailed-answer">',
      '    <div className="help-detailed-answer" data-answer-detail={detailLevel}>',
      'Group 7 Help answer-detail marker'
    );
  }

  const conditionalReplacements = [
    [
      '          <AnswerList heading="Detailed procedure" values={direct.detailedSteps} />',
      '          {!executiveAnswer ? <AnswerList heading="Detailed procedure" values={direct.detailedSteps} /> : null}'
    ],
    [
      '          <AnswerList heading="Important rules" values={direct.importantRules} />',
      '          {!conciseAnswer ? <AnswerList heading="Important rules" values={direct.importantRules} /> : null}'
    ],
    [
      '           <AnswerList heading="Required evidence" values={plan.requiredEvidence} />',
      '           {expandedAnswer ? <AnswerList heading="Required evidence" values={plan.requiredEvidence} /> : null}'
    ],
    [
      '           <AnswerList heading="Filters that must be resolved" values={plan.filtersToResolve} />',
      '           {technicalAnswer ? <AnswerList heading="Filters that must be resolved" values={plan.filtersToResolve} /> : null}'
    ],
    [
      '           <AnswerList heading="Deterministic calculations" values={plan.deterministicCalculations} />',
      '           {expandedAnswer ? <AnswerList heading="Deterministic calculations" values={plan.deterministicCalculations} /> : null}'
    ],
    [
      '           <AnswerList heading="Required answer sections" values={plan.answerSections} />',
      '           {!conciseAnswer ? <AnswerList heading="Required answer sections" values={plan.answerSections} /> : null}'
    ],
    [
      '           <AnswerList heading="Detailed execution sequence" values={plan.executionSteps} />',
      '           {expandedAnswer ? <AnswerList heading="Detailed execution sequence" values={plan.executionSteps} /> : null}'
    ],
    [
      '           <AnswerList heading="Privacy controls" values={plan.privacyControls} />',
      '           {!conciseAnswer ? <AnswerList heading="Privacy controls" values={plan.privacyControls} /> : null}'
    ],
    [
      '           <AnswerList heading="Missing inputs before exact execution" values={plan.missingInputs} />',
      '           {!conciseAnswer ? <AnswerList heading="Missing inputs before exact execution" values={plan.missingInputs} /> : null}'
    ]
  ];
  conditionalReplacements.forEach(([anchor, replacement]) => {
    if (source.includes(anchor)) source = source.replace(anchor, replacement);
  });

  if (!source.includes('Answer detail: {titleFrom(detailLevel)}')) {
    const anchor = '      <details className="help-answer-contract">';
    source = replaceRequired(source, anchor, [
      '      <div className="help-answer-preference-evidence">',
      '        <span>Answer detail: {titleFrom(detailLevel)}</span>',
      '        <span>Preference source: {titleFrom(answerPreferences.preferenceSource ?? \'saved_preference\')}</span>',
      '        {answerPreferences.includeRepositoryContext ? <span>Repository context requested</span> : null}',
      '        {answerPreferences.includeAssumptions ? <span>Assumptions requested</span> : null}',
      '        {answerPreferences.includeSourceCitations ? <span>Source citations requested</span> : null}',
      '      </div>',
      '      {technicalAnswer ? (',
      anchor
    ].join('\n'), 'Group 7 Help preference evidence');
    source = replaceRequired(
      source,
      '      </details>\n    </div>',
      '      </details>\n      ) : null}\n    </div>',
      'Group 7 Help technical answer contract'
    );
  }

  if (!source.includes('GROUP_7_HELP_GOVERNANCE_PANEL_START')) {
    const anchor = '          <div className="help-messages">';
    if (!source.includes(anchor)) throw new Error('Group 7 Help panel mount anchor is missing.');
    source = source.replace(anchor, [
      '          {/* GROUP_7_HELP_GOVERNANCE_PANEL_START */}',
      '          <HelpGovernancePanel />',
      '          {/* GROUP_7_HELP_GOVERNANCE_PANEL_END */}',
      anchor
    ].join('\n'));
  }

  source = source.replaceAll('Module 999 — Complete User Guide', 'Module 999 — System User Guide');
  source = source.replaceAll('Use the complete ProjectPulse guide', 'Use the System User Guide');

  if (count(source, governanceImport) !== 1 || count(source, preferenceImport) !== 1) throw new Error('Group 7 Help imports must appear once.');
  if (count(source, preferenceCall) !== 1) throw new Error('Group 7 Help preference application must appear once.');
  if (count(source, '<HelpGovernancePanel />') !== 1) throw new Error('Group 7 Help governance panel must mount once.');
  write(target.filePath, source);
}

function installSystemGuide() {
  const target = read('SystemUserGuide.jsx');
  let source = target.source;
  const governanceImport = "import { SystemUserGuideGovernancePanel } from './help/HelpGovernancePanel.jsx';";
  const logoImport = "import USSignalLogo from './enterprise/USSignalLogo.jsx';";
  const importAnchor = "import { compareProjectPulseModules } from './module-ordering.js';";
  if (!source.includes(governanceImport)) {
    if (!source.includes(importAnchor)) throw new Error('Group 7 System User Guide import anchor is missing.');
    source = source.replace(importAnchor, `${importAnchor}\n${governanceImport}\n${logoImport}`);
  }

  source = source.replaceAll('ProjectPulse Complete User Guide', 'System User Guide');
  source = source.replaceAll('Search the complete guide', 'Search the System User Guide');

  if (!source.includes('GROUP_7_SYSTEM_GUIDE_LOGO')) {
    const anchor = '      <header className="system-user-guide-hero">\n        <div>';
    if (!source.includes(anchor)) throw new Error('Group 7 System User Guide header anchor is missing.');
    source = source.replace(anchor, '      <header className="system-user-guide-hero">\n        {/* GROUP_7_SYSTEM_GUIDE_LOGO */}\n        <USSignalLogo size="large" />\n        <div>');
  }

  if (!source.includes('GROUP_7_SYSTEM_GUIDE_GOVERNANCE_START')) {
    const anchor = '      <section className="system-user-guide-principles" aria-label="Guide principles">';
    if (!source.includes(anchor)) throw new Error('Group 7 System User Guide governance anchor is missing.');
    source = source.replace(anchor, [
      '      {/* GROUP_7_SYSTEM_GUIDE_GOVERNANCE_START */}',
      '      <SystemUserGuideGovernancePanel />',
      '      {/* GROUP_7_SYSTEM_GUIDE_GOVERNANCE_END */}',
      '',
      anchor
    ].join('\n'));
  }

  if (count(source, governanceImport) !== 1 || count(source, logoImport) !== 1) throw new Error('Group 7 System User Guide imports must appear once.');
  if (count(source, '<SystemUserGuideGovernancePanel />') !== 1) throw new Error('Group 7 System User Guide governance panel must mount once.');
  if (count(source, '<USSignalLogo size="large" />') !== 1) throw new Error('Group 7 System User Guide official logo must mount once.');
  if (source.includes('ProjectPulse Complete User Guide')) throw new Error('The retired Module 999 title remains in SystemUserGuide.jsx.');
  write(target.filePath, source);
}

function installRegistry() {
  const target = read('module-availability-registry.js');
  let source = target.source;
  const legacy = "Object.freeze({ moduleNumber: '999', route: 'user-guide', displayName: 'ProjectPulse Complete User Guide', group: 'Help & Documentation' })";
  const current = "Object.freeze({ moduleNumber: '999', route: 'user-guide', displayName: 'System User Guide', group: 'Help & Documentation' })";
  if (source.includes(legacy)) source = source.replace(legacy, current);
  if (!source.includes(current)) throw new Error('Group 7 Module 999 registry identity could not be installed.');
  if (count(source, "moduleNumber: '999'") !== 1) throw new Error('Module 999 registry entry must remain unique.');
  write(target.filePath, source);
}

installApp();
installProviderPanel();
installHelp();
installSystemGuide();
installRegistry();
console.log('GROUP_7_AI_HELP_SYSTEM_GUIDE_INJECTION=PASS modules=064,999 help=governed preferences=saved-query-overridable');
