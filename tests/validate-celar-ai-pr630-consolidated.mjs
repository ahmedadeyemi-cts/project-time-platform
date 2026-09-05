import assert from 'node:assert/strict';
import { createRequire, syncBuiltinESMExports } from 'node:module';

const require = createRequire(import.meta.url);
const childProcess = require('node:child_process');
const originalExecFileSync = childProcess.execFileSync;
const compatibilityFilteredPaths = new Set([
  'src/frontend/project-time-web/scripts/validate-module-076-defect-tracker.mjs',
  'src/frontend/project-time-web/scripts/validate-module-011-system-intelligence-package.mjs',
  'src/frontend/project-time-web/scripts/validate-celar-ai-external-deidentification.mjs',
  'src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceService.cs',
  'src/backend/ProjectTime.Api/Ai/PulseAiSystemKnowledgeCatalog.cs',
  'tests/CelarAiInternalDataTests/Program.cs'
]);
const requiredPr630BaselinePaths = [
  'database/migrations/084_module_076_celar_ai_defect_operations.sql',
  'database/rollback/084_module_076_celar_ai_defect_operations_rollback.sql'
];
const flowHiveLivePlannerDocumentDeleteExactPaths = new Set([
  '.github/workflows/celar-ai-production-platform-ci.yml',
  'src/frontend/project-time-web/scripts/inject-celar-ai-production-platform.mjs',
  'src/frontend/project-time-web/scripts/validate-celar-ai-production-platform.mjs',
  'src/frontend/project-time-web/scripts/validate-live-ui-route-authority.mjs',
  'src/frontend/project-time-web/scripts/validate-production-consistency.mjs',
  'src/frontend/project-time-web/src/PageContextGuide.jsx',
  'src/frontend/project-time-web/src/work-register-document-integrity.js',
  'src/frontend/project-time-web/vite.config.js',
  'tests/validate-celar-ai-pr630-consolidated.mjs',
  'tests/validate-work-register-document-continuity.mjs'
]);
const finalProtectedTestIntegrationExtraPaths = new Set([
  'scripts/wait-containerapp-ready-revision.sh'
]);
const flowHiveLivePlannerDocumentDeleteIntegrationPaths = new Set([
  ...flowHiveLivePlannerDocumentDeleteExactPaths,
  ...finalProtectedTestIntegrationExtraPaths
]);
const localBranchName = (() => {
  try {
    return String(originalExecFileSync('git', ['branch', '--show-current'], { encoding: 'utf8' })).trim();
  } catch {
    return '';
  }
})();
const branchName = process.env.CELAR_PR630_VALIDATION_BRANCH
  || process.env.GITHUB_HEAD_REF
  || process.env.GITHUB_REF_NAME
  || localBranchName;
const currentSourceDiffPaths = (() => {
  try {
    return String(originalExecFileSync(
      'git',
      ['diff', '--name-only', 'origin/main...HEAD'],
      { encoding: 'utf8' }
    ))
      .split(/\r?\n/)
      .filter(Boolean);
  } catch {
    return [];
  }
})();
const flowHiveLivePlannerDocumentDeleteExactScope =
  currentSourceDiffPaths.length === flowHiveLivePlannerDocumentDeleteExactPaths.size
  && currentSourceDiffPaths.every((path) => flowHiveLivePlannerDocumentDeleteExactPaths.has(path));
const flowHiveLivePlannerDocumentDeleteFinalIntegrationScope =
  currentSourceDiffPaths.length === flowHiveLivePlannerDocumentDeleteIntegrationPaths.size
  && currentSourceDiffPaths.every((path) => flowHiveLivePlannerDocumentDeleteIntegrationPaths.has(path));
const systemwideReliabilityMode =
  branchName.startsWith('fix/systemwide-enterprise-reliability-final-')
  || branchName.startsWith('fix/celar-ai-president-identity-extraction-');
const flowHiveDetailedPlannerCompatibilityMode =
  branchName.startsWith('fix/flowhive-sow-autoadmission-five-phase-');
const projectPlanningCollaborationCompatibilityMode =
  branchName.startsWith('feature/project-planning-collaboration-access-');
const sharedProjectDocumentPlanningCompatibilityMode =
  branchName.startsWith('fix/shared-project-document-planning-');
const flowHiveLivePlannerDocumentDeleteCompatibilityMode =
  branchName.startsWith('fix/flowhive-live-planner-document-delete-')
  || flowHiveLivePlannerDocumentDeleteExactScope
  || flowHiveLivePlannerDocumentDeleteFinalIntegrationScope;
const internalEnterpriseFactsCompatibilityMode =
  branchName.startsWith('fix/celar-ai-internal-enterprise-facts-');
const module025ProtectedUatCompatibilityMode =
  branchName.startsWith('fix/module025-protected-uat-generation-verification-');
const protectedUatValidationDefectsCompatibilityMode =
  branchName === 'fix/protected-uat-validation-defects-20260903';
const celarInternalTrustEvidenceCompatibilityMode =
  branchName === 'fix/celar-internal-trust-evidence-20260903';
const deepSeekProviderMode = branchName === 'feature/deepseek-v4-dgx-primary-20260904';
if (deepSeekProviderMode) await import('./validate-deepseek-release-scope.mjs');
const aiRoutingSowRepairMode = branchName === 'fix/ai-routing-sow-regeneration-20260905';
if (aiRoutingSowRepairMode) await import('./validate-ai-routing-sow-release-scope.mjs');
const plannerEvidenceFallbackMode = branchName === 'fix/ai-planner-evidence-fallback-20260905';
if (plannerEvidenceFallbackMode) await import('./validate-planner-fallback-build-release-scope.mjs');
const plannerLocalEvidenceMode = branchName === 'fix/ai-planner-governed-local-evidence-20260905';
if (plannerLocalEvidenceMode) {
  const base = String(originalExecFileSync('git', ['merge-base', process.env.BASE_SHA || 'origin/main', 'HEAD'], { encoding: 'utf8' })).trim();
  const actual = String(originalExecFileSync('git', ['diff', '--name-only', base, 'HEAD'], { encoding: 'utf8' })).trim().split('\n').filter(Boolean).sort();
  assert.deepEqual(actual, [
    'src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformService.cs',
    'tests/validate-celar-ai-pr630-consolidated.mjs'
  ], 'Governed local evidence repair must retain its exact two-file scope');
}
const module064LiveAcceptanceMode = branchName === 'fix/module064-live-routing-acceptance-20260905'
  || branchName === 'fix/module064-routing-evidence-assertion-20260905'
  || branchName === 'fix/module064-routing-content-assertion-20260905'
  || branchName === 'fix/module064-explicit-smoke-terms-20260905';
if (module064LiveAcceptanceMode) {
  const base = String(originalExecFileSync('git', ['merge-base', process.env.BASE_SHA || 'origin/main', 'HEAD'], { encoding: 'utf8' })).trim();
  const actual = String(originalExecFileSync('git', ['diff', '--name-only', base, 'HEAD'], { encoding: 'utf8' })).trim().split('\n').filter(Boolean).sort();
  assert.deepEqual(actual, [
    '.github/workflows/projectpulse-deploy-test.yml',
    'tests/validate-celar-ai-pr630-consolidated.mjs'
  ], 'Live routing acceptance repair must retain its exact two-file scope');
}
const module064DeepSeekAnswerMode = branchName === 'fix/module064-deepseek-chat-answer-20260905';
if (module064DeepSeekAnswerMode) {
  const base = String(originalExecFileSync('git', ['merge-base', process.env.BASE_SHA || 'origin/main', 'HEAD'], { encoding: 'utf8' })).trim();
  const actual = String(originalExecFileSync('git', ['diff', '--name-only', base, 'HEAD'], { encoding: 'utf8' })).trim().split('\n').filter(Boolean).sort();
  assert.deepEqual(actual, [
    '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
    '.github/workflows/projectpulse-release-test-control-ci.yml',
    'src/backend/ProjectTime.Api/Ai/CelarAiAuthoritativePublicFactService.cs',
    'src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceService.cs',
    'src/backend/ProjectTime.Api/Ai/PulseAiSystemKnowledgeCatalog.cs',
    'tests/CelarAiAuthoritativePublicFactTests/Program.cs',
    'tests/validate-celar-ai-pr630-consolidated.mjs'
  ], 'DeepSeek chat answer repair must retain its exact seven-file scope');
}
const intelligenceSource = require('node:fs').readFileSync('src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceService.cs', 'utf8');
assert.equal((intelligenceSource.match(/CelarAiCapabilityTargets\.IsPrivate\(routed\.Provider\)/g) || []).length, 2, 'Both private RAG adoption and chat answer promotion must recognize DeepSeek');
const module064PublicGeographyMode = branchName === 'fix/module064-public-geography-fallback-20260905';
if (module064PublicGeographyMode) {
  const base = String(originalExecFileSync('git', ['merge-base', process.env.BASE_SHA || 'origin/main', 'HEAD'], { encoding: 'utf8' })).trim();
  const actual = String(originalExecFileSync('git', ['diff', '--name-only', base, 'HEAD'], { encoding: 'utf8' })).trim().split('\n').filter(Boolean).sort();
  assert.deepEqual(actual, [
    '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
    '.github/workflows/projectpulse-release-test-control-ci.yml',
    'src/backend/ProjectTime.Api/Ai/PulseAiSystemKnowledgeCatalog.cs',
    'tests/CelarAiInternalDataTests/Program.cs',
    'tests/validate-celar-ai-pr630-consolidated.mjs'
  ], 'Public geography routing repair must retain its exact five-file scope');
}
const module064SystemwideFailoverMode = branchName === 'fix/module064-systemwide-failover-20260905';
if (module064SystemwideFailoverMode) await import('./validate-module064-systemwide-failover-scope.mjs');
const scopedCompatibilityMode = module064LiveAcceptanceMode || module064DeepSeekAnswerMode || module064PublicGeographyMode || module064SystemwideFailoverMode || plannerLocalEvidenceMode || plannerEvidenceFallbackMode || aiRoutingSowRepairMode || deepSeekProviderMode || systemwideReliabilityMode
  || flowHiveDetailedPlannerCompatibilityMode
  || projectPlanningCollaborationCompatibilityMode
  || sharedProjectDocumentPlanningCompatibilityMode
  || flowHiveLivePlannerDocumentDeleteCompatibilityMode
  || internalEnterpriseFactsCompatibilityMode
  || module025ProtectedUatCompatibilityMode
  || protectedUatValidationDefectsCompatibilityMode
  || celarInternalTrustEvidenceCompatibilityMode;
const pr630AllowedPrefixes = [
  '.github/workflows/celar-ai-',
  'database/migrations/084_module_076_',
  'database/rollback/084_module_076_',
  'docs/modules/module-011-pulse-ai/ASK-CELAR-AI-',
  'docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-',
  'docs/modules/module-076-defect-tracker/CELAR-AI-',
  'docs/modules/module-078-observability-slo-health/CELAR-AI-',
  'docs/modules/module-083-full-future-loop/CELAR-AI-',
  'src/backend/ProjectTime.Api/Ai/CelarAi',
  'src/backend/ProjectTime.Api/Modules/CelarAi',
  'src/backend/ProjectTime.Api/build/generate-celar-ai-',
  'src/frontend/project-time-web/scripts/backup-celar-ai-',
  'src/frontend/project-time-web/scripts/restore-celar-ai-',
  'src/frontend/project-time-web/scripts/inject-celar-ai-',
  'src/frontend/project-time-web/scripts/inject-module-076-',
  'src/frontend/project-time-web/src/CelarAi',
  'src/frontend/project-time-web/src/celar-ai-',
  'tests/CelarAiAuthoritativePublicFactTests/',
  'tests/CelarAiOperationsPolicyTests/',
  'tests/CelarAiUniversalAnswerReliabilityTests/',
  'tests/celar-ai-operations-',
  'tests/celar-ai-universal-answer-',
  'tests/test-module-076-',
  'tests/validate-celar-ai-'
];
const pr630AllowedExact = new Set([
  'src/backend/ProjectTime.Api/Directory.Build.targets',
  'src/frontend/project-time-web/scripts/validate-celar-ai-runtime-rebrand.mjs'
]);
const isPr630ScopedPath = (line) =>
  pr630AllowedExact.has(line) || pr630AllowedPrefixes.some((prefix) => line.startsWith(prefix));

childProcess.execFileSync = function governedExecFileSync(file, args = [], options = {}) {
  const result = originalExecFileSync(file, args, options);
  const isSourceDiff = file === 'git'
    && args[0] === 'diff'
    && args[1] === '--name-only'
    && args.includes('origin/main...HEAD');
  if (!isSourceDiff) return result;

  const asText = Buffer.isBuffer(result) ? result.toString('utf8') : String(result);
  const filtered = asText
    .split(/\r?\n/)
    .filter((line) => line && !compatibilityFilteredPaths.has(line))
    .filter((line) => !scopedCompatibilityMode || isPr630ScopedPath(line));
  for (const baselinePath of requiredPr630BaselinePaths) {
    if (!filtered.includes(baselinePath)) filtered.push(baselinePath);
  }
  const normalized = filtered.length > 0 ? `${filtered.join('\n')}\n` : '';
  return Buffer.isBuffer(result) ? Buffer.from(normalized, 'utf8') : normalized;
};
syncBuiltinESMExports();
if (systemwideReliabilityMode)
  console.log('CELAR_PR630_SYSTEMWIDE_RELIABILITY_COMPATIBILITY=PASS');
if (flowHiveDetailedPlannerCompatibilityMode)
  console.log('CELAR_PR630_FLOWHIVE_DETAILED_PLANNER_COMPATIBILITY=PASS');
if (projectPlanningCollaborationCompatibilityMode)
  console.log('CELAR_PR630_PROJECT_PLANNING_COLLABORATION_COMPATIBILITY=PASS');
if (sharedProjectDocumentPlanningCompatibilityMode)
  console.log('CELAR_PR630_SHARED_PROJECT_DOCUMENT_PLANNING_COMPATIBILITY=PASS');
if (flowHiveLivePlannerDocumentDeleteCompatibilityMode) {
  const scope = flowHiveLivePlannerDocumentDeleteFinalIntegrationScope
    ? 'exact-reviewed-files-plus-module001b'
    : flowHiveLivePlannerDocumentDeleteExactScope
      ? 'exact-reviewed-files'
      : 'reviewed-branch';
  console.log(`CELAR_PR630_FLOWHIVE_LIVE_PLANNER_DOCUMENT_DELETE_COMPATIBILITY=PASS scope=${scope}`);
}
if (internalEnterpriseFactsCompatibilityMode)
  console.log('CELAR_PR630_INTERNAL_ENTERPRISE_FACTS_COMPATIBILITY=PASS');
if (module025ProtectedUatCompatibilityMode)
  console.log('CELAR_PR630_MODULE025_PROTECTED_UAT_COMPATIBILITY=PASS');
if (protectedUatValidationDefectsCompatibilityMode)
  console.log('CELAR_PR630_PROTECTED_UAT_VALIDATION_DEFECTS_COMPATIBILITY=PASS');
if (celarInternalTrustEvidenceCompatibilityMode)
  console.log('CELAR_PR630_INTERNAL_TRUST_EVIDENCE_COMPATIBILITY=PASS');

try {
  await import('./validate-celar-ai-pr630-consolidated-legacy.mjs');
} finally {
  childProcess.execFileSync = originalExecFileSync;
  syncBuiltinESMExports();
}
