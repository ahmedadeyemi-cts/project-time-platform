// npm install --prefix /tmp/contract-test-runtime @electric-sql/pglite@0.5.8
// PGLITE_MODULE=/tmp/contract-test-runtime/node_modules/@electric-sql/pglite/dist/index.js node tests/contracts-approval-funding.test.mjs
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const { PGlite } = await import(process.env.PGLITE_MODULE || '@electric-sql/pglite');
const db = new PGlite();
const file = (p) => readFile(new URL('../' + p, import.meta.url), 'utf8');
await db.exec(`
CREATE TABLE clients (client_id uuid PRIMARY KEY, client_name text);
CREATE TABLE app_users (user_id uuid PRIMARY KEY, display_name text, email text);
CREATE TABLE projects (project_id uuid PRIMARY KEY, client_id uuid, project_manager_user_id uuid, contract_type text);
CREATE TABLE project_tasks (task_id uuid PRIMARY KEY);
CREATE TABLE project_intake_requests (project_intake_request_id uuid PRIMARY KEY);
CREATE TABLE time_entries (time_entry_id uuid PRIMARY KEY, project_id uuid, user_id uuid, work_date date, hours numeric, status text, created_at timestamptz DEFAULT now());
CREATE TABLE client_contacts (client_id uuid, address_line1 text, address_line2 text, city text, postal_code text, is_primary boolean, display_order integer, created_at timestamptz);
`);
for (const name of ['060-module-contracts-boh-foundation.sql', '060b-module-contracts-prepaid-financial-xlsx.sql', '060c-contract-approval-funding.sql', '060c-contract-approval-funding.sql']) {
  await db.exec((await file('deployment/database/' + name)).replace('CREATE EXTENSION IF NOT EXISTS pgcrypto;', ''));
}
await db.exec('ALTER TABLE time_entries ADD COLUMN task_id uuid');
await db.exec(await file('database/migrations/131_time_approval_routing.sql'));
const ids = Array.from({ length: 8 }, (_, i) => `00000000-0000-0000-0000-${String(i + 1).padStart(12, '0')}`);
const [customer, user, pm, project, contract, entry, otherCustomer, otherContract] = ids;
await db.exec(`
INSERT INTO clients VALUES ('${customer}', 'Customer A'), ('${otherCustomer}', 'Customer B');
INSERT INTO app_users VALUES ('${user}', 'Engineer', 'engineer@example.invalid'), ('${pm}', 'PM', 'pm@example.invalid');
INSERT INTO projects VALUES ('${project}', '${customer}', '${pm}', 'Fixed Price');
INSERT INTO boh_contracts (boh_contract_id,client_id,contract_name,primary_account_executive_user_id,project_team_coordinator_user_id,start_date,original_expiration_date,effective_expiration_date,fixed_fee_amount)
VALUES ('${contract}','${customer}','Funding A','${user}','${user}', CURRENT_DATE - 1,CURRENT_DATE + 30,CURRENT_DATE + 30,1000),
('${otherContract}','${otherCustomer}','Funding B','${user}','${user}', CURRENT_DATE - 1,CURRENT_DATE + 30,CURRENT_DATE + 30,1000);
INSERT INTO contract_project_funding VALUES ('${project}','${contract}',100,'${user}',NOW());
INSERT INTO time_entries VALUES ('${entry}','${project}','${user}',CURRENT_DATE,2,'draft',NOW(),NULL);
`);
const totals = async () => (await db.query(`SELECT pending_hours::float8 AS ph, approved_hours::float8 AS ah, pending_amount::float8 AS pa, approved_amount::float8 AS aa, remaining_balance::float8 AS balance FROM vw_boh_contract_time_totals JOIN vw_boh_prepaid_balance_rows USING (boh_contract_id) WHERE boh_contract_id = '${contract}'`)).rows[0];
let checks = 0;
for (const hasPm of [true, false]) {
  await db.exec(`UPDATE projects SET project_manager_user_id = ${hasPm ? `'${pm}'` : 'NULL'}`);
  for (const [status, expected] of [['draft','excluded'],['submitted','pending'],['manager_approved',hasPm?'pending':'approved'],['pm_approved','approved'],['accounting_ready','approved'],['reconciled','approved'],['locked','approved'],['manager_declined','excluded'],['pm_declined','excluded'],['returned','excluded']]) {
    await db.exec(`UPDATE time_entries SET status = '${status}'`);
    assert.deepEqual(await totals(), {ph:expected==='pending'?2:0,ah:expected==='approved'?2:0,pa:expected==='pending'?200:0,aa:expected==='approved'?200:0,balance:expected==='excluded'?1000:800}, `${status}, PM=${hasPm}`);
    checks++;
  }
}
// A client-supplied ledger status must not approve canonical submitted time or double count it.
await db.exec(`UPDATE time_entries SET status='submitted'; INSERT INTO boh_usage_ledger (boh_contract_id,time_entry_id,project_id,user_id,work_date,hours,usage_status,billing_rate,usage_amount) VALUES ('${contract}','${entry}','${project}','${user}',CURRENT_DATE,2,'consumed',150,9999);`);
assert.deepEqual(await totals(), {ph:2,ah:0,pa:300,aa:0,balance:700}); checks++;
await db.exec('UPDATE time_entries SET hours=3, status=\'manager_approved\'');
assert.deepEqual(await totals(), {ph:0,ah:3,pa:0,aa:450,balance:550}); checks++;
// Non-project mapped time requires no PM; a mixed day waits only on its assigned PM.
await db.exec(`UPDATE time_entries SET project_id=NULL`);
assert.deepEqual(await totals(), {ph:0,ah:3,pa:0,aa:450,balance:550}); checks++;
await db.exec(`UPDATE time_entries SET project_id='${project}'; ALTER TABLE time_entries ADD COLUMN timesheet_id uuid DEFAULT '${user}';`);
const approvals = await file('src/backend/ProjectTime.Api/Modules/ProductionApprovalWorkModule.cs');
const finalGuard = approvals.slice(approvals.indexOf('private static async Task<bool> HasNoOutstandingPmApprovalAsync'))
  .match(/new NpgsqlCommand\("""([\s\S]*?)"""/)[1].replaceAll('@timesheet_id', `'${user}'`).replaceAll('@work_date', 'CURRENT_DATE');
assert.equal(Object.values((await db.query(finalGuard)).rows[0])[0], true); checks++;
const mixedProject = '00000000-0000-0000-0000-000000000009';
await db.exec(`INSERT INTO projects VALUES ('${mixedProject}','${customer}','${pm}','Time and Material');
INSERT INTO time_entries (time_entry_id,project_id,user_id,work_date,hours,status) VALUES (gen_random_uuid(),'${mixedProject}','${user}',CURRENT_DATE,1,'manager_approved');`);
assert.equal(Object.values((await db.query(finalGuard)).rows[0])[0], false); checks++;
await db.exec(`UPDATE time_entries SET status='pm_approved' WHERE project_id='${mixedProject}'`);
assert.equal(Object.values((await db.query(finalGuard)).rows[0])[0], true); checks++;
await db.exec(`UPDATE boh_contracts SET import_snapshot_at = NOW() + INTERVAL '1 second', imported_approved_amount = 450 WHERE boh_contract_id='${contract}'`);
assert.deepEqual(await totals(), {ph:0,ah:3,pa:0,aa:450,balance:550}); checks++;
await db.exec('DELETE FROM time_entries');
assert.deepEqual(await totals(), {ph:0,ah:0,pa:0,aa:450,balance:550}); checks++;
// Execute the actual API SQL to catch alias errors and enforce customer scope.
const api = await file('src/backend/ProjectTime.Api/Modules/ContractsPrepaidManagementModule.cs');
const eligible = api.slice(api.indexOf('private static async Task<IResult> GetEligibleAsync'));
const sql = eligible.match(/new NpgsqlCommand\("""([\s\S]*?)"""/)[1].replaceAll('@client_id',`'${customer}'`).replaceAll('@work_date','CURRENT_DATE');
const candidates = (await db.query(sql)).rows;
assert.equal(candidates.length, 1); assert.equal(candidates[0].boh_contract_id, contract); checks++;
// Server-side eligibility: reject wrong customer, expired/closed/exhausted and disallowed FP.
const funding = await file('src/backend/ProjectTime.Api/Modules/ContractProjectFunding.cs');
const guard = funding.match(/new NpgsqlCommand\("""([\s\S]*?)"""/)[1].replaceAll('@project',`'${project}'`);
assert.equal((await db.query(guard.replaceAll('@contract',`'${contract}'`))).rows.length,1); checks++;
assert.equal((await db.query(guard.replaceAll('@contract',`'${otherContract}'`))).rows.length,0); checks++;
for (const change of ["contract_status='closed'", "contract_status='draft'", "effective_expiration_date=CURRENT_DATE-1", 'fixed_fee_amount=0', 'eligible_fixed_price=FALSE']) {
  await db.exec('BEGIN');
  await db.exec(`UPDATE boh_contracts SET ${change} WHERE boh_contract_id='${contract}'`);
  assert.equal((await db.query(guard.replaceAll('@contract',`'${contract}'`))).rows.length,0,change);
  await db.exec('ROLLBACK'); checks++;
}
// Rollback must retain live automatic funding usage even without ledger rows.
await db.exec(`DELETE FROM boh_usage_ledger;
UPDATE boh_contracts SET import_snapshot_at=NULL, imported_approved_amount=0 WHERE boh_contract_id='${contract}';
INSERT INTO time_entries (time_entry_id,project_id,user_id,work_date,hours,status) VALUES ('${entry}','${project}','${user}',CURRENT_DATE,2,'submitted');`);
assert.deepEqual(await totals(), {ph:2,ah:0,pa:200,aa:0,balance:800}); checks++;
await db.exec(await file('deployment/database/060c-contract-approval-funding-rollback.sql'));
assert.deepEqual(await totals(), {ph:2,ah:0,pa:200,aa:0,balance:800}); checks++;
await db.exec(`UPDATE time_entries SET hours=3, status='manager_approved'`);
assert.deepEqual(await totals(), {ph:0,ah:3,pa:0,aa:300,balance:700}); checks++;

await db.exec(await file('deployment/database/060c-contract-approval-funding.sql'));
await db.exec(await file('database/migrations/131_time_approval_routing.sql'));
assert.deepEqual(await totals(), {ph:0,ah:3,pa:0,aa:300,balance:700}); checks++;
assert.equal((await db.query('SELECT COUNT(*)::int AS n FROM contract_project_funding')).rows[0].n, 1); checks++;
console.log(`PASS ${checks} contract approval/funding database cases; migration reapplies cleanly.`);
await db.exec(await file('scripts/release-test/verify-module060-contract-funding.sql'));
await db.close();
