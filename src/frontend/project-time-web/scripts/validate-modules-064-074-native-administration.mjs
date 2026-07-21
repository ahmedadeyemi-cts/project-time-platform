import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const frontend = path.resolve(scriptDirectory, '..');
const repository = path.resolve(frontend, '..', '..', '..');
const read = (relative) => fs.readFileSync(path.join(repository, relative), 'utf8');
const exists = (relative) => fs.existsSync(path.join(repository, relative));
const checks = [];
const check = (name, condition, evidence) => {
  checks.push(Boolean(condition));
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
};

const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/Module064074NativeAdministration.cs',
  panel: 'src/frontend/project-time-web/src/NativeModuleAdministrationPanel.jsx',
  styles: 'src/frontend/project-time-web/src/native-module-administration.css',
  app: 'src/frontend/project-time-web/src/App.jsx',
  program: 'src/backend/ProjectTime.Api/Program.cs',
  package: 'src/frontend/project-time-web/package.json',
  docker: 'deployment/containers/web/Dockerfile',
  migration: 'database/migrations/032_projectpulse_native_administration_documents.sql',
  rollback: 'database/rollback/032_projectpulse_native_administration_documents_rollback.sql',
  readme: 'docs/modules/modules-064-074-native-administration/README.md',
  catalog: 'docs/MODULE-CATALOG.md',
  register: 'docs/MODULE-WORK-REGISTER.md',
  tracker: 'docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md'
};

for (const [name, relative] of Object.entries(files)) {
  check(`MODULES_064_074_NATIVE_${name.toUpperCase()}_EXISTS`, exists(relative), relative);
}

const backend = read(files.backend);
const panel = read(files.panel);
const styles = read(files.styles);
const app = read(files.app);
const program = read(files.program);
const packageJson = JSON.parse(read(files.package));
const docker = read(files.docker);
const migration = read(files.migration);
const rollback = read(files.rollback);
const docs = [files.readme, files.catalog, files.register, files.tracker].map(read).join('\n');

const moduleNumbers = ['064', '065', '066', '067', '068', '069', '070', '073', '074'];
const routeMap = {
  '064': 'ai-provider-configuration',
  '065': 'entra-secret-administration',
  '067': 'global-mail-configuration',
  '068': 'system-architecture',
  '069': 'qualifications-certifications',
  '070': 'capacity-pipeline-forecast',
  '073': 'sales-coverage-alignment',
  '074': 'oem-vendor-directory'
};

check('MODULES_064_074_NATIVE_MODULE_SET', moduleNumbers.every((module) => backend.includes(`["${module}"]`) && migration.includes(`'${module}'`)), 'all nine remaining module numbers');
check('MODULES_064_074_NATIVE_ENDPOINTS', [
  '/api/native-administration/{moduleNumber}/schema',
  '/api/native-administration/{moduleNumber}/document',
  '/api/native-administration/{moduleNumber}/history',
  '/api/native-administration/{moduleNumber}/history/{revisionId:guid}/restore'
].every((route) => backend.includes(route)), 'schema, document, history, and restore routes');
check('MODULES_064_074_NATIVE_WRITE_AUTHORITY', backend.includes('SUPER_ADMINISTRATOR') && backend.includes('ADMINISTRATOR') && backend.includes('actual ProjectPulse session'), 'platform administrators and actual-session authority');
check('MODULES_064_074_NATIVE_VIEW_AS_BLOCK', backend.includes('actual_session_required') && backend.includes('IsViewAs(context)'), 'View-As mutation blocked');
check('MODULES_064_074_NATIVE_OPTIMISTIC_CONCURRENCY', backend.includes('expectedRevision') && backend.includes('revision_conflict') && backend.includes('FOR UPDATE'), 'revision conflict protection');
check('MODULES_064_074_NATIVE_AUDIT', backend.includes('projectpulse_module_audit_events') && backend.includes('native_administration_document'), 'shared sanitized audit evidence');
check('MODULES_064_074_NATIVE_SECRET_BOUNDARY', backend.includes('secret_value_field_rejected') && backend.includes('ForbiddenSecretPropertyNames') && !panel.includes('type="password"'), 'usable secrets are rejected and no secret input exists');
check('MODULES_064_074_NATIVE_NO_EXTERNAL_CALLS', !/new\s+HttpClient|IHttpClientFactory|GraphServiceClient|SendMail|SmtpClient|KeyVault|SecretClient/i.test(backend), 'no Entra, Key Vault, AI-provider, SMTP, or external client');
check('MODULES_064_074_NATIVE_SCHEMA_MODES', backend.includes('"configuration"') && backend.includes('"collection"') && panel.includes("schema?.mode === 'configuration'") && panel.includes("schema?.mode === 'collection'"), 'configuration and collection editors');
check('MODULES_064_074_NATIVE_IDENTITY_OPTIONS', backend.includes('ReadIdentityOptionsAsync') && panel.includes("field.type === 'identity'"), 'Module 062/app_users identity selection');
check('MODULES_064_074_NATIVE_SAVE_RESTORE_UI', panel.includes("method: 'PUT'") && panel.includes('/restore') && panel.includes('Save changes') && panel.includes('Revision history'), 'save and restore controls');
check('MODULES_064_074_NATIVE_ROUTE_MAP', Object.entries(routeMap).every(([module, route]) => app.includes(`'${route}': '${module}'`)), 'all module routes mapped to the shared panel');
check('MODULES_064_074_NATIVE_APP_IMPORT', app.split("import NativeModuleAdministrationPanel from './NativeModuleAdministrationPanel.jsx';").length - 1 === 1, 'shared panel imported once');
check('MODULES_064_074_NATIVE_APP_MOUNT', app.split('<NativeModuleAdministrationPanel').length - 1 === 1, 'shared panel mounted once before Module 059');
check('MODULES_064_074_NATIVE_PROGRAM_REGISTRATION', program.split('app.MapModule064074NativeAdministrationEndpoints();').length - 1 === 1, 'backend registered once');
check('MODULES_064_074_NATIVE_BUILD_GUARD', packageJson.scripts?.build?.includes('validate:modules064074-native-admin') && packageJson.scripts?.['validate:modules064074-native-admin']?.includes('validate-modules-064-074-native-administration.mjs'), 'production build validator');
check('MODULES_064_074_NATIVE_CONTAINER_CONTEXT', docker.includes(files.backend) && docker.includes(files.migration) && docker.includes(files.rollback) && (docker.includes(files.readme) || docker.includes(`${path.posix.dirname(files.readme)}/`)), 'container receives all validation sources');
check('MODULES_064_074_NATIVE_MIGRATION_TABLES', migration.includes('projectpulse_native_admin_documents') && migration.includes('projectpulse_native_admin_document_revisions') && migration.includes('Migration 031 must be applied before migration 032'), 'versioned native document tables and dependency gate');
check('MODULES_064_074_NATIVE_MIGRATION_NOT_APPLIED', docs.includes('MIGRATION_032_APPLIED=NO') && !migration.includes('\\connect') && !migration.includes('psql '), 'source-only migration status');
check('MODULES_064_074_NATIVE_ROLLBACK', rollback.includes('DROP TABLE IF EXISTS projectpulse_native_admin_document_revisions') && rollback.includes('DROP TABLE IF EXISTS projectpulse_native_admin_documents'), 'reviewed rollback source');
check('MODULES_064_074_NATIVE_SCOPED_STYLES', styles.includes('.native-module-administration') && !/(^|\n)\s*(body|html|\.app-shell|\.sidebar|\.topbar)\s*\{/m.test(styles), 'shared panel styles remain scoped');
check('MODULES_064_074_NATIVE_GOVERNANCE', docs.includes('PROJECTPULSE_NATIVE_ADMINISTRATION_MIGRATION_032') && docs.includes('Checkpoint B2'), 'catalog, register, tracker, and README aligned');

console.log('');
console.log(`MODULES_064_074_NATIVE_ADMIN_CHECKS=${checks.length}`);
console.log('MODULES_064_074_NATIVE_ADMINISTRATION=SOURCE_COMPLETE');
console.log('MODULES_064_074_NATIVE_PERSISTENCE=MIGRATION_032_CREATED_NOT_APPLIED');
console.log('MODULES_064_074_EXTERNAL_SYSTEM_ACTIVATION=NONE');
console.log('MODULES_064_074_SECRET_VALUES=REJECTED');

if (checks.some((value) => !value)) {
  console.error('MODULES_064_074_NATIVE_ADMIN_CONTRACT=FAILED');
  process.exit(1);
}

console.log('MODULES_064_074_NATIVE_ADMIN_CONTRACT=PASSED');
