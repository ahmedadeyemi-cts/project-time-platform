import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
const base = execFileSync('git', ['merge-base', process.env.BASE_SHA || 'origin/main', 'HEAD'], { encoding: 'utf8' }).trim();
const actual = execFileSync('git', ['diff', '--name-only', base, 'HEAD'], { encoding: 'utf8' }).trim().split('\n').filter(Boolean).sort();
assert.deepEqual(actual, [
  ".github/workflows/module033-project-forge-ci.yml",
  ".github/workflows/projectpulse-release-test-control-ci-reregistered.yml",
  ".github/workflows/projectpulse-release-test-control-ci.yml",
  "deployment/containers/api/Dockerfile",
  "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
  "src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformService.cs",
  "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
  "tests/validate-celar-ai-pr630-consolidated.mjs",
  "tests/validate-planner-fallback-build-release-scope.mjs"
], 'Planner and verified package-fetch repair must retain its exact nine-file scope');
console.log('PLANNER_FALLBACK_BUILD_RELEASE_SCOPE=PASS');
