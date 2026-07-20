import fs from 'node:fs';
import path from 'node:path';
const root = path.resolve(process.cwd(), '..', '..', '..');
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/OneAssistRoutingDirectoryModule.cs',
  native: 'src/backend/ProjectTime.Api/Modules/Module071072NativePersistence.cs',
  frontend: 'src/frontend/project-time-web/src/OneAssistRoutingDirectoryCenter.jsx',
  stylesheet: 'src/frontend/project-time-web/src/oneassist-routing-directory-center.css',
  migration: 'database/migrations/031_modules_071_072_native_persistence.sql',
  rollback: 'database/rollback/031_modules_071_072_native_persistence_rollback.sql',
  program: 'src/backend/ProjectTime.Api/Program.cs',
  app: 'src/frontend/project-time-web/src/App.jsx',
  package: 'src/frontend/project-time-web/package.json',
  docker: 'deployment/containers/web/Dockerfile',
  readme: 'docs/modules/module-072-oneassist-routing-directory/README.md',
  api: 'docs/modules/module-072-oneassist-routing-directory/API-CONTRACT.md',
  auth: 'docs/modules/module-072-oneassist-routing-directory/AUTHORIZATION-MATRIX.md'
};
const checks = [];
const check = (name, condition, evidence) => {
  checks.push(Boolean(condition));
  console.log(`MODULE_072_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
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
const forbidden = ['Cloud' + 'flare', 'CF-' + 'Access-Client', 'PROJECTPULSE_ONEASSIST_' + 'UPSTREAM', 'PROJECTPULSE_ONEASSIST_' + 'ACCESS', 'PROJECTPULSE_ONCALL_' + 'UPSTREAM', 'PROJECTPULSE_ONCALL_' + 'ACCESS'];
check('MAP_METHOD', backend.includes('MapOneAssistRoutingDirectoryEndpoints'), 'isolated endpoint map method');
check('PUBLIC_APIS', backend.includes('/api/public/v1/oneassist/routes') && backend.includes('/api/public/v1/oneassist/resolve'), 'public read APIs preserved');
check('MANAGEMENT_APIS', backend.includes('MapPut') && backend.includes('SaveRoutesAsync'), 'governed directory save');
check('PLATFORM_ADMIN_ROLES', backend.includes('SUPER_ADMINISTRATOR') && backend.includes('ADMINISTRATOR') && backend.includes('MANAGER') && backend.includes('PROJECT_TEAM_COORDINATOR'), 'approved management roles');
check('VIEW_AS_BLOCKED', backend.includes('actual_session_required') && backend.includes('IsViewAs(context)'), 'actual-session writes only');
check('NATIVE_ROUTES', native.includes('projectpulse_oneassist_routes') && migration.includes('projectpulse_oneassist_routes'), 'native routing directory');
check('NATIVE_REVISIONS', native.includes('projectpulse_oneassist_route_revisions') && migration.includes('projectpulse_oneassist_route_revisions'), 'immutable directory revisions');
check('AUDIT', native.includes('projectpulse_module_audit_events') && migration.includes('projectpulse_module_audit_events'), 'sanitized audit evidence');
check('PIN_CONTRACT', backend.includes('Length: 5') && migration.includes("routing_pin ~ '^[0-9]{5}$'") && migration.includes('ux_projectpulse_oneassist_active_pin'), 'five-digit unique routing PIN');
check('NO_EXTERNAL_ADAPTER', forbidden.every((token) => !backend.includes(token) && !frontend.includes(token) && !docs.includes(token)), 'external compatibility removed');
check('FRONTEND_NATIVE', frontend.includes('data-persistence="projectpulse-postgresql"') && frontend.includes('ProjectPulse PostgreSQL application database'), 'native storage visible');
check('IMPORT_PREVIEW', backend.includes('PreviewImportAsync') && frontend.includes('Apply to unsaved directory'), 'preview-first import preserved');
check('US_SIGNAL', frontend.includes('usSignalLogoDataUrl') && frontend.includes('projectpulse-module-standard'), 'US Signal branding');
check('ROLLBACK', rollback.includes('projectpulse_oneassist_routes') && rollback.includes('projectpulse_oneassist_route_revisions'), 'reviewed rollback source');
check('PROGRAM_REGISTRATION', program.split('app.MapOneAssistRoutingDirectoryEndpoints();').length - 1 === 1, 'registered once');
check('APP_MOUNT', app.split('<OneAssistRoutingDirectoryCenter authSession={authSession} />').length - 1 === 1, 'mounted once');
check('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module072') && packageJson.scripts?.build?.includes('validate:modules071072-native'), 'production build guard');
check('CONTAINER_CONTEXT', docker.includes(files.backend) && docker.includes(files.native) && docker.includes(files.migration), 'container receives native source');
check('SCOPED_STYLES', !/(^|\n)\s*(body|html|\.app-shell|\.sidebar|\.topbar)\s*\{/m.test(stylesheet), 'module styles remain scoped');
console.log('');
console.log(`MODULE_072_VALIDATION_CHECKS=${checks.length}`);
console.log('MODULE_072_IMPLEMENTATION=FULL_NATIVE_POSTGRESQL_PACKAGE');
console.log('MODULE_072_EXTERNAL_COMPATIBILITY=REMOVED');
console.log('MODULE_072_PERSISTENCE=MIGRATION_031_SOURCE_NOT_APPLIED');
if (checks.some((value) => !value)) { console.error('MODULE_072_CONTRACT=FAILED'); process.exit(1); }
console.log('MODULE_072_CONTRACT=PASSED');
