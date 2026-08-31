import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Compatibility entry point retained for older CI jobs. The current Migration
// 095 and project-scoped access contract are validated by the authoritative
// collaboration-access suite.
await import('./validate-project-planning-collaboration-access.mjs');

const here = path.dirname(fileURLToPath(import.meta.url));
const workRegisterAuthorization = fs.readFileSync(
  path.resolve(here, '../src/backend/ProjectTime.Api/Modules/WorkRegisterAuthorization.cs'),
  'utf8',
);
const flowHiveModule = fs.readFileSync(
  path.resolve(here, '../src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs'),
  'utf8',
);
const registrationCall = 'app.MapProjectPlanningCollaborationEndpoints();';
const countOccurrences = (source, needle) => source.split(needle).length - 1;

const sharedRegistrationCount = countOccurrences(workRegisterAuthorization, registrationCall);
const flowHiveRegistrationCount = countOccurrences(flowHiveModule, registrationCall);

if (sharedRegistrationCount !== 1) {
  throw new Error(
    `Shared startup must register project-planning collaboration endpoints exactly once; found ${sharedRegistrationCount}.`,
  );
}

if (flowHiveRegistrationCount !== 0) {
  throw new Error(
    'Project FlowHive must not register shared project-planning collaboration endpoints a second time.',
  );
}

console.log('project_planning_collaboration_endpoint_registration=PASS');
console.log('project_planning_collaboration_registration_count=1');
console.log('project_planning_collaboration_compatibility=PROJECT_PLANNING_COLLABORATION_V1');
