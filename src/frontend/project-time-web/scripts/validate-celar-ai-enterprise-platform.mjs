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

function section(source, start, end) {
  const startIndex = source.indexOf(start);
  if (startIndex < 0) return '';
  const endIndex = source.indexOf(end, startIndex + start.length);
  return endIndex < 0 ? source.slice(startIndex) : source.slice(startIndex, endIndex);
}

const files = {
  contracts: 'src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformContracts.cs',
  service: 'src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformService.cs',
  external: 'src/backend/ProjectTime.Api/Ai/CelarAiExternalReasoningService.cs',
  routing: 'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs',
  sanitizer: 'src/backend/ProjectTime.Api/Ai/PulseAiEscalationSanitizer.cs',
  people: 'src/backend/ProjectTime.Api/Ai/CelarAiPeopleAndGuidanceService.cs',
  module: 'src/backend/ProjectTime.Api/Modules/CelarAiEnterprisePlatformModule.cs',
  services: 'src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs',
  targets: 'src/backend/ProjectTime.Api/Directory.Build.targets',
  enterprise: 'src/frontend/project-time-web/src/CelarAiEnterprisePlatform.jsx',
  help: 'src/frontend/project-time-web/src/HelpAssistant.jsx',
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
const routing = read(files.routing);
const sanitizer = read(files.sanitizer);
const people = read(files.people);
const moduleSource = read(files.module);
const services = read(files.services);
const targets = read(files.targets);
const enterprise = read(files.enterprise);
const help = read(files.help);
const architecture = read(files.architecture);
const composer = read(files.composer);
const panel = read(files.panel);
const chatInjector = read(files.chatInjector);
const contextInjector = read(files.contextInjector);
const rebrandInjector = read(files.rebrandInjector);
const chatCss = read(files.chatCss);
const documentation = read(files.documentation);
const refusalResult = section(
  service,
  'private static CelarAiComposeResult RefusedComposeResult(',
  'private async Task<PulseAiPrivateRagAnswer> ExecutePrivateComposeAsync('
);

assert('SOLUTION_MODES', ['timesheet_description', 'sow_draft', 'project_plan', 'project_timeline', 'project_diagram'].every((value) => contracts.includes(`"${value}"`)), 'all enterprise composition modes are registered');
assert(
  'PRIVATE_RAG_COMPOSITION',
  service.includes('GenerateTimesheetAsync')
    && service.includes('GenerateFlowHivePlanAsync')
    && service.includes('FeatureCode: CelarAiCapabilityCatalog.SowGsdPlanning'),
  'Timesheet uses private grounding while SOW, FlowHive, and Project Forge use the structured private planning schema'
);
assert(
  'SAFETY_REFUSAL_CLEARS_ARTIFACTS',
  service.includes('if (routed.Outcome == ProjectPulseAiOutcomes.Refusal)')
    && service.includes('return RefusedComposeResult(mode, routed, correlationId);')
    && refusalResult.length > 0
    && [
      'ProjectId: null',
      'ProjectCode: string.Empty',
      'ProjectName: string.Empty',
      'DetailedAnswer: null',
      'FlowHivePlan: null',
      'SowDraft: null',
      'Timeline: []',
      'Diagram: null',
      'Citations: []',
      'MissingEvidence: []',
      'Conflicts: []',
      'ExternalAssistance: null'
    ].every((value) => refusalResult.includes(value)),
  'a routed safety refusal returns before composition and exposes no generated, private-source, or external-assistance artifacts'
);
assert(
  'PRIVATE_ARTIFACT_STATUS_SEPARATE_FROM_ASSISTANCE_TARGET',
  service.includes('var status = privateResult?.Status == "completed"')
    && service.includes('privateResult?.Status == "partial"')
    && service.includes('private_celar_rag_with_sanitized_generic_module064_assistance')
    && service.includes('private_evidence_composer_after_governed_local_route')
    && !service.includes('routed.Provider == CelarAiCapabilityTargets.CelarAi\n                && privateResult?.Status == "completed"'),
  'completed/partial describes the private citation-grounded artifact while selectedTarget and primaryExecutionPath separately disclose generic external/local assistance'
);
assert(
  'EXTERNAL_ASSISTANCE_ONLY_FOR_PUBLIC_PROVIDER',
  service.includes('if (routed.Provider is CelarAiCapabilityTargets.Claude or CelarAiCapabilityTargets.OpenAi')
    && service.includes('external = ToExternalAssistance(routed);')
    && service.includes('private_evidence_composer_after_governed_local_route')
    && !service.includes('if (!string.Equals(routed.Provider, CelarAiCapabilityTargets.CelarAi'),
  'ExternalAssistance is populated only for Claude/OpenAI; a local terminal route remains separate route metadata and cannot be represented as external model output'
);
assert('TIMELINE_AND_DIAGRAM', service.includes('BuildTimeline') && service.includes('BuildDiagram') && service.includes('MermaidSource'), 'project timelines and diagrams are composed deterministically from private planning output');
assert('NO_CONSEQUENTIAL_MUTATION', contracts.includes('timesheetSaved = false') && contracts.includes('sowPublished = false') && contracts.includes('projectPlanBaselined = false') && contracts.includes('customerDateCommitted = false'), 'generated artifacts are review-only');
assert('EXTERNAL_RUNTIME_POLICY', routing.includes('PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION') && routing.includes('PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED') && routing.includes('sanitized_external_policy_disabled'), 'automatic sanitized external reasoning requires both independent runtime privacy-policy flags');
assert('DLP_EXECUTION_GATE', sanitizer.includes('SanitizeForExecution') && sanitizer.includes('sanitized_capsule_execution_ready') && sanitizer.includes('named_people_and_customers') && sanitizer.includes('financial_values'), 'execution requires a fail-closed DLP capsule');
assert('NO_PRIVATE_EXTERNAL_CONTEXT', external.includes('Private document text is never eligible') && external.includes('Financial or commercial values are never eligible') && external.includes('People, assignment, workload, or employee records are never eligible'), 'private documents, people records, and financial values are prohibited');
assert(
  'CLOSED_SERVER_OWNED_EXTERNAL_CAPSULE',
  routing.includes('public static class CelarAiExternalCapsuleCatalog')
    && routing.includes('public const string SowScopeQuality')
    && routing.includes('public const string ProjectPlanQuality')
    && routing.includes('public const string ProjectTimelineQuality')
    && routing.includes('public const string ProjectDiagramQuality')
    && routing.includes('execution.ExternalCapsulePurpose')
    && routing.includes('Content: fixedCapsule.Capsule')
    && routing.includes('SystemPrompt = fixedCapsule.SystemPrompt')
    && routing.includes('sanitized_external_closed_purpose_required')
    && !routing.includes('PurposeBuiltExternalCapsule')
    && !routing.includes('PurposeBuiltExternalSystemPrompt')
    && service.includes('_router.GenerateWithPrivateTargetAsync(')
    && service.includes('ExternalCapsulePurpose: externalCapsulePurpose')
    && service.includes('AllowSanitizedExternalAssistance: false')
    && !service.includes('request.AllowSanitizedExternalFallback')
    && targets.includes('DestinationFiles="$(CelarAiRoutingGenerated)"'),
  'arbitrary input—including lowercase or unlabeled customer/person text—cannot be represented in the Module 011 public-provider request; unknown categories fail closed'
);
assert('MODULE_064_ROUTER', external.includes('CelarAiCapabilityRouter') && external.includes('_router.GenerateExternalAsync(') && external.includes('ExternalCapsulePurpose: serverOwnedPurposeCategory') && external.includes('ProjectPulseAiFeatures.ProjectFlowHivePlan') && external.includes('ProjectPulseAiFeatures.SowGsdPlanning'), 'approved generic reasoning uses the canonical closed-purpose Module 064 route');
assert('ENDPOINTS', moduleSource.includes('/api/celar-ai/v1/architecture') && contracts.includes('/api/celar-ai/v1/platform/readiness') && contracts.includes('/api/celar-ai/v1/compose'), 'enterprise readiness, architecture, and composer endpoints are present');
assert('SERVICES_REGISTERED', services.includes('CelarAiPeopleAndGuidanceService') && services.includes('CelarAiExternalReasoningService') && services.includes('CelarAiEnterprisePlatformService'), 'all enterprise services are registered');
assert('COMPILE_COMPATIBILITY', targets.includes('MapCelarAiEnterprisePlatformEndpoints') && targets.includes('persistence.EffectiveUserId') && targets.includes('CelarAiEnterprisePlatformService.g.cs'), 'endpoint mapping and durable-user persistence compile copies are generated');
assert('ARCHITECTURE_US_SIGNAL', architecture.includes('usSignalLogoDataUrl') && architecture.includes('Created by Dr. Ahmed Adeyemi') && architecture.includes('Module 064') && architecture.includes('Claude / OpenAI'), 'the page contains the US Signal private-first architecture and creator attribution');
assert('ARCHITECTURE_ACCESSIBLE', architecture.includes('role="img"') && architecture.includes('<title id="celar-ai-svg-title">') && architecture.includes('<desc id="celar-ai-svg-description">'), 'architecture diagram is accessible');
assert(
  'ARCHITECTURE_CONTEXT_FABRIC',
  moduleSource.includes('celar-ai-private-first-architecture-v5-context-fabric')
    && architecture.includes('Private content graph and retrieval')
    && architecture.includes('Temporal context · policy eligibility · authoritative versions · private fine-tuning lifecycle')
    && architecture.includes('Confidence · freshness · policy · live decision trace')
    && architecture.includes('Self-monitoring adapters'),
  'Module 011 shows the current content, temporal, policy, decision-trace, private-adapter, and fine-tuning architecture'
);
assert('ENTERPRISE_MOUNT', panel.includes("import CelarAiEnterprisePlatform") && panel.includes('<CelarAiEnterprisePlatform />'), 'Module 011 mounts the enterprise platform before lifecycle workbenches');
assert('COMPOSER_INTERFACE', composer.includes("'/api/celar-ai/v1/compose'") && composer.includes('Download SVG') && composer.includes('Mermaid source') && composer.includes('Fallback is automatic and governed by Module 064') && composer.includes('allowSanitizedExternalFallback: true'), 'composer supports private artifacts and diagrams while backend-managed fallback follows stored Module 064 order without an Engineer checkbox');
assert('CHAT_NORMAL_SIZE', chatCss.includes('width: min(560px') && chatCss.includes('height: min(720px') && chatCss.includes('resize: both'), 'chat defaults to a normal resizable working-companion window');
assert('CHAT_SIZE_CONTROLS', ['is-size-compact', 'is-size-standard', 'is-size-wide', 'is-size-fullscreen', 'is-minimized'].every((value) => chatCss.includes(value)), 'compact, standard, wide, fullscreen, and minimized states exist');
assert(
  'CHAT_SIZE_AND_MOVEMENT_EXECUTABLE',
  chatCss.includes('.help-panel.pulse-ai-help-panel.pulse-ai-system-chat.celar-ai-contextual-chat.is-size-compact')
    && chatCss.includes('.help-panel.pulse-ai-help-panel.pulse-ai-system-chat.celar-ai-contextual-chat.is-size-wide')
    && chatCss.includes('transform: translate(var(--celar-chat-x, 0), var(--celar-chat-y, 0))')
    && help.includes('function beginChatDrag(event)')
    && help.includes('function moveChat(event)')
    && help.includes('function selectChatSize(size)')
    && help.includes('data-movable='),
  'C/S/W/fullscreen have winning CSS dimensions and the desktop chat has bounded pointer movement plus reset'
);
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
assert(
  'AUTHORIZED_PROJECT_TYPEAHEAD',
  help.includes("getJson('/api/project-workspace/overview')")
    && help.includes('role="combobox"')
    && help.includes('role="listbox"')
    && help.includes('function selectProjectContext(project)')
    && help.includes('data-project-context="authorized-typeahead"'),
  'typing a project name or code suggests only projects returned by the authorized project-workspace API'
);
assert(
  'PRIVATE_ADAPTER_TRANSPORTS',
  services.includes('AddHttpClient("PulseAiPrivateOcr"')
    && services.includes('AddHttpClient("PulseAiPrivateEmbedding"')
    && services.includes('AddHttpClient("PulseAiPrivateInference"')
    && services.includes('AddHttpClient("PulseAiPrivateTraining"')
    && services.includes('ConfigurePrimaryHttpMessageHandler(() => PrivateHttpHandler())'),
  'OCR, embedding, inference, and fine-tuning use dedicated DNS-pinned private transports'
);
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
console.log('CELAR_AI_ENTERPRISE_EXTERNAL_FALLBACK=AUTOMATIC_WHEN_ROUTE_AND_RUNTIME_POLICY_ALLOW');
console.log('CELAR_AI_ENTERPRISE_STATE_MUTATIONS=0');

if (checks.some((check) => !check.condition)) {
  console.error('CELAR_AI_ENTERPRISE_PLATFORM_CONTRACT=FAILED');
  process.exit(1);
}
console.log('CELAR_AI_ENTERPRISE_PLATFORM_CONTRACT=PASSED');
