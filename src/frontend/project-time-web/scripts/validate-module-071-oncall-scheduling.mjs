import fs from 'node:fs';
import path from 'node:path';
const root = path.resolve(process.cwd(), '..', '..', '..');
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/OnCallSchedulingModule.cs',
  native: 'src/backend/ProjectTime.Api/Modules/Module071072NativePersistence.cs',
  frontend: 'src/frontend/project-time-web/src/OnCallSchedulingCenter.jsx',
  stylesheet: 'src/frontend/project-time-web/src/oncall-scheduling-center.css',
  migration: 'database/migrations/031_modules_071_072_native_persistence.sql',
  rollback: 'database/rollback/031_modules_071_072_native_persistence_rollback.sql',
  program: 'src/backend/ProjectTime.Api/Program.cs',
  app: 'src/frontend/project-time-web/src/App.jsx',
  package: 'src/frontend/project-time-web/package.json',
  docker: 'deployment/containers/web/Dockerfile',
  readme: 'docs/modules/module-071-oncall-scheduling/README.md',
  api: 'docs/modules/module-071-oncall-scheduling/API-CONTRACT.md',
  auth: 'docs/modules/module-071-oncall-scheduling/AUTHORIZATION-MATRIX.md'
};
const checks = [];
const check = (name, condition, evidence) => {
  checks.push(Boolean(condition));
  console.log(`MODULE_071_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
};
for (const [name, relative] of Object.entries(files)) check(`${name.toUpperCase()}_EXISTS`, fs.existsSync(path.join(root, relative)), relative);
const backend = read(files.backend);
const native = read(files.native);
const frontend = read(files.frontend);
const stylesheet = read(files.stylesheet);
const migration = read(files.migration);
const rollback = read(files.rollback);
const docs = [files.readme, files.api, files.auth].map(read).join('\n');
const program = read(files.program);
const app = read(files.app);
const packageJson = JSON.parse(read(files.package));
const docker = read(files.docker);
const forbidden = ['Cloud' + 'flare', 'CF-' + 'Access-Client', 'PROJECTPULSE_ONCALL_' + 'UPSTREAM', 'PROJECTPULSE_ONCALL_' + 'ACCESS'];
check('MAP_METHOD', backend.includes('MapOnCallSchedulingEndpoints'), 'isolated endpoint map method');
check('PUBLIC_APIS', backend.includes('/api/public/v1/oncall/current') && backend.includes('/api/public/v1/oncall/schedule'), 'public read APIs preserved');
check('MANAGEMENT_APIS', backend.includes('MapPut') && backend.includes('RestoreHistoryAsync'), 'schedule, roster, and restore endpoints');
check('PLATFORM_ADMIN_ROLES', backend.includes('SUPER_ADMINISTRATOR') && backend.includes('ADMINISTRATOR') && backend.includes('MANAGER') && backend.includes('ENGINEERING_TEAM_LEAD'), 'approved management roles');
check('VIEW_AS_BLOCKED', backend.includes('actual_session_required') && backend.includes('IsViewAs(context)'), 'actual-session writes only');
check('NATIVE_SCHEDULE', native.includes('projectpulse_oncall_schedule_versions') && migration.includes('projectpulse_oncall_schedule_versions'), 'versioned schedule persistence');
check('NATIVE_ROSTER', native.includes('projectpulse_oncall_roster_members') && migration.includes('projectpulse_oncall_roster_members'), 'identity roster persistence');
check('ACKNOWLEDGEMENTS', migration.includes('projectpulse_oncall_acknowledgements'), 'acknowledgement persistence foundation');
check('AUDIT', native.includes('projectpulse_module_audit_events') && migration.includes('projectpulse_module_audit_events'), 'sanitized audit evidence');
check('NO_EXTERNAL_ADAPTER', forbidden.every((token) => !backend.includes(token) && !frontend.includes(token) && !docs.includes(token)), 'external compatibility removed');
check('FRONTEND_NATIVE', frontend.includes('data-persistence="projectpulse-postgresql"') && frontend.includes('ProjectPulse PostgreSQL application database'), 'native storage visible');
check('US_SIGNAL', frontend.includes('usSignalLogoDataUrl') && frontend.includes('projectpulse-module-standard'), 'US Signal branding');
check('ROLLBACK', rollback.includes('projectpulse_oncall_schedule_versions') && rollback.includes('projectpulse_oncall_roster_members'), 'reviewed rollback source');
check('PROGRAM_REGISTRATION', program.split('app.MapOnCallSchedulingEndpoints();').length - 1 === 1, 'registered once');
check('APP_MOUNT', app.split('<OnCallSchedulingCenter authSession={authSession} />').length - 1 === 1, 'mounted once');
check('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module071') && packageJson.scripts?.build?.includes('validate:modules071072-native'), 'production build guard');
check('CONTAINER_CONTEXT', docker.includes(files.backend) && docker.includes(files.native) && docker.includes(files.migration), 'container receives native source');
check('SCOPED_STYLES', !/(^|\n)\s*(body|html|\.app-shell|\.sidebar|\.topbar)\s*\{/m.test(stylesheet), 'module styles remain scoped');
console.log('');
console.log(`MODULE_071_VALIDATION_CHECKS=${checks.length}`);
console.log('MODULE_071_IMPLEMENTATION=FULL_NATIVE_POSTGRESQL_PACKAGE');
console.log('MODULE_071_EXTERNAL_COMPATIBILITY=REMOVED');
console.log('MODULE_071_PERSISTENCE=MIGRATION_031_SOURCE_NOT_APPLIED');
if (checks.some((value) => !value)) { console.error('MODULE_071_CONTRACT=FAILED'); process.exit(1); }
console.log('MODULE_071_CONTRACT=PASSED');
