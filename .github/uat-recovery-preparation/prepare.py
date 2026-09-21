"""Prepare and test a closed repair tree. Never move a branch or deploy an app.

This helper belongs only to the preparation branch. The produced commit has the
reviewed main baseline as its parent and excludes this helper and its workflow.
A separately reviewed PR is still required to merge or deploy the repair.
"""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import urllib.request

REPO = 'ahmedadeyemi-cts/project-time-platform'
BASE = 'af060cbc311dcc7cce89c3ae2cc0bff040f5f6ea'
BASE_TREE = '5b0046a55a08dcae6fc5e79743144b4ebc92cf08'
BRANCH = 'fix/laya-protected-uat-recovery-20260921'
ROOT = Path.cwd()
PACK = ROOT/'.github/uat-recovery-preparation'
DEPLOY = '.github/workflows/projectpulse-deploy-test.yml'
SUPERVISOR = '.github/workflows/module025-protected-uat-control.yml'
VALIDATOR = 'scripts/release-test/verify-module025-quarantine-controller.py'
CI = '.github/workflows/flowhive-psa-release-control-ci.yml'
GATEWAY = 'deployment/laya/deploy-gateway.sh'
ADAPTER = 'deployment/oracle-celar/gateway/laya_decisions.py'
OLD_GROUP = '  group: projectpulse-deploy-test\n'
NEW_GROUP = '  group: projectpulse-deploy-test-recovery-20260921\n'
files = {}


def require(condition, message):
    if not condition: raise RuntimeError(message)


def source(path):
    return subprocess.check_output(['git', 'show', BASE + ':' + path], text=True)


def replace(text, old, new):
    require(text.count(old) == 1, 'Expected one reviewed patch anchor: ' + old[:100])
    return text.replace(old, new, 1)


def api(method, suffix, body=None):
    request = urllib.request.Request('https://api.github.com/repos/' + REPO + '/' + suffix,
        data=None if body is None else json.dumps(body).encode(), method=method,
        headers={'Authorization': 'Bearer ' + os.environ['GH_TOKEN'],
                 'Accept': 'application/vnd.github+json', 'Content-Type': 'application/json',
                 'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'reviewed-uat-repair-preparation'})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)


def marked(indent, name, body):
    return indent + '# LAYA_UAT_RECOVERY_BEGIN ' + name + '\n' + body + indent + '# LAYA_UAT_RECOVERY_END ' + name + '\n'


require(os.environ.get('GITHUB_REPOSITORY') == REPO, 'Wrong preparation repository')
require(os.environ.get('GITHUB_REF') == 'refs/heads/ops/prepare-laya-uat-recovery-20260921', 'Wrong preparation branch')
require(api('GET', 'git/ref/heads/main')['object']['sha'] == BASE, 'Main changed; do not overwrite newer work')

original = source(DEPLOY)
require(hashlib.sha256(original.encode()).hexdigest() == '3cbd088ab607eb3f3399ab254332adb818c946c7287c33e7a4a52f7e04a3f179', 'The reviewed deployment controller changed')
files[DEPLOY] = replace(original, OLD_GROUP, NEW_GROUP)
digest = hashlib.sha256(files[DEPLOY].encode()).hexdigest()

mapping = {
    'protected-test-queue-recovery.py': 'scripts/release-test/protected-test-queue-recovery.py',
    'protected-test-queue-recovery.test.py': 'tests/protected-test-queue-recovery.test.py',
    'test_managed_adapters.py': 'tests/laya/test_managed_adapters.py',
    'prepare-recovery-admission.py': 'tests/prepare-recovery-admission.py',
    'protected-test-queue-recovery-ci.yml': '.github/workflows/protected-test-queue-recovery-ci.yml',
}
for name, path in mapping.items():
    files[path] = (PACK/name).read_text().replace('@@CONTROLLER_SHA256@@', digest)

text = source(SUPERVISOR)
anchor = "      QUARANTINED_ZERO_JOB_RUN_ID_8: '35524946948'\n"
text = replace(text, anchor, anchor + marked('      ', 'register', "      QUARANTINED_ZERO_JOB_RUN_ID_9: '35645759101'\n"))
anchor = '          workflow_state="$(gh api "repos/${GITHUB_REPOSITORY}/actions/workflows/${DEPLOY_WORKFLOW_ID}" --jq \'.state\')"\n'
text = replace(text, anchor, marked('          ', 'preflight', '          python3 scripts/release-test/protected-test-queue-recovery.py "$requested_sha"\n') + anchor)
text = replace(text, "git diff --quiet '045b66ca01baa68b2f5b3f6eb9e063c23c335981' HEAD -- .github/workflows/projectpulse-deploy-test.yml",
               "python3 scripts/release-test/verify-module025-quarantine-controller.py --base '045b66ca01baa68b2f5b3f6eb9e063c23c335981'")
addition = '''            # This exact run never acquired a job. Cancellation is attempted in
            # the preflight; only unchanged zero-job evidence may remain fenced.
            if [[ "$run_id" == "$QUARANTINED_ZERO_JOB_RUN_ID_9" && \\
                  "$requested_sha" != 'af060cbc311dcc7cce89c3ae2cc0bff040f5f6ea' ]] && \\
               git merge-base --is-ancestor 'af060cbc311dcc7cce89c3ae2cc0bff040f5f6ea' HEAD && \\
               python3 scripts/release-test/verify-module025-quarantine-controller.py --base 'af060cbc311dcc7cce89c3ae2cc0bff040f5f6ea' && \\
               verify_zero_job_quarantine "$run_id" \\
                 'af060cbc311dcc7cce89c3ae2cc0bff040f5f6ea' \\
                 'main' \\
                 '2026-09-21T19:35:24Z'; then
              pending_json="$(gh api "repos/${GITHUB_REPOSITORY}/actions/runs/${run_id}/pending_deployments")" \\
                || fail 'Cannot verify pending approvals for the exact Laya release orphan.'
              if jq -e 'type == "array" and length == 0' <<<"$pending_json" >/dev/null; then
                quarantined_runs+=("$run_id")
                continue
              fi
            fi
'''
anchor = '            run_status="$(jq -r --argjson id "$run_id" \'.workflow_runs[] | select(.id == $id) | .status\' <<<"$runs_json")"\n'
text = replace(text, anchor, marked('            ', 'orphan', addition) + anchor)
files[SUPERVISOR] = text

text = source(VALIDATOR)
text = replace(text, '"c15ef12d5ce1bc54c15d8b31c87a50daa94bad17"), default=BASE)',
               '"c15ef12d5ce1bc54c15d8b31c87a50daa94bad17", "045b66ca01baa68b2f5b3f6eb9e063c23c335981", "' + BASE + '"), default=BASE)')
anchor = '    current = subprocess.check_output(["git", "show", "HEAD:" + CONTROLLER])\n'
addition = f'''    # Normalize only this exact reviewed concurrency-generation change.
    # Any additional byte change still fails the existing historical proof.
    if hashlib.sha256(current).hexdigest() == '{digest}':
        current = current.replace({NEW_GROUP.encode()!r}, {OLD_GROUP.encode()!r}, 1)
    if args.base in ('045b66ca01baa68b2f5b3f6eb9e063c23c335981', '{BASE}'):
        if current != original:
            raise SystemExit('The stale controller differs outside the exact queue-generation repair')
        print('MODULE025_QUARANTINE_CONTROLLER=PASS')
        return
'''
text = replace(text, anchor, anchor + addition)
files[VALIDATOR] = text

text = source(CI)
anchor = '      - name: Exact control-only scope and negative admission tests\n'
addition = '''      - name: Select the unchanged historical assertions for reviewed queue recovery
        if: github.head_ref == 'fix/laya-protected-uat-recovery-20260921'
        env:
          GITHUB_HEAD_REF: ${{ github.head_ref }}
        run: python3 tests/prepare-recovery-admission.py
'''
text = replace(text, anchor, marked('      ', 'fixture', addition) + anchor)
anchor = '          elif [[ "$GITHUB_HEAD_REF" == fix/flowhive-installed-verifier-20260921 ]]; then\n'
addition = '''          elif [[ "$GITHUB_HEAD_REF" == fix/laya-protected-uat-recovery-20260921 ]]; then
            python3 tests/protected-test-queue-recovery.test.py
'''
text = replace(text, anchor, marked('          ', 'ownership', addition) + anchor)
# Existing cleanup already removes this exact generated fixture; enable it only
# for the additional exact branch. Do not remove any test or failure assertion.
cleanup_if = "        if: always() && github.head_ref == 'feature/celar-laya-document-decisions-20260921'"
require(cleanup_if in text, 'Find and review the existing fixture cleanup condition')
text = replace(text, cleanup_if, "        if: always() && (github.head_ref == 'feature/celar-laya-document-decisions-20260921' || github.head_ref == 'fix/laya-protected-uat-recovery-20260921')")
files[CI] = text

text = source(GATEWAY)
text = replace(text, 'DROPIN=/etc/systemd/system/celar-ai-gateway.service.d/80-laya-decisions.conf\n',
               'DROPIN=/etc/systemd/system/celar-ai-gateway.service.d/80-laya-decisions.conf\nMANIFEST="$TARGET/laya-managed-release.json"\n')
text = replace(text, 'CHANGED=0\nSTARTED_LAYA=0\n', 'CHANGED=0\nSTARTED_LAYA=0\nEXISTING=0\nHAD_MANIFEST=0\n')
start = text.index('cleanup() {'); end = text.index('\ntrap cleanup EXIT', start)
cleanup = '''cleanup() {
    local status=$? restored=1
    trap - EXIT
    set +e
    if [[ "$status" != 0 ]]; then
        if [[ "$CHANGED" == 1 ]]; then
            if [[ "$EXISTING" == 1 ]]; then
                for name in laya_decisions.py wsgi_decisions.py; do
                    cp -p -- "$STAGE/original-$name" "$TARGET/$name" || restored=0
                done
                cp -p -- "$STAGE/original-dropin" "$DROPIN" || restored=0
                if [[ "$HAD_MANIFEST" == 1 ]]; then
                    cp -p -- "$STAGE/original-manifest" "$MANIFEST" || restored=0
                else
                    rm -f -- "$MANIFEST" || restored=0
                fi
            else
                rm -f -- "$TARGET/laya_decisions.py" "$TARGET/wsgi_decisions.py" "$DROPIN" "$MANIFEST" || restored=0
            fi
            systemctl daemon-reload || restored=0
            systemctl restart celar-ai-gateway.service || restored=0
            systemctl is-active --quiet celar-ai-gateway.service || restored=0
            if [[ "$restored" == 1 ]]; then
                echo 'LAYA_GATEWAY_RESTORE=PASS; prior managed release restored.'
            else
                echo 'LAYA_GATEWAY_RESTORE=FAILED; inspect the gateway before retrying.' >&2
            fi
        fi
        if [[ "$STARTED_LAYA" == 1 ]]; then
            systemctl stop celar-laya.service || echo 'Could not stop the newly started Laya service.' >&2
        fi
    fi
    rm -rf -- "$STAGE"
    exit "$status"
}'''
text = text[:start] + cleanup + text[end:]
old = '''    for name in laya_decisions.py wsgi_decisions.py; do
        [[ ! -L "$TARGET/$name" ]] && cmp -s "$SOURCE/$name" "$TARGET/$name" || { echo 'STOP: Different adapter version exists.'; exit 1; }
    done
    EXISTING=1
'''
managed = (PACK/'managed-block.py').read_text()
managed = managed.replace("    verify_managed(Path(sys.argv[1])", "    checked_file(Path(sys.argv[6]))\n    verify_managed(Path(sys.argv[1])")
new = '    python3 - "$ROOT" "$TARGET" "$MANIFEST" "$MODE" "' + BASE + '" "$DROPIN" <<\'LAYA_MANAGED\'\n' + managed + '''LAYA_MANAGED
    for name in laya_decisions.py wsgi_decisions.py; do
        cp -p -- "$TARGET/$name" "$STAGE/original-$name"
    done
    cp -p -- "$DROPIN" "$STAGE/original-dropin"
    if [[ -f "$MANIFEST" ]]; then
        HAD_MANIFEST=1
        cp -p -- "$MANIFEST" "$STAGE/original-manifest"
    fi
    EXISTING=1
'''
text = replace(text, old, new)
anchor = '''else
    for name in laya_decisions.py wsgi_decisions.py; do
        [[ ! -e "$TARGET/$name" && ! -L "$TARGET/$name" ]] || { echo 'STOP: Unmanaged adapter file exists.'; exit 1; }
'''
text = replace(text, anchor, '''else
    [[ ! -e "$MANIFEST" && ! -L "$MANIFEST" ]] || { echo 'STOP: Orphan managed-release evidence exists.'; exit 1; }
    for name in laya_decisions.py wsgi_decisions.py; do
        [[ ! -e "$TARGET/$name" && ! -L "$TARGET/$name" ]] || { echo 'STOP: Unmanaged adapter file exists.'; exit 1; }
''')
text = replace(text, '        rm -- "$DROPIN" "$TARGET/laya_decisions.py" "$TARGET/wsgi_decisions.py"\n',
               '        CHANGED=1\n        rm -- "$DROPIN" "$TARGET/laya_decisions.py" "$TARGET/wsgi_decisions.py"\n        [[ "$HAD_MANIFEST" == 0 ]] || rm -- "$MANIFEST"\n')
text = replace(text, "    echo 'LAYA_GATEWAY_ROLLBACK=PASS; database audit history retained.'\n", "    CHANGED=0\n    echo 'LAYA_GATEWAY_ROLLBACK=PASS; database audit history retained.'\n")
anchor = 'if ! systemctl is-active --quiet celar-laya.service; then\n'
addition = '''python3 - "$ROOT" "$SOURCE" "$STAGE/new-manifest" <<'LAYA_MANIFEST'
import hashlib, json, subprocess, sys
from pathlib import Path
root, source, target = map(Path, sys.argv[1:])
release = subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
data = {'version': 1, 'source_commit': release,
        'adapters': {name: hashlib.sha256((source/name).read_bytes()).hexdigest()
                     for name in ('laya_decisions.py', 'wsgi_decisions.py')}}
target.write_text(json.dumps(data, sort_keys=True) + '\\n')
LAYA_MANIFEST
'''
text = replace(text, anchor, addition + anchor)
anchor = 'if [[ "$EXISTING" == 0 ]]; then\n    CHANGED=1\n'
addition = '''NEEDS_UPDATE=0
[[ "$EXISTING" == 1 && "$HAD_MANIFEST" == 1 ]] || NEEDS_UPDATE=1
for name in laya_decisions.py wsgi_decisions.py; do
    if ! cmp -s "$SOURCE/$name" "$TARGET/$name"; then NEEDS_UPDATE=1; fi
done
if [[ "$NEEDS_UPDATE" == 1 ]]; then
    CHANGED=1
'''
text = replace(text, anchor, addition)
anchor = '    install -o root -g root -m 0644 "$STAGE/dropin" "$DROPIN"\n'
text = replace(text, anchor, anchor + '    install -o root -g root -m 0644 "$STAGE/new-manifest" "$MANIFEST"\n')
files[GATEWAY] = text
files[ADAPTER] = '# Managed adapter upgrades retain the previous reviewed release on failure.\n' + source(ADAPTER)

# Add a closed scope and inverse-normalization assertions to the new tests.
path = 'tests/protected-test-queue-recovery.test.py'
anchor = '    def test_application_provider_order_and_production_unchanged(self):\n'
allowed = sorted(files)
addition = f'''    def test_exact_recovery_file_scope(self):
        expected = {allowed!r}
        changed = subprocess.check_output(['git', '-C', str(ROOT), 'diff', '--name-only', BASE, 'HEAD'], text=True).splitlines()
        self.assertEqual(sorted(changed), expected)
        for row in subprocess.check_output(['git', '-C', str(ROOT), 'diff', '--name-status', BASE, 'HEAD'], text=True).splitlines():
            self.assertIn(row.split('\\t', 1)[0], ('A', 'M'))

    def test_existing_admission_ci_is_retained(self):
        path = '{CI}'
        text = (ROOT/path).read_text()
        pattern = r'^[ \\t]*# LAYA_UAT_RECOVERY_BEGIN ([a-z_]+)\\n.*?^[ \\t]*# LAYA_UAT_RECOVERY_END \\1\\n'
        self.assertCountEqual(re.findall(pattern, text, re.M | re.S), ['fixture', 'ownership'])
        text = re.sub(pattern, '', text, flags=re.M | re.S)
        changed = "        if: always() && (github.head_ref == 'feature/celar-laya-document-decisions-20260921' || github.head_ref == 'fix/laya-protected-uat-recovery-20260921')"
        original = "        if: always() && github.head_ref == 'feature/celar-laya-document-decisions-20260921'"
        self.assertEqual(text.count(changed), 1)
        self.assertEqual(text.replace(changed, original, 1).encode(), baseline(path))

'''
files[path] = replace(files[path], anchor, addition + anchor)

for path, text in files.items():
    target = ROOT/path; target.parent.mkdir(parents=True, exist_ok=True); target.write_text(text)
    if path.endswith('.py'): compile(text, path, 'exec')
subprocess.run(['bash', '-n', GATEWAY], check=True)
subprocess.run(['git', 'read-tree', BASE], check=True)
subprocess.run(['git', 'add', '--', *sorted(files)], check=True)
subprocess.run(['git', 'diff', '--cached', '--check'], check=True)
tree = subprocess.check_output(['git', 'write-tree'], text=True).strip()
env = {**os.environ, 'GIT_AUTHOR_NAME': 'Protected Test repair preparation', 'GIT_AUTHOR_EMAIL': 'actions@users.noreply.github.com',
       'GIT_COMMITTER_NAME': 'Protected Test repair preparation', 'GIT_COMMITTER_EMAIL': 'actions@users.noreply.github.com'}
local = subprocess.check_output(['git', 'commit-tree', tree, '-p', BASE, '-m', 'Prepare reviewed Protected Test queue recovery and managed Laya upgrades'], text=True, env=env).strip()
subprocess.run(['git', 'reset', '--hard', local], check=True)
# This reset drops the preparation-only files from this disposable checkout.
for command in (
    ['python3', 'tests/protected-test-queue-recovery.test.py'],
    ['python3', 'tests/laya/test_managed_adapters.py'],
    ['python3', 'tests/laya/test_gateway.py'],
    ['python3', 'tests/laya/test_release_wiring.py'],
    ['node', 'tests/validate-systemwide-image-build-controller.mjs'],
    ['node', 'tests/validate-systemwide-enterprise-reliability.mjs'],
    ['node', 'tests/validate-celar-ai-flowhive-authoritative-release.mjs'],
):
    subprocess.run(command, check=True)
with tempfile.NamedTemporaryFile() as ci_environment:
    test_env = {**os.environ, 'GITHUB_HEAD_REF': BRANCH, 'GITHUB_BASE_REF': 'main', 'GITHUB_ENV': ci_environment.name}
    try:
        subprocess.run(['python3', 'tests/prepare-recovery-admission.py'], env=test_env, check=True)
        subprocess.run(['node', '--test', 'tests/laya-admission-fixture.generated.test.mjs'], env=test_env, check=True)
    finally:
        (ROOT/'tests/laya-admission-fixture.generated.test.mjs').unlink(missing_ok=True)
require(api('GET', 'git/ref/heads/main')['object']['sha'] == BASE, 'Main changed; preparation must be reconciled before publishing')
entries = []
for path in sorted(files):
    mode = subprocess.check_output(['git', 'ls-files', '--stage', '--', path], text=True).split()[0]
    entries.append({'path': path, 'mode': mode, 'type': 'blob', 'content': (ROOT/path).read_text()})
remote_tree = api('POST', 'git/trees', {'base_tree': BASE_TREE, 'tree': entries})['sha']
require(remote_tree == tree, 'Remote prepared tree does not match the tree tested locally')
remote = api('POST', 'git/commits', {'message': 'Recover Protected Test queue and preserve managed Laya upgrades\n\nFixed serialized queue generation, exact zero-job run evidence and normal cancellation only. Preserve all deployment, migration, provider order, approval and production guards. Retain previous managed gateway release on failed upgrades. Offline source, negative, transaction and historical admission tests passed. No branch moved and no deployment performed by preparation.', 'tree': remote_tree, 'parents': [BASE]})['sha']
print('PREPARED_RECOVERY_COMMIT=' + remote, flush=True)
print('PREPARED_RECOVERY_TREE=' + remote_tree, flush=True)
print('PREPARED_RECOVERY_TESTS=PASS; MAIN_UNCHANGED=true; DEPLOYMENT_PERFORMED=false', flush=True)
with open(os.environ['GITHUB_STEP_SUMMARY'], 'a', encoding='utf-8') as output:
    output.write('## Prepared recovery commit\n\n`' + remote + '`\n\nTree `' + remote_tree + '`\n\nTests passed. No branch was moved and no deployment occurred.\n')
