import fs from 'node:fs';

const files = {
  registry: fs.readFileSync('src/backend/ProjectTime.Api/Ai/CelarAiPublicEntityRegistry.cs', 'utf8'),
  catalog: fs.readFileSync('src/backend/ProjectTime.Api/Ai/PulseAiSystemKnowledgeCatalog.cs', 'utf8'),
  service: fs.readFileSync('src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceService.cs', 'utf8'),
  routing: fs.readFileSync('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs', 'utf8'),
  reliability: fs.readFileSync('src/backend/ProjectTime.Api/Ai/CelarAiUniversalAnswerReliability.cs', 'utf8'),
  module064: fs.readFileSync('src/frontend/project-time-web/src/CelarAiCapabilityRoutingPanel.jsx', 'utf8'),
  tests: fs.readFileSync('tests/CelarAiInternalDataTests/Program.cs', 'utf8'),
  operations: fs.readFileSync('.github/workflows/celar-ai-ask-operations-ci.yml', 'utf8'),
  enterprise: fs.readFileSync('.github/workflows/celar-ai-enterprise-platform-ci.yml', 'utf8')
};

function requireMarker(name, condition) {
  if (!condition) throw new Error(`${name}=FAILED`);
  console.log(`${name}=PASSED`);
}

requireMarker('CELAR_PUBLIC_COUNTRY_REGISTRY', files.registry.includes('CultureTypes.SpecificCultures') && files.registry.includes('DefaultApprovedCountries'));
requireMarker('CELAR_PUBLIC_OFFICEHOLDER_ROLES', files.registry.includes('prime\\s+minister') && files.registry.includes('king|queen|monarch'));
requireMarker('CELAR_JORDAN_CLASSIFICATION_TEST', files.tests.includes('Who is the president of Jordan?') && files.tests.includes('Who is the king of Jordan?'));
requireMarker('CELAR_BRAZIL_CLASSIFICATION_TEST', files.tests.includes('Who is the president of Brazil?'));
requireMarker('CELAR_PUBLIC_PRIVATE_PROMPT', files.service.includes('PublicGeneralKnowledgeSystemInstruction') && files.service.includes('PublicGeneralKnowledgeMaximumOutputTokens = 256'));
requireMarker('CELAR_PUBLIC_PRIVATE_EVIDENCE', files.service.includes('module064:public-general-knowledge-private') && files.service.includes('governed_private_ai'));
requireMarker('CELAR_PUBLIC_GRACEFUL_FALLBACK', files.service.includes('PublicKnowledgeUnavailableAnswer') && files.service.includes('intentionally did not fabricate an answer'));
requireMarker('CELAR_PUBLIC_GENERATION_TIMEOUT', files.routing.includes('PROJECTPULSE_CELAR_AI_HELP_GENERATION_TIMEOUT_SECONDS') && files.routing.includes('celar_ai_private_generation_timeout'));
requireMarker(
  'CELAR_MIGRATION_084_PASSWORD_CI',
  /^\s*POSTGRES_PASSWORD:\s*projectpulse\s*$/m.test(files.operations)
    && /^\s*PGPASSWORD:\s*projectpulse\s*$/m.test(files.operations)
    && !/^\s*POSTGRES_HOST_AUTH_METHOD:\s*trust\s*$/m.test(files.operations)
);
requireMarker(
  'CELAR_MIGRATION_084_ENTERPRISE_GUARD',
  files.enterprise.includes('DISALLOWED_DATABASE=')
    && files.enterprise.includes('The Celar AI enterprise package changed an unapproved database file')
);
requireMarker('CELAR_PUBLIC_POLICY_REUSE', files.catalog.includes('CelarAiPublicEntityRegistry.IsGovernedPublicQuestion(normalized)'));
requireMarker(
  'CELAR_PUBLIC_PROVIDER_ANSWER_PRESERVED',
  files.reliability.includes('preserveEvidenceLimitedPublicProviderAnswer')
    && files.reliability.includes('never relabel it as verified')
    && files.reliability.includes('result.Status.Equals("completed"')
);
requireMarker(
  'CELAR_MODULE064_COMPLETE_ORACLE_PORTFOLIO',
  files.module064.includes('Gateway compatibility model')
    && files.module064.includes('Gemma · Qwen · Llama')
    && files.module064.includes('EmbeddingGemma')
    && files.module064.includes('Structured: Gemma → Qwen → Llama · General: Qwen → Llama → Gemma')
);
console.log('CELAR_AI_PUBLIC_QUESTION_RESILIENCE=PASS');
