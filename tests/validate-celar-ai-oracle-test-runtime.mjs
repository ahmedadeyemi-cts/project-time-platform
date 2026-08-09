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
const contracts = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeContracts.cs')
const services = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs')
const scanner = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateMalwareScanner.cs')
const runtime = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs')
const workflow = read('.github/workflows/projectpulse-deploy-celar-ai-private-runtime-test.yml')
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

for (const marker of [
  'workflow_dispatch:',
  'DEPLOY-CELAR-AI-ORACLE-RUNTIME-TO-TEST',
  'environment: test',
  'id-token: write',
  'azure/login@',
  'PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN',
  'https://celarai.onenecklab.com/v1/chat/completions',
  'https://celarai.onenecklab.com/v1/embeddings',
  'https://celarai.onenecklab.com/v1/extract',
  'https://celarai.onenecklab.com/v1/scan',
  'https://celarai.onenecklab.com/health',
  '129.213.82.144',
  'Rollback protected Test API configuration on failure',
  'MIGRATIONS_APPLIED=NONE',
  'PRODUCTION_MUTATION=NONE',
]) requireText(workflow, marker, 'guarded Oracle Test deployment workflow')

if (/^\s{2}push:\s*$/m.test(workflow)) {
  throw new Error('The Oracle runtime deployment workflow must remain manual-only.')
}
rejectText(workflow, 'environment: production', 'Production environment binding')
rejectText(workflow, '--insecure', 'TLS verification bypass')
rejectText(workflow, 'curl -k', 'TLS verification bypass')

for (const marker of [
  'Settings → Environments → test → Environment secrets',
  'PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN',
  'Azure Test API Container App',
  'Do not enter these URLs in Module 064',
]) requireText(docs, marker, 'operator placement guidance')

requireText(openCloud, 'status: deferred-until-opencloud', 'OpenCloud deferral')
requireText(openCloud, 'enabled: false', 'OpenCloud disabled state')

console.log('CELAR_AI_ORACLE_TEST_EXTERNAL_HTTPS_RUNTIME_STATIC_CONTRACT=PASS')
