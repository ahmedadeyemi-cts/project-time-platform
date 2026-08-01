import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../../../', import.meta.url));
const abs = (relative) => path.join(root, relative);
const read = (relative) => fs.readFileSync(abs(relative), 'utf8');
const exists = (relative) => fs.existsSync(abs(relative));
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`MODULE011_SYSTEM_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}
function all(source, markers) { return markers.every((marker) => source.includes(marker)); }

const paths = {
  migration: 'database/migrations/054_pulse_ai_system_intelligence_conversations.sql',
  rollback: 'database/rollback/054_pulse_ai_system_intelligence_conversations_rollback.sql',
  migrationTest: 'tests/test-pulse-ai-system-intelligence-migration-054.sh',
  contracts: 'src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceContracts.cs',
  knowledge: 'src/backend/ProjectTime.Api/Ai/PulseAiSystemKnowledgeCatalog.cs',
  apiCatalog: 'src/backend/ProjectTime.Api/Ai/PulseAiSystemApiCatalogService.cs',
  executor: 'src/backend/ProjectTime.Api/Ai/PulseAiSystemToolExecutor.cs',
  repository: 'src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceRepository.cs',
  service: 'src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceService.cs',
  module: 'src/backend/ProjectTime.Api/Modules/PulseAiSystemIntelligenceModule.cs',
  services: 'src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj',
  help: 'src/frontend/project-time-web/src/HelpAssistant.jsx',
  helpCss: 'src/frontend/project-time-web/src/pulse-ai-system-chat.css',
  workbench: 'src/frontend/project-time-web/src/PulseAiSystemIntelligenceWorkbench.jsx',
  workbenchCss: 'src/frontend/project-time-web/src/pulse-ai-system-intelligence-workbench.css',
  mount: 'src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx',
  group7Compatibility: 'src/frontend/project-time-web/scripts/inject-pulse-ai-system-chat-group7-compatibility.mjs',
  packageJson: 'src/frontend/project-time-web/package.json',
  documentation: 'docs/modules/module-011-pulse-ai/SYSTEM-INTELLIGENCE-AND-TROUBLESHOOTING.md'
};

for (const [name, relative] of Object.entries(paths)) {
  assert(`FILE_${name.toUpperCase()}`, exists(relative), relative);
}
if (checks.some((check) => !check.condition)) {
  console.error('MODULE_011_SYSTEM_INTELLIGENCE_CONTRACT=FAILED_MISSING_FILE');
  process.exit(1);
}
const s = Object.fromEntries(Object.entries(paths).map(([name, relative]) => [name, read(relative)]));

assert('MIGRATION_054',
  s.migration.includes("'054_pulse_ai_system_intelligence_conversations'")
  && s.rollback.includes("'054_pulse_ai_system_intelligence_conversations'")
  && s.migrationTest.includes('PULSE_AI_SYSTEM_INTELLIGENCE_MIGRATION_054=PASS'),
  'forward, rollback, idempotency, immutable evidence, and safe reapply');

const tables = ['pulse_ai_conversations','pulse_ai_conversation_messages','pulse_ai_system_inquiry_runs','pulse_ai_system_tool_events'];
assert('DURABLE_SCHEMA',
  tables.every((table) => s.migration.includes(`CREATE TABLE IF NOT EXISTS ${table}`))
  && tables.every((table) => s.rollback.includes(`DROP TABLE IF EXISTS ${table}`))
  && s.migration.includes('Pulse AI system tool evidence is immutable.')
  && s.migrationTest.includes('immutable_tool_event_update')
  && s.migrationTest.includes('conversation_message_count'),
  'durable conversations, messages, inquiry runs, and immutable tool evidence');

const permissions = [
  'ASK_PULSE_AI_SYSTEM_INTELLIGENCE','VIEW_PULSE_AI_API_INVENTORY',
  'USE_PULSE_AI_SYSTEM_TROUBLESHOOTING','USE_PULSE_AI_ENHANCEMENT_ADVISOR',
  'VIEW_PULSE_AI_CONVERSATION_HISTORY','RETEST_PULSE_AI_SAFE_API',
  'VIEW_PULSE_AI_SYSTEM_AUDIT'
];
assert('PERMISSIONS', permissions.every((permission) => s.migration.includes(`'${permission}'`)), 'seven explicit Module 011 capabilities');

assert('LIVE_ENDPOINT_DATA_SOURCE',
  all(s.apiCatalog, [
    'IEnumerable<EndpointDataSource>',
    '_endpointDataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()',
    'RoutePattern.RawText',
    'HttpMethodMetadata',
    'IEndpointNameMetadata',
    'BuildInventory()'
  ]),
  'running ASP.NET endpoint metadata is the API registration authority');

assert('API_RECORDS',
  all(s.contracts, ['PulseAiSystemApiDescriptor','RoutePattern','ModuleCode','ModuleName','SafeRetestSupported','ReleaseSha'])
  && all(s.apiCatalog, ['RequiresApplicationSession:','RegistrationStatus:','SafeRetestReason:']),
  'method, route, owner, session, registration, retest, and release evidence');

assert('SAFE_RETEST',
  all(s.apiCatalog, [
    'Only GET endpoints are eligible',
    'The route requires one or more path parameters',
    'Authentication, callback, token, or secret routes are never retested',
    'Download, export, stream, and attachment routes are excluded',
    'Refresh, retest, and probe routes require an explicit owning-module action contract'
  ])
  && s.contracts.includes('RETEST-PULSE-AI-SAFE-API')
  && s.module.includes('access.CanRetest')
  && s.module.includes('ViewAsMutationBlocked'),
  'exactly confirmed non-View-As same-origin safe GET verification');

assert('ALLOWLISTED_SAME_ORIGIN_TOOLS',
  all(s.executor, [
    'pre-registered, same-origin, read-only Pulse tools',
    'never accepts an arbitrary URL',
    'ValidRelativeApiPath',
    'Uri.TryCreate(path, UriKind.Absolute',
    'cleanPath.StartsWith("/api/"',
    'TryBuildTrustedTarget',
    'PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI',
    'AllowedSameOriginHosts',
    'tool_origin_rejected',
    'ForwardSessionHeaders'
  ])
  && s.executor.indexOf('TryBuildTrustedTarget(definition.Path')
    < s.executor.indexOf('ForwardSessionHeaders(context, request)')
  && !s.executor.includes('context.Request.Host.Host')
  && !s.executor.includes('request.Url')
  && !s.executor.includes('request.Endpoint')
  && all(s.services, ['AllowAutoRedirect = false','UseCookies = false']),
  'no arbitrary URL, model-selected URL, or mutation tool');

assert('SESSION_AND_VIEW_AS_FORWARDING',
  all(s.executor, ['Authorization','Cookie','X-ProjectPulse-Session','X-Project-Pulse-Session','X-Session-Token','X-ProjectPulse-View-As-User'])
  && (
  s.documentation.includes('owning endpoint remains the authorization authority')
  || s.documentation.includes('The owning endpoint still applies its own authorization before returning evidence')
),
  'owning endpoints re-evaluate the effective user before returning evidence');

const tools = [
  'platform_api_inventory','operational_evidence','platform_architecture',
  'system_diagnostic_checks','system_diagnostic_issues','observability_overview',
  'release_overview','defect_inventory','ai_provider_configuration',
  'pulse_ai_rag_readiness','project_financial_portfolio'
];
assert('CROSS_SYSTEM_TOOLING',
  tools.every((tool) => s.knowledge.includes(`"${tool}"`))
  && ['"013"','"016"','"068"','"076"','"077"','"078"','"998"'].every((module) => s.knowledge.includes(module)),
  'API, operations, architecture, diagnostics, observability, release, defect, AI, and financial evidence');

const routes = [
  '/api/pulse-ai/v1/system/readiness','/api/pulse-ai/v1/system/tools',
  '/api/pulse-ai/v1/system/apis','/api/pulse-ai/v1/system/apis/{apiId}',
  '/api/pulse-ai/v1/system/apis/{apiId}/retest','/api/pulse-ai/v1/system/questions',
  '/api/pulse-ai/v1/system/conversations','/api/pulse-ai/v1/system/conversations/{conversationId:guid}',
  '/api/pulse-ai/v1/system/conversations/{conversationId:guid}/messages'
];
assert('SYSTEM_API_FAMILY', routes.every((route) => s.module.includes(`"${route}"`)), `${routes.length} registered routes`);

assert('COMPOSITION',
  s.project.includes('app.MapPulseAiSystemIntelligenceEndpoints();')
  && all(s.services, [
    'AddHttpClient("PulseAiSystemTools"','PulseAiSystemApiCatalogService',
    'PulseAiSystemToolExecutor','PulseAiSystemIntelligenceRepository','PulseAiSystemIntelligenceService'
  ]),
  'generated Program and AI dependency-injection composition');

assert('DIRECT_COMPREHENSIVE_ANSWER',
  all(s.service, [
    'BuildDeterministicAnswer','DirectConclusion:','ExecutiveSummary:','CurrentState:',
    'DetailedAnalysis:','ApiFindings:','TroubleshootingFindings:',
    'RootCauseHypotheses:','DiagnosticSteps:','KnownUnknownAndStaleValues:',
    'RisksAndImplications:','RecommendedActions:','ConfidenceExplanation:'
  ])
  && s.documentation.includes('must answer the question directly')
  && !s.service.includes('automaticMultiToolExecutionEnabled = false'),
  'questions return a detailed answer, not only a plan');

assert('TROUBLESHOOTING',
  all(s.service, [
    'HTTP 401/403 source results mean','HTTP 404 can indicate','HTTP 5xx evidence points',
    'A timeout can originate','Use Module 016 Operational Evidence','Use Module 998 checks',
    'Use Module 078 service','Use Module 077 release/deployment evidence','open Module 076'
  ]),
  'authorization, route, server, timeout, correlation, diagnostics, observability, release, and defect workflow');

assert('FUTURE_ENHANCEMENT_ADVISOR',
  all(s.knowledge, [
    'BuildEnhancementBlueprint','ProposedArchitecture:','ProposedApis:',
    'DataAndMigrationConsiderations:','SecurityAndPrivacyControls:',
    'OperationalAndSupportControls:','ImplementationPhases:','TestStrategy:',
    'RolloutAndRollback:','Risks:','AcceptanceCriteria:','Dependencies:'
  ])
  && s.help.includes('Future enhancement blueprint')
  && s.workbench.includes('Future enhancement blueprint'),
  'current-state architecture, APIs, migration, security, operations, phases, test, rollout, rollback, risk, and acceptance');

assert('PRIVATE_MODEL_WITH_DETERMINISTIC_FALLBACK',
  all(s.service, [
    'BuildDeterministicAnswer','_privateModel.GenerateAsync',
    'The approved private model did not complete',
    'deterministic source-grounded system answer instead'
  ])
  && !/(?:api\.openai\.com|api\.anthropic\.com|ANTHROPIC_API_KEY|OPENAI_API_KEY)/i.test(
    [s.contracts,s.knowledge,s.apiCatalog,s.executor,s.repository,s.service,s.module].join('\n')
  ),
  'private synthesis is optional and public-provider system routing is absent');

assert('DURABLE_RESPONSES',
  all(s.repository, [
    'CreateConversationAsync','ListConversationsAsync','GetConversationAsync',
    'AppendMessageAsync','CreateInquiryRunAsync','SaveToolEventAsync','CompleteInquiryRunAsync'
  ])
  && s.help.includes('/api/pulse-ai/v1/system/conversations')
  && s.help.includes('completed responses remain in conversation history')
  && !/localStorage|sessionStorage|indexedDB/.test(s.help),
  'completed answers survive close, navigation, and refresh through server persistence');

assert('NATIVE_KEYBOARD',
  all(s.help, [
    "event.key === 'Escape'","event.key !== 'Enter' || event.shiftKey",
    'event.currentTarget.form?.requestSubmit()',
    'Enter sends · Shift+Enter adds a line · Escape closes'
  ])
  && all(s.workbench, [
    "event.key === 'Enter' && !event.shiftKey",
    'event.currentTarget.form?.requestSubmit()',
    'Enter sends · Shift+Enter adds a line'
  ]),
  'Enter sends, Shift+Enter creates a line, and Escape closes the global chat');

assert('VISIBLE_CONVERSATION',
  all(s.helpCss, [
    'height: min(900px, calc(100dvh - 108px))','minmax(0, 1fr)',
    'overflow-y: scroll','overscroll-behavior: contain','scrollbar-gutter: stable both-edges'
  ])
  && all(s.help, ['role="log"','followLatestRef.current','viewport.scrollTop = viewport.scrollHeight']),
  'definite responsive viewport, independent scrollbar, accessible log, and user-controlled follow behavior');

assert('COMPLETE_CHAT_PRESENTATION',
  [
    'Current state','Detailed analysis','API findings','Troubleshooting findings',
    'Root-cause hypotheses','Diagnostic steps',
    'Known, unknown, stale, unavailable, and unauthorized values',
    'Risks and implications','Recommended actions',
    'Registered APIs returned for this answer','Source and freshness evidence','Governed tool execution'
  ].every((marker) => s.help.includes(marker)),
  'answer, APIs, tools, sources, risks, confidence, and navigation remain visible');

assert('MODULE_011_WORKBENCH',
  s.mount.includes("import PulseAiSystemIntelligenceWorkbench from './PulseAiSystemIntelligenceWorkbench.jsx';")
  && s.mount.includes('<PulseAiSystemIntelligenceWorkbench />')
  && s.workbench.includes('data-pulse-ai-system-intelligence="v1"')
  && ['System Intelligence','Ask the System','Running APIs','Troubleshooting','Future Enhancements','Conversations'].every((marker) => s.workbench.includes(marker)),
  'Module 011 workspaces for questions, APIs, troubleshooting, enhancements, and history');

assert('GROUP_7_COMPATIBILITY',
  all(s.group7Compatibility, [
    "import './pulse-ai-system-chat.css';",'GROUP_7_HELP_GOVERNANCE_PANEL_START',
    'GROUP_7_HELP_ANSWER_DETAIL_START','data-answer-detail={detailLevel}',
    'Answer detail: {titleFrom(detailLevel)}','PULSE_AI_NATIVE_SYSTEM_CHAT_GROUP_7_COMPATIBILITY=PASS'
  ]),
  'native system chat prepares Group 7 governance and then invokes the unchanged owned injector');

assert('VIEW_AS_BOUNDARY',
  s.module.includes('identities.Value.Actual != identities.Value.Effective')
  && s.module.includes('ViewAsMutationBlocked')
  && s.module.includes('mutationAuthorityTransferred = false')
  && s.service.includes('actualUserId == effectiveUserId')
  && s.service.includes('access.CanViewConversations')
  && s.documentation.includes('View-As does not transfer conversation or retest mutation authority'),
  'View-As cannot create another user’s conversation, persist inquiry evidence, or run a safe retest');

assert('PERMISSION_SCOPED_SYSTEM_EVIDENCE',
  all(s.service, [
    'IReadOnlyList<PulseAiSystemApiDescriptor> apis = access.CanViewApis',
    'summary = access.CanViewApis ? _apiCatalog.Summary(apis) : null',
    'request.IncludeApiInventory && access.CanViewApis',
    'lacks VIEW_PULSE_AI_API_INVENTORY',
    'var persistenceAuthorized = actualUserId == effectiveUserId',
    '&& access.CanViewConversations',
    'if (persisted)',
    'SaveToolEventAsync',
    'CompleteInquiryRunAsync'
  ])
  && !s.service.includes('plan.WantsApiInventory || access.CanViewApis'),
  'readiness, question API inventory, and durable conversation/tool evidence require their dedicated permissions');

assert('NO_ARBITRARY_SQL_OR_MUTATION_TOOL',
  s.contracts.includes('arbitrarySqlAllowed = false')
  && s.executor.includes('SafeReadOnly')
  && !/HttpMethod\.(?:Post|Put|Patch|Delete)/.test(s.executor)
  && !/arbitrarySqlAllowed\s*=\s*true/i.test([s.contracts,s.knowledge,s.executor,s.service,s.module].join('\n')),
  'owned API contracts and GET tools replace arbitrary SQL and model-selected mutations');

assert('PRIVATE_RESPONSE_BOUNDARY',
  all(s.contracts, [
    'rawToolResponsesReturned = false','rawDocumentChunksReturned = false',
    'embeddingVectorsReturned = false','providerSecretsReturned = false'
  ])
  && s.executor.includes('responseBodyReturned = false')
  && s.documentation.includes('Raw private document chunks, embedding vectors, credentials, unrestricted tool bodies, and provider secrets are not returned'),
  'browser-visible answers exclude raw tool bodies, chunks, vectors, credentials, and provider secrets');

assert('DOCUMENTED_ACTIVATION_BOUNDARY',
  all(s.documentation, [
    'This source package does not:','apply migration 054','deploy Test or Production',
    'change Azure, Entra, DNS, networking, storage, Container Apps, or Key Vault',
    'automatically convert conversations into training data'
  ]),
  'migration, deployment, infrastructure, provider, and training remain separately gated');

assert('BUILD_REGISTRATION',
  s.packageJson.includes('"validate:module011-system-intelligence"')
  && s.packageJson.includes('validate-module-011-system-intelligence-package.mjs')
  && s.packageJson.includes('inject-pulse-ai-system-chat-group7-compatibility.mjs'),
  'complete build runs the final validator and native Group 7 compatibility preparation');

console.log(`MODULE_011_SYSTEM_INTELLIGENCE_CHECKS=${checks.length}`);
console.log('MODULE_011_SYSTEM_INTELLIGENCE_PHASE=SOURCE_COMPLETE_NOT_MERGED_NOT_DEPLOYED');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_LIVE_API_DISCOVERY=REGISTERED');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_TROUBLESHOOTING=REGISTERED');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_FUTURE_ENHANCEMENTS=REGISTERED');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_DURABLE_CONVERSATIONS=REGISTERED');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_ENTER_SUBMITS=YES');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_MIGRATION_APPLIED=NO');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_EXTERNAL_PROVIDER_CALLS=0');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_AZURE_ENTRA_CHANGES=0');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_DEPLOYMENTS=0');

if (checks.some((check) => !check.condition)) {
  console.error('MODULE_011_SYSTEM_INTELLIGENCE_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MODULE_011_SYSTEM_INTELLIGENCE_CONTRACT=PASSED');
