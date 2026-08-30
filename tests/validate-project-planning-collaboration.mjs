import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Compatibility entry point retained for older CI jobs. The current Migration
// 095 and project-scoped access contract are validated by the authoritative
// collaboration-access suite.
await import('./validate-project-planning-collaboration-access.mjs');

const here = path.dirname(fileURLToPath(import.meta.url));
const flowHiveModule = fs.readFileSync(
  path.resolve(here, '../src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs'),
  'utf8',
);

if (!flowHiveModule.includes('app.MapProjectPlanningCollaborationEndpoints();')) {
  throw new Error(
    'Project FlowHive startup mapping must register the shared project-planning collaboration endpoints.',
  );
}

console.log('project_planning_collaboration_endpoint_registration=PASS');
console.log('project_planning_collaboration_compatibility=PROJECT_PLANNING_COLLABORATION_V1');
