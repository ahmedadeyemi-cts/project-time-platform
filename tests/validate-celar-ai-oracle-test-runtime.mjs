import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const resolvePath = (relative) => path.join(root, relative)
const read = (relative) => fs.readFileSync(resolvePath(relative), 'utf8')
const exists = (relative) => fs.existsSync(resolvePath(relative))
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
const controller = read('.github/workflows/projectpulse-deploy-test.yml')
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

const privateTargetStart = capabilityRouting.indexOf('public sealed class CelarAiPrivateGenerationTarget')
const privateTargetEnd = capabilityRouting.indexOf(
  'public sealed record CelarAiPrivateProbeAttestation',
  privateTargetStart,
)
if (privateTargetStart < 0 || privateTargetEnd <= privateTargetStart) {
  throw new Error('Could not isolate CelarAiPrivateGenerationTarget.')
}
const privateTarget = capabilityRouting.slice(privateTargetStart, privateTargetEnd)
const markerCount = (content, value) => content.split(value).length - 1
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
for (const marker of [
  'release_candidate_exact_sow_attestation',
  'request.Feature',
  'PrivateReadinessPhrase = "CELAR PRIVATE MODEL READY"',
  'ProbeReadinessPhraseAsync',
  'module_064_private_model_readiness',
  'ResponseMatchesReadinessPhrase(content)',
  'readiness_phrase_and_model_verified',
  'DeriveContentChallenge(privateContext)',
  'exact_response_and_model_verified',
]) requireText(privateTarget, marker, 'separate reliable readiness and exact-SOW attestations')
rejectText(privateTarget, 'X-Celar-AI-Private-Boundary', 'legacy private-boundary header')
rejectText(privateTarget, 'X-Celar-AI-Feature', 'legacy private-feature header')

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

// Oracle/private runtime configuration is already attached to protected Test. The
// consolidated release controller must preserve it byte-for-byte rather than carry
// a second, unregistered deployment path that can reconfigure credentials/endpoints.
for (const marker of [
  'name: Deploy Pulse Current Main 654af0e to Protected Test',
  'branches: [main]',
  "'.github/workflows/projectpulse-deploy-test.yml'",
  'id-token: write',
  'environment: test',
  'TARGET_RELEASE_COMMIT: 654af0e469f1fba348f7f7dcbac0fde4c5346a59',
  'ref: 654af0e469f1fba348f7f7dcbac0fde4c5346a59',
  'azure/login@',
  'PROJECTPULSE_TEST_UAT_SESSION',
  'private-runtime-before.json',
  'private-runtime-after.json',
  'diff -u "$EVIDENCE_DIR/private-runtime-before.json" "$EVIDENCE_DIR/private-runtime-after.json"',
  'privateRuntimeConfigurationMutation:false',
  'migrationsApplied:[]',
  'PRODUCTION_MUTATION=NONE',
  'Restore exact prior Test images after failure',
]) requireText(controller, marker, 'registered protected-Test release controller')

for (const obsolete of [
  '.github/workflows/celar-ai-oracle-test-runtime-deploy.yml',
  '.github/workflows/celar-ai-oracle-test-runtime-activation-v2.yml',
  '.github/workflows/rerun-celar-ai-oracle-test-after-reference-fix.yml',
]) {
  if (exists(obsolete)) {
    throw new Error(`Obsolete unregistered Oracle deployment workflow remains: ${obsolete}`)
  }
}

rejectText(controller, 'workflow_dispatch:', 'manual deployment trigger')
rejectText(controller, 'environment: production', 'Production environment binding')
rejectText(controller, '--insecure', 'TLS verification bypass')
rejectText(controller, 'curl -k', 'TLS verification bypass')
rejectText(controller, 'PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN', 'Oracle credential mutation')
rejectText(controller, 'PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN=', 'private token mutation')
rejectText(controller, 'PROJECTPULSE_PRIVATE_MODEL_ENDPOINT=', 'private inference endpoint mutation')
rejectText(controller, 'PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT=', 'private embedding endpoint mutation')
rejectText(controller, 'PROJECTPULSE_PRIVATE_OCR_ENDPOINT=', 'private OCR endpoint mutation')
rejectText(controller, 'PROJECTPULSE_PRIVATE_MALWARE_SCAN_ENDPOINT=', 'private malware endpoint mutation')
rejectText(controller, 'az keyvault', 'Key Vault mutation')
rejectText(controller, 'psql', 'database mutation')
rejectText(controller, 'database/migrations', 'migration execution')

for (const marker of [
  'Settings → Environments → test → Environment secrets',
  'PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN',
  'Azure Test API Container App',
  'Do not enter these URLs in Module 064',
]) requireText(docs, marker, 'operator placement guidance')

requireText(openCloud, 'status: deferred-until-opencloud', 'OpenCloud deferral')
requireText(openCloud, 'enabled: false', 'OpenCloud disabled state')

console.log('CELAR_AI_ORACLE_TEST_EXTERNAL_HTTPS_RUNTIME_PRESERVATION_CONTRACT=PASS')
