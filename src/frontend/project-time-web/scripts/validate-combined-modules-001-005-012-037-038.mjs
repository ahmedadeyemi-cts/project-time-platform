import { spawnSync } from 'node:child_process';

const validators = [
  './scripts/validate-admin-runtime-stability.mjs',
  './scripts/validate-module-005-project-expense-upload.mjs',
  './scripts/validate-module-001-timesheet-timer-mobile.mjs',
  './scripts/validate-module-012-scoped-role-admin.mjs',
  './scripts/validate-module-037-effective-matrix.mjs',
  './scripts/validate-runtime-role-policy-ptc-data.mjs'
];

for (const validator of validators) {
  console.log(`COMBINED_MODULE_VALIDATOR_START=${validator}`);
  const result = spawnSync(process.execPath, [validator], {
    cwd: process.cwd(),
    env: process.env,
    stdio: 'inherit'
  });

  if (result.error) {
    console.error(`COMBINED_MODULE_VALIDATOR_ERROR=${validator} ${result.error.message}`);
    process.exit(1);
  }

  if (result.status !== 0) {
    console.error(`COMBINED_MODULE_VALIDATOR_FAILED=${validator} status=${result.status ?? 'unknown'}`);
    process.exit(result.status ?? 1);
  }

  console.log(`COMBINED_MODULE_VALIDATOR_PASSED=${validator}`);
}

console.log('FUNCTIONAL_RUNTIME_UAT_001_005_012_037_038_CONTRACTS=PASS');
