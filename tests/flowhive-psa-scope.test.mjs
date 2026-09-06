import test from 'node:test';
import assert from 'node:assert/strict';
import { verifyPaths, verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';
const scope = ['src/backend/ProjectTime.Api/Modules/ProjectFlowHiveExecutionPolicy.cs', 'tests/flowhive-psa-scope.mjs'];
test('exact sorted reviewed component paths pass', () => verifyPaths([...scope].reverse(), scope));
test('extra and missing changes are both rejected', () => {
  assert.throws(() => verifyPaths(scope.slice(1), scope));
  assert.throws(() => verifyPaths([...scope, 'tests/flowhive-psa-extra.mjs'], scope));
});
test('duplicates, wildcards and traversal are not valid manifests', () => {
  for (const value of [[...scope, scope[0]], ['tests/flowhive-psa-*.mjs'], ['tests/../flowhive-psa-scope.mjs']])
    assert.throws(() => verifyPaths(value, value));
});
test('a manifest cannot authorize deployment, secrets, source transport or unrelated module edits', () => {
  for (const name of ['.github/workflows/projectpulse-deploy-test.yml',
    'deployment/containers/api/Dockerfile', 'scripts/release-test/run-anything.sh',
    '.github/workflows/flowhive-reviewed-source-apply.yml', '.github/flowhive-reviewed-source.patch',
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs',
    'src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs', '.env']) {
    assert.throws(() => verifyPaths([name], [name]), name);
  }
});
test('only specified FlowHive migrations are accepted', () => {
  const name='database/migrations/104_flowhive_bounded_ai_execution.sql';verifyPaths([name], [name]);
  assert.throws(() => verifyPaths(['database/migrations/999_unreviewed.sql'], ['database/migrations/999_unreviewed.sql']));
});
test('read-only validation cannot acquire deployment privileges', () => {
  const good='permissions:\n  contents: read\njobs:\n  tests:\n    runs-on: ubuntu-latest\n';
  verifyReadOnlyWorkflow(good, 'fixture');
  for (const addition of ['    environment: test\n', '    uses: azure/login@v2\n',
    '    id-token: write\n', '    token: ${{ secrets.PRODUCTION_KEY }}\n'])
    assert.throws(() => verifyReadOnlyWorkflow(good+addition, 'fixture'));
});
