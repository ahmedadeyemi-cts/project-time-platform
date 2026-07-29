import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const absolute = (relative) => path.join(repositoryRoot, relative);
const exists = (relative) => fs.existsSync(absolute(relative));
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const assertions = [];

function assert(name, condition, evidence) {
  assertions.push({ name, condition, evidence });
  console.log(`MODULE011_DEEP_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

function walk(relativeDirectory) {
  const directory = absolute(relativeDirectory);
  if (!fs.existsSync(directory)) return [];
  const results = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const relative = path.join(relativeDirectory, entry.name).replaceAll('\\', '/');
    if (entry.isDirectory()) results.push(...walk(relative));
    else results.push(relative);
  }
  return results;
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

if (assertions.some((row) => !row.condition)) {
  console.error('MODULE_011_DEEP_INTELLIGENCE_CONTRACT=FAILED_MISSING_FILE');
  process.exit(1);
}

const content = Object.fromEntries(
  Object.entries(paths).map(([name, relative]) => [name, read(relative)])
);
const backend = [
  content.contracts,
  content.grounding,
  content.planner,
  content.sanitizer,
  content.module,
  content.services,
  content.timesheet,
  content.flowHiveFactory
].join('\n');
const frontend = [
  content.workbench,
  content.workbenchCss,
  content.mount,
  content.help,
  content.helpCss,
  content.helpBoundaryCss
].join('\n');
const docs = [content.runtimeDoc, content.qualityDoc, content.foundationDoc, content.flowHiveAiDoc].join('\n');

const requiredRoutes = [
  '/api/pulse-ai/v1/overview',
  '/api/pulse-ai/v1/private-runtime/readiness',
  '/api/pulse-ai/v1/tools',
  '/api/pulse-ai/v1/timesheet/context-preview',
  '/api/pulse-ai/v1/help-search/plan',
  '/api/pulse-ai/v1/flowhive/context-preview',
  '/api/pulse-ai/v1/insights/plan',
  '/api/pulse-ai/v1/external-escalation/sanitize-preview'
];
for (const route of requiredRoutes) {
  assert(
    `ROUTE_${route.replaceAll(/[^a-z0-9]+/gi, '_').toUpperCase()}`,
    content.module.includes(`"${route}"`),
    route
  );
}

assert(
  'ENDPOINT_REGISTRATION',
  content.project.includes('app.MapPulseAiDeepIntelligenceEndpoints();')
    && content.module.includes('MapPulseAiDeepIntelligenceEndpoints'),
  'generated Program registration maps the isolated Module 011 family'
);

assert(
  'SERVICE_REGISTRATION',
  content.services.includes('services.AddHttpContextAccessor();')
    && content.services.includes('PulseAiDocumentGroundingService')
    && content.services.includes('PulseAiQuestionPlanner')
    && content.services.includes('PulseAiEscalationSanitizer'),
  'private grounding, planning, sanitization, and effective-user context are registered'
);

assert(
  'EFFECTIVE_USER_BOUNDARY',
  content.module.includes('ProjectPulseEffectiveUserId')
    && content.module.includes('ProjectPulseActualUserId')
    && content.grounding.includes('ProjectPulseEffectiveUserId') === false
    && content.grounding.includes('Guid effectiveUserId'),
  'the endpoint resolves actual/effective identity and passes only the effective user to the grounding service'
);

assert(
  'DOCUMENT_SCHEMA_INSPECTION',
  content.grounding.includes('information_schema.columns')
    && content.grounding.includes("to_regclass('public.project_intake_documents')")
    && content.grounding.includes('ai_timesheet_context_enabled')
    && content.grounding.includes('ai_context_summary')
    && content.grounding.includes('ai_context_last_processed_at'),
  'the service detects existing optional document capabilities before querying them'
);

assert(
  'PROJECT_SCOPE_ENFORCEMENT',
  content.grounding.includes('project_manager_user_id = @user_id')
    && content.grounding.includes('project_assignments')
    && content.grounding.includes('engineering_resource_requests')
    && content.grounding.includes('project_outside_effective_user_scope'),
  'project PM, assignment, resource-request, and broad-role boundaries are enforced'
);

assert(
  'TIMESHEET_DOCUMENT_FILTERS',
  content.grounding.includes('COALESCE(d.engineering_visible, FALSE) = TRUE')
    && content.grounding.includes('COALESCE(d.ai_timesheet_context_enabled, FALSE) = TRUE')
    && content.grounding.includes('requireTimesheetContextFlag: true'),
  'Module 001 grounding requires engineering visibility and explicit AI-timesheet eligibility'
);

assert(
  'DOCUMENT_PRIORITY',
  ['WHEN \'sow\' THEN 10', 'WHEN \'gsd\' THEN 20', 'WHEN \'architecture\' THEN 30', 'WHEN \'order\' THEN 40']
    .every((marker) => content.grounding.includes(marker)),
  'SOW, GSD, architecture/design, order, and supporting source precedence is deterministic'
);

assert(
  'NO_RAW_DOCUMENT_PUBLIC_RESPONSE',
  content.contracts.includes('rawDocumentTextReturned = false')
    && content.contracts.includes('rawDocumentTextSentExternally = false')
    && content.contracts.includes('public object ToEvidence()')
    && !content.contracts.match(/ToEvidence\(\)[\s\S]{0,1600}ContextSummary\s*=/),
  'public evidence contains metadata and readiness but not the private context summary'
);

assert(
  'COVERAGE_CONFLICT_AND_MISSING_EVIDENCE',
  content.grounding.includes('CoverageScore')
    && content.grounding.includes('CoverageLevel')
    && content.grounding.includes('BuildMissingInputs')
    && content.grounding.includes('BuildConflicts')
    && content.grounding.includes('eligible SOW documents')
    && content.grounding.includes('eligible GSD documents'),
  'the grounding result measures source coverage and surfaces version conflicts and missing inputs'
);

assert(
  'PRIVATE_RUNTIME_READINESS',
  [
    'PROJECTPULSE_PRIVATE_AI_ENDPOINT',
    'PROJECTPULSE_PRIVATE_AI_MODEL',
    'PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT',
    'PROJECTPULSE_PRIVATE_EMBEDDING_MODEL',
    'PROJECTPULSE_PRIVATE_VECTOR_INDEX',
    'PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION'
  ].every((name) => content.grounding.includes(name))
    && content.module.includes('externalEscalationReady = false'),
  'private model, embedding, vector, and external-policy readiness is detected without activation'
);

assert(
  'EXISTING_TIMESHEET_PATH_ENHANCED',
  content.timesheet.includes('PulseAiDocumentGroundingService')
    && content.timesheet.includes('BuildTimesheetContextAsync')
    && content.timesheet.includes('grounding.HasReadyPrivateContext')
    && content.timesheet.includes('BuildPrivateGroundedSuggestion')
    && content.timesheet.includes('BuildRemotePromptWithoutPrivateDocuments'),
  'the existing Module 001 service receives private grounding without a duplicate workflow'
);

assert(
  'PRIVATE_TIMESHEET_NO_REMOTE_DOCUMENT_CONTEXT',
  content.timesheet.includes('ProjectPulseAiProviders.Local')
    && content.timesheet.includes('Raw document text and extracted summaries were not sent to Claude or OpenAI')
    && content.timesheet.includes('No SOW, GSD, architecture, contract, rate, financial, customer-document, or extracted private-document content is included')
    && !content.timesheet.includes('grounding.Documents.Select(document => document.ContextSummary')
    && !content.timesheet.includes('grounding.ContextSummary'),
  'ready private document context never enters the Module 064 remote prompt'
);

assert(
  'TIMESHEET_ENGINEER_CONTROL',
  content.timesheet.includes('The Engineer must confirm')
    && content.runtimeDoc.includes('Engineer must review and explicitly apply')
    && content.qualityDoc.includes('cannot change hours, date, time type, project, task, category, allocation'),
  'the Engineer remains responsible for reported work, application, save, and submission'
);

assert(
  'QUESTION_MULTI_DOMAIN_PLANNING',
  ['help_and_documentation', 'projects_delivery_documents', 'time_work_utilization', 'flowhive_planning', 'financial_commercial', 'identity_permissions_security', 'platform_operations']
    .every((domain) => content.planner.includes(`Code: "${domain}"`))
    && content.planner.includes('DistinctBy(domain => domain.Code)'),
  'questions can span product, project, time, FlowHive, finance, security, and operations domains'
);

assert(
  'DETAILED_ANSWER_CONTRACT',
  content.planner.includes('extremely_detailed_comprehensive_source_grounded')
    && content.planner.includes('Executive answer')
    && content.planner.includes('Detailed evidence')
    && content.planner.includes('Sources and freshness')
    && content.qualityDoc.includes('Required response structure'),
  'answers require direct conclusions, detailed evidence, calculations, uncertainty, actions, and freshness'
);

assert(
  'PRODUCT_KNOWLEDGE_DEPTH',
  [
    'Generate a document-grounded timesheet description',
    'Understand ProjectPulse access and permissions',
    'Create and maintain a ProjectPulse project',
    'Prepare an internal project document for Pulse AI',
    'Create a detailed FlowHive planning draft',
    'Ask a detailed reporting or financial question',
    'Configure and govern an AI provider',
    'Report and investigate a ProjectPulse defect'
  ].every((marker) => content.planner.includes(marker)),
  'detailed direct guidance covers the highest-value ProjectPulse questions'
);

assert(
  'SEMANTIC_QUERY_NO_ARBITRARY_SQL',
  content.planner.includes('queryType = "governed_semantic_read_plan"')
    && content.planner.includes('arbitrarySqlAllowed = false')
    && content.planner.includes('deterministicValuesRequired = true')
    && content.planner.includes('unknownValuesPreserved = true')
    && !content.planner.match(/SELECT\s|INSERT\s|UPDATE\s|DELETE\s/i),
  'the planner creates governed metric/dimension plans rather than generated SQL'
);

assert(
  'FINANCIAL_PR220_DEPENDENCY',
  content.planner.includes('dependent_on_open_pr_220_before_runtime_consumption')
    && content.module.includes('sourcePr = 220')
    && [
      '/api/project-financials/portfolio',
      '/api/project-financials/reporting-summary',
      '/api/project-financials/projects/{projectId}',
      '/api/project-financials/sources'
    ].every((route) => content.module.includes(route))
    && content.module.includes('not_registered_in_this_dependent_branch'),
  'Pulse AI plans consumption of PR #220 without duplicating its financial calculations'
);

assert(
  'FLOWHIVE_PRIVATE_FIRST',
  content.flowHiveFactory.includes('requiredProviderOrder = new[] { "private_model", "local_template" }')
    && content.flowHiveFactory.includes('legacyExternalRouteRejected = new[] { "claude", "openai", "local_template" }')
    && content.flowHiveFactory.includes('promptSha256')
    && content.flowHiveFactory.includes('rawPromptReturned = false')
    && content.flowHiveFactory.includes('privateDocumentContentIncluded = false')
    && content.flowHiveFactory.includes('sanitized_reasoning_capsule_only'),
  'detailed FlowHive context is private and only an abstract capsule can be considered externally'
);

assert(
  'FLOWHIVE_NO_DIRECT_CLIENT',
  !/new\s+HttpClient|IHttpClientFactory|api\.anthropic|api\.openai|ANTHROPIC_API_KEY|OPENAI_API_KEY/i.test(content.flowHiveFactory)
    && content.flowHiveFactory.includes('The preview calls no model or provider.'),
  'FlowHive preview contains no provider client, key, or execution'
);

assert(
  'FLOWHIVE_HUMAN_CONTROL',
  content.flowHiveFactory.includes('cannot establish a baseline, assign resources, reserve capacity, or commit customer dates')
    && content.flowHiveAiDoc.includes('Project Manager reviews')
    && content.flowHiveAiDoc.includes('Engineering')
    && content.flowHiveAiDoc.includes('cannot modify canonical tasks'),
  'FlowHive remains draft-only with PM/Engineering review and no autonomous baseline'
);

assert(
  'SANITIZER_CATEGORIES',
  ['SecretAssignment', 'Email', 'Url', 'Ipv4', 'GuidValue', 'CurrencyValue', 'Phone', 'LongIdentifier', 'PersonOrCustomerLabel']
    .every((marker) => content.sanitizer.includes(marker))
    && content.sanitizer.includes('ExternalExecutionAuthorized: false'),
  'sanitization detects credentials, identities, infrastructure, records, and financial values'
);

assert(
  'SANITIZER_PREVIEW_ONLY',
  content.module.includes('externalExecutionAuthorized = false')
    && content.module.includes('providerCalled = false')
    && content.module.includes('module064RouteChanged = false')
    && !/HttpClient|IHttpClientFactory|ProjectPulseAiRouter/.test(content.sanitizer),
  'the sanitizer is local, deterministic, non-executing, and non-routing'
);

assert(
  'GLOBAL_HELP_INTEGRATION',
  content.help.includes("'/api/pulse-ai/v1/help-search/plan'")
    && content.help.includes('DetailedAssistantAnswer')
    && content.help.includes('Automatic multi-tool execution is not yet enabled')
    && content.help.includes('fallbackAnswer')
    && content.help.includes('retired Work Task Builder no longer owns project or task creation'),
  'global Help uses detailed Pulse AI planning and preserves a corrected non-fabricating fallback'
);

assert(
  'HELP_NO_UNSAFE_HTML',
  !content.help.includes('dangerouslySetInnerHTML')
    && !content.help.includes('innerHTML')
    && content.help.includes('NavigationTargets'),
  'Help renders structured React content and safe navigation controls'
);

assert(
  'WORKBENCH_MOUNT',
  content.mount.includes("import PulseAiDeepIntelligenceWorkbench from './PulseAiDeepIntelligenceWorkbench.jsx';")
    && content.mount.includes('<PulseAiDeepIntelligenceWorkbench />')
    && content.workbench.includes("import './pulse-ai-deep-intelligence-workbench.css';"),
  'the deep workbench is mounted inside the established Module 011 compatibility route'
);

assert(
  'WORKBENCH_FUNCTIONAL_TABS',
  ['Private Runtime', 'Timesheet Grounding', 'Help & Search', 'FlowHive Planning', 'Reports & Financials', 'Privacy Capsule', 'Tool Registry']
    .every((label) => content.workbench.includes(`label: '${label}'`))
    && requiredRoutes.every((route) => content.workbench.includes(route)),
  'the workbench calls every registered deep-intelligence preview surface'
);

assert(
  'WORKBENCH_FULL_EVIDENCE',
  content.workbench.includes('View complete structured evidence')
    && content.workbench.includes('JSON.stringify(payload, null, 2)')
    && content.workbench.includes('Detailed procedure')
    && content.workbench.includes('Comprehensive execution sequence'),
  'operators can inspect detailed results and complete structured evidence'
);

assert(
  'RESPONSIVE_UI',
  content.workbenchCss.includes('@media (max-width: 980px)')
    && content.workbenchCss.includes('@media (max-width: 720px)')
    && content.workbenchCss.includes('[data-theme="dark"]')
    && content.helpCss.includes('@media (max-width: 760px)')
    && content.helpCss.includes('@media (max-width: 620px)'),
  'Module 011 and global Help support desktop, mobile, and dark-theme layouts'
);

assert(
  'NO_PRIVATE_BROWSER_PERSISTENCE',
  !content.workbench.includes('localStorage')
    && !content.workbench.includes('sessionStorage')
    && !content.workbench.includes('indexedDB')
    && !content.help.includes('localStorage')
    && !content.help.includes('sessionStorage')
    && !content.help.includes('indexedDB'),
  'questions, documents, evidence, and capsules are not persisted by the new browser surfaces'
);

assert(
  'READ_ONLY_SQL',
  !/\b(?:INSERT\s+INTO|UPDATE\s+[a-z_]|DELETE\s+FROM|ALTER\s+TABLE|CREATE\s+TABLE|DROP\s+TABLE|TRUNCATE\s+TABLE)\b/i.test(
    [content.grounding, content.module, content.planner, content.sanitizer].join('\n')
  ),
  'new Pulse AI backend code contains no mutating SQL or schema statement'
);

assert(
  'NO_DIRECT_PROVIDER_ENDPOINTS',
  !/api\.anthropic\.com|api\.openai\.com|generativelanguage\.googleapis\.com|\/v1\/chat\/completions|\/v1\/responses/i.test(
    [content.grounding, content.planner, content.sanitizer, content.module, content.workbench, content.help].join('\n')
  ),
  'new deep-intelligence paths contain no direct public-provider endpoint'
);

assert(
  'NO_PROVIDER_SECRET_MANAGEMENT',
  !/PROJECTPULSE_(?:CLAUDE|OPENAI)_API_KEY|ANTHROPIC_API_KEY|OPENAI_API_KEY|ApplyStoredSecret|secret\/replace/i.test(
    [content.grounding, content.planner, content.sanitizer, content.module, content.workbench, content.help].join('\n')
  ),
  'the package does not read, write, replace, or return provider credentials'
);

assert(
  'DOCUMENTED_NO_MUTATION',
  content.runtimeDoc.includes('no database migration')
    && content.runtimeDoc.includes('no Azure or Entra change')
    && content.runtimeDoc.includes('no external model execution')
    && content.runtimeDoc.includes('no deployment or rollback workflow')
    && content.qualityDoc.includes('cannot train, approve, promote, deploy, or roll back itself'),
  'documentation records the locked database, cloud, provider, training, and deployment boundary'
);

assert(
  'FOUNDATION_SCOPE_PRESERVED',
  content.policy.includes('InternalDocumentBoundary')
    && content.policy.includes('ExternalProviderBoundary')
    && content.foundationDoc.includes('Timesheet intelligence')
    && content.foundationDoc.includes('System-wide Help and Search')
    && content.foundationDoc.includes('Reporting and financial intelligence'),
  'the dependent implementation follows the authoritative PR #219 mission and privacy policy'
);

assert(
  'FOUNDATION_VALIDATORS_PRESERVED',
  content.foundationValidator.includes('MODULE_011_PULSE_AI_CONTRACT=PASSED')
    && content.flowHiveValidator.includes('MODULE_066_SHARED_AI_ONLY'),
  'existing Module 011 and Module 066 source contracts remain present'
);

const pulseAiMigrations = walk('database/migrations')
  .filter((relative) => /(?:module[-_]?011|pulse[-_]?ai|deep[-_]?intelligence)/i.test(relative));
assert(
  'NO_MIGRATION',
  pulseAiMigrations.length === 0,
  pulseAiMigrations.length === 0
    ? 'no Module 011 deep-intelligence migration exists'
    : `unexpected migration paths: ${pulseAiMigrations.join(', ')}`
);

const pulseAiDeploymentFiles = [
  ...walk('.github/workflows'),
  ...walk('scripts'),
  ...walk('deployment')
].filter((relative) => /module[-_]?011.*(?:deploy|migration|azure|entra|container)|pulse[-_]?ai.*(?:deploy|migration|azure|entra|container)/i.test(relative));
assert(
  'NO_DEPLOYMENT_OR_ENVIRONMENT_ACTION',
  pulseAiDeploymentFiles.length === 0,
  pulseAiDeploymentFiles.length === 0
    ? 'no Module 011 deployment, migration, Azure, Entra, or Container action exists'
    : `unexpected environment-changing paths: ${pulseAiDeploymentFiles.join(', ')}`
);

console.log(`MODULE_011_DEEP_INTELLIGENCE_CHECKS=${assertions.length}`);
console.log('MODULE_011_DEEP_INTELLIGENCE_PHASE=PRIVATE_READ_ONLY_RUNTIME_FOUNDATION');
console.log('MODULE_011_DEEP_INTELLIGENCE_TIMESHEET_PRIVATE_GROUNDING=REGISTERED');
console.log('MODULE_011_DEEP_INTELLIGENCE_HELP_SEARCH_PLANNING=REGISTERED');
console.log('MODULE_011_DEEP_INTELLIGENCE_FLOWHIVE_PRIVATE_FIRST=REGISTERED');
console.log('MODULE_011_DEEP_INTELLIGENCE_FINANCIAL_PR220_CONSUMPTION=GATED');
console.log('MODULE_011_DEEP_INTELLIGENCE_EXTERNAL_PROVIDER_CALLS=0');
console.log('MODULE_011_DEEP_INTELLIGENCE_DATABASE_CHANGES=0');
console.log('MODULE_011_DEEP_INTELLIGENCE_AZURE_ENTRA_CHANGES=0');
console.log('MODULE_011_DEEP_INTELLIGENCE_DEPLOYMENTS=0');

if (assertions.some((row) => !row.condition)) {
  console.error('MODULE_011_DEEP_INTELLIGENCE_CONTRACT=FAILED');
  process.exit(1);
}

console.log('MODULE_011_DEEP_INTELLIGENCE_CONTRACT=PASSED');
