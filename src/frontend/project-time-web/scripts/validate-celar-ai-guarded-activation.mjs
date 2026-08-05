import fs from 'node:fs';

const root = new URL('../../../../', import.meta.url);
const read = (path) => fs.readFileSync(new URL(path, root), 'utf8');
const workflow = read('.github/workflows/celar-ai-guarded-activation.yml');
const activation = read('scripts/run-celar-ai-guarded-activation.sh');
const job = read('scripts/run-celar-ai-containerapp-job.sh');
const probe = read('scripts/celar-ai-private-network-probe.sh');
const migrations = read('scripts/celar-ai-apply-required-migrations.sh');
const probeDockerfile = read('deployment/containers/celar-ai-probe/Dockerfile');
const migratorDockerfile = read('deployment/containers/celar-ai-migrator/Dockerfile');
const apiDockerfile = read('deployment/containers/api/Dockerfile');
const webDockerfile = read('deployment/containers/web/Dockerfile');
const routingModule = read('src/backend/ProjectTime.Api/Modules/CelarAiCapabilityRoutingModule.cs');
const routing = read('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs');
const hardeningRunbook = read('docs/modules/module-064-ai-provider-configuration/CELAR-AI-PRODUCTION-HARDENING.md');
const controlOnlyPaths = [
  '.github/workflows/celar-ai-guarded-activation.yml',
  'scripts/celar-ai-apply-required-migrations.sh',
  'scripts/celar-ai-private-network-probe.sh',
  'scripts/run-celar-ai-guarded-activation.sh',
  'scripts/run-celar-ai-containerapp-job.sh',
  'deployment/containers/celar-ai-probe/Dockerfile',
  'deployment/containers/celar-ai-migrator/Dockerfile',
  'deployment/containers/api/Dockerfile',
  'deployment/containers/web/Dockerfile',
  'src/frontend/project-time-web/scripts/validate-celar-ai-guarded-activation.mjs',
  'docs/modules/module-064-ai-provider-configuration/CELAR-AI-PRODUCTION-HARDENING.md',
];

const checks = new Map([
  ['EXACT_MAIN_SHA', workflow.includes('refs/remotes/origin/main') && workflow.includes('expected_main_sha')],
  ['TWO_SHA_RELEASE_AUTHORITY', workflow.includes('expected_source_sha:') && workflow.includes('PROJECTPULSE_EXPECTED_RELEASE_SHA: ${{ inputs.expected_source_sha }}') && workflow.includes('PROJECTPULSE_EXPECTED_CONTROL_SHA: ${{ inputs.expected_main_sha }}') && activation.includes('Application source and activation-control SHAs must be distinct') && job.includes('A distinct exact control SHA is required')],
  ['SOURCE_ANCESTOR_OF_CONTROL', workflow.includes('git merge-base --is-ancestor "$PROJECTPULSE_EXPECTED_RELEASE_SHA" "$PROJECTPULSE_EXPECTED_CONTROL_SHA"')],
  ['CONTROL_ONLY_COMMIT_RANGE', workflow.includes('git diff --no-renames --name-only "$PROJECTPULSE_EXPECTED_RELEASE_SHA..$PROJECTPULSE_EXPECTED_CONTROL_SHA"') && workflow.includes('Unauthorized source/control delta') && controlOnlyPaths.every((path) => workflow.includes(path))],
  ['DETACHED_EXACT_SOURCE_BUILD', workflow.includes('git worktree add --detach "$source_root" "$PROJECTPULSE_EXPECTED_RELEASE_SHA"') && workflow.includes('git -C "$source_root" diff --exit-code') && workflow.includes('CELAR_AI_EXACT_DETACHED_SOURCE_BUILD=PASSED')],
  ['SOURCE_CONTRACT_VALIDATORS', workflow.includes('node ./scripts/validate-celar-ai-production-readiness.mjs') && workflow.includes('node ./scripts/validate-module-001-ai-task-grounding.mjs') && workflow.includes('node ./scripts/validate-celar-ai-external-deidentification.mjs')],
  ['SOURCE_API_BUILD_CONTEXT', workflow.includes('--file "$source_root/deployment/containers/api/Dockerfile" "$source_root"') && workflow.includes('install -m 0644 deployment/containers/api/Dockerfile "$source_root/deployment/containers/api/Dockerfile"') && workflow.includes('cmp -s deployment/containers/api/Dockerfile')],
  ['SOURCE_MIGRATION_CONTENT', workflow.includes('cp "$source_root"/database/migrations/') && workflow.includes('release-commit')],
  ['TWO_SHA_EVIDENCE', workflow.includes('Exact application source SHA: $PROJECTPULSE_EXPECTED_RELEASE_SHA') && workflow.includes('Exact current-main control SHA: $PROJECTPULSE_EXPECTED_CONTROL_SHA')],
  ['PROTECTED_ENVIRONMENT', workflow.includes('environment: ${{ inputs.environment }}')],
  ['PREFLIGHT_ACTIVATE_MODES', workflow.includes('options: [preflight, activate]')],
  ['TYPED_ENVIRONMENT_CONFIRMATION', workflow.includes('ACTIVATE-CELAR-AI-PRODUCTION') && activation.includes('expected_confirmation')],
  ['CHANGE_TICKET_EVIDENCE', workflow.includes('change_ticket') && workflow.includes('Authorized by: $GITHUB_ACTOR')],
  ['OIDC_ONLY', workflow.includes('id-token: write') && !workflow.includes('AZURE_CLIENT_SECRET')],
  ['EPHEMERAL_GIT_FETCH_AUTH', workflow.includes('persist-credentials: false') && workflow.includes("credential.helper='!f()") && workflow.includes('GITHUB_TOKEN: ${{ github.token }}')],
  ['IMMUTABLE_DIGESTS', activation.includes('@sha256:') && workflow.includes('manifest show-metadata')],
  ['DIGEST_PINNED_ACTIVATION_BASES', workflow.includes('PROJECTPULSE_CELAR_API_SDK_BASE_IMAGE') && workflow.includes('PROJECTPULSE_CELAR_API_RUNTIME_BASE_IMAGE') && workflow.includes('PROJECTPULSE_CELAR_PROBE_BASE_IMAGE') && workflow.includes('PROJECTPULSE_CELAR_MIGRATOR_BASE_IMAGE') && activation.includes('valid_base_digest') && probeDockerfile.includes('FROM ${CELAR_PROBE_BASE_IMAGE}') && migratorDockerfile.includes('FROM ${CELAR_MIGRATOR_BASE_IMAGE}') && apiDockerfile.includes('FROM ${PROJECTPULSE_DOTNET_SDK_BASE_IMAGE}') && apiDockerfile.includes('FROM ${PROJECTPULSE_DOTNET_ASPNET_BASE_IMAGE}')],
  ['WEB_BASE_OVERRIDE_SUPPORTED', webDockerfile.includes('FROM ${PROJECTPULSE_NODE_BASE_IMAGE}') && webDockerfile.includes('FROM ${PROJECTPULSE_NGINX_BASE_IMAGE}')],
  ['EXACT_SOURCE_WEB_PREREQUISITE', workflow.includes('AZURE_WEB_APP: ${{ vars.AZURE_WEB_APP }}') && activation.includes('PROJECTPULSE_SOURCE_COMMIT') && activation.includes('exactly one active revision') && activation.includes('.properties.healthState == "Healthy"') && activation.includes('web_source" == "$expected_sha')],
  ['SERVED_WEB_TASK_GROUNDING_MARKERS', activation.includes('/api/timesheets/ai-description-suggestions') && activation.includes('nonProjectTimeCategoryId') && activation.includes('Generate a customer-facing description')],
  ['SERVED_WEB_CELAR_UI_MARKERS', activation.includes('AI Provider Configuration Center') && activation.includes('Enable the private Celar AI target') && activation.includes('Default: Celar AI')],
  ['WEB_NO_CACHE_PROOF', activation.includes("-H 'Cache-Control: no-cache'") && activation.includes('index.html?celar_source=$expected_sha') && activation.includes('fail-closed cache policy')],
  ['WEB_REVERIFIED_AT_PROMOTION', workflow.includes('PROJECTPULSE_CELAR_ACTIVATION_MODE=verify-web') && workflow.includes('The exact-source web prerequisite changed before API promotion') && workflow.indexOf('PROJECTPULSE_CELAR_ACTIVATION_MODE=verify-web') < workflow.indexOf('az containerapp ingress traffic set', workflow.indexOf('Promote exact proven candidate'))],
  ['ACTIVATION_NEVER_MUTATES_WEB', !workflow.includes('project-health-dashboard-web') && !activation.includes('containerapp update -g "$resource_group" -n "$web_app"') && !activation.includes('ingress traffic set -g "$resource_group" -n "$web_app"') && workflow.includes('Web image changed by this activation: no')],
  ['WEB_EVIDENCE_RECORDED', workflow.includes('Active exact-source web revision:') && workflow.includes('Active exact-source web image:') && workflow.includes('Active exact-source web source SHA:')],
  ['WEB_PREREQUISITE_OPERATIONAL_RUNBOOK', hardeningRunbook.includes('standard exact-release controller') && hardeningRunbook.includes('PROJECTPULSE_SOURCE_COMMIT=<expected_source_sha>') && hardeningRunbook.includes('single-revision mode') && hardeningRunbook.includes('read-only for web') && hardeningRunbook.includes('fails activation closed without mutating web')],
  ['NON_ROOT_ACTIVATION_IMAGES', probeDockerfile.includes('USER 10001:10001') && migratorDockerfile.includes('USER postgres')],
  ['MIGRATIONS_EXACT', migrations.includes('052_document_intelligence_runtime.sql') && migrations.includes('053_intelligence_answer_orchestration.sql') && migrations.includes('061_celar_ai_capability_routing.sql')],
  ['MIGRATION_071_ADDITIVE', migrations.includes('071_ai_runtime_production_hardening.sql')],
  ['PROJECT_FORGE_070_NOT_APPLIED', !migrations.includes('\\ir database/migrations/070_module_033_project_forge.sql')],
  ['MIGRATION_ADVISORY_LOCK', migrations.includes('pg_advisory_lock') && migrations.includes('pg_advisory_unlock')],
  ['MIGRATION_CHECKSUMS', migrations.includes('sha256sum --check --strict')],
  ['NO_DB_ROLLBACK', !migrations.includes('rollback/071') && workflow.includes('databaseRollback=false')],
  ['EPHEMERAL_STORAGE_REJECTED', activation.includes('/dev/shm') && activation.includes('/var/tmp') && activation.includes('/run')],
  ['CROSS_REPLICA_CANARY', workflow.includes('storage-write') && workflow.includes('storage-read')],
  ['PRIVATE_DNS_ONLY', probe.includes('DNS returned a public address') && !probe.includes('^127\\.') && !probe.includes('fe80:')],
  ['PRIVATE_HOST_ALLOWLIST_PROBED', probe.includes('host_allowlisted') && probe.includes('protected private allowlist') && job.includes('add_value PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST')],
  ['PROBE_DNS_RESULT_PINNED', probe.includes('--resolve "$host:$port:$pinned_ip"') && probe.includes('nc -z -w 5 "${clam_ips[0]}"')],
  ['TLS_NO_REDIRECT', probe.includes("--tlsv1.2 --max-time 30 --max-redirs 0")],
  ['EMBEDDING_RUNTIME_PAYLOAD', probe.includes('input:["Celar AI private embedding activation probe."]') && probe.includes('encoding_format:"float"') && probe.includes('.data[0].embedding') && probe.includes('non-empty numeric vector')],
  ['SCANNER_RUNTIME_ENV_NAMES', activation.includes('PROJECTPULSE_PULSE_AI_CLAMAV_HOST') && activation.includes('PROJECTPULSE_PULSE_AI_CLAMAV_PORT') && !activation.includes('{name:"PROJECTPULSE_CLAMAV_HOST"')],
  ['MODULE064_FRESH_PROBE', probe.includes('private_model_available') && probe.includes('productionReadiness.ready == true')],
  ['AUTOQUEUE_PRINCIPAL_AUTHORIZED', probe.includes('productionReadiness.processing.servicePrincipalAuthorized == true')],
  ['SOW_READY_GATE', probe.includes('readySowDocumentCount >= 1')],
  ['EXACT_SOW_REPROCESS_APPROVAL', probe.includes('--arg expectedSourceSha256 "$expected_hash"') && probe.includes('expectedSourceSha256:$expectedSourceSha256') && probe.includes('APPROVE-PULSE-AI-PRIVATE-DOCUMENT-VERSION') && probe.includes('document_version_approved') && probe.includes('activeVersionSourceSha256 == $hash') && probe.includes('documentCategory | ascii_downcase') && probe.includes('activeChunkCount >= 1')],
  ['TIMESHEET_ROUTE_ORDER', probe.includes('["celar_ai","claude","openai","local_template"]')],
  ['EXTERNAL_PRIVACY_APPROVAL', activation.includes('PROJECTPULSE_CELAR_PRIVACY_APPROVAL_REFERENCE') && activation.includes('PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION') && activation.includes('PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED')],
  ['E2E_ID_ONLY', probe.includes('End-to-end request must use IDs only') && probe.includes('has("customerName") | not')],
  ['E2E_CUSTOMER_READY_SENTENCES', probe.includes('length >= 120 and length <= 1500') && probe.includes('($sentences | length) >= 2') && probe.includes('($sentences | length) <= 4') && probe.includes('customerReady=true sentences=2-4')],
  ['E2E_NO_MARKDOWN_OR_INTERNAL_LEAKAGE', probe.includes('```|`|\\\\[[^]]+') && probe.includes('projectpulse|celar[ -]?ai|claude|openai') && probe.includes('internalIdentifiers=false') && probe.includes('/api/|https?://')],
  ['SOW_POLLS_HAVE_ONE_DELAY_EACH', (probe.match(/^\s*sleep 15\s*$/gm) || []).length === 2 && !/sleep 15\s+sleep 15/.test(probe)],
  ['API_CANDIDATE_SOURCE_STAMP', activation.includes('{name:"PROJECTPULSE_SOURCE_COMMIT",value:$source}') && activation.includes('candidate_source" == "$expected_sha') && activation.includes('does not expose exactly one PROJECTPULSE_SOURCE_COMMIT value')],
  ['API_SOURCE_REVERIFIED_AT_PROMOTION', workflow.includes('candidate_metadata="$(az containerapp revision show') && workflow.includes('candidate_source" == "$PROJECTPULSE_EXPECTED_RELEASE_SHA"') && workflow.includes('The candidate API source commit changed') && workflow.indexOf('candidate_source="$(jq -er', workflow.indexOf('Promote exact proven candidate')) < workflow.indexOf('az containerapp ingress traffic set', workflow.indexOf('Promote exact proven candidate'))],
  ['CANDIDATE_ZERO_TRAFFIC', activation.includes('$candidate_revision=0') && workflow.includes('$candidate=100')],
  ['SINGLE_CANDIDATE_STAGE_STEP', (workflow.match(/^\s*- name: Stage zero-traffic candidate\s*$/gm) || []).length === 1 && /- name: Stage zero-traffic candidate\s+id: candidate/.test(workflow)],
  ['UNAMBIGUOUS_WEIGHTED_BASELINE', activation.includes('one unambiguous 100-percent baseline revision') && activation.includes('latest_revision') && activation.includes('An unpromoted later revision exists')],
  ['CANDIDATE_ONLY_ROLLBACK', workflow.includes('Candidate-only rollback on failure')],
  ['ROLLBACK_LATER_RELEASE_GUARD', workflow.includes('A later weighted revision exists')],
  ['STAGING_ROLLBACK_LATER_RELEASE_GUARD', activation.includes('later_release_or_traffic_change') && activation.includes('all(.[]; .revisionName == $current or .revisionName == $candidate)')],
  ['SHARED_DEPLOYMENT_CONCURRENCY', workflow.includes('group: projectpulse-deploy-${{ inputs.environment }}')],
  ['PROMOTION_REAUTHORIZATION', workflow.includes('Control main changed after candidate verification') && workflow.includes('A later API revision exists') && workflow.includes('Traffic changed after candidate verification') && workflow.includes('properties.latestRevisionName') && workflow.includes('authorized source/control ancestry changed')],
  ['DEDICATED_JOB_IDENTITIES', workflow.includes('AZURE_CELAR_MIGRATOR_IDENTITY_RESOURCE_ID') && workflow.includes('AZURE_CELAR_PROBE_IDENTITY_RESOURCE_ID') && workflow.includes('AZURE_CELAR_APPLICATION_IDENTITY_RESOURCE_ID') && job.includes('userAssignedIdentities') && job.includes('job_identity') && !job.includes("identity=\"$(jq -c '.identity' \"$app\")\"")],
  ['API_KEYVAULT_IDENTITY_ATTACHED', activation.includes('api_user_identities') && activation.includes('API Key Vault identity is not attached')],
  ['KEY_VAULT_REFS', activation.includes('keyvaultref:') && activation.includes('identityref:')],
  ['KEY_VAULT_REFS_VERSION_PINNED', activation.includes('secret-name/version') && job.includes('secret-name/version')],
  ['ENCRYPTION_VALUE_NOT_READ_BY_RUNNER', activation.includes('valueReadByRunner=false') && !activation.includes('--query value') && probe.includes('productionReadiness.ready == true')],
  ['CIPHERTEXT_KEY_ID_FENCE', migrations.includes('provider-ciphertext-key-id') && migrations.includes('private-profile-ciphertext-key-id')],
  ['SECRET_VALUES_NOT_INPUTS', !workflow.includes('private_inference_token:') && !workflow.includes('encryption_key:')],
  ['NO_REMOTE_SECRET_LOG_ECHO', !probe.includes('::add-mask::')],
  ['PUBLIC_PROVIDER_LIVE_HEALTH', probe.includes('/api/ai-configuration/health/refresh') && probe.includes('.provider == "claude"') && probe.includes('.provider == "openai"') && probe.includes('.probeStatus == "available"')],
  ['SANITIZED_EXTERNAL_FALLBACK_E2E', probe.includes('/api/ai-configuration/sanitized-external-fallback/production-test') && probe.includes('sanitized_external_fallback_production_probe_succeeded') && probe.includes('{provider:"claude", status:"sanitized_generation_succeeded"}') && probe.includes('{provider:"openai", status:"sanitized_generation_succeeded"}') && probe.includes('.policy.providerContentReturned == false') && probe.includes('.policy.sharedRouteChanged == false') && routingModule.includes('/api/ai-configuration/sanitized-external-fallback/production-test') && routingModule.includes('providerContentReturned = false') && routing.includes('fixedGenericCapsule') && routing.includes('sanitized_generation_succeeded')],
]);

let failed = 0;
for (const [name, ok] of checks) {
  console.log(`${name}=${ok ? 'PASSED' : 'FAILED'}`);
  if (!ok) failed += 1;
}
if (failed) process.exitCode = 1;
else console.log(`CELAR_AI_GUARDED_ACTIVATION_VALIDATION=PASSED (${checks.size}/${checks.size})`);
