import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8')
const requireText = (content, value, evidence) => {
  if (!content.includes(value)) throw new Error(`Missing ${evidence}: ${value}`)
}
const rejectText = (content, value, evidence) => {
  if (content.includes(value)) throw new Error(`Unexpected ${evidence}: ${value}`)
}

const workflow = read('.github/workflows/celar-ai-oracle-test-runtime-reactivate.yml')
const docs = read('docs/modules/module-011-pulse-ai/ORACLE-TEST-EXTERNAL-HTTPS-RUNTIME.md')
const behavior = read('tests/CelarAiOracleExternalRuntimeTests/Program.cs')

for (const marker of [
  'name: ProjectPulse Reactivate Celar AI Oracle HTTPS Runtime in Protected Test',
  'workflow_dispatch:',
  'release_commit:',
  'approval_reference:',
  'REACTIVATE-CELAR-AI-ORACLE-RUNTIME-IN-PROTECTED-TEST',
  'environment: test',
  'group: projectpulse-deploy-test',
  'cancel-in-progress: false',
  'ORACLE_RUNTIME_HOST: celarai.onenecklab.com',
  'ORACLE_RUNTIME_IP: 141.148.19.235',
  'PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN',
  'X-Pulse-AI-Privacy-Boundary: private_pulse_runtime_only',
  'PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_EXPECTED_IP="$ORACLE_RUNTIME_IP"',
  'PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT="$ORACLE_INFERENCE_ENDPOINT"',
  'PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT="$ORACLE_EMBEDDING_ENDPOINT"',
  'PROJECTPULSE_PRIVATE_OCR_ENDPOINT="$ORACLE_OCR_ENDPOINT"',
  'PROJECTPULSE_PRIVATE_MALWARE_SCAN_ENDPOINT="$ORACLE_MALWARE_SCAN_ENDPOINT"',
  'PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST="$ORACLE_RUNTIME_HOST"',
  'PROJECTPULSE_CELAR_AI_TRAINING_ENABLED=false',
  'PRODUCTION_MUTATION=NONE',
  'MIGRATIONS_APPLIED=NONE',
  'Rollback protected Test API configuration on failure',
  'oracle-before-env.json',
  'oracle-before-image',
  'az containerapp secret remove',
  'secretMaterialRecorded:false',
]) requireText(workflow, marker, 'protected Test reactivation controller')

for (const marker of [
  '((.data | length) == 1)',
  '((.data[0].embedding | length) == 768)',
  '([.data[0].embedding[] | numbers] | length) == 768',
  'rawDocumentContentLogged == false',
  'trainingEnabled == false',
  'externalEscalationEnabled == false',
]) requireText(workflow, marker, 'live Oracle acceptance')

for (const prohibited of [
  'environment: production',
  'PROJECTPULSE_PRODUCTION',
  'PROJECTPULSE_TEST_DATABASE_URL',
  'az keyvault',
  '--insecure',
  'curl -k',
  '129.213.82.144',
  'celar-ai-oracle-test-runtime-deploy.yml',
]) rejectText(workflow, prohibited, 'reactivation safety boundary')

requireText(docs, '141.148.19.235', 'replacement Oracle public IPv4')
requireText(docs, 'celar-ai-oracle-test-runtime-reactivate.yml', 'current reactivation workflow documentation')
rejectText(docs, 'initially `129.213.82.144`', 'retired Oracle IPv4 guidance')

// The behavior test deliberately centralizes the replacement address so every
// positive reset, parser assertion, and pinning assertion consumes one value.
requireText(
  behavior,
  'const string ExpectedOracleAddress = "141.148.19.235";',
  'behavior-test replacement public IPv4 constant',
)
requireText(
  behavior,
  'IPAddress.Parse(ExpectedOracleAddress)',
  'behavior-test pinned-address assertion',
)
requireText(
  behavior,
  'Set(PulseAiExternalHttpsRuntimePolicy.ExpectedIpVariable, ExpectedOracleAddress);',
  'behavior-test configured expected-address use',
)
rejectText(behavior, '129.213.82.144', 'retired behavior-test public IPv4 pin')

console.log('CELAR_AI_ORACLE_TEST_REACTIVATION_CONTRACT=PASS')
console.log('CELAR_AI_ORACLE_TEST_EXPECTED_IP=141.148.19.235')
console.log('PRODUCTION_MUTATION=NONE')
