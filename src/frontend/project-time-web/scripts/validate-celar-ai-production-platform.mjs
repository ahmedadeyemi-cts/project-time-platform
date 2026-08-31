import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relative) => fs.readFileSync(path.join(repo, relative), 'utf8');
const exists = (relative) => fs.existsSync(path.join(repo, relative));
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition });
  console.log(`CELAR_AI_PRODUCTION_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const files = {
  module: 'src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs',
  enterpriseModule: 'src/backend/ProjectTime.Api/Modules/CelarAiEnterprisePlatformModule.cs',
  platform: 'src/frontend/project-time-web/src/CelarAiProductionPlatform.jsx',
  css: 'src/frontend/project-time-web/src/celar-ai-production-platform.css',
  injector: 'src/frontend/project-time-web/scripts/inject-celar-ai-production-platform.mjs',
  contextInjector: 'src/frontend/project-time-web/scripts/inject-celar-ai-enterprise-chat-context.mjs',
  workTaskBuilder: 'src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx',
  help: 'src/frontend/project-time-web/src/HelpAssistant.jsx',
  flowHive: 'src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx'
};

for (const [name, relative] of Object.entries(files)) assert(`FILE_${name.toUpperCase()}`, exists(relative), relative);
if (checks.some((check) => !check.condition)) process.exit(1);

const moduleSource = read(files.module);
const enterpriseModule = read(files.enterpriseModule);
const platform = read(files.platform);
const css = read(files.css);
const injector = read(files.injector);
const contextInjector = read(files.contextInjector);
const panel = read(files.workTaskBuilder);
const help = read(files.help);
const flowHive = read(files.flowHive);

assert('INTENT_FIRST', ['current_date_time', 'system_version', 'capabilities', 'procedure', 'people_activity', 'api_inventory', 'troubleshooting', 'financial_and_reporting', 'documents_and_rag', 'projects_and_delivery', 'future_enhancement'].every((value) => moduleSource.includes(value)), 'narrow production intents are present');
assert('BASIC_COMPETENCY', ['What day is it today?', 'What is the current system version?', 'How do I enter my time?', 'What is my team working on this week?', 'Which APIs are running?'].every((value) => moduleSource.includes(value)), 'promotion-blocking utility and platform cases are frozen');
assert('NO_API_BOILERPLATE', moduleSource.includes('did not substitute API counts or generic diagnostic boilerplate') && moduleSource.includes('API counts are not a substitute'), 'generic API counts cannot replace an answer');
assert('TRUST_CONTRACT', ['verified_current_fact', 'verified_document_fact', 'verified_with_limitations', 'procedure', 'draft', 'insufficient_evidence'].every((value) => moduleSource.includes(value)), 'answer trust classifications are implemented');
assert('DATE_TIME_DIRECT', moduleSource.includes('Current request clock') && moduleSource.includes('ClientTimeZone'), 'date/time uses deterministic request evidence');
assert('VERSION_DIRECT', moduleSource.includes('PROJECTPULSE_RELEASE_COMMIT') && moduleSource.includes('Assembly.GetExecutingAssembly'), 'system version uses live process and release metadata');
assert('LIFECYCLE_TABLES', ['celar_ai_dataset_versions', 'celar_ai_training_jobs', 'celar_ai_evaluation_runs', 'celar_ai_model_versions', 'celar_ai_model_deployments', 'celar_ai_answer_quality_events', 'celar_ai_lifecycle_audit'].every((value) => moduleSource.includes(value)), 'complete lifecycle metadata schema is present');
assert('NO_RAW_TRAINING_DATA', moduleSource.includes('rawTrainingExamplesStoredInPulse = false') && moduleSource.includes('rawExamplesIncludedInRequest = false'), 'Pulse stores references and checksums, not raw examples');
assert('PRIVATE_TRAINING_POLICY', moduleSource.includes('PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint') && moduleSource.includes('PROJECTPULSE_CELAR_AI_TRAINING_HOST_ALLOWLIST'), 'private training endpoint policy is enforced');
assert('PRODUCTION_ENDPOINTS', moduleSource.includes('/api/celar-ai/v2/chat') && moduleSource.includes('/api/project-flowhive/ai/production-generate') && moduleSource.includes('/api/celar-ai/v1/production/datasets'), 'production chat, lifecycle, and compatibility FlowHive endpoints are present');
assert('ENDPOINTS_MAPPED', enterpriseModule.includes('MapCelarAiProductionPlatformEndpoints'), 'production endpoints are registered from the existing Celar AI application mapping');
assert('FLOWHIVE_EXECUTION', moduleSource.includes('executionEnabled = true') && moduleSource.includes('ProjectFlowHiveScheduleEngine.Calculate') && moduleSource.includes('CelarAiCapabilityTargets.DefaultOrder'), 'FlowHive calls Celar AI and deterministic scheduling');
assert('FLOWHIVE_REVIEW_ONLY', moduleSource.includes('baselineEstablished = false') && moduleSource.includes('customerDateCommitted = false') && moduleSource.includes('persistencePerformed = false'), 'FlowHive generation remains review-only');
assert('UNIFIED_MODULE011', platform.includes('CelarAiEnterprisePlatform') && platform.includes('PulseAiPrivateRagWorkbench') && platform.includes('PulseAiSystemIntelligenceWorkbench') && panel.includes('<CelarAiProductionPlatform />'), 'one authoritative Module 011 shell mounts populated workspaces');
assert('ARCHITECTURE_OVERVIEW', platform.includes('<CelarAiEnterprisePlatform />'), 'Overview mounts the US Signal architecture and enterprise composer');
assert('POPULATED_TABS', ['Overview', 'Knowledge & RAG', 'Tools & Coverage', 'Datasets', 'Training', 'Evaluations', 'Model Registry', 'Deployments', 'Governance'].every((value) => platform.includes(value)), 'every production tab has real content');
assert('SCHEMA_INIT_CONTROL', platform.includes('/api/celar-ai/v1/production/schema/initialize') && platform.includes('Initialize production lifecycle schema'), 'actual administrators can initialize lifecycle schema');
assert('CHAT_V2', help.includes("'/api/celar-ai/v2/chat'") && help.includes('clientTimeZone') && help.includes('TrustSummary'), 'global chat uses intent-first v2 and visible trust status');
const legacyFlowHiveBrowserCall = /(?:postJson|fetch)\(\s*['"`]\/api\/project-flowhive\/ai\/production-generate/.test(flowHive);
const durableFlowHivePlanner = flowHive.includes('AI Planning Workspace')
  && flowHive.includes('runAiPlannerOperation')
  && flowHive.includes('/api/project-flowhive/projects/${selectedProjectId}/ai-planner/runs')
  && flowHive.includes('/ai-planner/runs/${result.runId}')
  && flowHive.includes('FlowHiveEvidenceReadiness')
  && flowHive.includes('resolving the project SOW and GSD')
  && flowHive.includes('scopeOfServicesLocated')
  && flowHive.includes('approvedSowCitationCount');
assert('FLOWHIVE_UI', durableFlowHivePlanner && !legacyFlowHiveBrowserCall, 'FlowHive V2 creates and polls durable project-scoped AI planner runs, surfaces SOW evidence readiness/citations, and has no executable legacy production-generate browser call');
assert('INJECTOR_CHAIN', contextInjector.includes("inject-celar-ai-production-platform.mjs") && injector.includes('CELAR_AI_PRODUCTION_INJECTOR=PASSED'), 'production source integration runs after compatibility injectors');
assert('RESPONSIVE_STYLE', css.includes('.celar-production-platform') && css.includes('.celar-trust-banner') && css.includes('.celar-flowhive-production-result') && css.includes('@media(max-width:900px)'), 'production shell, trust, FlowHive, and responsive styles exist');
assert('NO_DUPLICATE_LEGACY_ROUTE', !moduleSource.includes('const string FlowHiveRoute = "/api/project-flowhive/ai/generate"'), 'production FlowHive route does not collide with the compatibility endpoint');
assert('NO_DEPLOYMENT_MUTATION', !moduleSource.includes('az containerapp') && !moduleSource.includes('kubectl') && !moduleSource.includes('git push'), 'application endpoints do not deploy infrastructure or source');

console.log(`CELAR_AI_PRODUCTION_CHECKS=${checks.length}`);
if (checks.some((check) => !check.condition)) {
  console.error('CELAR_AI_PRODUCTION_PLATFORM_CONTRACT=FAILED');
  process.exit(1);
}
console.log('CELAR_AI_PRODUCTION_PLATFORM_CONTRACT=PASSED');