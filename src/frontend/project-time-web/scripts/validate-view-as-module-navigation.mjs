import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  PROJECTPULSE_MODULES,
  RETIRED_PROJECTPULSE_MODULES,
  moduleForRoute
} from '../src/module-availability-registry.js';
import { resolveModuleNavigationAccess } from '../src/module-navigation-access-policy.js';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, '../../../..');
const read = (relative) => readFileSync(resolve(repositoryRoot, relative), 'utf8');
const assert = (condition, message) => {
  if (!condition) throw new Error(message);
};

const applicationCodes = PROJECTPULSE_MODULES.map((module) => module.moduleNumber.toUpperCase());
const retiredCodes = RETIRED_PROJECTPULSE_MODULES.map((module) => module.moduleNumber.toUpperCase());
assert(applicationCodes.length > 50, 'The complete application module registry must be evaluated.');
assert(new Set(applicationCodes).size === applicationCodes.length, 'Application module numbers must be unique.');

for (const module of PROJECTPULSE_MODULES) {
  const resolved = moduleForRoute(`#${module.route}`);
  assert(resolved?.moduleNumber === module.moduleNumber, `Route ${module.route} must resolve to Module ${module.moduleNumber}.`);
}

const dynamicModules = [
  { moduleCode: '001', isActive: true },
  { moduleCode: '002', isActive: true },
  { moduleCode: '019', isActive: true },
  { moduleCode: '030', isActive: true },
  { moduleCode: '033', isActive: true },
  { moduleCode: '066', isActive: true },
  { moduleCode: '081', isActive: false }
];
const grants = [
  { roleCode: 'ENGINEER', moduleCode: '001', actionCode: 'MODULE_ACCESS', grantEffect: 'GRANT' },
  { roleCode: 'ENGINEER', moduleCode: '030', actionCode: 'MODULE_ACCESS', grantEffect: 'DENY' },
  { roleCode: 'ENGINEER', moduleCode: '081', actionCode: 'MODULE_ACCESS', grantEffect: 'GRANT' },
  { roleCode: 'PROJECT_MANAGEMENT', moduleCode: '030', actionCode: 'MODULE_ACCESS', grantEffect: 'GRANT' }
];
const legacyFallback = ['002', '019', '033', '066'].map((moduleCode) => ({
  roleCode: 'ENGINEER',
  moduleCode,
  actionCode: 'LEGACY_FALLBACK',
  conditions: { legacyAuthorizationPreserved: true }
}));

const engineerPreview = resolveModuleNavigationAccess({
  applicationModules: PROJECTPULSE_MODULES,
  dynamicModules,
  grants,
  legacyFallback,
  actorRoleCodes: ['ENGINEER'],
  actualSessionPermanentFullControl: false,
  retiredModuleNumbers: retiredCodes
});
const engineerDenied = new Set(engineerPreview.deniedModuleNumbers);
assert(engineerDenied.has('030'), 'An explicit effective-role MODULE_ACCESS denial must block navigation.');
assert(engineerDenied.has('081'), 'An explicitly inactive dynamic module must remain blocked even when a grant exists.');
assert(!engineerDenied.has('001'), 'An explicit effective-role grant must remain navigable.');
for (const moduleCode of ['002', '019', '033', '066']) {
  assert(!engineerDenied.has(moduleCode), `Legacy fallback Module ${moduleCode} must remain endpoint-authorized.`);
}
for (const moduleCode of engineerPreview.unregisteredLegacyModuleNumbers) {
  assert(!engineerDenied.has(moduleCode), `Unregistered Module ${moduleCode} must not be converted into a client-side denial.`);
}
for (const moduleCode of retiredCodes) {
  assert(engineerDenied.has(moduleCode), `Retired Module ${moduleCode} must remain blocked.`);
}

const projectManagerPreview = resolveModuleNavigationAccess({
  applicationModules: PROJECTPULSE_MODULES,
  dynamicModules,
  grants,
  legacyFallback,
  actorRoleCodes: ['PROJECT_MANAGEMENT'],
  actualSessionPermanentFullControl: false,
  retiredModuleNumbers: retiredCodes
});
assert(!new Set(projectManagerPreview.deniedModuleNumbers).has('030'), 'A denial assigned only to another role must not leak into the PM preview.');

const actualAdministrator = resolveModuleNavigationAccess({
  applicationModules: PROJECTPULSE_MODULES,
  dynamicModules,
  grants,
  legacyFallback,
  actorRoleCodes: ['SUPER_ADMINISTRATOR'],
  actualSessionPermanentFullControl: true,
  retiredModuleNumbers: retiredCodes
});
const administratorDenied = new Set(actualAdministrator.deniedModuleNumbers);
assert(!administratorDenied.has('030'), 'Own-session permanent Full Control must override role-policy denials.');
assert(!administratorDenied.has('081'), 'Own-session permanent Full Control preserves administrative inspection of inactive dynamic modules.');
for (const moduleCode of retiredCodes) {
  assert(administratorDenied.has(moduleCode), `Statically retired Module ${moduleCode} must remain retired.`);
}

const classificationCoverage = new Set([
  ...engineerPreview.activeDynamicModuleNumbers,
  ...engineerPreview.inactiveDynamicModuleNumbers,
  ...engineerPreview.unregisteredLegacyModuleNumbers,
  ...retiredCodes
]);
for (const moduleCode of applicationCodes) {
  assert(classificationCoverage.has(moduleCode), `Module ${moduleCode} must have an explicit navigation classification.`);
}

const bridge = read('src/frontend/project-time-web/src/module-availability-bridge.js');
assert(bridge.includes("nativeFetch('/api/rbac/v1/modules?includeInactive=true'"), 'The bridge must load active and inactive dynamic module lifecycle evidence.');
assert(bridge.includes('resolveModuleNavigationAccess'), 'The bridge must use the shared access policy.');
assert(bridge.includes('const sequence = ++refreshSequence;'), 'Permission refreshes must be sequenced.');
assert(bridge.includes('sequence !== refreshSequence'), 'Stale View-As permission responses must be discarded.');
assert(bridge.includes("window.addEventListener('hashchange', applyVisibility);"), 'Route changes must not start redundant permission refreshes.');
assert(!bridge.includes('!activeModuleNumbers.has(number)'), 'Missing dynamic-catalog registration must never become an automatic denial.');
assert(!bridge.includes("window.addEventListener('hashchange', () => {\n    applyVisibility();\n    void refreshPermissions();"), 'Hash navigation must not race a second permission request.');

const backend = read('src/backend/ProjectTime.Api/DynamicRbacAdministrationModule.g.cs');
assert(backend.includes('includeInactive'), 'The dynamic RBAC API must expose inactive lifecycle evidence.');
assert(backend.includes('legacyAuthorizationPreserved = true'), 'The backend legacy-fallback contract must remain explicit.');
assert(backend.includes('Existing endpoint authorization remains in effect'), 'Unconfigured RBAC pairs must remain on endpoint authorization.');

console.log(`VIEW_AS_MODULE_NAVIGATION_REGISTRY_COUNT=${applicationCodes.length}`);
console.log(`VIEW_AS_MODULE_NAVIGATION_UNREGISTERED_LEGACY_COUNT=${engineerPreview.unregisteredLegacyModuleNumbers.length}`);
console.log('VIEW_AS_MODULE_NAVIGATION_CONTINUITY=PASS explicit-deny=true inactive=true legacy-fallback=true unregistered-legacy=true race-safe=true');
