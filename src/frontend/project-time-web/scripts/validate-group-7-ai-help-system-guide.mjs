import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '../../..');
const sourceRoot = path.join(webRoot, 'src');
const fullRepositoryContext = fs.existsSync(path.join(repositoryRoot, '.git'))
  || fs.existsSync(path.join(repositoryRoot, '.github/workflows/projectpulse-ci.yml'));

const files = {
  readinessStore: path.join(sourceRoot, 'ai/ai-provider-readiness-store.js'),
  readinessController: path.join(sourceRoot, 'ai/AiProviderReadinessController.jsx'),
  readinessPanel: path.join(sourceRoot, 'ai/AiProviderReadinessPanel.jsx'),
  readinessCss: path.join(sourceRoot, 'ai/ai-provider-readiness.css'),
  preferences: path.join(sourceRoot, 'help/help-answer-preferences.js'),
  governance: path.join(sourceRoot, 'help/HelpGovernancePanel.jsx'),
  governanceCss: path.join(sourceRoot, 'help/help-governance.css'),
  injector: path.join(scriptDirectory, 'inject-group-7-ai-help-system-guide.mjs'),
  group6Injector: path.join(scriptDirectory, 'inject-group-6-enterprise-presentation.mjs'),
  package: path.join(webRoot, 'package.json'),
  app: path.join(sourceRoot, 'App.jsx'),
  provider: path.join(sourceRoot, 'AiProviderConfigurationCenter.jsx'),
  help: path.join(sourceRoot, 'HelpAssistant.jsx'),
  guide: path.join(sourceRoot, 'SystemUserGuide.jsx'),
  registry: path.join(sourceRoot, 'module-availability-registry.js'),
  documentation: path.join(repositoryRoot, 'docs/modules/group-7-ai-help-system-guide/README.md')
};

let checks = 0;
function read(filePath) {
  if (!fs.existsSync(filePath)) throw new Error(`Required Group 7 file is missing: ${path.relative(repositoryRoot, filePath)}`);
  return fs.readFileSync(filePath, 'utf8');
}
function assert(condition, message) {
  checks += 1;
  if (!condition) throw new Error(message);
}
function contains(source, marker, label) {
  assert(source.includes(marker), `${label} is missing: ${marker}`);
}
function count(source, marker) {
  return source.split(marker).length - 1;
}

const readinessStore = read(files.readinessStore);
const readinessController = read(files.readinessController);
const readinessPanel = read(files.readinessPanel);
const readinessCss = read(files.readinessCss);
const preferences = read(files.preferences);
const governance = read(files.governance);
const governanceCss = read(files.governanceCss);
const injector = read(files.injector);
const packageJson = JSON.parse(read(files.package));

for (const state of [
  'checking',
  'available',
  'unavailable',
  'not_configured',
  'authentication_failed',
  'rate_limited',
  'provider_error'
]) contains(readinessStore, `'${state}'`, 'Module 064 readiness state');
contains(readinessStore, 'let inFlightRequest = null', 'duplicate-request prevention');
contains(readinessStore, 'if (inFlightRequest) return inFlightRequest', 'shared in-flight readiness request');
contains(readinessStore, 'projectPulse.aiProviderReadiness.v1', 'last verified readiness cache');
contains(readinessStore, 'authenticated_startup', 'authenticated startup readiness');
contains(readinessStore, 'authenticated_background', 'background readiness refresh');
contains(readinessStore, 'manual_retest', 'manual provider Retest');
contains(readinessStore, 'lastVerifiedAt', 'last verified timestamp');
contains(readinessStore, 'The refresh failed; the last verified non-secret status remains visible', 'failed-refresh continuity');
contains(readinessStore, "'/api/ai-configuration/health'", 'read-only provider health endpoint');
contains(readinessStore, "'/api/ai-configuration/health/refresh'", 'manual provider refresh endpoint');
assert(!/apiKey|clientSecret|password\s*:/.test(readinessStore), 'The provider readiness store must not cache provider secrets.');
contains(readinessController, 'startAiProviderReadinessMonitoring', 'global authenticated readiness controller');
contains(readinessController, 'stopAiProviderReadinessMonitoring', 'readiness cleanup');
contains(readinessPanel, 'Retest providers', 'manual Retest action');
contains(readinessPanel, 'Last verified AI provider readiness', 'stable readiness presentation');
contains(readinessPanel, 'lastVerifiedAt', 'last check timestamp presentation');
contains(readinessPanel, "from '../enterprise/EnterpriseModulePresentation.jsx'", 'Group 6 component consumption');
contains(readinessCss, '.group7-provider-readiness', 'Module 064 readiness styling');
contains(readinessCss, ':focus-visible', 'Module 064 accessible focus styling');

for (const level of [
  'concise',
  'standard',
  'detailed',
  'highly_detailed',
  'technical',
  'executive',
  'step_by_step'
]) contains(preferences, `'${level}'`, 'saved answer-detail preference');
for (const option of [
  'includeRepositoryContext',
  'includeAssumptions',
  'includeSourceCitations'
]) contains(preferences, option, 'saved answer preference option');
for (const override of [
  '/concise',
  '/detailed',
  '/highly-detailed',
  '/technical',
  '/executive',
  '/step-by-step'
]) contains(preferences, override, 'query-level answer preference override');
contains(preferences, 'preferenceSource', 'saved versus query preference evidence');
contains(preferences, 'answerDetail', 'Help API answer-detail query contract');
contains(preferences, 'projectPulse.helpAnswerPreferences.v1.', 'per-identity preference storage');

const hierarchy = [
  'System User Guide',
  'Module descriptions and API metadata',
  'Repository documentation',
  'Permission-aware AI repository search',
  'Escalation or issue creation'
];
hierarchy.forEach((tier) => contains(governance, tier, 'governed Help hierarchy'));
contains(governance, 'Report an Issue', 'Help issue escalation');
contains(governance, 'Feature Request', 'Help feature escalation');
contains(governance, 'HelpAnswerPreferenceControls', 'saved Help preference controls');
contains(governance, 'every active module', 'System User Guide active-module coverage');
contains(governance, 'screenshots or evidence references', 'System User Guide screenshot/evidence requirement');
contains(governance, 'integration setup', 'System User Guide integration setup coverage');
contains(governance, 'reporting instructions', 'System User Guide reporting coverage');
contains(governanceCss, '.group7-help-governance', 'Help governance styling');
contains(governanceCss, '.group7-system-guide-overview', 'System User Guide governance styling');

for (const marker of [
  'GROUP_7_AI_PROVIDER_READINESS_CONTROLLER_START',
  'GROUP_7_MODULE_064_READINESS_PANEL_START',
  'GROUP_7_HELP_GOVERNANCE_PANEL_START',
  'GROUP_7_HELP_ANSWER_DETAIL_START',
  'GROUP_7_SYSTEM_GUIDE_LOGO',
  'GROUP_7_SYSTEM_GUIDE_GOVERNANCE_START'
]) contains(injector, marker, 'Group 7 idempotent installer');
contains(injector, 'applyHelpAnswerPreferences(url, question)', 'query-preference application');
contains(injector, 'data-answer-detail={detailLevel}', 'answer-detail rendering contract');
contains(injector, "displayName: 'System User Guide'", 'Module 999 rename');
assert(!injector.includes('enterprise-more-navigation'), 'Group 7 must not rewrite the permission-aware More menu.');
assert(!injector.includes('GlobalMailConfigurationCenter'), 'Group 7 must not recreate the retired Module 067 configuration surface.');

const predev = packageJson.scripts?.predev ?? '';
const prebuild = packageJson.scripts?.prebuild ?? '';
const build = packageJson.scripts?.build ?? '';
contains(predev, 'inject-group-6-enterprise-presentation.mjs', 'Group 6 predev dependency');
contains(predev, 'inject-group-7-ai-help-system-guide.mjs', 'Group 7 predev installer');
contains(prebuild, 'inject-group-6-enterprise-presentation.mjs', 'Group 6 prebuild dependency');
contains(prebuild, 'inject-group-7-ai-help-system-guide.mjs', 'Group 7 prebuild installer');
contains(build, 'validate:group6-enterprise-presentation', 'Group 6 validation dependency');
contains(build, 'validate:group7-ai-help-system-guide', 'Group 7 complete-build validation');
assert(
  packageJson.scripts?.['validate:group7-ai-help-system-guide']
    === 'node ./scripts/validate-group-7-ai-help-system-guide.mjs',
  'The Group 7 package validator must be authoritative.'
);

if (fullRepositoryContext) {
  const documentation = read(files.documentation);
  for (const marker of [
    'Module 064',
    'Help',
    'Module 999',
    'System User Guide',
    'No migration',
    'No deployment',
    'PR #277',
    'Group 6'
  ]) contains(documentation, marker, 'Group 7 documentation');
}

execFileSync(process.execPath, [files.group6Injector], { cwd: webRoot, stdio: 'inherit' });
execFileSync(process.execPath, [files.injector], { cwd: webRoot, stdio: 'inherit' });

const generatedApp = read(files.app);
const generatedProvider = read(files.provider);
const generatedHelp = read(files.help);
const generatedGuide = read(files.guide);
const generatedRegistry = read(files.registry);

assert(count(generatedApp, "import AiProviderReadinessController from './ai/AiProviderReadinessController.jsx';") === 1, 'Generated App must import the readiness controller once.');
assert(count(generatedApp, '<AiProviderReadinessController authSession={authSession} />') === 1, 'Generated App must mount the readiness controller once.');
assert(count(generatedProvider, '<AiProviderReadinessPanel />') === 1, 'Generated Module 064 must mount the stable readiness panel once.');
assert(count(generatedHelp, '<HelpGovernancePanel />') === 1, 'Generated Help must mount the governance panel once.');
contains(generatedHelp, 'const answerPreferences = applyHelpAnswerPreferences(url, question);', 'generated Help saved preference application');
contains(generatedHelp, 'return { ...payload, answerPreferences };', 'generated Help preference evidence');
contains(generatedHelp, 'data-answer-detail={detailLevel}', 'generated Help answer shaping');
contains(generatedHelp, 'Module 999 — System User Guide', 'generated Help Module 999 label');
assert(!generatedHelp.includes('Module 999 — Complete User Guide'), 'The retired Help label must be removed.');
contains(generatedGuide, '<h1>System User Guide</h1>', 'generated Module 999 title');
contains(generatedGuide, '<SystemUserGuideGovernancePanel />', 'generated Module 999 governance panel');
contains(generatedGuide, '<USSignalLogo size="large" />', 'generated Module 999 official logo');
assert(!generatedGuide.includes('ProjectPulse Complete User Guide'), 'The retired Module 999 title must be removed.');
contains(generatedRegistry, "displayName: 'System User Guide'", 'generated Module 999 registry identity');
assert(count(generatedRegistry, "moduleNumber: '999'") === 1, 'Generated Module 999 registry identity must remain unique.');

console.log(`GROUP_7_VALIDATION_CHECKS=${checks}`);
console.log(`GROUP_7_FULL_REPOSITORY_CONTEXT=${fullRepositoryContext ? 'YES' : 'NO'}`);
console.log('GROUP_7_AI_HELP_SYSTEM_USER_GUIDE=PASS');
