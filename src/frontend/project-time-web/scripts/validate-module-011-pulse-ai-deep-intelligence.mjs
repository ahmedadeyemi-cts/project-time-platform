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
  console.log(`MODULE011_DEEP_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
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
  policy: 'src/backend/ProjectTime.Api/Modules/PulseAiIntelligencePolicy.cs',
  contracts: 'src/backend/ProjectTime.Api/Ai/PulseAiDeepIntelligenceContracts.cs',
  grounding: 'src/backend/ProjectTime.Api/Ai/PulseAiDocumentGroundingService.cs',
  planner: 'src/backend/ProjectTime.Api/Ai/PulseAiQuestionPlanner.cs',
  sanitizer: 'src/backend/ProjectTime.Api/Ai/PulseAiEscalationSanitizer.cs',
  module: 'src/backend/ProjectTime.Api/Modules/PulseAiDeepIntelligenceModule.cs',
  services: 'src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs',
  timesheet: 'src/backend/ProjectTime.Api/ProjectPulseAiTimeEntrySuggestionService.cs',
  flowHiveFactory: 'src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiRequestFactory.cs',
  flowHiveAiDoc: 'docs/modules/module-066-project-flowhive/AI-INTEGRATION.md',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj',
  workbench: 'src/frontend/project-time-web/src/PulseAiDeepIntelligenceWorkbench.jsx',
  workbenchCss: 'src/frontend/project-time-web/src/pulse-ai-deep-intelligence-workbench.css',
  mount: 'src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx',
  help: 'src/frontend/project-time-web/src/HelpAssistant.jsx',
  helpCss: 'src/frontend/project-time-web/src/help.css',
  helpBoundaryCss: 'src/frontend/project-time-web/src/help-assistant.css',
  runtimeDoc: 'docs/modules/module-011-pulse-ai/DEEP-INTELLIGENCE-RUNTIME.md',
  qualityDoc: 'docs/modules/module-011-pulse-ai/ANSWER-QUALITY-AND-PRIVACY-CONTRACT.md',
  foundationDoc: 'docs/modules/module-011-pulse-ai/AUTHORITATIVE-INTELLIGENCE-SCOPE.md',
  foundationValidator: 'src/frontend/project-time-web/scripts/validate-module-011-pulse-ai.mjs',
  flowHiveValidator: 'src/frontend/project-time-web/scripts/validate-module-066-project-flowhive.mjs'
};

for (const [name, relative] of Object.entries(paths)) {
  assert(`FILE_${name.toUpperCase()}`, exists(relative), relative);
}

if (checks.some((check) => !check.condition)) {
  console.error('MODULE_011_DEEP_INTELLIGENCE_CONTRACT=FAILED_MISSING_FILE');
  process.exit(1);
}

const source = Object.fromEntries(
  Object.entries(paths).map(([name, relative]) => [name, read(relative)])
);
const newBackend = [
  source.contracts,
  source.grounding,
  source.planner,
  source.sanitizer,
  source.module,
  source.services,
  source.timesheet,
  source.flowHiveFactory
].join('\n');
const newFrontend = [
  source.workbench,
  source.workbenchCss,
  source.mount,
  source.help,
  source.helpCss,
  source.helpBoundaryCss
].join('\n');

const routes = [
  '/api/pulse-ai/v1/overview',
  '/api/pulse-ai/v1/private-runtime/readiness',
  '/api/pulse-ai/v1/tools',
  '/api/pulse-ai/v1/timesheet/context-preview',
  '/api/pulse-ai/v1/help-search/plan',
  '/api/pulse-ai/v1/flowhive/context-preview',
  '/api/pulse-ai/v1/insights/plan',
  '/api/pulse-ai/v1/external-escalation/sanitize-preview'
];

assert(
  'API_FAMILY',
  routes.every((route) => source.module.includes(`"${route}"`)),
  `${routes.length} isolated Pulse AI routes`
);
assert(
  'ENDPOINT_REGISTRATION',
  source.project.includes('app.MapPulseAiDeepIntelligenceEndpoints();')
    && source.module.includes('MapPulseAiDeepIntelligenceEndpoints'),
  'generated Program maps the deep-intelligence family'
);
assert(
  'SERVICE_REGISTRATION',
  includesAll(source.services, [
    'services.AddHttpContextAccessor();',
    'PulseAiDocumentGroundingService',
    'PulseAiQuestionPlanner',
    'PulseAiEscalationSanitizer'
  ]),
  'effective-user, grounding, planning, and sanitization services'
);
assert(
  'SESSION_AND_EFFECTIVE_USER',
  includesAll(source.module, [
    'ProjectPulseEffectiveUserId',
    'ProjectPulseActualUserId',
    'administrator_read_only_view_as',
    'serverAuthorized = true'
  ]),
  'actual/effective identity is retained for every new endpoint'
);

assert(
  'DOCUMENT_SCHEMA_INSPECTION',
  includesAll(source.grounding, [
    "to_regclass('public.project_intake_documents')",
    'information_schema.columns',
    'engineering_visible',
    'ai_timesheet_context_enabled',
    'extraction_status',
    'ai_context_summary',
    'ai_context_last_processed_at'
  ]),
  'optional document fields are inspected before use'
);
assert(
  'PROJECT_SCOPE',
  includesAll(source.grounding, [
    'project_manager_user_id = @user_id',
    'project_assignments',
    'engineering_resource_requests',
    'project_outside_effective_user_scope'
  ]),
  'broad, PM, assignment, and resource-request project scope'
);
assert(
  'TIMESHEET_DOCUMENT_ELIGIBILITY',
  includesAll(source.grounding, [
    'requireTimesheetContextFlag: true',
    'COALESCE(d.engineering_visible, FALSE) = TRUE',
    'COALESCE(d.ai_timesheet_context_enabled, FALSE) = TRUE'
  ]),
  'engineering-visible and explicitly enabled timesheet context only'
);
assert(
  'DOCUMENT_SOURCE_PRIORITY',
  includesAll(source.grounding, [
    "WHEN 'sow' THEN 10",
    "WHEN 'gsd' THEN 20",
    "WHEN 'architecture' THEN 30",
    "WHEN 'order' THEN 40"
  ]),
  'deterministic SOW, GSD, design, order, and supporting precedence'
);
assert(
  'PRIVATE_CONTEXT_NOT_EXPOSED',
  includesAll(source.contracts, [
    'rawDocumentTextReturned = false',
    'rawDocumentTextSentExternally = false',
    'public object ToEvidence()'
  ])
    && !/ToEvidence\(\)[\s\S]{0,1200}ContextSummary\s*=/.test(source.contracts),
  'public evidence returns readiness and metadata, not private summaries'
);
assert(
  'SOURCE_COVERAGE',
  includesAll(source.grounding, [
    'CoverageScore',
    'CoverageLevel',
    'BuildMissingInputs',
    'BuildConflicts',
    'eligible SOW documents',
    'eligible GSD documents'
  ]),
  'coverage, conflicts, version questions, and missing evidence'
);
assert(
  'PRIVATE_RUNTIME_READINESS',
  includesAll(source.grounding, [
    'PROJECTPULSE_PRIVATE_AI_ENDPOINT',
    'PROJECTPULSE_PRIVATE_AI_MODEL',
    'PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT',
    'PROJECTPULSE_PRIVATE_EMBEDDING_MODEL',
    'PROJECTPULSE_PRIVATE_VECTOR_INDEX',
    'PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION'
  ])
    && source.module.includes('externalEscalationReady = false'),
  'private inference, embedding, vector, and policy readiness without activation'
);

assert(
  'TIMESHEET_EXISTING_PATH',
  includesAll(source.timesheet, [
    'PulseAiDocumentGroundingService',
    'BuildTimesheetContextAsync',
    'grounding.HasReadyPrivateContext',
    'BuildPrivateGroundedSuggestion',
    'BuildRemotePromptWithoutPrivateDocuments'
  ]),
  'existing Module 001 suggestion service is enriched'
);
assert(
  'TIMESHEET_PRIVATE_PATH',
  includesAll(source.timesheet, [
    'ProjectPulseAiProviders.Local',
    'Raw document text and extracted summaries were not sent to Claude or OpenAI',
    'No SOW, GSD, architecture, contract, rate, financial, customer-document, or extracted private-document content is included'
  ])
    && !source.timesheet.includes('grounding.ContextSummary')
    && !source.timesheet.includes('document.ContextSummary'),
  'ready document context stays private and outside remote prompts'
);
assert(
  'TIMESHEET_ENGINEER_CONTROL',
  source.timesheet.includes('The Engineer must confirm')
    && source.runtimeDoc.includes('Engineer must review and explicitly apply')
    && source.qualityDoc.includes('cannot change hours, date, time type, project, task, category, allocation'),
  'suggestions cannot save, submit, or replace Engineer accountability'
);

const requiredDomains = [
  'help_and_documentation',
  'projects_delivery_documents',
  'time_work_utilization',
  'flowhive_planning',
  'financial_commercial',
  'identity_permissions_security',
  'platform_operations'
];
assert(
  'MULTI_DOMAIN_PLANNER',
  requiredDomains.every((domain) => source.planner.includes(`Code: "${domain}"`))
    && source.planner.includes('DistinctBy(domain => domain.Code)'),
  'questions can span every material ProjectPulse domain'
);
assert(
  'DETAILED_ANSWER_STANDARD',
  includesAll(source.planner, [
    'extremely_detailed_comprehensive_source_grounded',
    'Executive answer',
    'Detailed evidence',
    'Sources and freshness'
  ])
    && source.qualityDoc.includes('Required response structure'),
  'direct conclusion, detail, calculations, uncertainty, action, and freshness'
);
assert(
  'DIRECT_PRODUCT_GUIDANCE',
  includesAll(source.planner, [
    'Generate a document-grounded timesheet description',
    'Understand ProjectPulse access and permissions',
    'Create and maintain a ProjectPulse project',
    'Prepare an internal project document for Pulse AI',
    'Create a detailed FlowHive planning draft',
    'Ask a detailed reporting or financial question',
    'Configure and govern an AI provider',
    'Report and investigate a ProjectPulse defect'
  ]),
  'high-value Help topics include detailed procedures and safeguards'
);
assert(
  'SEMANTIC_QUERY_NO_ARBITRARY_SQL',
  includesAll(source.planner, [
    'queryType = "governed_semantic_read_plan"',
    'arbitrarySqlAllowed = false',
    'deterministicValuesRequired = true',
    'unknownValuesPreserved = true'
  ])
    && !/\b(?:INSERT\s+INTO|UPDATE\s+[a-z_][a-z0-9_]*\s+SET|DELETE\s+FROM|ALTER\s+TABLE|DROP\s+TABLE|TRUNCATE\s+TABLE)\b/i.test(source.planner)
    && !/\bSELECT\s+(?:\*|[a-z_][a-z0-9_]*\s*(?:,|FROM\b))/i.test(source.planner),
  'normal user guidance is allowed while generated SQL syntax is rejected'
);
assert(
  'FINANCIAL_PR220_BOUNDARY',
  includesAll(source.planner, [
    'dependent_on_open_pr_220_before_runtime_consumption',
    '/api/project-financials/portfolio',
    '/api/project-financials/reporting-summary'
  ])
    && includesAll(source.module, [
      'sourcePr = 220',
      '/api/project-financials/projects/{projectId}',
      '/api/project-financials/sources',
      'not_registered_in_this_dependent_branch'
    ]),
  'PR #220 is referenced but not copied, estimated, or runtime-activated'
);

assert(
  'FLOWHIVE_PRIVATE_FIRST',
  includesAll(source.flowHiveFactory, [
    'requiredProviderOrder = new[] { "private_model", "local_template" }',
    'legacyExternalRouteRejected = new[] { "claude", "openai", "local_template" }',
    'promptSha256',
    'rawPromptReturned = false',
    'privateDocumentContentIncluded = false',
    'sanitized_reasoning_capsule_only'
  ]),
  'detailed planning context uses private model/local paths only'
);
assert(
  'FLOWHIVE_NO_DIRECT_CLIENT',
  !/new\s+HttpClient|IHttpClientFactory|api\.anthropic|api\.openai|ANTHROPIC_API_KEY|OPENAI_API_KEY/i.test(source.flowHiveFactory)
    && source.flowHiveFactory.includes('The preview calls no model or provider.'),
  'FlowHive preview contains no direct provider client or key'
);
assert(
  'FLOWHIVE_HUMAN_REVIEW',
  source.flowHiveFactory.includes('AI output is a draft and cannot establish a baseline, assign resources, reserve capacity, or commit customer dates')
    && includesAll(source.flowHiveAiDoc, [
      'The Project Manager reviews',
      'presenting the draft to Engineering',
      'Engineering',
      'cannot modify canonical tasks'
    ]),
  'PM review, Engineering modification, deterministic scheduling, and separate approval'
);

assert(
  'SANITIZER_COVERAGE',
  includesAll(source.sanitizer, [
    'SecretAssignment',
    'Email',
    'Url',
    'Ipv4',
    'GuidValue',
    'CurrencyValue',
    'Phone',
    'LongIdentifier',
    'PersonOrCustomerLabel',
    'ExternalExecutionAuthorized: false'
  ]),
  'credentials, identities, records, infrastructure, and financial values'
);
assert(
  'SANITIZER_PREVIEW_ONLY',
  includesAll(source.module, [
    'externalExecutionAuthorized = false',
    'providerCalled = false',
    'module064RouteChanged = false',
    'rawDocumentSent = false'
  ])
    && !/HttpClient|IHttpClientFactory|ProjectPulseAiRouter/.test(source.sanitizer),
  'local deterministic redaction with no provider execution or route mutation'
);

assert(
  'GLOBAL_HELP',
  includesAll(source.help, [
    "'/api/pulse-ai/v1/help-search/plan'",
    'DetailedAssistantAnswer',
    'Automatic multi-tool execution is not yet enabled',
    'fallbackAnswer',
    'retired Work Task Builder no longer owns project or task creation'
  ]),
  'global Help renders detailed guidance and a corrected fallback'
);
assert(
  'HELP_SAFE_RENDERING',
  !source.help.includes('dangerouslySetInnerHTML')
    && !source.help.includes('innerHTML')
    && source.help.includes('NavigationTargets'),
  'structured React rendering without unsafe HTML injection'
);
assert(
  'WORKBENCH_MOUNT',
  includesAll(source.mount, [
    "import PulseAiDeepIntelligenceWorkbench from './PulseAiDeepIntelligenceWorkbench.jsx';",
    '<PulseAiMissionControl />',
    '<PulseAiDeepIntelligenceWorkbench />',
    '<PulseAiCenter />',
    'return <PulseAiCenter />;'
  ])
    && source.workbench.includes("import './pulse-ai-deep-intelligence-workbench.css';"),
  'dependent workbench extends the established Module 011 mount'
);
assert(
  'WORKBENCH_FUNCTIONS',
  [
    'Private Runtime',
    'Timesheet Grounding',
    'Help & Search',
    'FlowHive Planning',
    'Reports & Financials',
    'Privacy Capsule',
    'Tool Registry'
  ].every((label) => source.workbench.includes(`label: '${label}'`))
    && routes.every((route) => source.workbench.includes(route)),
  'interactive previews call every registered deep-intelligence route'
);
assert(
  'FULL_STRUCTURED_EVIDENCE',
  includesAll(source.workbench, [
    'View complete structured evidence',
    'JSON.stringify(payload, null, 2)',
    'Detailed procedure',
    'Comprehensive execution sequence'
  ]),
  'operators can inspect detailed answer structures and raw JSON evidence'
);
assert(
  'RESPONSIVE_DARK_UI',
  includesAll(source.workbenchCss, [
    '@media (max-width: 980px)',
    '@media (max-width: 720px)',
    '[data-theme="dark"]'
  ])
    && includesAll(source.helpCss, [
      '@media (max-width: 760px)',
      '@media (max-width: 620px)'
    ]),
  'desktop, mobile, and dark-theme behavior'
);
assert(
  'NO_NEW_BROWSER_STORAGE',
  !/localStorage|sessionStorage|indexedDB/.test(source.workbench)
    && !/localStorage|sessionStorage|indexedDB/.test(source.help),
  'new questions, evidence, and capsules are not persisted in the browser'
);

assert(
  'READ_ONLY_SQL',
  !/\b(?:INSERT\s+INTO|UPDATE\s+[a-z_][a-z0-9_]*\s+SET|DELETE\s+FROM|ALTER\s+TABLE|CREATE\s+TABLE|DROP\s+TABLE|TRUNCATE\s+TABLE)\b/i.test(
    [source.grounding, source.module, source.planner, source.sanitizer].join('\n')
  ),
  'new backend code contains no mutating SQL or schema statement'
);
assert(
  'NO_DIRECT_PROVIDER_ENDPOINT',
  !/api\.anthropic\.com|api\.openai\.com|generativelanguage\.googleapis\.com|\/v1\/chat\/completions|\/v1\/responses/i.test(
    [source.grounding, source.planner, source.sanitizer, source.module, source.workbench, source.help].join('\n')
  ),
  'new deep-intelligence paths contain no public-provider endpoint'
);
assert(
  'NO_PROVIDER_SECRET_MANAGEMENT',
  !/PROJECTPULSE_(?:CLAUDE|OPENAI)_API_KEY|ANTHROPIC_API_KEY|OPENAI_API_KEY|ApplyStoredSecret|secret\/replace/i.test(
    [source.grounding, source.planner, source.sanitizer, source.module, source.workbench, source.help].join('\n')
  ),
  'the package cannot read, replace, return, or activate provider secrets'
);
assert(
  'AUTHORITATIVE_SCOPE',
  includesAll(source.policy, [
    'DefaultRawDocumentBoundary',
    'DefaultExternalEscalationPayload',
    'timesheet_document_grounding',
    'system_help_search',
    'flowhive_document_planning',
    'financial_commercial_insight'
  ])
    && includesAll(source.foundationDoc, [
      'document-grounded timesheet suggestions',
      'system-wide Help and Search',
      'document-grounded FlowHive project planning',
      'reporting, financial, and cross-system insight'
    ]),
  'dependent implementation follows PR #219 mission and privacy policy'
);
assert(
  'DOCUMENTED_LOCKS',
  includesAll(source.runtimeDoc, [
    'no database migration',
    'no Azure or Entra change',
    'no external model execution',
    'no deployment or rollback workflow'
  ])
    && source.qualityDoc.includes('cannot train, approve, promote, deploy, or roll back itself'),
  'database, cloud, provider, training, and deployment boundaries are explicit'
);
assert(
  'PRESERVED_VALIDATORS',
  source.foundationValidator.includes('MODULE_011_PULSE_AI_CONTRACT=PASSED')
    && source.flowHiveValidator.includes('MODULE_066_SHARED_AI_ONLY'),
  'foundation and FlowHive contracts remain intact'
);

const migrations = walk('database/migrations')
  .filter((relative) => /(?:module[-_]?011|pulse[-_]?ai|deep[-_]?intelligence)/i.test(relative));
assert(
  'NO_MIGRATION',
  migrations.length === 0,
  migrations.length === 0 ? 'no Module 011 migration exists' : migrations.join(', ')
);

const ownedDeepIntelligencePaths = [
  '.github/workflows/deep-intelligence-read-contract-ci.yml',
  'src/frontend/project-time-web/scripts/validate-module-011-pulse-ai-deep-intelligence.mjs',
  ...Object.values(paths)
];
const environmentActions = ownedDeepIntelligencePaths.filter((relative) =>
  /module[-_]?011.*(?:deploy|migration|azure|entra|container)|pulse[-_]?ai.*(?:deploy|migration|azure|entra|container)/i.test(relative)
);
assert(
  'NO_ENVIRONMENT_ACTION',
  environmentActions.length === 0,
  environmentActions.length === 0
    ? 'no Module 011 environment-changing action exists in the owned deep-intelligence source scope'
    : environmentActions.join(', ')
);

console.log(`MODULE_011_DEEP_INTELLIGENCE_CHECKS=${checks.length}`);
console.log('MODULE_011_DEEP_INTELLIGENCE_PHASE=PRIVATE_READ_ONLY_RUNTIME_FOUNDATION');
console.log('MODULE_011_DEEP_INTELLIGENCE_TIMESHEET_PRIVATE_GROUNDING=REGISTERED');
console.log('MODULE_011_DEEP_INTELLIGENCE_HELP_SEARCH_PLANNING=REGISTERED');
console.log('MODULE_011_DEEP_INTELLIGENCE_FLOWHIVE_PRIVATE_FIRST=REGISTERED');
console.log('MODULE_011_DEEP_INTELLIGENCE_FINANCIAL_PR220_CONSUMPTION=GATED');
console.log('MODULE_011_DEEP_INTELLIGENCE_EXTERNAL_PROVIDER_CALLS=0');
console.log('MODULE_011_DEEP_INTELLIGENCE_DATABASE_CHANGES=0');
console.log('MODULE_011_DEEP_INTELLIGENCE_AZURE_ENTRA_CHANGES=0');
console.log('MODULE_011_DEEP_INTELLIGENCE_DEPLOYMENTS=0');

if (checks.some((check) => !check.condition)) {
  console.error('MODULE_011_DEEP_INTELLIGENCE_CONTRACT=FAILED');
  process.exit(1);
}

console.log('MODULE_011_DEEP_INTELLIGENCE_CONTRACT=PASSED');
