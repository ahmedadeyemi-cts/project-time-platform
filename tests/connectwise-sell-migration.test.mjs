// Run with @electric-sql/pglite installed, or PROJECTPULSE_PGLITE_MODULE set to its entrypoint.
import assert from 'node:assert/strict';
import fs from 'node:fs';
const { PGlite } = await import(process.env.PROJECTPULSE_PGLITE_MODULE || '@electric-sql/pglite');
const db = new PGlite();
const migration = fs.readFileSync(new URL('../database/migrations/111_connectwise_sell_provider.sql', import.meta.url), 'utf8');
// Minimal prerequisite fixture mirrors the affected schemas; no production database.
await db.exec(`
 CREATE TABLE crm_integration_providers (
  provider_key text PRIMARY KEY, provider_name text, provider_type text, provider_status text,
  auth_model text CHECK(auth_model IN ('api_key','oauth2')), configuration_scope text,
  secret_storage_policy text, base_url text, health_check_url text, api_key_header text,
  api_key_prefix text, record_lookup_url_template text, import_mapping_json jsonb,
  is_builtin boolean, is_enabled boolean, availability_status text,
  supports_accounts boolean, supports_opportunities boolean, supports_quotes boolean,
  supports_attachments boolean, notes text, updated_at timestamptz DEFAULT now());
 CREATE TABLE crm_integration_credentials(provider_key text REFERENCES crm_integration_providers,
  ciphertext text, PRIMARY KEY(provider_key));
 INSERT INTO crm_integration_providers(provider_key,provider_name,auth_model,is_builtin,is_enabled,availability_status)
 VALUES('zendesk_sell','Original legacy vendor','oauth2',true,true,'available'),('salesforce','Salesforce','oauth2',true,true,'available');
 INSERT INTO crm_integration_credentials VALUES('zendesk_sell','original-encrypted-bytes');
 CREATE TABLE customer_directory_source_authority (
  source_mode text, provider_key text REFERENCES crm_integration_providers, updated_at timestamptz,
  CONSTRAINT ck_customer_directory_source_authority_provider CHECK (
    (source_mode='sell' AND provider_key='zendesk_sell') OR
    (source_mode='crm' AND provider_key IS NOT NULL AND provider_key<>'zendesk_sell') OR
    (source_mode='manual' AND provider_key IS NULL)));
 INSERT INTO customer_directory_source_authority VALUES('sell','zendesk_sell',now());
 CREATE TABLE customer_directory_source_authority_history (
  previous_source_mode text, previous_provider_key text, next_source_mode text, next_provider_key text);
 CREATE TABLE customer_directory_source_links (source_system text, source_record_id text);
 INSERT INTO customer_directory_source_links VALUES ('SELL','123');
 CREATE TABLE module025_sow_sell_submissions (submission_id int PRIMARY KEY,
  destination_key varchar(80) NOT NULL CHECK(destination_key='zendesk_sell'), receipt text);
 INSERT INTO module025_sow_sell_submissions VALUES (1,'zendesk_sell','immutable-receipt');
 CREATE TABLE reporting_external_connection_catalog(connection_key text PRIMARY KEY,
  connection_name text,connection_type text,provider_category text,operational_owner text);
`);
const rows = async sql => (await db.query(sql)).rows;
await db.exec(migration);
assert.deepEqual(await rows('SELECT source_mode,provider_key FROM customer_directory_source_authority'), [{source_mode:'sell',provider_key:'connectwise_sell'}]);
assert.equal((await rows("SELECT is_enabled FROM crm_integration_providers WHERE provider_key='connectwise_sell'"))[0].is_enabled, false);
assert.equal((await rows("SELECT is_enabled FROM crm_integration_providers WHERE provider_key='zendesk_sell'"))[0].is_enabled, false);
assert.deepEqual(await rows('SELECT * FROM crm_integration_credentials'), [{provider_key:'zendesk_sell',ciphertext:'original-encrypted-bytes'}]);
assert.deepEqual(await rows('SELECT * FROM customer_directory_source_links'), [{source_system:'SELL',source_record_id:'123'}]);
assert.deepEqual(await rows('SELECT * FROM module025_sow_sell_submissions'), [{submission_id:1,destination_key:'zendesk_sell',receipt:'immutable-receipt'}]);
await db.exec("INSERT INTO module025_sow_sell_submissions VALUES (2,'connectwise_sell','new-receipt'); UPDATE crm_integration_providers SET is_enabled=true, availability_status='available', notes='keep reviewed configuration' WHERE provider_key='connectwise_sell'; INSERT INTO crm_integration_credentials VALUES('connectwise_sell','new-encrypted-keys');");
await db.exec(migration);
assert.equal((await rows('SELECT * FROM customer_directory_source_authority_history')).length, 1);
assert.equal((await rows("SELECT notes FROM crm_integration_providers WHERE provider_key='connectwise_sell'"))[0].notes, 'keep reviewed configuration');
assert.equal((await rows("SELECT ciphertext FROM crm_integration_credentials WHERE provider_key='connectwise_sell'"))[0].ciphertext, 'new-encrypted-keys');
for (const selection of [{source_mode:'manual',provider_key:null},{source_mode:'crm',provider_key:'salesforce'}]) {
 await db.query('UPDATE customer_directory_source_authority SET source_mode=$1,provider_key=$2',[selection.source_mode,selection.provider_key]);
 await db.exec(migration);
 assert.deepEqual(await rows('SELECT source_mode,provider_key FROM customer_directory_source_authority'),[selection]);
}
await assert.rejects(db.exec("INSERT INTO module025_sow_sell_submissions VALUES (3,'unknown','bad')"));
await db.close();
console.log('ConnectWise SELL migration: repeatable; configuration, credentials, customer lineage, receipts and other source selections preserved.');
