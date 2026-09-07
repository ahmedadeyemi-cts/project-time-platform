from pathlib import Path
import re
import subprocess

r = Path('.')
p = r / '.github/workflows/projectpulse-deploy-test.yml'
s = p.read_text()
s = re.sub(r"<<<<<<< HEAD\n        if: \$\{\{ !cancelled\(\) && steps\.uat\.outcome == 'success' \}\}\n=======\n        if: steps\.psa_admission\.outputs\.authorized != 'true'\n>>>>>>> origin/evidence-main", "        if: ${{ !cancelled() && steps.psa_admission.outputs.authorized != 'true' && steps.uat.outcome == 'success' }}", s)
s = re.sub(r"<<<<<<< HEAD\n        if: \$\{\{ !cancelled\(\) && steps\.module025_fixture\.outcome == 'success' \}\}\n=======\n        if: steps\.psa_admission\.outputs\.authorized != 'true'\n>>>>>>> origin/evidence-main", "        if: ${{ !cancelled() && steps.psa_admission.outputs.authorized != 'true' && steps.module025_fixture.outcome == 'success' }}", s)
for key in ['assigned_work_uat', 'utilization_uat']:
    old = f"        id: {key}\n        if: ${{{{ !cancelled() && steps.uat.outcome == 'success' }}}}"
    new = f"        id: {key}\n        if: ${{{{ !cancelled() && (steps.uat.outcome == 'success' || steps.psa_live_uat.outputs.deployment_health_verified == 'true') }}}}"
    assert s.count(old) == 1
    s = s.replace(old, new)
assert '<<<<<<<' not in s
p.write_text(s)

p = r / '.github/workflows/celar-ai-oracle-gitops-ci.yml'
s = p.read_text()
assert s.count('.gatewayVersion == "1.1.6"') == 1
s = s.replace('.gatewayVersion == "1.1.6"', '.gatewayVersion == "1.1.7" and\n            .ollamaMaxLoadedModels == 1 and\n            .ollamaNumParallel == 1')
s = s.replace('          python3 tests/test-celar-sow-runtime-deadlines.py', '          python3 tests/test-celar-sow-runtime-deadlines.py\n          python3 tests/test-ollama-memory-policy.py\n          python3 tests/test-celar-runtime-evidence.py')
p.write_text(s)

p = r / '.github/workflows/flowhive-psa-release-control-ci.yml'
s = p.read_text()
s = s.replace('          CONTROL_BASE: ${{ github.event.pull_request.base.sha }}', '          CONTROL_BASE: ${{ github.event.pull_request.base.sha }}\n          GITHUB_HEAD_REF: ${{ github.head_ref }}')
s = s.replace('          node tests/flowhive-psa-release-control.mjs', '''          if [[ "$GITHUB_HEAD_REF" == 'fix/sow-runtime-diagnostics-and-uat-isolation-20260906' ]]; then
            node tests/validate-sow-generation-uat-scope.mjs
          else
            node tests/flowhive-psa-release-control.mjs
          fi''')
p.write_text(s)

p = r / 'tests/test-sow-uat-isolation.py'
s = p.read_text()
s = s.replace("for name in ('assigned_work_uat', 'utilization_uat', 'module025_fixture'):", "for name in ('assigned_work_uat', 'utilization_uat'):")
s = s.replace('assert condition == "${{ !cancelled() && steps.uat.outcome == \'success\' }}", (name, condition)', 'assert condition == "${{ !cancelled() && (steps.uat.outcome == \'success\' || steps.psa_live_uat.outputs.deployment_health_verified == \'true\') }}", (name, condition)')
s = s.replace('assert by_id[\'module025_uat\'][\'if\'] == "${{ !cancelled() && steps.module025_fixture.outcome == \'success\' }}"', '''assert by_id['module025_fixture']['if'] == "${{ !cancelled() && steps.psa_admission.outputs.authorized != 'true' && steps.uat.outcome == 'success' }}"
assert by_id['module025_fixture'].get('continue-on-error', 'false') == 'false'
assert by_id['module025_uat']['if'] == "${{ !cancelled() && steps.psa_admission.outputs.authorized != 'true' && steps.module025_fixture.outcome == 'success' }}"''')
p.write_text(s)

p = r / 'tests/validate-sow-generation-uat-scope.mjs'
s = p.read_text()
s = s.replace("assert.equal(normalized, oldSteps.get(key),", "assert.equal(normalized, revisedGates.has(key) ? oldSteps.get(key)?.replace(/^        if:.*\\n/m, '') : oldSteps.get(key),")
point = "  execFileSync('python3', ['tests/test-ollama-memory-policy.py'], {cwd: root, stdio:'inherit'});"
insert = '''  const oracleCi = '.github/workflows/celar-ai-oracle-gitops-ci.yml';
  const oldCi = git('show', `${base}:${oracleCi}`);
  const expectedCi = oldCi.replace('.gatewayVersion == "1.1.6"', '.gatewayVersion == "1.1.7" and\\n            .ollamaMaxLoadedModels == 1 and\\n            .ollamaNumParallel == 1')
    .replace('          python3 tests/test-celar-sow-runtime-deadlines.py', '          python3 tests/test-celar-sow-runtime-deadlines.py\\n          python3 tests/test-ollama-memory-policy.py\\n          python3 tests/test-celar-runtime-evidence.py');
  assert.equal(readFileSync(new URL(`../${oracleCi}`, import.meta.url), 'utf8').trimEnd(), expectedCi,
    'Oracle validation changes must only correct the pinned manifest and add policy/privacy tests');
'''
assert point in s
s = s.replace(point, point + '\n' + insert)
p.write_text(s)

p = r / 'tests/flowhive-psa-release-workflow.test.py'
s = p.read_text()
s = s.replace('''    for key in ['uat','module025_fixture','module025_uat']:
        assert byid[key]['if']=="steps.psa_admission.outputs.authorized != 'true'"''', '''    assert byid['uat']['if']=="steps.psa_admission.outputs.authorized != 'true'"
    assert byid['module025_fixture']['if']=="${{ !cancelled() && steps.psa_admission.outputs.authorized != 'true' && steps.uat.outcome == 'success' }}"
    assert byid['module025_uat']['if']=="${{ !cancelled() && steps.psa_admission.outputs.authorized != 'true' && steps.module025_fixture.outcome == 'success' }}"
    for key in ['assigned_work_uat','utilization_uat']:
        assert byid[key]['if']=="${{ !cancelled() && (steps.uat.outcome == 'success' || steps.psa_live_uat.outputs.deployment_health_verified == 'true') }}"
    assert steps.index(byid['assigned_work_uat'])<steps.index(byid['utilization_uat'])<steps.index(byid['module025_fixture'])''')
start = s.index("        before=old['jobs']['deploy']['steps'];")
end = s.index('\nif __name__', start)
s = s[:start] + '''        # This integration starts from the already merged #875 controller.
        # Compare by unique step name because #874 deliberately moves the work
        # gates before SOW composition; never accept adding/dropping a step.
        before=old['jobs']['deploy']['steps']; after=self.doc['jobs']['deploy']['steps']
        old_steps={step['name']:step for step in before}
        self.assertEqual(len(old_steps),len(before))
        self.assertEqual(len(after),len(before))
        self.assertEqual(set(old_steps),{step['name'] for step in after})
        revised={'assigned_work_uat','utilization_uat','module025_fixture','module025_uat'}
        for step in after:
            a=copy.deepcopy(old_steps[step['name']]);b=copy.deepcopy(step)
            if b.get('id') in revised:
                a.pop('if',None);b.pop('if',None)
                if b['id']=='module025_fixture':
                    b['run']=b['run'].replace('echo "expires_at=$FIXTURE_EXPIRES_AT" >> "$GITHUB_OUTPUT"\\n','')
                if b['id']=='module025_uat':
                    b['env'].pop('MODULE025_UAT_EXPIRES_AT',None)
            self.assertEqual(a,b,step['name'])
        self.assertEqual(old['on'],self.doc['on'])
        self.assertEqual(old['jobs']['deploy']['env'],self.doc['jobs']['deploy']['env'])

''' + s[end:]
p.write_text(s)

p = r / '.github/sow-runtime-isolation-files.txt'
names = set(p.read_text().splitlines())
names.update(['.github/workflows/celar-ai-oracle-gitops-ci.yml', '.github/workflows/flowhive-psa-release-control-ci.yml', 'tests/flowhive-psa-release-workflow.test.py'])
p.write_text('\n'.join(sorted(names)) + '\n')

p = r / 'docs/sow-runtime-investigation-20260906.md'
s = p.read_text()
s += '''

## Combined FlowHive release reconciliation

PR #874 is being integrated with the already merged #875 Protected Test control
plane, at base `8c84d91b8a4f7d1583118f6d701185b355c9684f`, before inclusion in the
unmerged #872 candidate. The independent assigned-work and utilization checks
remain ahead of Module 025 composition and also run when the PSA lane establishes
deployment health, even if its AI acceptance fails. The PSA candidate must never
enable Module 025 authorization fixtures. The fixture expiration output and
cleanup reserve remain enforced for normal main releases.

The Oracle validator now checks the intended `1.1.7` gateway manifest, one loaded
model and one inference lane, and executes the memory-policy and privacy tests.
Its former `1.1.6` equality was a stale test, not evidence that the new runtime
policy was invalid. No runtime setting, provider order or timeout was relaxed to
pass that check. FlowHive's separate five-minute durable operation limit is not
changed by Module 025's longer multi-phase SOW budget.

The exact integration scope is 25 files. The original release permissions,
triggers, environment, concurrency, image ownership and rollback are compared
against the merged controller. Local scope, parsed workflow, false-success and
Oracle shell contracts passed; final combined CI and authenticated live provider
acceptance must still be recorded. Repository merge does not itself prove the
Oracle GitOps host applied the new manifest or that either live AI flow passed.
'''
p.write_text(s)

subprocess.run(['git', 'add', '--all'], check=True)
subprocess.run(['git', 'diff', '--cached', '--check'], check=True)
tree = subprocess.check_output(['git', 'write-tree'], text=True).strip()
assert tree == '8c0acef5d74a46f51834f68c0d5312fd0458cb61', 'The prepared source is not the exact locally reviewed tree.'
print('REVIEWED_INTEGRATION_TREE=' + tree)
