import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(scriptDirectory, '..');
const policyPath = path.join(frontendRoot, 'src', 'module-navigation-access-policy.js');

if (!fs.existsSync(policyPath)) {
  throw new Error('Shared workspace navigation access policy is missing.');
}

const legacyGrantFilter = "      if (canonicalRoleCode(grant?.actionCode ?? grant?.ActionCode) !== 'MODULE_ACCESS') continue;";
const actionCodeDeclaration = '      const actionCode = canonicalRoleCode(grant?.actionCode ?? grant?.ActionCode);';
const governedGrantFilter = "      if (!['MODULE_ACCESS', 'MODULE_VIEW'].includes(actionCode)) continue;";
const governedGrantContract = `${actionCodeDeclaration}\n${governedGrantFilter}`;

let policy = fs.readFileSync(policyPath, 'utf8');
const legacyCount = policy.split(legacyGrantFilter).length - 1;

if (legacyCount > 1) {
  throw new Error(`Shared workspace navigation policy contains ${legacyCount} legacy grant filters; expected at most one.`);
}

if (legacyCount === 1) {
  if (policy.includes(actionCodeDeclaration) || policy.includes(governedGrantFilter)) {
    throw new Error('Shared workspace navigation policy contains mixed legacy and governed grant contracts.');
  }
  policy = policy.replace(legacyGrantFilter, governedGrantContract);
  fs.writeFileSync(policyPath, policy, 'utf8');
}

const finalPolicy = fs.readFileSync(policyPath, 'utf8');
const declarationCount = finalPolicy.split(actionCodeDeclaration).length - 1;
const governedFilterCount = finalPolicy.split(governedGrantFilter).length - 1;

if (finalPolicy.includes(legacyGrantFilter)) {
  throw new Error('Legacy MODULE_ACCESS-only workspace navigation contract remains after convergence.');
}
if (declarationCount !== 1 || governedFilterCount !== 1) {
  throw new Error(
    `Governed workspace navigation contract is missing or duplicated; declaration=${declarationCount}, filter=${governedFilterCount}.`
  );
}
for (const required of [
  "effect === 'DENY'",
  'explicitDeniedModuleNumbers.add(moduleCode)',
  'actualSessionPermanentFullControl'
]) {
  if (!finalPolicy.includes(required)) {
    throw new Error(`Shared workspace navigation policy lost required authorization behavior: ${required}`);
  }
}

console.log(
  `WORKSPACE_NAVIGATION_POLICY_CONVERGENCE=${legacyCount === 1 ? 'REPAIRED_LEGACY_GENERATION' : 'ALREADY_GOVERNED'} actions=MODULE_ACCESS,MODULE_VIEW explicitDeny=preserved`
);
