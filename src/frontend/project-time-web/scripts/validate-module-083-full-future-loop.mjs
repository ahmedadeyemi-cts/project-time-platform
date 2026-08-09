import './inject-module-083-full-future-loop.mjs';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../../../', import.meta.url));
const rel = (value) => path.join(root, value);
const read = (value) => fs.readFileSync(rel(value), 'utf8');
const files = [
  'src/backend/ProjectTime.Api/Modules/FullFutureLoopModule.cs',
  'src/backend/ProjectTime.Api/Modules/ReleaseDeploymentControlModule.cs',
  'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs',
  'src/frontend/project-time-web/src/FullFutureLoopCenter.jsx',
  'src/frontend/project-time-web/src/full-future-loop-center.css',
  'src/frontend/project-time-web/src/App.jsx',
  'src/frontend/project-time-web/src/module-availability-registry.js',
  'database/migrations/082_module_083_full_future_loop.sql',
  'database/rollback/082_module_083_full_future_loop_rollback.sql',
  'docs/modules/module-083-full-future-loop/README.md',
  'docs/modules/module-083-full-future-loop/TESTING.md'
];
let checks = 0;
let failures = 0;
function test(name, condition) {
  checks += 1;
  if (!condition) failures += 1;
  console.log(`MODULE_083_${name}=${condition ? 'PASSED' : 'FAILED'}`);
}
files.forEach((file) => test(`FILE_${path.basename(file).replace(/\W/g, '_').toUpperCase()}`, fs.existsSync(rel(file))));
const backend = read(files[0]);
const bridge = read(files[1]);
const availability = read(files[2]);
const frontend = read(files[3]);
const css = read(files[4]);
const app = read(files[5]);
const registry = read(files[6]);
const migration = read(files[7]);
const rollback = read(files[8]);

test('ENDPOINTS', ['/capabilities','/access','/summary','/loops','/actions','/run-full-sandbox','/reset','/agent-keep','/history'].every((value) => backend.includes(value)));
test('PERSISTENT_SANDBOX', backend.includes('full_future_loop_items') && backend.includes('full_future_loop_events') && backend.includes('full_future_loop_artifacts'));
test('STATE_MACHINE', ['approve_governance','complete_private_build','run_canary_pass','promote_sandbox','record_production_signal','relay_repair_issue','complete_repair','run_repair_canary_pass','promote_again','verify_close'].every((value) => backend.includes(value)));
test('COMPLETE_LOOP_EXECUTION', backend.includes('full_sandbox_loop_completed') && backend.includes('actionsExecuted') && backend.includes('Automated complete sandbox demonstration'));
test('AGENT_KEEP_BOUNDARY', backend.includes('agent_keep_interaction') && backend.includes('No private source access') && backend.includes('githubMutationEnabled = false'));
test('VIEW_AS_READ_ONLY', backend.includes('EnterpriseGovernanceResults.ViewAsReadOnly') && backend.includes('access.IsViewAs'));
test('NO_EXTERNAL_MUTATION_CLIENT', !backend.includes('HttpClient') && !backend.includes('Octokit') && !backend.includes('Process.Start'));
test('IMMUTABLE_EVIDENCE', migration.includes('pulse082_immutable_full_future_loop_evidence') && migration.includes('full_future_loop_events') && migration.includes('full_future_loop_artifacts'));
test('SANDBOX_CONSTRAINT', migration.includes("CHECK (environment='sandbox')") && backend.includes('productionMutationEnabled = false'));
test('RBAC', ['VIEW_FULL_FUTURE_LOOP_083','RUN_FULL_FUTURE_LOOP_SANDBOX_083','MANAGE_FULL_FUTURE_LOOP_083','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'].every((value) => migration.includes(value)));
test('ROLLBACK_OWNERSHIP', rollback.includes('full_future_loop_082_role_grants') && rollback.includes('full_future_loop_082_permissions_created'));
test('ENDPOINT_BRIDGE', bridge.includes('endpoints.MapFullFutureLoopEndpoints();'));
test('MODULE_AVAILABILITY', availability.includes('["083"] = Module("083", "full-future-loop", "Full Future Loop", "Platform Operations")'));
test('APP_INTEGRATION', app.includes("import FullFutureLoopCenter from './FullFutureLoopCenter.jsx';") && app.includes("activeRoute === 'full-future-loop'") && app.includes('<FullFutureLoopCenter authSession={authSession} />'));
test('APP_ROUTE_ISOLATION', app.includes("        'full-future-loop',") && app.includes('MODULE_083_FULL_FUTURE_LOOP_INSTALLED_REGISTRY_START'));
test('APP_PERMISSION_VISIBILITY', ['VIEW_FULL_FUTURE_LOOP_083','RUN_FULL_FUTURE_LOOP_SANDBOX_083','MANAGE_FULL_FUTURE_LOOP_083','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'].every((value) => app.includes(value)));
test('REGISTRY', registry.includes("moduleNumber: '083'") && registry.includes("route: 'full-future-loop'"));
test('INTERACTIVE_UI', frontend.includes('Run complete loop') && frontend.includes('Agent Keep') && frontend.includes('Create a Full Future Loop test') && frontend.includes('data-module="083"'));
test('LIGHT_DARK_CONTRAST', css.includes("[data-theme='dark'] .ffl-center") && css.includes('--ffl-panel') && css.includes('--ffl-ink'));
test('SAFE_FAILURE_TEST', frontend.includes('run_canary_fail') && frontend.includes('run_repair_canary_fail'));
test('MIGRATION_ID', migration.includes("'082_module_083_full_future_loop'") && backend.includes('082_module_083_full_future_loop'));
console.log(`MODULE_083_VALIDATION_CHECKS=${checks}`);
console.log(`MODULE_083_CONTRACT=${failures ? 'FAILED' : 'PASSED'}`);
process.exitCode = failures ? 1 : 0;
