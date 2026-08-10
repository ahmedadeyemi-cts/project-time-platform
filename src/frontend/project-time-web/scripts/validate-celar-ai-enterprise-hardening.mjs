import fs from 'node:fs';

function text(path) {
  return fs.readFileSync(path, 'utf8');
}

function requireContains(content, marker, label) {
  if (!content.includes(marker)) throw new Error(`Missing ${label}: ${marker}`);
}

const workflow = text('.github/workflows/celar-ai-oracle-test-runtime-deploy.yml');
requireContains(workflow, "X-ProjectPulse-Module-Number: 064", 'Module 064 activation authorization');
requireContains(workflow, 'pulse-private-model-probe-safe.json', 'sanitized private-model activation diagnostic');
requireContains(workflow, 'AUTH_011', 'separate document-runtime authorization boundary');

const registry = text('src/backend/ProjectTime.Api/Ai/CelarAiPublicEntityRegistry.cs');
requireContains(registry, 'US Signal', 'approved public entity');
requireContains(registry, 'PROJECTPULSE_CELAR_AI_PUBLIC_ENTITY_ALLOWLIST', 'deployment-managed public entity allowlist');
requireContains(registry, 'EnterpriseContextCue', 'private-context fail-closed boundary');

const knowledge = text('src/backend/ProjectTime.Api/Ai/PulseAiSystemKnowledgeCatalog.cs');
if ((knowledge.match(/CelarAiPublicEntityRegistry.IsGovernedPublicQuestion/g) ?? []).length !== 2) {
  throw new Error('Public entity classification must be applied at both scope and explicit-public boundaries.');
}

const router = text('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs');
requireContains(router, 'public_general_question_low_confidence_answer', 'low-confidence public answer code');
if ((router.match(/TryRejectPublicAnswer/g) ?? []).length < 3) {
  throw new Error('Public answer quality must gate private, Claude, and OpenAI routing.');
}
requireContains(router, 'continue to Claude/OpenAI', 'private low-confidence escalation explanation');

const main = text('src/frontend/project-time-web/src/main.jsx');
requireContains(main, "import './enterprise-theme-completion.css';", 'global theme completion import');
const theme = text('src/frontend/project-time-web/src/enterprise-theme-completion.css');
for (const marker of ['group7-provider-readiness', 'ai-provider-center__provider', 'celar-ai-routing__route-card', "data-theme='dark'", 'pulse-header-theme-switcher']) {
  requireContains(theme, marker, 'theme completion selector');
}

const panel = text('src/frontend/project-time-web/src/CelarAiCapabilityRoutingPanel.jsx');
requireContains(panel, 'This protected endpoint is deployment-managed', 'protected endpoint explanation');

console.log('CELAR_AI_ENTERPRISE_HARDENING_VALIDATION=PASS');
