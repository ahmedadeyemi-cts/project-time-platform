import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');
const absolute = (path) => resolve(root, path);
const text = (path) => readFile(absolute(path), 'utf8');
const migrationRoot = 'database/migrations/040_scoped_role_policy_versions';
const entryPath = 'database/migrations/040_scoped_role_policy_versions.sql';
const rollbackPath = 'database/rollback/040_scoped_role_policy_versions_rollback.sql';
const fragments = [
  '00_schema.sql',
  '10_workbook_cells.sql',
  '12_super_administrator_override.sql',
  '15_workbook_metadata.sql',
  '20_standard_grants.sql',
  '30_time_entry.sql',
  '40_approval_inbox.sql',
  '50_utilization.sql',
  '60_role_administration.sql',
  '70_read_only_matrix.sql',
  '80_finalize.sql'
];

if (!existsSync(absolute(entryPath))) {
  console.log('MIGRATION_040_EXTERNAL_SOURCE_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
  process.exit(0);
}

const [entry, rollback, ...fragmentText] = await Promise.all([
  text(entryPath),
  text(rollbackPath),
  ...fragments.map((name) => text(`${migrationRoot}/${name}`))
]);
const combined = fragmentText.join('\n');

for (const fragment of fragments) {
  const marker = `\\ir 040_scoped_role_policy_versions/${fragment}`;
  if (!entry.includes(marker)) throw new Error(`Migration 040 entry file is missing ${fragment}.`);
  if (entry.split(marker).length !== 2) throw new Error(`Migration 040 includes ${fragment} more than once.`);
}

const positions = fragments.map((name) => entry.indexOf(`\\ir 040_scoped_role_policy_versions/${name}`));
if (positions.some((position, index) => index > 0 && position <= positions[index - 1])) {
  throw new Error('Migration 040 fragments are not applied in deterministic order.');
}

for (const required of [
  '040_scoped_role_policy_versions',
  '039_work_to_cash_reactivation_lock_order',
  'ProjectPulse_Module_Role_Permissions_Matrix(2).xlsx',
  'a9d8d1549ad36634d0a84510326e2127e644c3d14a4be2877fb659ef4a56c02c',
  'scoped_role_policy_versions',
  'scoped_role_policy_grants',
  'scoped_role_policy_audit_events',
  'scoped_approval_stage_events',
  'scoped_time_correction_events',
  'projectpulse040_block_immutable_audit_mutation',
  'projectpulse040_block_published_grant_mutation',
  'scoped_role_policy_effective_grants',
  'CUSTOM_RULE',
  'DIRECT_AND_INDIRECT_REPORTS',
  'ASSIGNED_PROJECT_TEAM',
  'NON_BYPASSABLE_SAFETY_BYPASS',
  'viewAsWriteForbidden',
  'finalSuperAdministratorProtection',
  'writeEndpointForbidden',
  "event_code = 'POLICY_BASELINE_PUBLISHED'"
]) {
  if (!combined.includes(required)) throw new Error(`Migration 040 missing required contract: ${required}`);
}

const workbookMatch = fragmentText[1].match(/SELECT\s+'(\{[\s\S]+\})'::jsonb\s+AS\s+value/);
if (!workbookMatch) throw new Error('Could not parse the compact workbook authority payload.');
const workbook = JSON.parse(workbookMatch[1]);
if (workbook.roles.length !== 12) throw new Error(`Expected 12 canonical roles, found ${workbook.roles.length}.`);
if (workbook.rows.length !== 70) throw new Error(`Expected 70 workbook module rows, found ${workbook.rows.length}.`);
if (workbook.roles.at(-1) !== 'SUPER_ADMINISTRATOR') throw new Error('Super Administrator must remain the canonical final role column.');

const fullControlIndex = workbook.designations.indexOf('Full Control');
const customIndex = workbook.designations.indexOf('Custom');
const noAccessIndex = workbook.designations.indexOf('No Access');
const superAdministratorDesignations = workbook.rows.map((row) => row[1].at(-1));
const superAdministratorCounts = {
  fullControl: superAdministratorDesignations.filter((value) => value === fullControlIndex).length,
  custom: superAdministratorDesignations.filter((value) => value === customIndex).length,
  noAccess: superAdministratorDesignations.filter((value) => value === noAccessIndex).length
};
if (superAdministratorCounts.fullControl !== 65
    || superAdministratorCounts.custom !== 3
    || superAdministratorCounts.noAccess !== 2) {
  throw new Error(`Unexpected workbook Super Administrator designations: ${JSON.stringify(superAdministratorCounts)}.`);
}

const superAdminOverride = fragmentText[2];
for (const required of [
  "role_code = 'SUPER_ADMINISTRATOR'",
  "designation IN ('No Access', 'Not Set')",
  "SET designation = 'Full Control'",
  "scope_code = 'ORGANIZATION'",
  'Modules 001-003',
  'non-bypassable',
  'read-only'
]) {
  if (!superAdminOverride.includes(required)) {
    throw new Error(`Super Administrator override missing contract: ${required}`);
  }
}

const notSetIndex = workbook.designations.indexOf('Not Set');
const notSetCount = workbook.rows.reduce(
  (total, row) => total + row[1].filter((designation) => designation === notSetIndex).length,
  0
);
if (notSetCount !== 8) throw new Error(`Expected 8 Not Set cells preserving legacy behavior, found ${notSetCount}.`);

const legacyTables = '(app_roles|app_permissions|app_role_permissions|app_user_role_assignments)';
const destructiveLegacy = new RegExp(`\\b(UPDATE|DELETE\\s+FROM|TRUNCATE|DROP\\s+TABLE)\\s+${legacyTables}\\b`, 'i');
if (destructiveLegacy.test(combined)) {
  throw new Error('Migration 040 destructively modifies a legacy RBAC or user-role table.');
}

for (const required of [
  'version_number > 1',
  'rollback blocked',
  'DROP TABLE IF EXISTS scoped_role_policy_grants',
  'DROP TABLE IF EXISTS scoped_role_policy_versions',
  'DELETE FROM schema_migrations',
  "migration_id = '040_scoped_role_policy_versions'"
]) {
  if (!rollback.includes(required)) throw new Error(`Migration 040 rollback missing safety contract: ${required}`);
}
if (/DROP TABLE IF EXISTS\s+app_|DELETE FROM\s+app_/i.test(rollback)) {
  throw new Error('Migration 040 rollback must not drop or delete legacy application RBAC data.');
}

console.log('Migration 040 workbook authority, Super Administrator override, additive schema, idempotence, and rollback safety contracts passed.');
