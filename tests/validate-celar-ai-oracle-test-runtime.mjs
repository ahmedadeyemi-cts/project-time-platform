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

const policy = read('src/backend/ProjectTime.Api/Ai/PulseAiExternalHttpsRuntimePolicy.cs')
const releasePolicy = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiReleaseRuntimePolicy.cs')
const contracts = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeContracts.cs')
const services = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs')
const scanner = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateMalwareScanner.cs')
const runtime = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs')
const embeddings = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateEmbeddingClient.cs')
const capabilityRouting = read('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs')
const currentTestController = read('.github/workflows/projectpulse-deploy-test.yml')
const retiredActivationWorkflow = path.join(
  root,
  '.github/workflows/celar-ai-oracle-test-runtime-deploy.yml',
)
const docs = read('docs/modules/module-011-pulse-ai/ORACLE-TEST-EXTERNAL-HTTPS-RUNTIME.md')
const openCloud = read('deployment/environments/opencloud-template.yml')

for (const marker of [
  'PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_ENABLED',
  'PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_EXPECTED_IP',
  'PROJECTPULSE_PRIVATE_MALWARE_SCAN_ENDPOINT',
  'celarai.onenecklab.com',
  '/v1/chat/completions',
  '/v1/embeddings',
  '/v1/extract',
  '/v1/scan',
  '/health',
  'PROJECTPULSE_ENVIRONMENT is exactly test',
  'external_https_dns_pin_mismatch',
  'TryGetPinnedAddress',
]) requireText(policy, marker, 'external HTTPS policy')

requireText(releasePolicy, 'github-environment://', 'GitHub Environment token provenance scheme')
requireText(services, 'PulseAiExternalRuntimeReadiness', 'authenticated startup readiness client')
requireText(services, 'PulseAiPrivateMalwareScan', 'authenticated malware-scan client')
requireText(services, 'PulseAiExternalHttpsRuntimeGuard', 'startup guard registration')
requireText(services, 'TryGetPinnedAddress', 'connect-time public IPv4 pin')
requireText(services, 'AllowAutoRedirect = false', 'redirect prohibition')
rejectText(services, 'DangerousAcceptAnyServerCertificateValidator', 'TLS validation bypass')
rejectText(services, 'ServerCertificateCustomValidationCallback', 'custom certificate-validation bypass')

requireText(contracts, 'HttpsMalwareScanConfigured', 'HTTPS malware scanner option')
requireText(contracts, 'MalwareScannerConfigured', 'unified scanner readiness')
requireText(contracts, 'PulseAiExternalHttpsRuntimePolicy.VerifyEndpointAsync', 'external endpoint preflight')
requireText(scanner, 'ScanWithHttpsGatewayAsync', 'HTTPS malware scan implementation')
requireText(scanner, 'scanner_response_invalid', 'fail-closed malformed response')
requireText(scanner, 'X-Pulse-AI-Privacy-Boundary', 'privacy boundary header')
requireText(scanner, 'MaximumGatewayResponseBytes', 'bounded scanner response')
requireText(runtime, 'authenticated Test-only HTTPS malware scanning gateway', 'runtime readiness evidence')

const privateTargetStart = capabilityRouting.indexOf(
  'public sealed class CelarAiPrivateGenerationTarget',
)
const privateTargetEnd = capabilityRouting.indexOf(
  'public sealed record CelarAiPrivateProbeAttestation',
  privateTargetStart,
)
if (privateTargetStart < 0 || privateTargetEnd <= privateTargetStart) {
  throw new Error('Could not isolate CelarAiPrivateGenerationTarget.')
}
const privateTarget = capabilityRouting.slice(privateTargetStart, privateTargetEnd)
const markerCount = (content, value) => content.split(value).length - 1
// Module 064 provider readiness uses a fixed identity-free phrase, while
// release-candidate verification retains the separate content-derived SOW challenge.
for (const [marker, expected] of [
  ['X-Pulse-AI-Privacy-Boundary', 3],
  ['PulseAiPrivateRagPolicy.PrivacyBoundary', 3],
  ['X-Pulse-AI-Feature', 3],
  ['X-Pulse-AI-Correlation-Id', 3],
  ['X-Pulse-AI-External-Escalation', 3],
]) {
  const actual = markerCount(privateTarget, marker)
  if (actual !== expected) {
    throw new Error(`Expected ${expected} ${marker} markers; found ${actual}.`)
  }
}
requireText(
  privateTarget,
  'release_candidate_exact_sow_attestation',
  'exact private-model attestation feature',
)
requireText(privateTarget, 'request.Feature', 'runtime private-generation feature')
for (const marker of [
  'PrivateReadinessPhrase = "CELAR PRIVATE MODEL READY"',
  'ProbeReadinessPhraseAsync',
  'module_064_private_model_readiness',
  'ResponseMatchesReadinessPhrase(content)',
  'readiness_phrase_and_model_verified',
  'DeriveContentChallenge(privateContext)',
  'exact_response_and_model_verified',
]) requireText(privateTarget, marker, 'separate reliable readiness and exact-SOW attestations')
rejectText(
  privateTarget,
  'X-Celar-AI-Private-Boundary',
  'legacy private-boundary header',
)
rejectText(
  privateTarget,
  'X-Celar-AI-Feature',
  'legacy private-feature header',
)

for (const marker of [
  'ParseObjectEnvelope',
  'ParseArrayEnvelope',
  'ParseEmbeddingItems',
  'TryReadVector',
  'HasConsistentDimension',
  'JsonValueKind.Number',
  'double.IsNaN',
  'double.IsInfinity',
  '!indexed.TryAdd(index, vector)',
]) requireText(embeddings, marker, 'fail-closed Oracle embedding response compatibility')
rejectText(embeddings, 'indexed[index] = values', 'duplicate-index overwrite')

if (fs.existsSync(retiredActivationWorkflow)) {
  throw new Error(
    'The completed one-time Oracle Test activation controller must remain retired; ' +
    'the current protected-Test controller preserves the already-approved runtime binding.',
  )
}

for (const marker of [
  'environment: test',
  'group: projectpulse-deploy-test',
  'cancel-in-progress: false',
  'runtime-contract-before.json',
  'runtime-contract-after.json',
  'diff -u "$EVIDENCE_DIR/runtime-contract-before.json" "$EVIDENCE_DIR/runtime-contract-after.json"',
  'PROJECTPULSE_SOURCE_COMMIT="$TARGET_RELEASE_COMMIT"',
  'applicationOnlyAfterMigration:true',
  'PRODUCTION_MUTATION=NONE',
  'privateRuntimeConfigurationMutation:false',
  'Restore exact prior Test images after failure',
]) requireText(currentTestController, marker, 'current protected-Test runtime-preservation controller')

for (const marker of [
  'workflow_dispatch:',
  'release_sha:',
  'release_branch:',
  'fix/shared-project-document-planning-20260819',
  '^[0-9a-f]{40}$',
  'current authorized branch head',
]) requireText(currentTestController, marker, 'guarded exact-SHA Protected Test dispatch')

for (const marker of [
  'PROJECTPULSE_(PRIVATE_|PULSE_AI_PRIVATE_|PULSE_AI_DOCUMENT_|PULSE_AI_CLAMAV|CELAR_AI_|UPLOAD_ROOT)',
  'CELAR_AI_',
]) requireText(currentTestController, marker, 'private-runtime environment preservation allowlist')

rejectText(currentTestController, 'environment: production', 'Production environment binding')
rejectText(currentTestController, 'az keyvault', 'unapproved key-vault access')
rejectText(currentTestController, 'PROJECTPULSE_TEST_DATABASE_URL', 'database-secret access')
rejectText(currentTestController, 'celarai.onenecklab.com', 'Oracle runtime endpoint mutation')
rejectText(currentTestController, 'PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN', 'Oracle runtime token mutation')
rejectText(currentTestController, '--insecure', 'TLS verification bypass')
rejectText(currentTestController, 'curl -k', 'TLS verification bypass')

for (const marker of [
  'Settings → Environments → test → Environment secrets',
  'PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN',
  'Azure Test API Container App',
  'Do not enter these URLs in Module 064',
]) requireText(docs, marker, 'operator placement guidance')

requireText(openCloud, 'status: deferred-until-opencloud', 'OpenCloud deferral')
requireText(openCloud, 'enabled: false', 'OpenCloud disabled state')

console.log('CELAR_AI_ORACLE_TEST_EXTERNAL_HTTPS_RUNTIME_STATIC_CONTRACT=PASS')
console.log('CELAR_AI_ORACLE_TEST_ONE_TIME_ACTIVATION_CONTROLLER=RETIRED')
console.log('CELAR_AI_ORACLE_TEST_CURRENT_CONTROLLER=PRIVATE_RUNTIME_PRESERVING')
