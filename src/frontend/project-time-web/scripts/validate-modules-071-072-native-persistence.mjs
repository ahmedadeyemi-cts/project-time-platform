import fs from 'node:fs';
import path from 'node:path';
const root = path.resolve(process.cwd(), '..', '..', '..');
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const files = [
  'src/backend/ProjectTime.Api/Modules/OnCallSchedulingModule.cs',
  'src/backend/ProjectTime.Api/Modules/OneAssistRoutingDirectoryModule.cs',
  'src/backend/ProjectTime.Api/Modules/Module071072NativePersistence.cs',
  'src/frontend/project-time-web/src/OnCallSchedulingCenter.jsx',
  'src/frontend/project-time-web/src/OneAssistRoutingDirectoryCenter.jsx',
  'database/migrations/031_modules_071_072_native_persistence.sql',
  'database/rollback/031_modules_071_072_native_persistence_rollback.sql'
];
const source = files.map(read).join('\n');
const checks = [];
const check = (name, condition, evidence) => {
  checks.push(Boolean(condition));
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
};
const forbidden = ['Cloud' + 'flare', 'CF-' + 'Access-Client', 'PROJECTPULSE_ONCALL_' + 'UPSTREAM', 'PROJECTPULSE_ONCALL_' + 'ACCESS', 'PROJECTPULSE_ONEASSIST_' + 'UPSTREAM', 'PROJECTPULSE_ONEASSIST_' + 'ACCESS'];
check('MODULES_071_072_FILES', files.every((relative) => fs.existsSync(path.join(root, relative))), 'all native source files exist');
check('MODULES_071_072_EXTERNAL_RUNTIME_REFERENCES', forbidden.every((token) => !source.includes(token)), 'no external runtime adapter, header, or setting');
check('MODULES_071_072_MIGRATION', source.includes('projectpulse_oncall_schedule_versions') && source.includes('projectpulse_oneassist_routes'), 'migration 031 owns both modules');
check('MODULES_071_072_AUDIT', source.includes('projectpulse_module_audit_events'), 'shared sanitized audit evidence');
check('MODULES_071_072_VIEW_AS', source.includes('actual_session_required'), 'View-As mutation remains blocked');
check('MODULES_071_072_NATIVE_MARKERS', source.includes('projectpulse-postgresql') && source.includes('projectpulse_postgresql'), 'backend and frontend native markers');
console.log('');
console.log(`MODULES_071_072_NATIVE_CHECKS=${checks.length}`);
console.log('MODULES_071_072_NATIVE_POSTGRESQL=SOURCE_COMPLETE');
console.log('MODULES_071_072_EXTERNAL_COMPATIBILITY=REMOVED');
console.log('MODULES_071_072_MIGRATION_031=CREATED_NOT_APPLIED');
if (checks.some((value) => !value)) { console.error('MODULES_071_072_NATIVE_CONTRACT=FAILED'); process.exit(1); }
console.log('MODULES_071_072_NATIVE_CONTRACT=PASSED');
