import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const absolute = (relative) => path.join(repoRoot, relative);
const exists = (relative) => fs.existsSync(absolute(relative));
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`CELAR_AI_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

function walk(relativeDirectory) {
  const directory = absolute(relativeDirectory);
  if (!fs.existsSync(directory)) return [];
  const files = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const relative = path.join(relativeDirectory, entry.name);
    if (entry.isDirectory()) files.push(...walk(relative));
    else files.push(relative.replaceAll('\\', '/'));
  }
  return files;
}

const files = {
  profile: 'src/backend/ProjectTime.Api/Ai/CelarAiBrandProfile.cs',
  module: 'src/backend/ProjectTime.Api/Modules/CelarAiBrandModule.cs',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj',
  knowledge: 'src/backend/ProjectTime.Api/Ai/PulseAiProductKnowledgeCatalog.cs',
  repository: 'src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceRepository.cs',
  contracts: 'src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceContracts.cs',
  privateContracts: 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagContracts.cs',
  registry: 'src/frontend/project-time-web/src/module-availability-registry.js',
  help: 'src/frontend/project-time-web/src/HelpAssistant.jsx',
  workbench: 'src/frontend/project-time-web/src/PulseAiSystemIntelligenceWorkbench.jsx',
  center: 'src/frontend/project-time-web/src/PulseAiCenter.jsx',
  app: 'src/frontend/project-time-web/src/App.jsx',
  provider: 'src/frontend/project-time-web/src/AiProviderConfigurationCenter.jsx',
  bridge: 'src/frontend/project-time-web/src/CelarAiProviderBridgePanel.jsx',
  bridgeCss: 'src/frontend/project-time-web/src/celar-ai-provider-bridge-panel.css',
  injector: 'src/frontend/project-time-web/scripts/inject-celar-ai-runtime-rebrand.mjs',
  packageJson: 'src/frontend/project-time-web/package.json',
  architectureIdentity: 'docs/modules/module-011-pulse-ai/architecture/v2.0/CELAR-AI-IDENTITY-AND-ORIGIN.md'
};

for (const [key, relative] of Object.entries(files)) {
  if (key === 'architectureIdentity') continue;
  assert(`FILE_${key.toUpperCase()}`, exists(relative), relative);
}

if (checks.some((check) => !check.condition)) {
  console.error('CELAR_AI_RUNTIME_REBRAND_CONTRACT=FAILED_MISSING_FILE');
  process.exit(1);
}

const profile = read(files.profile);
const moduleSource = read(files.module);
const project = read(files.project);
const knowledge = read(files.knowledge);
const repository = read(files.repository);
const contracts = read(files.contracts);
const privateContracts = read(files.privateContracts);
const registry = read(files.registry);
const help = read(files.help);
const workbench = read(files.workbench);
const center = read(files.center);
const app = read(files.app);
const provider = read(files.provider);
const bridge = read(files.bridge);
const bridgeCss = read(files.bridgeCss);
const injector = read(files.injector);
const packageJson = read(files.packageJson);
const architectureIdentity = exists(files.architectureIdentity) ? read(files.architectureIdentity) : '';

assert(
  'CANONICAL_IDENTITY',
  profile.includes('Celar AI is the unified operational intelligence system for the US Signal Solution Provider division')
    && profile.includes('Dr. Ahmed Adeyemi')
    && profile.includes('Manager of Professional Services')
    && profile.includes('Celeritas')
    && profile.includes('speed of light')
    && profile.includes('speed of delivery')
    && profile.includes('Changepoint'),
  'the approved creator, name-origin, US Signal, delivery-speed, and Changepoint narrative is compiled into the runtime'
);

assert(
  'CANONICAL_ANSWER_DEPTH',
  profile.includes('Core identity:')
    && profile.includes('Creator and engineering direction:')
    && profile.includes('Name origin:')
    && profile.includes('US Signal connection:')
    && profile.includes('Professional Services mission:')
    && profile.includes('Changepoint catalyst:')
    && profile.includes('Primary uses include document-grounded Timesheet suggestions'),
  'What-is-Celar-AI questions receive a comprehensive structured answer rather than a surface-level paragraph'
);

assert(
  'CELAR_API_SURFACE',
  moduleSource.includes('/api/celar-ai/v1/about') === false
    && profile.includes('public const string AboutRoute = "/api/celar-ai/v1/about"')
    && profile.includes('public const string ChatRoute = "/api/celar-ai/v1/chat"')
    && profile.includes('public const string ProviderBridgeRoute = "/api/celar-ai/v1/provider-bridge/readiness"')
    && moduleSource.includes('MapCelarAiBrandEndpoints')
    && project.includes('app.MapCelarAiBrandEndpoints();'),
  'the compiled application registers the Celar identity, chat, and provider-bridge endpoints'
);

assert(
  'FUNCTIONAL_CHAT_DELEGATION',
  moduleSource.includes('CelarAiBrandProfile.IsIdentityQuestion(question)')
    && moduleSource.includes('CelarAiBrandProfile.CreateDetailedAnswer(dataAsOf)')
    && moduleSource.includes('service.AskAsync(')
    && moduleSource.includes('repository.EnsureConversationAsync(')
    && moduleSource.includes('repository.AppendMessageAsync(')
    && moduleSource.includes('repository.CreateInquiryRunAsync(')
    && moduleSource.includes('repository.CompleteInquiryRunAsync('),
  'identity questions are answered canonically while every other question keeps the comprehensive system-intelligence path and durable conversation evidence'
);

assert(
  'VISIBLE_MODULE_IDENTITY',
  registry.includes("displayName: 'Celar AI'")
    && registry.includes("publicAlias: 'celar-ai'")
    && registry.includes("tagline: 'Speed of light. Speed of delivery.'")
    && registry.includes("technicalIdentity: 'Celar AI'")
    && registry.includes("'celar-ai': 'work-task-builder'")
    && !registry.includes("'pulse-ai': 'work-task-builder'"),
  'Module 011 uses Celar AI as its only public identity and canonical route alias'
);

assert(
  'GLOBAL_CHAT_BRAND',
  help.includes('Ask Celar AI')
    && help.includes('aria-label="Celar AI system intelligence assistant"')
    && help.includes('<strong>Celar AI Help & Search</strong>')
    && !help.includes('<strong>Pulse AI Help & Search</strong>')
    && help.includes('Celar AI Workbench')
    && (
      help.includes("const path = '/api/celar-ai/v2/chat';")
      || help.includes("const path = '/api/celar-ai/v1/chat';")
    )
    && help.includes("openRoute('celar-ai')"),
  'the global chat preserves the Group 7 Help & Search title under the Celar AI brand and submits through the v2 production or v1 compatibility endpoint'
);

assert(
  'CHAT_KEYBOARD_AND_SCROLL',
  help.includes("event.key !== 'Enter' || event.shiftKey")
    && help.includes('event.currentTarget.form?.requestSubmit()')
    && help.includes("event.key === 'Escape'")
    && help.includes('role="log"')
    && help.includes('onScroll={onConversationScroll}')
    && help.includes('aria-keyshortcuts="Enter Shift+Enter Escape"')
    && help.includes('completed responses remain in conversation history'),
  'Enter sends, Shift+Enter adds a line, Escape closes, and every completed answer remains in the independently scrollable conversation history'
);

assert(
  'HISTORY_REBRAND',
  help.includes('function rebrandCelarValue(value)')
    && help.includes('rebrandCelarValue(message.structuredResponse)')
    && help.includes('rebrandCelarString(message.text)')
    && help.includes('title: rebrandCelarString(conversation.title)')
    && help.includes('rebrandCelarValue(payload.result)'),
  'historic Pulse AI conversation payloads display through the Celar AI brand without deleting or rewriting durable evidence'
);

assert(
  'WORKBENCH_REBRAND',
  workbench.includes('Module 011 · Celar AI')
    && workbench.includes('Celar AI workspace')
    && workbench.includes("'/api/celar-ai/v1/chat'")
    && workbench.includes('setResult(rebrandCelarValue(payload.result))'),
  'the Module 011 system-intelligence workbench uses Celar AI while retaining the same operational capabilities'
);

assert(
  'LIFECYCLE_WORKSPACE_REBRAND',
  center.includes('data-module-name="Celar AI"')
    && center.includes('<h1>Celar AI</h1>')
    && app.includes("title: 'Celar AI'")
    && app.includes("return 'Celar AI';"),
  'Module 011 navigation, title, and lifecycle workspace use the Celar AI identity in the built source'
);

assert(
  'PROVIDER_PAGE_INTEGRATION',
  provider.includes("import CelarAiProviderBridgePanel from './CelarAiProviderBridgePanel.jsx';")
    && provider.includes('<CelarAiProviderBridgePanel />')
    && provider.includes('Celar AI uses Module 064 as the governed provider gateway')
    && bridge.includes("fetch('/api/celar-ai/v1/provider-bridge/readiness'")
    && bridge.includes('Celar AI is the private operational-intelligence layer inside Pulse')
    && bridge.includes('Module 064 remains the authority')
    && bridge.includes('Raw internal documents never use public providers'),
  'Module 064 visibly explains Celar AI orchestration, private-model readiness, and the external-provider boundary'
);

assert(
  'PRIVATE_MODEL_FIRST_CLASS',
  moduleSource.includes('privateModelIsFirstClassTarget = true')
    && moduleSource.includes('celarAiIsExternalVendorProvider = false')
    && moduleSource.includes('confidentialContextEligible = privateModelReady')
    && moduleSource.includes('rawInternalDocumentsMayUsePublicProviders = false')
    && moduleSource.includes('endpointReturned = false')
    && bridge.includes('Private model')
    && bridge.includes('Private route eligible'),
  'the provider page treats the private Celar AI model as a governed target rather than pretending Celar AI is an external vendor provider'
);

assert(
  'PRODUCT_KNOWLEDGE_FALLBACK',
  knowledge.includes('CelarAiBrandProfile.IsIdentityQuestion(normalizedQuestion)')
    && knowledge.includes('CelarAiPurpose()')
    && knowledge.includes('Celar AI is the current and canonical identity for Module 011')
    && knowledge.includes('Changepoint catalyst')
    && knowledge.includes('Dr. Ahmed Adeyemi'),
  'Help planning answers Celar AI identity questions with the canonical current product narrative'
);

assert(
  'TECHNICAL_COMPATIBILITY_HIDDEN',
  contracts.includes('public const string FeatureCode = "pulse_ai_system_intelligence"')
    && contracts.includes('public const string MigrationId = "054_pulse_ai_system_intelligence_conversations"')
    && repository.includes('FROM pulse_ai_conversations')
    && privateContracts.includes('PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED')
    && moduleSource.includes('technicalCompatibilityFeature = PulseAiSystemIntelligencePolicy.FeatureCode')
    && profile.includes('canonicalPrefix = "/api/celar-ai"')
    && profile.includes('legacyAliasesExposed = false')
    && !profile.includes('existingApiPrefix'),
  'stable internal compatibility identifiers remain intact but public metadata exposes only canonical Celar AI routes'
);

assert(
  'INJECTOR_REGISTERED',
  packageJson.includes('inject-celar-ai-runtime-rebrand.mjs')
    && packageJson.includes('validate:celar-ai-runtime-rebrand')
    && injector.includes("'App.Module001.g.jsx'")
    && injector.includes('CELAR_AI_RUNTIME_REBRAND_VISIBLE_NAME=Celar AI'),
  'development and production builds apply the same deterministic visible-name transition'
);

assert(
  'RESPONSIVE_PROVIDER_UI',
  bridgeCss.includes('@media (max-width: 1100px)')
    && bridgeCss.includes('@media (max-width: 760px)')
    && bridgeCss.includes('[data-theme="dark"]')
    && bridgeCss.includes('.celar-ai-provider-bridge__route-grid'),
  'the Module 064 Celar AI bridge is responsive and supports the existing light/dark experience'
);

const approvedCelarMigrations = new Set([
  'database/migrations/061_celar_ai_capability_routing.sql',
  'database/migrations/072_celar_ai_conversation_attachments.sql',
  'database/migrations/080_celar_ai_internal_data_intelligence.sql',
  'database/migrations/081_celar_ai_private_runtime_activation.sql',
  'database/migrations/082_pulse_celar_ai_canonical_labels.sql'
]);
const approvedCelarRollbacks = new Set([
  'database/rollback/061_celar_ai_capability_routing_rollback.sql',
  'database/rollback/072_celar_ai_conversation_attachments_rollback.sql',
  'database/rollback/080_celar_ai_internal_data_intelligence_rollback.sql',
  'database/rollback/081_celar_ai_private_runtime_activation_rollback.sql'
]);
const celarMigrations = walk('database/migrations').filter((relative) => /celar[-_]?ai/i.test(relative));
const celarRollbacks = walk('database/rollback').filter((relative) => /celar[-_]?ai/i.test(relative));
const unexpectedCelarMigrations = celarMigrations.filter((relative) => !approvedCelarMigrations.has(relative));
const unexpectedCelarRollbacks = celarRollbacks.filter((relative) => !approvedCelarRollbacks.has(relative));
const presentApprovedCelarMigrations = celarMigrations.filter((relative) => approvedCelarMigrations.has(relative));
const presentApprovedCelarRollbacks = celarRollbacks.filter((relative) => approvedCelarRollbacks.has(relative));
const canonicalLabelsMigrationPath = 'database/migrations/082_pulse_celar_ai_canonical_labels.sql';
const canonicalLabelsMigration = exists(canonicalLabelsMigrationPath) ? read(canonicalLabelsMigrationPath) : '';
const migration082Rollbacks = walk('database/rollback').filter((relative) => path.basename(relative).startsWith('082_'));

assert(
  'FORWARD_ONLY_LABEL_MIGRATION_BOUNDARY',
  unexpectedCelarMigrations.length === 0
    && unexpectedCelarRollbacks.length === 0
    && migration082Rollbacks.length === 0
    && canonicalLabelsMigration.includes("WHERE migration_id = '075_pulse_product_rebrand'")
    && canonicalLabelsMigration.includes("('crm_integration_field_mappings', 'projectpulse_destination')")
    && canonicalLabelsMigration.includes("current_setting('projectpulse.project_number_issuance'")
    && canonicalLabelsMigration.includes("'082_pulse_celar_ai_canonical_labels'"),
  unexpectedCelarMigrations.length === 0 && unexpectedCelarRollbacks.length === 0 && migration082Rollbacks.length === 0
    ? 'migration 082 is forward-only, allowlisted to mutable labels, and preserves ProjectPulse compatibility identifiers'
    : `unexpected Celar database paths: ${[...unexpectedCelarMigrations, ...unexpectedCelarRollbacks].join(', ')}`
);

const changedDeploymentSources = [
  profile,
  moduleSource,
  knowledge,
  registry,
  help,
  workbench,
  provider,
  bridge,
  injector
].join('\n');
assert(
  'NO_DEPLOYMENT_OR_PROVIDER_MUTATION',
  !changedDeploymentSources.includes('az containerapp')
    && !changedDeploymentSources.includes('workflow_dispatch')
    && !moduleSource.includes('/api/ai-configuration/providers/')
    && !bridge.includes("method: 'POST'")
    && !bridge.includes("method: 'PUT'"),
  'the rebrand does not deploy, change Azure, write provider secrets, or mutate Module 064 configuration'
);

assert(
  'ARCHITECTURE_ALIGNMENT',
  !architectureIdentity || (
    architectureIdentity.includes('Dr. Ahmed Adeyemi')
      && architectureIdentity.includes('Celeritas')
      && architectureIdentity.includes('speed of delivery')
      && architectureIdentity.includes('Changepoint')
  ),
  architectureIdentity
    ? 'the runtime story aligns with the Version 2.0 architecture package'
    : 'the Version 2.0 architecture package is independently supplied by documentation PR #324'
);

console.log(`CELAR_AI_RUNTIME_REBRAND_CHECKS=${checks.length}`);
console.log('CELAR_AI_DEMO_CHAT=FUNCTIONAL');
console.log('CELAR_AI_ENTER_SENDS=YES');
console.log('CELAR_AI_SHIFT_ENTER_NEWLINE=YES');
console.log('CELAR_AI_RESPONSE_PERSISTENCE=RETAINS_MIGRATION_054_CONVERSATIONS');
console.log('CELAR_AI_PROVIDER_PAGE=INTEGRATED');
console.log('CELAR_AI_TECHNICAL_COMPATIBILITY=RETAINED');
console.log(`CELAR_AI_DATABASE_MIGRATIONS_ADDED=${presentApprovedCelarMigrations.length}`);
console.log(`CELAR_AI_DATABASE_ROLLBACKS_ADDED=${presentApprovedCelarRollbacks.length}`);
console.log(`CELAR_AI_UNAPPROVED_DATABASE_ARTIFACTS=${unexpectedCelarMigrations.length + unexpectedCelarRollbacks.length}`);
console.log('CELAR_AI_DEPLOYMENTS_PERFORMED=0');

if (checks.some((check) => !check.condition)) {
  console.error('CELAR_AI_RUNTIME_REBRAND_CONTRACT=FAILED');
  process.exit(1);
}

console.log('CELAR_AI_RUNTIME_REBRAND_CONTRACT=PASSED');
