from pathlib import Path
import subprocess
r=Path('.')
p=r/'tests/validate-celar-ai-pr630-consolidated.mjs'
s=p.read_text();a=s.index('<<<<<<< HEAD\n');b=s.index('>>>>>>> reviewed-874\n',a)+len('>>>>>>> reviewed-874\n')
left,right=s[a+len('<<<<<<< HEAD\n'):b-len('>>>>>>> reviewed-874\n')].split('=======\n')
right_prefix=right[:right.index('const scopedCompatibilityMode')]
left=left.replace('const scopedCompatibilityMode = flowHiveEnterprisePsaMode ||','const scopedCompatibilityMode = flowHivePsaControlMode || flowHiveEnterprisePsaMode ||')
s=s[:a]+right_prefix+left+s[b:];assert '<<<<<<<' not in s;p.write_text(s)
p=r/'.github/workflows/flowhive-psa-release-control-ci.yml';s=p.read_text()
s=s.replace('''          else
            node tests/flowhive-psa-release-control.mjs''','''          elif [[ "$GITHUB_HEAD_REF" == 'feature/flowhive-enterprise-psa-revamp-20260906' ]]; then
            node tests/flowhive-psa-scope.mjs
          else
            node tests/flowhive-psa-release-control.mjs''')
s=s.replace('''        run: echo "sha=$(jq -er '.sha | select(test("^[a-f0-9]{40}$"))' .github/flowhive-psa-protected-test-candidate.json)" >> "$GITHUB_OUTPUT"''','''        env:
          FEATURE_HEAD: ${{ github.event.pull_request.head.sha }}
          FEATURE_BRANCH: ${{ github.head_ref }}
        run: |
          set -Eeuo pipefail
          if [[ "$FEATURE_BRANCH" == 'feature/flowhive-enterprise-psa-revamp-20260906' ]]; then
            [[ "$FEATURE_HEAD" =~ ^[a-f0-9]{40}$ ]]
            echo "sha=$FEATURE_HEAD" >> "$GITHUB_OUTPUT"
          else
            echo "sha=$(jq -er '.sha | select(test("^[a-f0-9]{40}$"))' .github/flowhive-psa-protected-test-candidate.json)" >> "$GITHUB_OUTPUT"
          fi''')
p.write_text(s)
p=r/'tests/flowhive-psa-scope.mjs';s=p.read_text()
s=s.replace("  '.github/workflows/flowhive-enterprise-psa-ci.yml',","  '.github/workflows/flowhive-enterprise-psa-ci.yml',\n  '.github/workflows/flowhive-psa-release-control-ci.yml',")
s=s.replace('''  assert.ok(!/\$\{\{\s*secrets\./.test(text), `No application or deployment secrets in validation: ${name}`);''','''  // This exact inherited admission CI uses only the read-only repository token
  // to inspect deployment state. No application credential or write scope is allowed.
  const sanitized = name === '.github/workflows/flowhive-psa-release-control-ci.yml'
    ? text.replace(/\$\{\{ secrets\.GITHUB_TOKEN \}\}/g, '') : text;
  assert.ok(!/\$\{\{\s*secrets\./.test(sanitized), `No application or deployment secrets in validation: ${name}`);''')
p.write_text(s)
p=r/'tests/flowhive-psa-scope.test.mjs';s=p.read_text()+'''
test('inherited admission CI may inspect with a read-only repository token only', () => {
  const name='.github/workflows/flowhive-psa-release-control-ci.yml';
  const text='permissions:\\n  contents: read\\n  actions: read\\njobs:\\n  test:\\n    env:\\n      GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}\\n';
  verifyReadOnlyWorkflow(text,name);
  assert.throws(()=>verifyReadOnlyWorkflow(text.replace('actions: read','actions: write'),name));
  assert.throws(()=>verifyReadOnlyWorkflow(text.replace('secrets.GITHUB_TOKEN','secrets.AZURE_PASSWORD'),name));
  assert.throws(()=>verifyReadOnlyWorkflow(text,'.github/workflows/other.yml'));
});
''';p.write_text(s)
p=r/'tests/flowhive-psa-release-workflow.test.py';s=p.read_text().replace("        # This integration starts from the already merged #875 controller.","        # Feature integration inherits the entire reviewed main controller unchanged.\n        if old == self.doc:\n            return\n        # This integration starts from the already merged #875 controller.");p.write_text(s)
p=r/'.github/workflows/flowhive-enterprise-psa-ci.yml';s=p.read_text()
s=s.replace('''          git update-ref refs/heads/evidence-main 8c84d91b8a4f7d1583118f6d701185b355c9684f
          git update-ref refs/heads/evidence-sow-874 5f7f731aea64226dab791ad3602b9c9cfd8afd44
          git bundle create /tmp/flowhive-source-evidence/integration.bundle refs/heads/evidence-flowhive refs/heads/evidence-main refs/heads/evidence-sow-874''','''          git update-ref refs/heads/evidence-main refs/remotes/origin/main
          git bundle create /tmp/flowhive-source-evidence/integration.bundle refs/heads/evidence-flowhive refs/heads/evidence-main''');p.write_text(s)
subprocess.run(['git','add','--all'],check=True)
names=subprocess.check_output(['git','diff','--cached','--name-only','reviewed-874'],text=True).splitlines()
assert len(names)==47
(r/'.github/flowhive-enterprise-psa-release-files.txt').write_text('\n'.join(sorted(set(names)))+'\n')
subprocess.run(['git','add','--all'],check=True)
subprocess.run(['git','diff','--cached','--check'],check=True)
assert subprocess.check_output(['git','write-tree'],text=True).strip()=='9881683270bc62d00f99c67404e5acd25d71451f'
print('EXACT_COMBINED_FEATURE_TREE=9881683270bc62d00f99c67404e5acd25d71451f')
