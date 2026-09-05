import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8');
const controller = read('.github/workflows/projectpulse-deploy-test.yml');
const recovery = controller.split('      - name: Guard exact source, manual Test scope, and no-migration boundary')[1].split('      - name: Snapshot protected Test and preserve rollback contract')[0];
const workflow = controller;
for (const marker of [
  'environment: test', "if: github.ref == 'refs/heads/main'",  'PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN',
  'Oracle runtime DNS does not match the approved IPv4 pin', 'Incorrect-token readiness must return 401',
  'Preserve the currently deployed immutable API image', 'PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED=true',
  'Rollback protected Test API configuration on failure', 'ORACLE_EMBEDDING_RESPONSE=VALID',
  'clamavSignatureVersion', 'PRODUCTION_MUTATION=NONE',
]) assert.ok(workflow.includes(marker), marker);
for (const forbidden of ['environment: production', 'curl -k', '--insecure', 'build-pr55-acr-image.sh', 'set +e'])
  assert.ok(!recovery.includes(forbidden), forbidden);
assert.ok(workflow.indexOf('Prove authenticated Oracle HTTPS services') < workflow.indexOf('Snapshot the protected Test API'));
assert.ok(workflow.indexOf('bash scripts/wait-containerapp-ready-revision.sh', workflow.indexOf('RESTORED_REVISION=')) < workflow.indexOf('az containerapp secret remove'));
const migration = read('scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh');
for (const marker of ['migration-execution.json', 'migration-replicas.json', 'migration-system-events.json', '--replica "$replica"', 'ContainerAppSystemLogs_CL'])
  assert.ok(migration.includes(marker), marker);
console.log('PROTECTED_UAT_RECOVERY_SECURITY_AND_DIAGNOSTICS=PASS');
