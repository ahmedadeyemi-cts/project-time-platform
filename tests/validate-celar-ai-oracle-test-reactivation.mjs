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
const count = (content, value) => content.split(value).length - 1

const workflow = read('.github/workflows/celar-ai-oracle-test-runtime-reactivate.yml')
const docs = read('docs/modules/module-011-pulse-ai/ORACLE-TEST-EXTERNAL-HTTPS-RUNTIME.md')
const behavior = read('tests/CelarAiOracleExternalRuntimeTests/Program.cs')
const policy = read('src/backend/ProjectTime.Api/Ai/PulseAiExternalHttpsRuntimePolicy.cs')

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
  'Rollback protected Test API configuration on failure or cancellation',
  '(failure() || cancelled())',
  'oracle-before-env.json',
  'oracle-before-image',
  'az containerapp secret remove',
  'secretMaterialRecorded:false',
]) requireText(workflow, marker, 'protected Test reactivation controller')

for (const marker of [
  'ORACLE_CURL=(--resolve "$ORACLE_RUNTIME_HOST:443:$ORACLE_RUNTIME_IP" --noproxy "$ORACLE_RUNTIME_HOST")',
  'curl -fsS --max-time 45 "${ORACLE_CURL[@]}" --config "$AUTH_CONFIG"',
  'curl -fsS --max-time 270 "${ORACLE_CURL[@]}" --config "$AUTH_CONFIG"',
  'curl -fsS --max-time 180 "${ORACLE_CURL[@]}" --config "$AUTH_CONFIG"',
  'curl -fsS --max-time 300 "${ORACLE_CURL[@]}" --config "$AUTH_CONFIG"',
]) requireText(workflow, marker, 'connect-time Oracle IP pin and proxy bypass')

if (count(workflow, '"${ORACLE_CURL[@]}"') < 7) {
  throw new Error('Expected every Oracle preflight curl to use the pinned ORACLE_CURL array.')
}

for (const marker of [
  '((.data | length) == 1)',
  '((.data[0].embedding | length) == 768)',
  '([.data[0].embedding[] | numbers] | length) == 768',
  'rawDocumentContentLogged == false',
  'trainingEnabled == false',
  'externalEscalationEnabled == false',
  '[[ "$ORACLE_CLAMAV_SIGNATURE_VERSION" =~ ^daily-[0-9]+$ ]]',
  '.signature == $signature',
  'command -v convert',
  "-annotate +0+0 'CELAR OCR OK'",
  '"$ORACLE_OCR_ENDPOINT" > "$RUNNER_TEMP/oracle-ocr.json"',
  '($text | contains("CELAR")) and ($text | contains("OCR"))',
]) requireText(workflow, marker, 'live Oracle acceptance')

rejectText(
  workflow,
  '[[ "$ORACLE_CLAMAV_SIGNATURE_VERSION" =~ ^[A-Za-z0-9._:-]{3,120}$ ]]',
  'placeholder-compatible ClamAV attestation',
)

const rollbackMarker = '- name: Rollback protected Test API configuration on failure or cancellation'
const rollbackStart = workflow.indexOf(rollbackMarker)
if (rollbackStart < 0) throw new Error('Could not isolate Protected Test rollback block.')
const rollback = workflow.slice(rollbackStart)
for (const marker of [
  'set -Eeuo pipefail',
  '[[ -n "$OLD_IMAGE" ]]',
  'az containerapp update "${UPDATE[@]}"',
  'ROLLBACK_REVISION="$(az containerapp show',
  'scripts/wait-containerapp-ready-revision.sh "$RESOURCE_GROUP" "$API_APP" "$ROLLBACK_REVISION" "$OLD_IMAGE"',
  'az containerapp secret remove',
  "echo 'PROTECTED_TEST_ROLLBACK=PASS'",
]) requireText(rollback, marker, 'fail-closed rollback contract')
rejectText(rollback, 'set +e', 'rollback error suppression')
rejectText(rollback, '--only-show-errors || true', 'rollback secret-removal error suppression')
const restorePosition = rollback.indexOf('az containerapp update "${UPDATE[@]}"')
const waitPosition = rollback.indexOf('scripts/wait-containerapp-ready-revision.sh')
const secretRemovePosition = rollback.indexOf('az containerapp secret remove')
if (!(restorePosition >= 0 && restorePosition < waitPosition && waitPosition < secretRemovePosition)) {
  throw new Error('Run-scoped secret may only be removed after the previous API image/environment is ready.')
}

for (const marker of [
  'private static readonly Regex ClamAvSignatureVersion',
  '@"^daily-[0-9]+$"',
  'if (!ClamAvSignatureVersion.IsMatch(signatureVersion))',
  'requires a concrete ClamAV daily-<version> signature attestation',
]) requireText(policy, marker, 'application ClamAV signature policy')

for (const marker of [
  'const string ConcreteClamAvSignature = "daily-28087";',
  'Set("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION", "runtime_managed");',
  '"placeholder ClamAV signature evidence is rejected"',
  'Set("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION", "clamav-runtime-managed");',
  '"legacy placeholder ClamAV signature evidence is rejected"',
  'Set("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION", ConcreteClamAvSignature);',
]) requireText(behavior, marker, 'ClamAV signature behavior coverage')

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
console.log('CELAR_AI_ORACLE_PREFLIGHT_CONNECT_PIN=PASS')
console.log('CELAR_AI_ORACLE_CLAMAV_CONCRETE_ATTESTATION=PASS')
console.log('CELAR_AI_ORACLE_OCR_LIVE_PROBE=PASS')
console.log('CELAR_AI_ORACLE_CANCEL_ROLLBACK=PASS')
console.log('CELAR_AI_ORACLE_ROLLBACK_FAILURE_PROPAGATION=PASS')
console.log('PRODUCTION_MUTATION=NONE')
