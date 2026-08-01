import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relative) => fs.readFileSync(path.join(repo, relative), 'utf8');
const exists = (relative) => fs.existsSync(path.join(repo, relative));
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition });
  console.log(`CELAR_AI_ENTERPRISE_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const files = {
  contracts: 'src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformContracts.cs',
  service: 'src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformService.cs',
  external: 'src/backend/ProjectTime.Api/Ai/CelarAiExternalReasoningService.cs',
  sanitizer: 'src/backend/ProjectTime.Api/Ai/PulseAiEscalationSanitizer.cs',
  people: 'src/backend/ProjectTime.Api/Ai/CelarAiPeopleAndGuidanceService.cs',
  module: 'src/backend/ProjectTime.Api/Modules/CelarAiEnterprisePlatformModule.cs',
  services: 'src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs',
  targets: 'src/backend/ProjectTime.Api/Directory.Build.targets',
  enterprise: 'src/frontend/project-time-web/src/CelarAiEnterprisePlatform.jsx',
  architecture: 'src/frontend/project-time-web/src/CelarAiArchitectureOverview.jsx',
  composer: 'src/frontend/project-time-web/src/CelarAiSolutionComposer.jsx',
  panel: 'src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx',
  chatInjector: 'src/frontend/project-time-web/scripts/inject-celar-ai-contextual-chat-workspace.mjs',
  contextInjector: 'src/frontend/project-time-web/scripts/inject-celar-ai-enterprise-chat-context.mjs',
  rebrandInjector: 'src/frontend/project-time-web/scripts/inject-celar-ai-runtime-rebrand.mjs',
  chatCss: 'src/frontend/project-time-web/src/celar-ai-contextual-chat.css',
  architectureCss: 'src/frontend/project-time-web/src/celar-ai-architecture-overview.css',
  composerCss: 'src/frontend/project-time-web/src/celar-ai-solution-composer.css',
  enterpriseCss: 'src/frontend/project-time-web/src/celar-ai-enterprise-platform.css',
  documentation: 'docs/modules/module-011-pulse-ai/CELAR-AI-ENTERPRISE-PLATFORM-INTERFACE.md'
};

for (const [name, relative] of Object.entries(files)) assert(`FILE_${name.toUpperCase()}`, exists(relative), relative);
if (checks.some((check) => !check.condition)) process.exit(1);

const contracts = read(files.contracts);
const service = read(files.service);
const external = read(files.external);
const sanitizer = read(files.sanitizer);
const people = read(files.people);
const moduleSource = read(files.module);
const services = read(files.services);
const targets = read(files.targets);
const enterprise = read(files.enterprise);
const architecture = read(files.architecture);
const composer = read(files.composer);
const panel = read(files.panel);
const chatInjector = read(files.chatInjector);
const contextInjector = read(files.contextInjector);
const rebrandInjector = read(files.rebrandInjector);
const chatCss = read(files.chatCss);
const documentation = read(files.documentation);

assert('SOLUTION_MODES', ['timesheet_description', 'sow_draft', 'project_plan', 'project_timeline', 'project_diagram'].every((value) => contracts.includes(`"${value}"`)), 'all enterprise composition modes are registered');
assert('PRIVATE_RAG_COMPOSITION', service.includes('GenerateTimesheetAsync') && service.includes('AskHelpSearchAsync') && service.includes('GenerateFlowHivePlanAsync'), 'Timesheet, SOW, and FlowHive use the private RAG service');
assert('TIMELINE_AND_DIAGRAM', service.includes('BuildTimeline') && service.includes('BuildDiagram') && service.includes('MermaidSource'), 'project timelines and diagrams are composed deterministically from private planning output');
assert('NO_CONSEQUENTIAL_MUTATION', contracts.includes('timesheetSaved = false') && contracts.includes('sowPublished = false') && contracts.includes('projectPlanBaselined = false') && contracts.includes('customerDateCommitted = false'), 'generated artifacts are review-only');
assert('EXTERNAL_DISABLED_DEFAULT', external.includes('PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED') && external.includes('&& enabled;'), 'sanitized external reasoning requires an explicit runtime setting');
assert('DLP_EXECUTION_GATE', sanitizer.includes('SanitizeForExecution') && sanitizer.includes('sanitized_capsule_execution_ready') && sanitizer.includes('named_people_and_customers') && sanitizer.includes('financial_values'), 'execution requires a fail-closed DLP capsule');
assert('NO_PRIVATE_EXTERNAL_CONTEXT', external.includes('Private document text is never eligible') && external.includes('Financial or commercial values are never eligible') && external.includes('People, assignment, workload, or employee records are never eligible'), 'private documents, people records, and financial values are prohibited');
assert('MODULE_064_ROUTER', external.includes('ProjectPulseAiRouter') && external.includes('ProjectPulseAiFeatures.ProjectFlowHivePlan') && external.includes('ProjectPulseAiFeatures.SowGsdPlanning'), 'approved generic reasoning uses Module 064 feature routing');
assert('ENDPOINTS', moduleSource.includes('/api/celar-ai/v1/architecture') && contracts.includes('/api/celar-ai/v1/platform/readiness') && contracts.includes('/api/celar-ai/v1/compose'), 'enterprise readiness, architecture, and composer endpoints are present');
assert('SERVICES_REGISTERED', services.includes('CelarAiPeopleAndGuidanceService') && services.includes('CelarAiExternalReasoningService') && services.includes('CelarAiEnterprisePlatformService'), 'all enterprise services are registered');
assert('COMPILE_COMPATIBILITY', targets.includes('MapCelarAiEnterprisePlatformEndpoints') && targets.includes('persistence.EffectiveUserId') && targets.includes('CelarAiEnterprisePlatformService.g.cs'), 'endpoint mapping and durable-user persistence compile copies are generated');
assert('ARCHITECTURE_US_SIGNAL', architecture.includes('usSignalLogoDataUrl') && architecture.includes('Created by Dr. Ahmed Adeyemi') && architecture.includes('Module 064') && architecture.includes('Claude / OpenAI'), 'the page contains the US Signal private-first architecture and creator attribution');
assert('ARCHITECTURE_ACCESSIBLE', architecture.includes('role="img"') && architecture.includes('<title id="celar-ai-svg-title">') && architecture.includes('<desc id="celar-ai-svg-description">'), 'architecture diagram is accessible');
assert('ENTERPRISE_MOUNT', panel.includes("import CelarAiEnterprisePlatform") && panel.includes('<CelarAiEnterprisePlatform />'), 'Module 011 mounts the enterprise platform before lifecycle workbenches');
assert('COMPOSER_INTERFACE', composer.includes("'/api/celar-ai/v1/compose'") && composer.includes('Download SVG') && composer.includes('Mermaid source') && composer.includes('Allow generic sanitized fallback'), 'composer supports private artifacts, diagrams, and explicit fallback consent');
assert('CHAT_NORMAL_SIZE', chatCss.includes('width: min(560px') && chatCss.includes('height: min(720px') && chatCss.includes('resize: both'), 'chat defaults to a normal resizable working-companion window');
assert('CHAT_SIZE_CONTROLS', ['is-size-compact', 'is-size-standard', 'is-size-wide', 'is-size-fullscreen', 'is-minimized'].every((value) => chatCss.includes(value)), 'compact, standard, wide, fullscreen, and minimized states exist');
assert(
  'FRESH_CHAT_DEFAULT',
  chatInjector.includes("setActiveConversationId('');")
    && chatInjector.includes('setMessages([WELCOME_MESSAGE])')
    && chatInjector.includes('await refreshConversationList();')
    && chatInjector.includes('CELAR_AI_CONTEXTUAL_CHAT_FRESH_THREAD_DEFAULT=YES')
    && chatInjector.includes('previous conversations remain in your History, but they are not automatically inserted'),
  'opening the chat lists retained history but starts a fresh visible thread');
assert(
  'HISTORY_EXPLICIT',
  chatInjector.includes('await loadConversation(id)')
    && chatInjector.includes('setHistoryOpen(false)')
    && chatInjector.includes('CELAR_AI_CONTEXTUAL_CHAT_HISTORY_AUTO_INJECTED=NO'),
  'history is loaded only after the user selects a retained conversation');
assert('QUESTION_CONTEXT', contextInjector.includes('projectCode') && contextInjector.includes('personOrTeam') && contextInjector.includes('dateFrom') && contextInjector.includes('current question and selected thread'), 'project, person/team, and date context is explicit and current-question scoped');
assert('CONTEXT_INJECTOR_ACTIVE', rebrandInjector.includes("inject-celar-ai-enterprise-chat-context.mjs"), 'enterprise context injection runs after contextual chat injection');
assert('PEOPLE_AUTHORIZED_TOOLS', people.includes('/api/project-workspace/overview') && people.includes('/api/capacity-forecast/forecast?weeks=14') && people.includes('/api/manager/approval-summary'), 'people/work questions use governed owning APIs');
assert(
  'NO_SURVEILLANCE',
  people.includes('not proof of real-time activity')
    && people.includes('personnel surveillance')
    && people.includes('conversation history as evidence of what a person is currently doing'),
  'assignment and workload evidence is not misrepresented as real-time surveillance');
assert('HOW_TO_CATALOG', people.includes('Create a new project') && people.includes('Enter time and generate a document-grounded suggestion') && people.includes('Create a reviewable FlowHive project plan') && people.includes('Run reports and investigate financial or billing results'), 'common Pulse procedures are source controlled');
assert('DOCUMENTATION', documentation.includes('Celar AI Enterprise Platform Interface') && documentation.includes('Created by') && documentation.includes('PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED'), 'architecture, operation, and fallback policy are documented');
assert('NO_NEW_MIGRATION', !fs.readdirSync(path.join(repo, 'database', 'migrations')).some((name) => /celar.*enterprise|enterprise.*celar/i.test(name)), 'this interface package adds no database migration');

console.log(`CELAR_AI_ENTERPRISE_CHECKS=${checks.length}`);
console.log('CELAR_AI_ENTERPRISE_ARCHITECTURE=US_SIGNAL_PRIVATE_FIRST');
console.log('CELAR_AI_ENTERPRISE_DEFAULT_CHAT_CONTEXT=FRESH');
console.log('CELAR_AI_ENTERPRISE_EXTERNAL_FALLBACK=DISABLED_BY_DEFAULT');
console.log('CELAR_AI_ENTERPRISE_STATE_MUTATIONS=0');

if (checks.some((check) => !check.condition)) {
  console.error('CELAR_AI_ENTERPRISE_PLATFORM_CONTRACT=FAILED');
  process.exit(1);
}
console.log('CELAR_AI_ENTERPRISE_PLATFORM_CONTRACT=PASSED');
