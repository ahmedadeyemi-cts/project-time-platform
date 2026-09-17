import assert from 'node:assert/strict';
import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { PROJECTPULSE_MODULES } from '../../src/frontend/project-time-web/src/module-availability-registry.js';

const root = fileURLToPath(new URL('../../', import.meta.url));
// Explicit historical routes from migration 040; arbitrary route changes fail closed.
const legacyRoutes = { '006': 'psa-modules', '071': 'on-call-scheduling', '075': 'Source-integrated scope',
  '077': 'Source-integrated scope', '078': 'Source-integrated scope', '079': 'Source-integrated scope',
  '080': 'Source-integrated scope', '998': 'system-diagnostic-remediation' };
export function catalogRows(modules = PROJECTPULSE_MODULES, backend = fs.readFileSync(path.join(root, 'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs'), 'utf8')) {
  const rows = modules.map(m => ({ module_code: m.moduleNumber, module_name: m.displayName, route_scope: m.route,
    legacy_route: legacyRoutes[m.moduleNumber] || m.route })).sort((a,b) => a.module_code.localeCompare(b.module_code));
  assert.equal(new Set(rows.map(m => m.module_code)).size, rows.length, 'Duplicate module ID');
  assert.equal(new Set(rows.map(m => m.route_scope)).size, rows.length, 'Duplicate module route');
  for (const row of rows) {
    assert.match(row.module_code, /^\d{3}[A-Z]?$/);
    assert.match(row.route_scope, /^[a-z0-9]+(?:-[a-z0-9]+)*$/);
    assert.ok(row.module_name.trim());
  }
  const apiRows = [...backend.matchAll(/\["([^"]+)"\]\s*=\s*Module\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)"/g)].map(m => {
    assert.equal(m[1], m[2], 'Backend module key differs from identity');
    return {module_code:m[1], module_name:m[4], route_scope:m[3]};
  }).sort((a,b) => a.module_code.localeCompare(b.module_code));
  assert.deepEqual(apiRows, rows.map(({legacy_route,...r}) => r), 'Frontend/backend module registry drift');
  return rows;
}
export function reconciliationSql(releaseSha, modules, backend) {
  assert.match(releaseSha, /^[a-f0-9]{40}$/);
  const rows = catalogRows(modules, backend);
  const json = JSON.stringify(rows);
  const digest = crypto.createHash('sha256').update(json).digest('hex');
  const literal = value => "'" + value.replaceAll("'", "''") + "'";
  return fs.readFileSync(path.join(root, 'database/migrations/108_builtin_module_catalog_reconciliation.sql'), 'utf8')
    .replace('/*CATALOG_JSON*/', literal(json)).replaceAll('/*RELEASE_SHA*/', literal(releaseSha))
    .replaceAll('/*CATALOG_SHA256*/', literal(digest));
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [releaseSha, output] = process.argv.slice(2);
  if (!output) throw new Error('Usage: node reconcile-module-catalog.mjs <release-sha> <output.sql>');
  fs.writeFileSync(output, reconciliationSql(releaseSha));
  console.log(`MODULE_CATALOG_SOURCE=PASS modules=${catalogRows().length} release=${releaseSha}`);
}
