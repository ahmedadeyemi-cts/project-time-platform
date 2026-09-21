"""Exact repair scope; protected deployment authority remains unchanged."""
from pathlib import Path
import os
import subprocess
import sys
BASE = '219fad3923da2e08cdd1a84d54031d9dad76bf64'
BRANCH = 'fix/flowhive-sow-private-generation-20260921'
REGISTRATIONS = [('scripts/ci/validate-celar-ai-enterprise-source-boundary.sh',
  'if [[ "$HEAD_BRANCH" == fix/flowhive-sow-private-generation-20260921 ]]; then\n'
  '  python3 tests/flowhive-sow-private-generation-scope.py\n'
  '  exit 0\n'
  'fi\n'
  '\n'
  'if [[ "$HEAD_BRANCH" == fix/module064-authoritative-model-catalog-20260920 ]]; then',
  'if [[ "$HEAD_BRANCH" == fix/module064-authoritative-model-catalog-20260920 ]]; then'),
 ('.github/workflows/module-management-owner-drawer-ci.yml',
  '          if [[ "$HEAD_BRANCH" == fix/flowhive-sow-private-generation-20260921 ]]; then\n'
  '            python3 tests/flowhive-sow-private-generation-scope.py\n'
  '            exit 0\n'
  '          fi\n'
  '          if [[ "$HEAD_BRANCH" == fix/module064-authoritative-model-catalog-20260920 ]]; then',
  '          if [[ "$HEAD_BRANCH" == fix/module064-authoritative-model-catalog-20260920 ]]; then'),
 ('.github/workflows/module064-automatic-provider-health-ci.yml',
  '          if [[ "$HEAD_BRANCH" == fix/flowhive-sow-private-generation-20260921 ]]; then\n'
  '            python3 tests/flowhive-sow-private-generation-scope.py\n'
  '            exit 0\n'
  '          fi\n'
  '          if [[ "$HEAD_BRANCH" == fix/module064-authoritative-model-catalog-20260920 ]]; then',
  '          if [[ "$HEAD_BRANCH" == fix/module064-authoritative-model-catalog-20260920 ]]; then'),
 ('.github/workflows/flowhive-psa-release-control-ci.yml',
  '          if [[ "$GITHUB_HEAD_REF" == fix/flowhive-sow-private-generation-20260921 ]]; then\n'
  '            python3 tests/flowhive-sow-private-generation-scope.py\n'
  '          elif [[ "$GITHUB_HEAD_REF" == fix/module064-authoritative-model-catalog-20260920 ]]; then',
  '          if [[ "$GITHUB_HEAD_REF" == fix/module064-authoritative-model-catalog-20260920 ]]; then'),
 ('scripts/release-test/validate-protected-test-controller-branches.sh',
  'if [[ "$HEAD_BRANCH" == fix/flowhive-sow-private-generation-20260921 ]]; then\n'
  '  python3 tests/flowhive-sow-private-generation-scope.py\n'
  '  node tests/validate-systemwide-image-build-controller.mjs\n'
  'elif [[ "$HEAD_BRANCH" == fix/module064-authoritative-model-catalog-20260920 ]]; then',
  'if [[ "$HEAD_BRANCH" == fix/module064-authoritative-model-catalog-20260920 ]]; then'),
 ('tests/validate-celar-ai-pr630-consolidated.mjs',
  "const privateGenerationRepairMode = branchName === 'fix/flowhive-sow-private-generation-20260921';\n"
  "if (privateGenerationRepairMode) childProcess.execFileSync('python3', "
  "['tests/flowhive-sow-private-generation-scope.py'], {stdio:'inherit'});\n"
  'const scopedCompatibilityMode = privateGenerationRepairMode || ',
  'const scopedCompatibilityMode = '),
 ('tests/flowhive-psa-admission.test.mjs',
  'const privateGenerationRepair = process.env.GITHUB_HEAD_REF === '
  "'fix/flowhive-sow-private-generation-20260921';\n"
  'const module025VerifierCorrection = privateGenerationRepair || ',
  'const module025VerifierCorrection = '),
 ('tests/flowhive-psa-admission.test.mjs',
  "const module025VerifierBase = privateGenerationRepair ? '219fad3923da2e08cdd1a84d54031d9dad76bf64' : ",
  'const module025VerifierBase = '),
 ('.github/workflows/celar-ai-oracle-gitops-ci.yml',
  '.gatewayVersion == "1.1.9" and',
  '.gatewayVersion == "1.1.8" and'),
 ('.github/workflows/module033-project-forge-ci.yml',
  '          if [[ "$HEAD_BRANCH" == \'fix/flowhive-sow-private-generation-20260921\' ]]; then\n'
  '            python3 tests/flowhive-sow-private-generation-scope.py\n'
  '            PROTECTED_DEPLOYMENT="$(grep -Fvx \\\n'
  "              -e 'deployment/oracle-celar/gateway/wsgi.py' \\\n"
  "              -e 'deployment/oracle-celar/release.json' \\\n"
  '              <<<"$PROTECTED_DEPLOYMENT" || true)"\n'
  '          fi\n'
  '          if [[ "$HEAD_BRANCH" == \'fix/module025-private-generation-recovery\' ]]; then\n'
  '            python3 tests/module025-private-generation-scope.py\n'
  '            PROTECTED_DEPLOYMENT="$(grep -Fvx \\\n'
  "              -e 'deployment/oracle-celar/gateway/wsgi.py' \\\n"
  "              -e 'deployment/oracle-celar/release.json' \\\n"
  '              <<<"$PROTECTED_DEPLOYMENT" || true)"\n'
  '          fi\n',
  '          if [[ "$HEAD_BRANCH" == \'fix/module025-private-generation-recovery\' ]]; then\n'
  '            python3 tests/module025-private-generation-scope.py\n'
  '            PROTECTED_DEPLOYMENT="$(grep -Fvx \\\n'
  "              -e 'deployment/oracle-celar/gateway/wsgi.py' \\\n"
  "              -e 'deployment/oracle-celar/release.json' \\\n"
  '              <<<"$PROTECTED_DEPLOYMENT" || true)"\n'
  '          fi\n'),
 ('.github/workflows/modules010065-microsoft-runtime-ci.yml',
  '          elif [[ "$HEAD_BRANCH" == fix/flowhive-sow-private-generation-20260921 ]]; then\n'
  '            python3 tests/flowhive-sow-private-generation-scope.py\n'
  '            RESERVED=""\n'
  '            echo "MODULES_010_065_VALIDATION_MODE=CONNECTWISE_REGRESSION" >> "$GITHUB_ENV"\n'
  '          elif [[ "$HEAD_BRANCH" == fix/module025-private-generation-recovery ]]; then\n'
  '            python3 tests/module025-private-generation-scope.py\n'
  '            RESERVED=""\n'
  '            echo "MODULES_010_065_VALIDATION_MODE=CONNECTWISE_REGRESSION" >> "$GITHUB_ENV"\n',
  '          elif [[ "$HEAD_BRANCH" == fix/module025-private-generation-recovery ]]; then\n'
  '            python3 tests/module025-private-generation-scope.py\n'
  '            RESERVED=""\n'
  '            echo "MODULES_010_065_VALIDATION_MODE=CONNECTWISE_REGRESSION" >> "$GITHUB_ENV"\n'),
 ('.github/workflows/group5-financial-operations-recovery-ci.yml',
  '          if [[ "$HEAD_BRANCH" == fix/flowhive-sow-private-generation-20260921 ]]; then\n'
  '            python3 tests/flowhive-sow-private-generation-scope.py\n'
  '            echo "GROUP_5_VALIDATION_MODE=CONNECTWISE_REGRESSION" >> "$GITHUB_ENV"\n'
  '          elif [[ "$HEAD_BRANCH" == fix/module025-private-generation-recovery ]]; then\n'
  '            python3 tests/module025-private-generation-scope.py\n'
  '            echo "GROUP_5_VALIDATION_MODE=CONNECTWISE_REGRESSION" >> "$GITHUB_ENV"\n',
  '          if [[ "$HEAD_BRANCH" == fix/module025-private-generation-recovery ]]; then\n'
  '            python3 tests/module025-private-generation-scope.py\n'
  '            echo "GROUP_5_VALIDATION_MODE=CONNECTWISE_REGRESSION" >> "$GITHUB_ENV"\n'),
 ('scripts/ci/validate-module030-source-boundary.sh',
  'elif [[ "$HEAD_BRANCH" == fix/flowhive-sow-private-generation-20260921 ]]; then\n'
  '  python3 tests/flowhive-sow-private-generation-scope.py\n'
  '  exit 0\n'
  'elif [[ "$HEAD_BRANCH" == fix/module025-private-generation-recovery ]]; then\n'
  '  python3 tests/module025-private-generation-scope.py\n'
  '  exit 0\n',
  'elif [[ "$HEAD_BRANCH" == fix/module025-private-generation-recovery ]]; then\n'
  '  python3 tests/module025-private-generation-scope.py\n'
  '  exit 0\n'),
 ('.github/workflows/pulse-ai-help-chat-usability-ci.yml',
  '            fix/flowhive-sow-private-generation-20260921)\n'
  '              python3 tests/flowhive-sow-private-generation-scope.py\n'
  '              echo "PULSE_AI_HELP_CHAT_VALIDATION_MODE=CONNECTWISE_REGRESSION" >> "$GITHUB_ENV"\n'
  '              ;;\n'
  '            fix/module025-private-generation-recovery)\n'
  '              python3 tests/module025-private-generation-scope.py\n'
  '              echo "PULSE_AI_HELP_CHAT_VALIDATION_MODE=CONNECTWISE_REGRESSION" >> "$GITHUB_ENV"\n'
  '              ;;\n',
  '            fix/module025-private-generation-recovery)\n'
  '              python3 tests/module025-private-generation-scope.py\n'
  '              echo "PULSE_AI_HELP_CHAT_VALIDATION_MODE=CONNECTWISE_REGRESSION" >> "$GITHUB_ENV"\n'
  '              ;;\n')]

MANIFEST = '.github/flowhive-sow-private-generation-files.txt'
def git(*args): return subprocess.check_output(['git', *args]).decode()
def verify(actual, expected):
    assert expected == sorted(set(expected))
    assert all(p and not any(v in p for v in ['..','*','?','\\']) for p in expected)
    assert set(actual) == set(expected), f'Unexpected repair scope: {set(actual) ^ set(expected)}'
expected = Path(MANIFEST).read_text().splitlines()
verify(expected, expected)
for mutation in [expected[1:], expected + ['.github/workflows/projectpulse-deploy-production.yml']]:
    try: verify(mutation, expected)
    except AssertionError: continue
    raise AssertionError('Scope accepted an omitted or unauthorized path')
if '--self-test' in sys.argv:
    print('PRIVATE_GENERATION_SCOPE_NEGATIVE_TESTS=PASS')
    sys.exit(0)
if os.getenv('GITHUB_HEAD_REF'): assert os.environ['GITHUB_HEAD_REF'] == BRANCH
if os.getenv('GITHUB_EVENT_NAME'): assert os.environ['GITHUB_EVENT_NAME'] == 'pull_request'
assert git('merge-base', BASE, 'HEAD').strip() == BASE
actual = set(git('diff','--name-only',BASE).splitlines()) | set(git('ls-files','--others','--exclude-standard').splitlines())
verify(actual, expected)
normalized = {}
for path, addition, original in reversed(REGISTRATIONS):
    source = normalized.get(path, Path(path).read_text())
    assert source.count(addition) == 1, f'Missing or repeated registration: {path}'
    normalized[path] = source.replace(addition, original, 1)
for path, source in normalized.items():
    assert source == git('show',BASE+':'+path), f'Unrelated CI changes: {path}'
for path in ['.github/workflows/projectpulse-deploy-test.yml',
             '.github/workflows/projectpulse-deploy-production.yml',
             '.github/workflows/module025-protected-uat-control.yml',
             '.github/workflows/flowhive-psa-installed-acceptance.yml',
             '.github/flowhive-psa-protected-test-candidate.json',
             '.github/flowhive-psa-release-control-files.txt',
             'scripts/release-test/flowhive-psa-admission.mjs',
             'scripts/release-test/dispatch-flowhive-psa-test.mjs']:
    assert Path(path).read_bytes() == subprocess.check_output(['git','show',BASE+':'+path]), path
assert not any(p.startswith('database/') for p in actual)
workflow = Path('.github/workflows/flowhive-sow-private-generation-ci.yml').read_text()
assert 'permissions:\n  contents: read\n' in workflow
for prohibited in ['id-token:', 'environment:', 'secrets.', 'workflow_dispatch:', 'continue-on-error:']:
    assert prohibited not in workflow, prohibited
subprocess.run(['git','diff','--check',BASE],check=True)
print(f'PRIVATE_GENERATION_SCOPE=PASS files={len(expected)} release_authority=unchanged')
