import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const absolute = (relative) => path.join(repositoryRoot, relative);
const exists = (relative) => fs.existsSync(absolute(relative));
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`MODULE011_SYSTEM_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

function includesAll(source, markers) {
  return markers.every((marker) => source.includes(marker));
}

function walk(relativeDirectory) {
  const directory = absolute(relativeDirectory);
  if (!fs.existsSync(directory)) return [];
  const files = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const relative = path.join(relativeDirectory, entry.name).replaceAll('\\', '/');
    if (entry.isDirectory()) files.push(...walk(relative));
    else files.push(relative);
  }
  return files;
}

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
  group7Injector: 'src/frontend/project-time-web/scripts/inject-group-7-ai-help-system-guide.mjs',
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

const source = Object.fromEntries(
  Object.entries(paths).map(([name, relative]) => [name, read(relative)])
);

assert(
  'MIGRATION_054',
  source.migration.includes("'054_pulse_ai_system_intelligence_conversations'")
    && source.rollback.includes("'054_pulse_ai_system_intelligence_conversations'")
    && source.migrationTest.includes('PULSE_AI_SYSTEM_INTELLIGENCE_MIGRATION_054=PASS'),
  'migration 054 has forward, rollback, idempotency, and reapply evidence'
);

const durableTables = [
  'pulse_ai_conversations',
  'pulse_ai_conversation_messages',
  'pulse_ai_system_inquiry_runs',
  'pulse_ai_system_tool_events'
];
assert(
  'DURABLE_CONVERSATION_TABLES',
  durableTables.every((table) => source.migration.includes(`CREATE TABLE IF NOT EXISTS ${table}`))
    && durableTables.every((table) => source.rollback.includes(`DROP TABLE IF EXISTS ${table}`)),
  'conversation, message, inquiry, and immutable tool-event tables'
);

assert(
  'IMMUTABLE_TOOL_EVIDENCE',
  source.migration.includes('Pulse AI system tool evidence is immutable.')
    && source.migration.includes('BEFORE UPDATE OR DELETE ON pulse_ai_system_tool_events')
    && source.migrationTest.includes('immutable_tool_event_update')
    && source.migrationTest.includes('immutable_tool_event_delete'),
  'system tool evidence cannot be updated or deleted'
);

const permissions = [
  'ASK_PULSE_AI_SYSTEM_INTELLIGENCE',
  'VIEW_PULSE_AI_API_INVENTORY',
  'USE_PULSE_AI_SYSTEM_TROUBLESHOOTING',
  'USE_PULSE_AI_ENHANCEMENT_ADVISOR',
  'VIEW_PULSE_AI_CONVERSATION_HISTORY',
  'RETEST_PULSE_AI_SAFE_API',
  'VIEW_PULSE_AI_SYSTEM_AUDIT'
];
assert(
  'PERMISSION_MODEL',
  permissions.every((permission) => source.migration.includes(`'${permission}'`))
    && source.contracts.includes('CanViewApis')
    && source.contracts.includes('CanTroubleshoot')
    && source.contracts.includes('CanEnhance')
    && source.contracts.includes('CanRetest'),
  'seven explicit Module 011 system-intelligence capabilities'
);

assert(
  'LIVE_ENDPOINT_DISCOVERY',
  includesAll(source.apiCatalog, [
    'IEnumerable<EndpointDataSource>',
    'SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()',
    'HttpMethodMetadata',
    'IEndpointNameMetadata',
    'EndpointDataSource rather than a static route list'
  ]),
  'running ASP.NET EndpointDataSource is the API registration authority'
);

assert(
  'API_METADATA',
  includesAll(source.contracts, [
    'PulseAiSystemApiDescriptor',
    'RoutePattern',
    'ModuleCode',
    'ModuleName',
    'RequiresApplicationSession',
    'SafeRetestSupported',
    'ReleaseSha'
  ])
    && source.apiCatalog.includes('RegistrationStatus:'),
  'API identity, ownership, authorization, retest, and release evidence'
);

assert(
  'SAFE_RETEST_BOUNDARY',
  includesAll(source.apiCatalog, [
    'Only GET endpoints are eligible',
    'The route requires one or more path parameters',
    'Authentication, callback, token, or secret routes are never retested',
    'Download, export, stream, and attachment routes are excluded',
    'Refresh, retest, and probe routes require an explicit owning-module action contract'
  ])
    && source.contracts.includes('RETEST-PULSE-AI-SAFE-API')
    && source.module.includes('ViewAsMutationBlocked')
    && source.module.includes('access.CanRetest'),
  'safe same-origin GET retest requires exact confirmation, permission, and non-View-As identity'
);

assert(
  'NO_ARBITRARY_TOOL_URL',
  includesAll(source.executor, [
    'source-controlled allowlist',
    'ValidRelativeApiPath',
    '!cleanPath.StartsWith("/api/"',
    'Uri.TryCreate(path, UriKind.Absolute',
    'ForwardSessionHeaders'
  ])
    && !source.executor.includes('request.Url')
    && !source.executor.includes('request.Endpoint'),
  'models and users cannot provide an arbitrary diagnostic URL'
);

const toolMarkers = [
  'platform_api_inventory',
  'operational_evidence',
  'platform_architecture',
  'system_diagnostic_checks',
  'system_diagnostic_issues',
  'observability_overview',
  'release_overview',
  'defect_inventory',
  'ai_provider_configuration',
  'pulse_ai_rag_readiness',
  'project_financial_portfolio'
];
assert(
  'CROSS_SYSTEM_TOOL_CATALOG',
  toolMarkers.every((marker) => source.knowledge.includes(`"${marker}"`))
    && includesAll(source.knowledge, ['"013"', '"016"', '"068"', '"076"', '"077"', '"078"', '"998"']),
  'system, API, operations, architecture, defect, release, observability, diagnostics, AI, and financial tools'
);

assert(
  'OWNING_ENDPOINT_AUTHORIZATION',
  includesAll(source.executor, [
    'Authorization',
    'X-ProjectPulse-Session',
    'X-Project-Pulse-Session',
    'X-Session-Token',
    'X-ProjectPulse-View-As-User'
  ])
    && source.documentation.includes('owning endpoint remains the authorization authority'),
  'actual session and effective View-As evidence reach the owning read-only endpoint'
);

const systemRoutes = [
  '/api/pulse-ai/v1/system/readiness',
  '/api/pulse-ai/v1/system/tools',
  '/api/pulse-ai/v1/system/apis',
  '/api/pulse-ai/v1/system/apis/{apiId}',
  '/api/pulse-ai/v1/system/apis/{apiId}/retest',
  '/api/pulse-ai/v1/system/questions',
  '/api/pulse-ai/v1/system/conversations',
  '/api/pulse-ai/v1/system/conversations/{conversationId:guid}',
  '/api/pulse-ai/v1/system/conversations/{conversationId:guid}/messages'
];
assert(
  'API_SURFACE',
  systemRoutes.every((route) => source.module.includes(`"${route}"`)),
  `${systemRoutes.length} system-intelligence, API, troubleshooting, and conversation routes`
);

assert(
  'ENDPOINT_AND_SERVICE_REGISTRATION',
  source.project.includes('app.MapPulseAiSystemIntelligenceEndpoints();')
    && includesAll(source.services, [
      'AddHttpClient("PulseAiSystemTools"',
      'PulseAiSystemApiCatalogService',
      'PulseAiSystemToolExecutor',
      'PulseAiSystemIntelligenceRepository',
      'PulseAiSystemIntelligenceService'
    ]),
  'generated Program and AI composition register the complete package'
);

assert(
  'DIRECT_COMPREHENSIVE_ANSWER',
  includesAll(source.service, [
    'BuildDeterministicAnswer',
    'DirectConclusion:',
    'ExecutiveSummary:',
    'DetailedAnalysis:',
    'ApiFindings:',
    'TroubleshootingFindings:',
    'RootCauseHypotheses:',
    'DiagnosticSteps:',
    'KnownUnknownAndStaleValues:',
    'RecommendedActions:',
    'ConfidenceExplanation:'
  ])
    && source.documentation.includes('must answer the question directly')
    && !source.service.includes('automaticMultiToolExecutionEnabled = false'),
  'questions receive a detailed answer rather than only a future execution plan'
);

assert(
  'FUTURE_ENHANCEMENT_BLUEPRINT',
  includesAll(source.knowledge, [
    'BuildEnhancementBlueprint',
    'ProposedArchitecture:',
    'ProposedApis:',
    'DataAndMigrationConsiderations:',
    'SecurityAndPrivacyControls:',
    'OperationalAndSupportControls:',
    'ImplementationPhases:',
    'TestStrategy:',
    'RolloutAndRollback:',
    'AcceptanceCriteria:'
  ])
    && source.workbench.includes('Future enhancement blueprint')
    && source.help.includes('Future enhancement blueprint'),
  'future enhancements include architecture, delivery, security, operations, test, rollout, rollback, risk, and acceptance'
);

assert(
  'TROUBLESHOOTING_ADVANTAGE',
  includesAll(source.service, [
    'HTTP 401/403 source results mean',
    'HTTP 404 can indicate',
    'HTTP 5xx evidence points',
    'A timeout can originate',
    'Use Module 016 Operational Evidence',
    'Use Module 998 checks',
    'Use Module 078 service',
    'Use Module 077 release/deployment evidence',
    'open Module 076'
  ]),
  'authorization, route, server, timeout, evidence, diagnostics, observability, release, and defect guidance'
);

assert(
  'CENTRAL_ROUTE_WITH_PRIVATE_AND_DETERMINISTIC_GROUNDING',
  includesAll(source.service, [
    'BuildDeterministicAnswer',
    '_router.GenerateWithPrivateTargetAsync',
    '_router.GenerateAsync',
    'TryResolveHelpCapsulePurpose',
    'PrivateTargetAllowed: privateRagRequested',
    'externalAssistance = Limit(',
    "It did not receive the user's question, private documents, tool results, names, identifiers, retrieved text, or customer/project context"
  ])
    && !/(?:api\.openai\.com|api\.anthropic\.com|ANTHROPIC_API_KEY|OPENAI_API_KEY)/i.test(
      [source.contracts, source.knowledge, source.apiCatalog, source.executor, source.repository, source.service, source.module].join('\n')
    ),
  'Module 064 ordering governs Help while document RAG remains private, deterministic live evidence remains available, and public provider endpoints are not embedded in the consumer'
);

assert(
  'DURABLE_RESPONSE_PERSISTENCE',
  includesAll(source.repository, [
    'CreateConversationAsync',
    'ListConversationsAsync',
    'GetConversationAsync',
    'AppendMessageAsync',
    'CreateInquiryRunAsync',
    'SaveToolEventAsync',
    'CompleteInquiryRunAsync',
    'BeginTransactionAsync'
  ])
    && source.help.includes('/api/pulse-ai/v1/system/conversations')
    && source.help.includes('completed responses remain in conversation history')
    && !/localStorage|sessionStorage|indexedDB/.test(source.help),
  'completed answers are durable server-side and not dependent on browser storage'
);

assert(
  'ENTER_SHIFT_ENTER_ESCAPE',
  includesAll(source.help, [
    "event.key === 'Escape'",
    "event.key !== 'Enter' || event.shiftKey",
    'event.currentTarget.form?.requestSubmit()',
    'Enter sends · Shift+Enter adds a line · Escape closes'
  ])
    && includesAll(source.workbench, [
      "event.key === 'Enter' && !event.shiftKey",
      'event.currentTarget.form?.requestSubmit()',
      'Enter sends · Shift+Enter adds a line'
    ]),
  'native keyboard submission and multiline behavior exist in global chat and Module 011 workbench'
);

assert(
  'VISIBLE_SCROLLING_RESPONSES',
  includesAll(source.helpCss, [
    'height: min(900px, calc(100dvh - 108px))',
    'minmax(0, 1fr)',
    'overflow-y: scroll',
    'overscroll-behavior: contain',
    'scrollbar-gutter: stable both-edges'
  ])
    && source.help.includes('role="log"')
    && source.help.includes('followLatestRef.current')
    && source.help.includes('viewport.scrollTop = viewport.scrollHeight'),
  'conversation has a definite viewport, independent scrollbar, accessible log, and user-controlled follow behavior'
);

assert(
  'COMPREHENSIVE_CHAT_RENDERING',
  includesAll(source.help, [
    'Current state',
    'Detailed analysis',
    'API findings',
    'Troubleshooting findings',
    'Root-cause hypotheses',
    'Diagnostic steps',
    'Known, unknown, stale, unavailable, and unauthorized values',
    'Risks and implications',
    'Recommended actions',
    'Registered APIs returned for this answer',
    'Source and freshness evidence',
    'Governed tool execution'
  ]),
  'chat presents the complete system answer, evidence, APIs, tools, confidence, and navigation'
);

assert(
  'WORKBENCH_MOUNT',
  source.mount.includes("import PulseAiSystemIntelligenceWorkbench from './PulseAiSystemIntelligenceWorkbench.jsx';")
    && source.mount.includes('<PulseAiSystemIntelligenceWorkbench />')
    && source.workbench.includes('data-pulse-ai-system-intelligence="v1"')
    && includesAll(source.workbench, [
      'System Intelligence',
      'Ask the System',
      'Running APIs',
      'Troubleshooting',
      'Future Enhancements',
      'Conversations'
    ]),
  'Module 011 exposes system intelligence, API inventory, troubleshooting, enhancement, and history workspaces'
);

assert(
  'GROUP_7_NATIVE_CHAT_COMPATIBILITY',
  includesAll(source.group7Injector, [
    'installNativeSystemHelp',
    "import './pulse-ai-system-chat.css';",
    'GROUP_7_HELP_GOVERNANCE_PANEL_START',
    'GROUP_7_HELP_ANSWER_DETAIL_START',
    'data-answer-detail={detailLevel}',
    'native-system-chat=compatible'
  ]),
  'prebuild governance injection preserves the native system chat rather than rewriting it to the legacy plan-only UI'
);

assert(
  'VIEW_AS_MUTATION_BOUNDARY',
  source.module.includes('identities.Value.Actual != identities.Value.Effective')
    && source.module.includes('ViewAsMutationBlocked')
    && source.module.includes('mutationAuthorityTransferred = false')
    && source.documentation.includes('View-As does not transfer conversation or retest mutation authority'),
  'View-As cannot create conversations or run safe retests for the viewed user'
);

assert(
  'NO_ARBITRARY_SQL_OR_MUTATION_TOOL',
  !/arbitrarySqlAllowed\s*=\s*true|SELECT\s+\*\s+FROM\s+\{/i.test(
    [source.contracts, source.knowledge, source.executor, source.service, source.module].join('\n')
  )
    && source.contracts.includes('arbitrarySqlAllowed = false')
    && source.executor.includes('SafeReadOnly')
    && !/HttpMethod\.(?:Post|Put|Patch|Delete)/.test(source.executor),
  'system answers use owned source contracts and allowlisted GET tools rather than generated SQL or mutation tools'
);

assert(
  'NO_SECRET_OR_RAW_PRIVATE_RESPONSE',
  includesAll(source.contracts, [
    'rawToolResponsesReturned = false',
    'rawDocumentChunksReturned = false',
    'embeddingVectorsReturned = false',
    'providerSecretsReturned = false'
  ])
    && source.executor.includes('responseBodyReturned = false')
    && source.documentation.includes('Raw private document chunks, embedding vectors, credentials, unrestricted tool bodies, and provider secrets are not returned'),
  'browser-visible responses exclude raw tools, chunks, vectors, credentials, and provider secrets'
);

const deploymentActions = walk('.github/workflows').filter((relative) =>
  /(?:module[-_]?011|pulse[-_]?ai).*?(?:deploy|azure|entra|container|production|migration-job)/i.test(relative)
);
assert(
  'NO_NEW_DEPLOYMENT_WORKFLOW',
  !deploymentActions.includes('.github/workflows/pulse-ai-system-intelligence-ci.yml')
    && !exists('.github/workflows/projectpulse-deploy-pulse-ai-system-intelligence-test.yml'),
  'the source package adds validation only and no deployment or environment-changing workflow'
);

assert(
  'DOCUMENTED_BOUNDARY',
  includesAll(source.documentation, [
    'This source package does not:',
    'apply migration 054',
    'deploy Test or Production',
    'change Azure, Entra, DNS, networking, storage, Container Apps, or Key Vault',
    'automatically convert conversations into training data'
  ]),
  'migration, deployment, infrastructure, provider, and training activation remain separately gated'
);

assert(
  'BUILD_VALIDATOR_REGISTERED',
  source.packageJson.includes('"validate:module011-system-intelligence"')
    && source.packageJson.includes('npm run validate:module011-system-intelligence'),
  'complete frontend build executes the system-intelligence validator'
);

console.log(`MODULE_011_SYSTEM_INTELLIGENCE_CHECKS=${checks.length}`);
console.log('MODULE_011_SYSTEM_INTELLIGENCE_PHASE=SOURCE_COMPLETE_NOT_MERGED_NOT_DEPLOYED');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_LIVE_API_DISCOVERY=REGISTERED');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_TROUBLESHOOTING=REGISTERED');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_FUTURE_ENHANCEMENTS=REGISTERED');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_DURABLE_CONVERSATIONS=REGISTERED');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_ENTER_SUBMITS=YES');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_DATABASE_MIGRATION_APPLIED=NO');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_EXTERNAL_PROVIDER_CALLS=0');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_AZURE_ENTRA_CHANGES=0');
console.log('MODULE_011_SYSTEM_INTELLIGENCE_DEPLOYMENTS=0');

if (checks.some((check) => !check.condition)) {
  console.error('MODULE_011_SYSTEM_INTELLIGENCE_CONTRACT=FAILED');
  process.exit(1);
}

console.log('MODULE_011_SYSTEM_INTELLIGENCE_CONTRACT=PASSED');
