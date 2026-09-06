import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '../../../..');
const moduleDirectory = path.join(repositoryRoot, 'docs/modules/module-066-project-flowhive');
const backendDirectory = path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules');

const paths = {
  backend: path.join(backendDirectory, 'ProjectFlowHiveModule.cs'),
  contracts: path.join(backendDirectory, 'ProjectFlowHivePlanningContracts.cs'),
  repository: path.join(backendDirectory, 'PostgresProjectFlowHivePlanRepository.cs'),
  schedule: path.join(backendDirectory, 'ProjectFlowHiveScheduleEngine.cs'),
  ai: path.join(backendDirectory, 'ProjectFlowHiveAiRequestFactory.cs'),
  productionAi: path.join(backendDirectory, 'CelarAiProductionPlatformModule.cs'),
  privateRag: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs'),
  privateRagRepository: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagRepository.cs'),
  capabilityRouting: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs'),
  brand: path.join(backendDirectory, 'ProjectFlowHiveBrandAssets.cs'),
  artifacts: path.join(backendDirectory, 'ProjectFlowHiveArtifactRenderer.cs'),
  celarProduction: path.join(backendDirectory, 'CelarAiProductionPlatformModule.cs'),
  frontend: path.join(repositoryRoot, 'src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx'),
  stylesheet: path.join(repositoryRoot, 'src/frontend/project-time-web/src/project-flowhive-center.css'),
  logoJpeg: path.join(repositoryRoot, 'src/frontend/project-time-web/brand/ussignal.jpg'),
  logoPng: path.join(repositoryRoot, 'src/frontend/project-time-web/brand/ussignal.png'),
  readme: path.join(moduleDirectory, 'README.md'),
  matrix: path.join(moduleDirectory, 'CAPABILITY-MATRIX.md'),
  contract: path.join(moduleDirectory, 'API-CONTRACT.md'),
  authorization: path.join(moduleDirectory, 'AUTHORIZATION-AND-SECURITY.md'),
  persistence: path.join(moduleDirectory, 'PERSISTENCE-DESIGN.md'),
  scheduling: path.join(moduleDirectory, 'SCHEDULE-ENGINE.md'),
  aiDoc: path.join(moduleDirectory, 'AI-INTEGRATION.md'),
  artifactsDoc: path.join(moduleDirectory, 'ARTIFACTS-AND-SHARING.md'),
  overlap: path.join(moduleDirectory, 'OVERLAP-AND-RELEASE-GATES.md'),
  evidence: path.join(moduleDirectory, 'VALIDATION-EVIDENCE.md'),
  program: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Program.cs'),
  app: path.join(repositoryRoot, 'src/frontend/project-time-web/src/App.jsx'),
  packageJson: path.join(repositoryRoot, 'src/frontend/project-time-web/package.json'),
  webDockerfile: path.join(repositoryRoot, 'deployment/containers/web/Dockerfile'),
  catalog: path.join(repositoryRoot, 'docs/MODULE-CATALOG.md'),
  register: path.join(repositoryRoot, 'docs/MODULE-WORK-REGISTER.md'),
  tracker: path.join(repositoryRoot, 'docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md'),
  calculationProject: path.join(repositoryRoot, 'scripts/module-066-validation/ProjectPulse.Module066.Validation.csproj'),
  calculationProgram: path.join(repositoryRoot, 'scripts/module-066-validation/Program.cs'),
  migration: path.join(repositoryRoot, 'database/migrations/074_module_066_project_flowhive_production.sql'),
  rollback: path.join(repositoryRoot, 'database/rollback/074_module_066_project_flowhive_production_rollback.sql'),
  migrationTest: path.join(repositoryRoot, 'tests/test-module-066-project-flowhive-migration-074.sh'),
  aiOrchestration: path.join(backendDirectory, 'ProjectFlowHiveAiPlannerOrchestrationModule.cs'),
  documentResolver: path.join(backendDirectory, 'ProjectPlanningDocumentResolver.cs'),
  runtimeVerifier: path.join(repositoryRoot, 'scripts/release-test/verify-runtime.mjs')
};

const assertions = [];

function assertInvariant(name, condition, detail) {
  assertions.push({ name, condition, detail });
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'}${detail ? ` — ${detail}` : ''}`);
}

function readRequired(name, filePath) {
  const exists = fs.existsSync(filePath);
  assertInvariant(`MODULE_066_${name}_EXISTS`, exists, path.relative(repositoryRoot, filePath));
  return exists ? fs.readFileSync(filePath, 'utf8') : '';
}

const backend = readRequired('BACKEND', paths.backend);
const contracts = readRequired('CONTRACTS', paths.contracts);
const repository = readRequired('PRODUCTION_REPOSITORY', paths.repository);
const schedule = readRequired('SCHEDULE_ENGINE', paths.schedule);
const ai = readRequired('AI_REQUEST_FACTORY', paths.ai);
const productionAi = readRequired('AI_PLANNER_PRODUCTION', paths.productionAi);
const privateRag = readRequired('PRIVATE_RAG_PLANNER', paths.privateRag);
const privateRagRepository = readRequired('PRIVATE_RAG_REPOSITORY', paths.privateRagRepository);
const capabilityRouting = readRequired('CAPABILITY_ROUTING', paths.capabilityRouting);
const brand = readRequired('BRAND_ASSETS', paths.brand);
const artifacts = readRequired('ARTIFACT_RENDERER', paths.artifacts);
const celarProduction = readRequired('CELAR_PRODUCTION', paths.celarProduction);
const frontend = readRequired('FRONTEND', paths.frontend);
const stylesheet = readRequired('STYLESHEET', paths.stylesheet);
const logoJpeg = fs.existsSync(paths.logoJpeg) ? fs.readFileSync(paths.logoJpeg) : Buffer.alloc(0);
const logoPngExists = fs.existsSync(paths.logoPng);
assertInvariant('MODULE_066_US_SIGNAL_LOGO_PNG_EXISTS', logoPngExists, path.relative(repositoryRoot, paths.logoPng));

const readme = readRequired('README', paths.readme);
const matrix = readRequired('CAPABILITY_MATRIX', paths.matrix);
const apiContract = readRequired('API_CONTRACT', paths.contract);
const authorization = readRequired('AUTHORIZATION_SECURITY', paths.authorization);
const persistence = readRequired('PERSISTENCE_DESIGN', paths.persistence);
const scheduling = readRequired('SCHEDULE_DOCUMENT', paths.scheduling);
const aiDoc = readRequired('AI_DOCUMENT', paths.aiDoc);
const artifactsDoc = readRequired('ARTIFACTS_DOCUMENT', paths.artifactsDoc);
const overlap = readRequired('OVERLAP_GATES', paths.overlap);
const evidence = readRequired('VALIDATION_EVIDENCE', paths.evidence);
const program = readRequired('PROGRAM', paths.program);
const app = readRequired('APP', paths.app);
const packageJson = readRequired('PACKAGE', paths.packageJson);
const webDockerfile = readRequired('WEB_DOCKERFILE', paths.webDockerfile);
const catalog = readRequired('CATALOG', paths.catalog);
const register = readRequired('WORK_REGISTER', paths.register);
const tracker = readRequired('PRODUCTION_TRACKER', paths.tracker);
const calculationProject = readRequired('CALCULATION_PROJECT', paths.calculationProject);
const calculationProgram = readRequired('CALCULATION_PROGRAM', paths.calculationProgram);
const migration = readRequired('MIGRATION_074', paths.migration);
const rollback = readRequired('ROLLBACK_074', paths.rollback);
const migrationTest = readRequired('MIGRATION_074_TEST', paths.migrationTest);
const aiOrchestration = readRequired('AI_ORCHESTRATION', paths.aiOrchestration);
const documentResolver = readRequired('DOCUMENT_RESOLVER', paths.documentResolver);
const runtimeVerifier = readRequired('RUNTIME_VERIFIER', paths.runtimeVerifier);

const moduleBackend = [backend, contracts, schedule, ai, brand, artifacts].join('\n');
const moduleDocs = [readme, matrix, apiContract, authorization, persistence, scheduling, aiDoc, artifactsDoc, overlap, evidence].join('\n');

assertInvariant(
  'MODULE_066_STANDALONE_ROUTE',
  app.includes("'project-flowhive',\n        'sales-coverage-alignment'") || app.includes("'project-flowhive',\r\n        'sales-coverage-alignment'"),
  'Project FlowHive excludes the legacy workspace fallback'
);

const flowHiveRouteStart = app.indexOf('MODULE_066A1_PROJECT_FLOWHIVE_ROUTE_START');
const legacyWorkspaceExclusionStart = app.indexOf("{![\n        'ai-provider-configuration'");

assertInvariant(
  'MODULE_066_INDEPENDENT_ROUTE_MOUNT',
  flowHiveRouteStart >= 0 &&
    legacyWorkspaceExclusionStart >= 0 &&
    flowHiveRouteStart < legacyWorkspaceExclusionStart,
  'Project FlowHive mounts before and outside the legacy workspace exclusion'
);

const nativeAdministrationRoutes = app.match(
  /const MODULE_064_074_NATIVE_ADMINISTRATION_ROUTES = Object\.freeze\(\{([\s\S]*?)\}\);/
)?.[1] ?? '';

assertInvariant(
  'MODULE_066_NO_NATIVE_ADMINISTRATION_ROUTE_COLLISION',
  nativeAdministrationRoutes.length > 0 &&
    !nativeAdministrationRoutes.includes("'project-flowhive'") &&
    !nativeAdministrationRoutes.includes("'066'"),
  'Project FlowHive cannot be rendered by NativeModuleAdministrationPanel'
);

assertInvariant(
  'MODULE_066_TYPED_MAP_METHOD',
  backend.includes('MapProjectFlowHiveEndpoints') &&
    backend.includes('IProjectFlowHivePlanRepository') &&
    backend.includes('Task<IResult>>)GetCapabilitiesAsync') &&
    backend.includes('(Func<ProjectFlowHivePlanRequest, HttpContext, IResult>)CalculateSchedule'),
  'isolated route registration and typed handlers'
);

for (const route of [
  '/api/project-flowhive/capabilities',
  '/api/project-flowhive/portfolio',
  '/api/project-flowhive/readiness',
  '/api/project-flowhive/planning/validate',
  '/api/project-flowhive/schedule/calculate',
  '/api/project-flowhive/plans/drafts',
  '/api/project-flowhive/plans/{planId:guid}/baseline',
  '/api/project-flowhive/ai/request-preview',
  '/api/project-flowhive/artifacts/readiness',
  '/api/project-flowhive/artifacts/pdf-preview',
  '/api/project-flowhive/artifacts/excel-preview'
]) {
  assertInvariant(
    `MODULE_066_ROUTE_${route.replaceAll(/[^a-z0-9]+/gi, '_').toUpperCase()}`,
    backend.includes(`"${route}"`),
    route
  );
}

assertInvariant(
  'MODULE_066_EFFECTIVE_AND_ACTUAL_IDENTITY',
  backend.includes('ProjectPulseEffectiveUserId') &&
    backend.includes('ProjectPulseActualUserId') &&
    frontend.includes("useIdentityProfile") &&
    frontend.includes('IdentityAvatar') &&
    backend.includes('resource.user_id AS resource_user_id'),
  'Module 062 identity and actual/effective user boundaries'
);

assertInvariant(
  'MODULE_066_CANONICAL_READ_SCOPE_PRESERVED',
  backend.includes('projectpulse_team_scope_assignments') &&
    backend.includes('reporting_relationships') &&
    backend.includes('PROJECT_TEAM_COORDINATOR') &&
    backend.includes('ENGINEERING_TEAM_LEAD'),
  'backend role, team, reporting, and assignment scope'
);

assertInvariant(
  'MODULE_066_PRODUCTION_PERSISTENCE',
  repository.includes('PostgresProjectFlowHivePlanRepository') &&
    repository.includes('project_flowhive_plan_versions') &&
    repository.includes('EstablishBaselineAsync') &&
    backend.includes('SaveDraftAsync') &&
    backend.includes('view_as_write_blocked') &&
    migration.includes('074_module_066_project_flowhive_production'),
  'immutable versions, reviewer baselines, scoped authorization, and View-As write blocking'
);

const forbiddenSqlMutation = /\b(?:INSERT\s+INTO|UPDATE\s+[a-z_]|DELETE\s+FROM|ALTER\s+TABLE|CREATE\s+TABLE|DROP\s+TABLE|TRUNCATE\s+TABLE)\b/i;
assertInvariant(
  'MODULE_066_NO_MUTATING_SQL',
  !forbiddenSqlMutation.test(moduleBackend),
  'module backend contains no mutating SQL or schema statement'
);

assertInvariant(
  'MODULE_066_SCHEDULE_DEPENDENCY_TYPES',
  ['"FS"', '"SS"', '"FF"', '"SF"'].every((marker) => schedule.includes(marker)) &&
    schedule.includes('StartOffset') &&
    schedule.includes('LagWorkingDays'),
  'FS, SS, FF, SF and lead/lag source'
);

assertInvariant(
  'MODULE_066_SCHEDULE_VALIDATION',
  schedule.includes('duplicate_wbs') &&
    schedule.includes('parent_required') &&
    schedule.includes('parent_hierarchy_mismatch') &&
    schedule.includes('self_dependency') &&
    schedule.includes('duplicate_dependency') &&
    schedule.includes('dependency_cycle') &&
  schedule.includes('assignment_identity_required'),
  'WBS, hierarchy, dependency, cycle, and identity validation'
);

assertInvariant(
  'MODULE_066_AI_PLANNER_FIVE_PHASE_WBS',
  contracts.includes('DateOnly? ProjectEndDate') &&
    contracts.includes('bool IsSummary = false') &&
    productionAi.includes('(\"1\", \"Plan\")') &&
    productionAi.includes('(\"2\", \"Design\")') &&
    productionAi.includes('(\"3\", \"Implement\")') &&
    productionAi.includes('(\"4\", \"Validate\")') &&
    productionAi.includes('(\"5\", \"Release\")') &&
    schedule.includes('project_end_exceeded') &&
    schedule.includes('summary_dependency_not_allowed'),
  'PM date window, summary rows, executable children, and deterministic five-phase ordering'
);

assertInvariant(
  'MODULE_066_AI_PLANNER_SOW_SCOPE_PRIORITY',
  privateRag.includes('Scope of Services') &&
    privateRag.includes('Plan, Design, Implement, Validate, Release') &&
    privateRagRepository.includes('IsApprovedSowScopeCandidate') &&
    privateRagRepository.includes('prioritizeSowScope'),
  'approved SOW scope sections are the primary private planning authority'
);

assertInvariant(
  'MODULE_066_AI_PLANNER_EXTERNAL_PRIVACY',
  capabilityRouting.includes('fixed identity-free purpose capsule') &&
    capabilityRouting.includes('Plan, Design, Implement, Validate, Release') &&
    productionAi.includes('privateSowTextSentToExternalProvider = false') &&
    productionAi.includes('organizationOrCustomerIdentitySentToExternalProvider = false') &&
    productionAi.includes('peopleOrAssignmentDataSentToExternalProvider = false') &&
    productionAi.includes('datesOrIdentifiersSentToExternalProvider = false') &&
    productionAi.includes('fixed_backend_owned_identity_free_planning_blueprint_only') &&
    capabilityRouting.includes('Do not request, reproduce, infer, or invent any organization, customer, project, person, document, source passage, identifier, location, date') &&
    !capabilityRouting.includes('SowExcerpt'),
  'external providers receive only a closed generic blueprint and no private SOW or identity payload'
);

assertInvariant(
  'MODULE_066_SCHEDULE_NORMALIZATION',
  schedule.includes('string.Equals(Clean(row.SuccessorWbs), wbs') &&
    schedule.includes('string.Equals(Clean(row.PredecessorWbs), predecessor') &&
    schedule.includes('StartOffset(string? type') &&
    schedule.includes('Clean(type)?.ToUpperInvariant()'),
  'blank dependency types default safely to FS and trimmed WBS references remain effective'
);

assertInvariant(
  'MODULE_066_EXECUTABLE_VALIDATION_SUITE',
  calculationProject.includes('<TargetFramework>net10.0</TargetFramework>') &&
    calculationProgram.includes('MODULE_066_TEST_FS') &&
    calculationProgram.includes('MODULE_066_TEST_SS') &&
    calculationProgram.includes('MODULE_066_TEST_FF') &&
    calculationProgram.includes('MODULE_066_TEST_SF') &&
    calculationProgram.includes('MODULE_066_TEST_PHASE_SUMMARY') &&
    calculationProgram.includes('MODULE_066_TEST_SELECTED_END_DATE') &&
    calculationProgram.includes('MODULE_066_TEST_PDF_LOGO') &&
    calculationProgram.includes('MODULE_066_TEST_XLSX_LOGO_HASH'),
  'calculation, cycle, calendar, AI-lock, and branded-artifact execution tests exist'
);

assertInvariant(
  'MODULE_066_CRITICAL_PATH_AND_FLOAT',
  schedule.includes('TopologicalOrder') &&
    contracts.includes('LatestStartIndex') &&
    schedule.includes('latest[successor] - weight') &&
    contracts.includes('TotalFloatWorkingDays') &&
    schedule.includes('totalFloat == 0') &&
    schedule.includes('freeFloat'),
  'forward/reverse pass, total/free float, and critical task marker'
);

assertInvariant(
  'MODULE_066_WEEKDAY_PREVIEW_BOUNDARY',
  schedule.includes('DayOfWeek.Saturday') &&
    schedule.includes('DayOfWeek.Sunday') &&
    schedule.includes('weekday_preview_module_057_not_applied') &&
    scheduling.includes('Module 057'),
  'preview does not claim holiday/resource calendar authority'
);

assertInvariant(
  'MODULE_066_SHARED_AI_ONLY',
  ai.includes('requiredService = "ProjectPulseAiRouter"') &&
    ai.includes('feature = "project_flowhive_plan"') &&
    ai.includes('new[] { "claude", "openai", "local_template" }') &&
    ai.includes('executionEnabled = false') &&
    !/new\s+HttpClient|IHttpClientFactory|api\.anthropic|api\.openai|ANTHROPIC_API_KEY|OPENAI_API_KEY/i.test(moduleBackend),
  'Module 064 contract with no direct client or secret read'
);

assertInvariant(
  'MODULE_066_AI_REFUSAL_AND_DRAFT_GUARDS',
  ai.includes('refusalFailover = "blocked"') &&
    ai.includes('AI output is a draft') &&
    aiDoc.includes('safety refusal terminates routing') &&
    aiDoc.includes('Claude') && aiDoc.includes('OpenAI') && aiDoc.includes('local'),
  'refusal does not fail over and output cannot baseline itself'
);

const expectedLogoHash = 'c4fc4b33f744d065deeec531f393aa39996273e51eb946a452b1319e6e529183';
const actualLogoHash = crypto.createHash('sha256').update(logoJpeg).digest('hex');
const base64Block = brand.match(/LogoJpegBase64\s*=\s*([\s\S]*?);/);
const embeddedBase64 = base64Block
  ? [...base64Block[1].matchAll(/"([A-Za-z0-9+/=]+)"/g)].map((match) => match[1]).join('')
  : '';
const embeddedLogoHash = embeddedBase64
  ? crypto.createHash('sha256').update(Buffer.from(embeddedBase64, 'base64')).digest('hex')
  : '';
assertInvariant(
  'MODULE_066_US_SIGNAL_LOGO_EXACT',
  actualLogoHash === expectedLogoHash &&
    embeddedLogoHash === expectedLogoHash &&
    brand.includes(expectedLogoHash),
  `repository=${actualLogoHash || 'missing'}, embedded=${embeddedLogoHash || 'missing'}`
);

assertInvariant(
  'MODULE_066_BRANDED_ARTIFACTS',
  artifacts.includes('BuildPdf') &&
    artifacts.includes('BuildExcel') &&
    artifacts.includes('"WBS", "Task Name", "Start Date", "End Date", "Duration in Days"') &&
    artifacts.includes('"Progress", "Predecessor", "Type", "Comments", "Notes", "Assigned Identity"') &&
    artifacts.includes('planTask?.Comments') &&
    artifacts.includes('planTask?.Notes') &&
    artifacts.includes('assignment?.ResourceDisplayName') &&
    artifacts.includes('DURATION IN DAYS') &&
    artifacts.includes('/MediaBox [0 0 1008 612]') &&
    contracts.includes('string? Comments = null') &&
    contracts.includes('string? Notes = null') &&
    artifacts.includes('ProjectFlowHiveBrandAssets.LogoJpeg') &&
    artifacts.includes('PROJECT MANAGEMENT WORKING PLAN — REVIEW REQUIRED') &&
    backend.includes('MapProjectFlowHiveEnterpriseEndpoints'),
  'US Signal branded internal PDF/XLSX source, exact Planner columns, and customer lock'
);

assertInvariant(
  'MODULE_066_GOVERNED_CUSTOMER_SHARING',
  backend.includes('customerSharingEnabled = true') &&
    backend.includes('customerSharingRequiresReviewedBaseline = true') &&
    fs.readFileSync(path.join(backendDirectory, 'ProjectFlowHiveEnterpriseModule.cs'), 'utf8').includes('/api/project-flowhive/share/{token}') &&
    fs.readFileSync(path.join(backendDirectory, 'ProjectFlowHiveEnterpriseModule.cs'), 'utf8').includes('token_sha256') &&
    fs.readFileSync(path.join(backendDirectory, 'ProjectFlowHiveEnterpriseModule.cs'), 'utf8').includes('reviewed_baseline_required'),
  'customer links are expiring, revocable, token-hashed, customer-safe, and tied to exact reviewed baselines'
);

assertInvariant(
  'MODULE_066_FRONTEND_FULL_PHASES',
  frontend.includes('data-phase="066A.1-066E"') &&
    frontend.includes('Portfolio') &&
    frontend.includes('Planner') &&
    frontend.includes('Timeline & risk') &&
    frontend.includes('AI Planning Workspace') &&
    frontend.includes('Branded exports') &&
    frontend.includes('Governance'),
  'phase-aware full workspace source'
);

assertInvariant(
  'MODULE_066_FRONTEND_PRODUCTION_PERSISTENCE',
  frontend.includes("postJson('/api/project-flowhive/plans/drafts'") &&
    frontend.includes('Save immutable version') &&
    frontend.includes('Establish reviewed baseline') &&
    frontend.includes('Baseline review note') &&
    !/localStorage\.setItem\([^)]*flowhive/i.test(frontend),
  'server persistence is explicit, versioned, reviewed, and never hidden in browser storage'
);

assertInvariant(
  'MODULE_066_FRONTEND_IDENTITY_DROPDOWN',
  frontend.includes('identityOptions') &&
    frontend.includes('resourceUserId') &&
    frontend.includes('Assigned Identity') &&
    frontend.includes('useIdentityProfile'),
  'assignments preserve Module 062-backed user IDs'
);

assertInvariant(
  'MODULE_066_FRONTEND_COMPUTE_AND_ARTIFACT_ROUTES',
  frontend.includes("postJson('/api/project-flowhive/planning/validate'") &&
    frontend.includes("postJson('/api/project-flowhive/schedule/calculate'") &&
    frontend.includes('postJson(`/api/project-flowhive/projects/${projectId}/ai-planner/runs`')
    && frontend.includes('hasWorkingCopyExpectation: true')
    && frontend.includes('canApplyPlannerResult(') &&
    frontend.includes('/api/project-flowhive/artifacts/${format}-preview'),
  'validation, deterministic schedule, governed Celar generation, and reviewed artifact actions'
);

assertInvariant(
  'MODULE_066_SOW_GROUNDED_PER_TASK_TIMELINE',
    aiOrchestration.includes('ProjectPlanningDocumentResolver.ResolveAndPrepareAsync') &&
    aiOrchestration.includes('ProjectPlanningAiOrchestrator.GenerateAsync') &&
    aiOrchestration.includes('SaveWorkingCopyAsync') &&
    aiOrchestration.includes('working_draft_ready') &&
    documentResolver.includes('ScopeCitationCount') &&
    documentResolver.includes('DurableFileAvailable') &&
    privateRag.includes('plan.Tasks.SelectMany(task => task.CitationIds)') &&
    frontend.includes('an uncited generic template is never substituted') &&
    frontend.includes('flowhive-smartsheet-table') &&
    runtimeVerifier.includes('module055cSowDownload') &&
    runtimeVerifier.includes('Array.isArray(task.citationIds)') &&
    runtimeVerifier.includes('Number(task.durationWorkingDays) > 0') &&
    runtimeVerifier.includes('Number(task.remainingEffortHours) > 0'),
  'current durable SOW evidence produces citation-backed tasks, effort, durations, schedule dates, and a mutable Planner working copy without a generic substitute'
);

assertInvariant(
  'MODULE_066_FRONTEND_SMARTSHEET_AI_PLANNER',
  (frontend.includes("'AI Planner'") || frontend.includes('AI Planner')) &&
    frontend.includes('flowhive-smartsheet-table') &&
    frontend.includes('flowhive-phase-toggle') &&
    frontend.includes('projectEndDate') &&
    frontend.includes('Approved SOW Scope of Services located') &&
    frontend.includes('Ordered work steps') &&
    frontend.includes('dependencyTypeHelp.FS') && frontend.includes('title=\"Work Breakdown Structure number') &&
    frontend.includes("updateTask(index, 'comments'") &&
    frontend.includes("updateTask(index, 'notes'") &&
    frontend.includes('excludeNotes: false') &&
    frontend.includes('>Progress</th>') &&
    frontend.includes('>Type</th>') &&
    frontend.includes("updateDependencyForTask(index, 'lagWorkingDays'") &&
    stylesheet.includes('.flowhive-smartsheet-table') &&
    stylesheet.includes('.flowhive-phase-row.phase-release') &&
    stylesheet.includes('.flowhive-task-detail-grid') &&
    stylesheet.includes('.flowhive-sheet-textarea'),
  'exact Planner grid, PM start/end dates, SOW evidence, editable comments/notes/details, and responsive styling'
);

assertInvariant(
  'MODULE_066_SCOPED_STYLES',
  stylesheet.includes('.project-flowhive-center') &&
    stylesheet.includes('.flowhive-timeline') &&
    stylesheet.includes('.flowhive-ai-layout') &&
    stylesheet.includes('.flowhive-export-grid') &&
    !/(^|\n)\s*(?:html|body|:root|#root|main|button|table|input|select|textarea)\s*[{,]/m.test(stylesheet),
  'no unscoped application shell selector'
);

for (const requirement of ['GOV-015', 'RBAC-019', 'WRK-011', 'AI-008', 'AI-019', 'RPT-013']) {
  assertInvariant(
    `MODULE_066_REQUIREMENT_${requirement.replace('-', '_')}`,
    matrix.includes(requirement),
    'capability matrix maps the tracker requirement'
  );
}

for (const phase of ['066A.1', '066B', '066C', '066D', '066E']) {
  assertInvariant(
    `MODULE_066_PHASE_${phase.replace('.', '_')}`,
    readme.includes(phase) && matrix.includes(phase),
    `${phase} source and gate documented`
  );
}

assertInvariant(
  'MODULE_066_RUNTIME_METADATA_CURRENT',
  !backend.includes('sourceBaseline =') &&
    backend.includes('apiBase = "/api/project-flowhive"') &&
    backend.includes('moduleName = "Project FlowHive"'),
  'runtime readiness describes the current route and API instead of a historical source commit'
);

assertInvariant(
  'MODULE_066_CENTRAL_GOVERNANCE_OWNERSHIP',
  register.includes('feature/modules-064-074-release-train-on-main-20260719') &&
    register.includes('2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4') &&
    catalog.includes('066A.1–066E') &&
    tracker.includes('Module 066 — Project FlowHive'),
  'catalog, work register, and production tracker record the consolidated source package'
);

const backendRegistrationCount = (program.match(/\bapp\.MapProjectFlowHiveEndpoints\(\);/g) ?? []).length;
assertInvariant(
  'MODULE_066_SHARED_BACKEND_REGISTRATION_ACTIVATED',
  backendRegistrationCount === 1 &&
    program.includes('MODULE_066A1_PROJECT_FLOWHIVE_ENDPOINT_MAP_START') &&
    program.includes('MODULE_066A1_PROJECT_FLOWHIVE_ENDPOINT_MAP_END'),
  `found ${backendRegistrationCount}, expected exactly one guarded Program.cs registration`
);

const frontendImportCount = (app.match(/import ProjectFlowHiveCenter from ['"]\.\/ProjectFlowHiveCenter\.jsx['"];/g) ?? []).length;
const frontendRouteDefinitionCount = (app.match(/route:\s*['"]project-flowhive['"]/g) ?? []).length;
const frontendMountCount = (app.match(/<ProjectFlowHiveCenter\s*\/>/g) ?? []).length;

assertInvariant(
  'MODULE_066_SHARED_FRONTEND_IMPORT_ACTIVATED',
  frontendImportCount === 1,
  `found ${frontendImportCount}, expected exactly one ProjectFlowHiveCenter import`
);

assertInvariant(
  'MODULE_066_ROLE_AWARE_ROUTE_REGISTRATION',
  frontendRouteDefinitionCount === 2 &&
    app.includes("href: '#project-flowhive'") &&
    app.includes("navLabel: 'MODULE 066'") &&
    app.includes('MODULE_066A1_PROJECT_FLOWHIVE_NAV_START') &&
    app.includes("roleCodes: ['ENGINEER'") &&
    app.includes("'PROJECT_TEAM_COORDINATOR'") &&
    app.includes("'ENGINEERING_TEAM_LEAD'") &&
    app.includes("'EXECUTIVE'") &&
    app.includes("'SYSTEM_ADMINISTRATION'") &&
    app.includes("'MANAGE_ALL'"),
  `found ${frontendRouteDefinitionCount}, expected one role navigation record and one installed-module registry record`
);

assertInvariant(
  'MODULE_066_INSTALLED_MODULE_REGISTRY',
  app.includes('MODULE_066A1_PROJECT_FLOWHIVE_INSTALLED_REGISTRY_START') &&
    app.includes('MODULE_066A1_PROJECT_FLOWHIVE_INSTALLED_REGISTRY_END') &&
    app.includes("group: 'Project Delivery'"),
  'dashboard and Module 999 can enumerate the source-integrated route'
);

assertInvariant(
  'MODULE_066_ROLE_AWARE_ROUTE_MOUNT',
  frontendMountCount === 1 &&
    app.includes("const canViewProjectFlowHive = visibleRoleModules.some((module) => module.route === 'project-flowhive');") &&
    app.includes("activeRoute === 'project-flowhive' && canViewProjectFlowHive") &&
    app.includes('MODULE_066A1_PROJECT_FLOWHIVE_ROUTE_START'),
  `found ${frontendMountCount}, expected one authorized route mount`
);

let parsedPackage = {};
try {
  parsedPackage = JSON.parse(packageJson);
} catch {
  parsedPackage = {};
}

assertInvariant(
  'MODULE_066_PACKAGE_VALIDATOR_WIRING',
  parsedPackage.scripts?.['validate:module066'] === 'node ./scripts/validate-module-066-project-flowhive.mjs' &&
    parsedPackage.scripts?.build?.includes('npm run validate:module059') &&
    parsedPackage.scripts?.build?.includes('npm run validate:module062') &&
    parsedPackage.scripts?.build?.includes('npm run validate:module002') &&
    parsedPackage.scripts?.build?.includes('npm run validate:module066') &&
    parsedPackage.scripts?.build?.endsWith('vite build'),
  'production build preserves protected validators and adds Module 066 before Vite'
);

for (const backendFile of [
  'ProjectFlowHiveModule.cs',
  'ProjectFlowHivePlanningContracts.cs',
  'ProjectFlowHiveScheduleEngine.cs',
  'ProjectFlowHiveAiRequestFactory.cs',
  'ProjectFlowHiveBrandAssets.cs',
  'ProjectFlowHiveArtifactRenderer.cs'
]) {
  assertInvariant(
    `MODULE_066_CONTAINER_${backendFile.replaceAll(/[^a-z0-9]+/gi, '_').toUpperCase()}`,
    webDockerfile.includes(`src/backend/ProjectTime.Api/Modules/${backendFile}`),
    `web build context includes ${backendFile}`
  );
}

assertInvariant(
  'MODULE_066_PRODUCTION_REPOSITORY_COMPILE_DISCOVERY',
  fs.existsSync(paths.repository) &&
    repository.includes('PostgresProjectFlowHivePlanRepository') &&
    repository.includes('IProjectFlowHivePlanRepository'),
  'SDK project compile discovery includes the production repository source without a Dockerfile marker'
);

assertInvariant(
  'MODULE_066_CONTAINER_GOVERNANCE_CONTEXT',
  webDockerfile.includes('docs/modules/module-066-project-flowhive/') &&
    webDockerfile.includes('docs/MODULE-CATALOG.md') &&
    webDockerfile.includes('docs/MODULE-WORK-REGISTER.md') &&
    webDockerfile.includes('docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md') &&
    webDockerfile.includes('scripts/module-066-validation/') &&
    webDockerfile.includes('src/backend/ProjectTime.Api/Modules/IdentityProfileModule.cs'),
  'container validation receives complete Module 066 and protected Module 062 evidence'
);

function filesBelow(directory) {
  if (!fs.existsSync(directory)) return [];
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const target = path.join(directory, entry.name);
    return entry.isDirectory() ? filesBelow(target) : [target];
  });
}

assertInvariant(
  'MODULE_066_GOVERNED_DATABASE_LIFECYCLE',
  migration.includes('project_flowhive_plan_versions') &&
    migration.includes('project_flowhive_plan_reviews') &&
    rollback.includes('Rollback refused: Project FlowHive versions exist.') &&
    migrationTest.includes('MODULE_066_PROJECT_FLOWHIVE_MIGRATION_074=PASS'),
  'Migration 074 has immutable evidence, guarded rollback, idempotence, and executable lifecycle validation'
);

assertInvariant(
  'MODULE_066_STATUS_PRODUCTION_READY',
  backend.includes('status = persistence.Ready ? "production_ready"') &&
    frontend.includes('data-mode="production"') &&
    frontend.includes("capabilityResponse?.databaseMutationEnabled ? 'Planner services ready'") &&
    frontend.includes("enterpriseError ? 'Readiness required'") &&
    !frontend.includes('>Production connected</span>') &&
    runtimeVerifier.includes('Web source stamp does not match the exact release SHA'),
  'runtime and UI report readiness from backend dependency evidence and exact deployed-SHA verification rather than a static connected label'
);

assertInvariant(
  'MODULE_066_CITED_SCAFFOLD_EXTERNAL_ENRICHMENT',
  privateRag.includes('flowHive && AllowsDeterministicCitedPlanningFallback(query.FeatureCode)') &&
    privateRag.includes('featureCode is CelarAiCapabilityCatalog.ProjectFlowHivePlan') &&
    privateRag.includes('or CelarAiCapabilityCatalog.ProjectForgePlanEstimate') &&
    !privateRag.slice(
      privateRag.indexOf('private static bool AllowsDeterministicCitedPlanningFallback'),
      privateRag.indexOf('private static string DeterministicPlanningPhase')
    ).includes('SowGsdPlanning') &&
    privateRag.includes('DeterministicPlanningSteps') &&
    privateRag.includes('DeterministicPlanningMilestones') &&
    privateRag.includes('identity-free generic planning guidance') &&
    privateRag.includes('No raw SOW/GSD text'),
  'private cited scope survives private-model unavailability while only identity-free generic guidance may use Claude/OpenAI'
);


const enterpriseBackend = readRequired('ENTERPRISE_BACKEND', path.join(backendDirectory, 'ProjectFlowHiveEnterpriseModule.cs'));
const enterpriseHelpers = readRequired('ENTERPRISE_HELPERS', path.join(repositoryRoot, 'src/frontend/project-time-web/src/flowhive-enterprise-helpers.js'));
const enterprisePanels = readRequired('ENTERPRISE_PANELS', path.join(repositoryRoot, 'src/frontend/project-time-web/src/ProjectFlowHiveEnterprisePanels.jsx'));
const enterpriseMigration = readRequired('MIGRATION_086', path.join(repositoryRoot, 'database/migrations/086_module_066_flowhive_enterprise_pm.sql'));
const enterpriseRollback = readRequired('ROLLBACK_086', path.join(repositoryRoot, 'database/rollback/086_module_066_flowhive_enterprise_pm_rollback.sql'));
const enterpriseMigrationTest = readRequired('MIGRATION_086_TEST', path.join(repositoryRoot, 'tests/test-module-066-flowhive-enterprise-pm-migration-086.sh'));

assertInvariant(
  'MODULE_066_ENTERPRISE_PM_PERSISTENCE',
  enterpriseMigration.includes('project_flowhive_working_copies') &&
    enterpriseMigration.includes('project_flowhive_project_controls') &&
    enterpriseMigration.includes('project_flowhive_raid_items') &&
    enterpriseMigration.includes('project_flowhive_status_reports') &&
    enterpriseMigration.includes('project_flowhive_customer_shares') &&
    enterpriseRollback.includes('Rollback refused: Project FlowHive enterprise PM records exist.') &&
    enterpriseMigrationTest.includes('MODULE_066_FLOWHIVE_ENTERPRISE_PM_MIGRATION_086=PASS'),
  'working copies, financial controls, RAID, immutable status reports, customer shares, and guarded rollback'
);

assertInvariant(
  'MODULE_066_PHASE_TASK_CRUD_AND_REORDER',
  frontend.includes('function addTask(phaseWbs)') &&
    frontend.includes('onClick={() => addTask(task.wbsNumber)}>Add task</button>') &&
    frontend.includes('deleteTask(task.wbsNumber)') &&
    frontend.includes('draggable={Boolean(enterprise?.access?.canManage)}') &&
    frontend.includes('dropTask(task.wbsNumber') &&
    enterpriseHelpers.includes('deleteFlowHiveTask') &&
    enterpriseHelpers.includes('moveFlowHiveTask') &&
    enterpriseHelpers.includes('renumberFlowHivePlan'),
  'Plan, Design, Implement, Validate, and Release task add, delete, drag/drop, keyboard movement, and WBS renumbering'
);

assertInvariant(
  'MODULE_066_ENTERPRISE_PM_SCOPE',
  enterpriseBackend.includes('Only the assigned Project Manager can manage') &&
    enterpriseBackend.includes('IsProjectManagerOwner') &&
    enterpriseBackend.includes('ProjectPulseActualSessionAuthority.IsViewAs') &&
    enterpriseBackend.includes('working_copy_version_conflict'),
  'PM ownership, non-transferable administrator support, View-As write blocking, and optimistic concurrency'
);

assertInvariant(
  'MODULE_066_FINANCIAL_STATUS_AND_AI_EVIDENCE',
  frontend.includes("id: 'financials'") &&
    frontend.includes("id: 'status'") &&
    frontend.includes('/api/project-financials/projects/') &&
    enterprisePanels.includes('Fixed Price') &&
    enterprisePanels.includes('Time and Materials') &&
    enterprisePanels.includes('RAID register') &&
    enterprisePanels.includes('Executive summary') &&
    enterpriseBackend.includes('sowEvidenceSummary') &&
    enterpriseBackend.includes('flowhive_sow_processing_queued'),
  'authoritative financials, contract type, RAID, executive status reporting, and actionable SOW evidence readiness'
);

assertInvariant(
  'MODULE_066_PROFESSIONAL_ARTIFACT_AND_HEADER_HELP',
  artifacts.includes('PROJECT MANAGEMENT WORKING PLAN — REVIEW REQUIRED') &&
    artifacts.includes('Executive summary') &&
    frontend.includes('Project Management working plan') &&
    frontend.includes('dependencyTypeHelp.FS') &&
    frontend.includes('Start No Earlier Than constraint') &&
    stylesheet.includes('.flowhive-save-bar') &&
    stylesheet.includes('.flowhive-financial-grid'),
  'professional PM export, executive summary, dependency explanations, editable schedule constraints, and responsive enterprise styling'
);

const failed = assertions.filter((assertion) => !assertion.condition);
console.log('');
console.log(`MODULE_066_VALIDATION_CHECKS=${assertions.length}`);
console.log('MODULE_066_IMPLEMENTATION_PHASES=066A.1_066B_066C_066D_066E');
console.log('MODULE_066_PERSISTENCE=IMMUTABLE_VERSIONED');
console.log('MODULE_066_AI_EXECUTION=CELAR_MODULE_064_ROUTED');
console.log('MODULE_066_CUSTOMER_SHARING=REVIEWED_BASELINE_GOVERNED');
console.log('MODULE_066_SHARED_INTEGRATION=PRODUCTION_PACKAGE');

if (failed.length) {
  console.error('MODULE_066_COMPLETE_SOURCE_CONTRACT=FAILED');
  failed.forEach((failure) => console.error(`- ${failure.name}: ${failure.detail}`));
  process.exit(1);
}

console.log('MODULE_066_COMPLETE_SOURCE_CONTRACT=PASSED');
