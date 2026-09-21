"""Prepare exact recovery proofs and retain all existing validation assertions."""
from pathlib import Path

path = Path(__file__).with_name('prepare-base.py')
text = path.read_text()
start = text.index('# Existing cleanup already removes this exact generated fixture;')
end = text.index('files[CI] = text', start)
text = text[:start] + '''# Keep the existing branch cleanup byte-identical and add one for this fixture.
match = re.search(r'^([ \\t]*)# LAYA_RELEASE_END fixture_cleanup\\n', text, re.M)
require(match is not None, 'The existing Laya fixture cleanup boundary is missing')
anchor = match[0]
addition = """      - name: Remove queue-recovery historical fixture
        if: always() && github.head_ref == 'fix/laya-protected-uat-recovery-20260921'
        run: rm -f -- tests/laya-admission-fixture.generated.test.mjs
"""
text = replace(text, anchor, anchor + marked('      ', 'cleanup', addition))
''' + text[end:]
text = text.replace("self.assertCountEqual(re.findall(pattern, text, re.M | re.S), ['fixture', 'ownership'])", "self.assertCountEqual(re.findall(pattern, text, re.M | re.S), ['fixture', 'ownership', 'cleanup'])", 1)
start = text.index('        changed = "        if: always() && (github.head_ref')
end = text.index('\n\n', start)
text = text[:start] + '        self.assertEqual(text.encode(), baseline(path))' + text[end:]
proof = path.with_name('c15-proof.txt').read_text()
patch = 'addition = ' + repr(proof) + '\nanchor = "    if args.base in (\'045b66ca01baa68b2f5b3f6eb9e063c23c335981\'"\nrequire(text.count(anchor) == 1, "Exact reviewed base selection missing")\ntext = text.replace(anchor, addition + anchor, 1)\nfiles[VALIDATOR] = text'
text = text.replace('files[VALIDATOR] = text', patch, 1)

negative_tests = '''    def test_existing_serialization_assertions_are_retained(self):
        path = 'tests/validate-systemwide-enterprise-reliability.mjs'
        updated = (ROOT/path).read_text()
        self.assertIn(recovery.NEW_GROUP, updated)
        pattern = r'// LAYA_UAT_RECOVERY_BEGIN serialization\\n.*?// LAYA_UAT_RECOVERY_END serialization\\n'
        self.assertEqual(len(re.findall(pattern, updated, re.S)), 1)
        normalized = re.sub(pattern, '', updated, flags=re.S)
        self.assertEqual(normalized.replace(recovery.NEW_GROUP, 'projectpulse-deploy-test').encode(), baseline(path))

    def test_existing_validator_rejects_unsafe_concurrency(self):
        controller = ROOT/recovery.CONTROLLER
        original = controller.read_text()
        mutations = (
            ('group: ' + recovery.NEW_GROUP, 'group: unreviewed-test-queue'),
            ('group: ' + recovery.NEW_GROUP, 'group: $' + '{{ github.run_id }}'),
            ('cancel-in-progress: false', 'cancel-in-progress: true'),
        )
        try:
            for old, new in mutations:
                self.assertEqual(original.count(old), 1)
                controller.write_text(original.replace(old, new, 1))
                result = subprocess.run(['node', 'tests/validate-systemwide-enterprise-reliability.mjs'],
                                        cwd=ROOT, text=True, capture_output=True, timeout=30)
                self.assertNotEqual(result.returncode, 0, new)
                self.assertIn('serialize deployments without cancellation', result.stderr)
        finally:
            controller.write_text(original)

'''
serialization_guard = r'''// LAYA_UAT_RECOVERY_BEGIN serialization
const reviewedTestConcurrency = 'concurrency:\n  group: projectpulse-deploy-test-recovery-20260921\n  queue: max\n  cancel-in-progress: false\n';
if (deployment.split(reviewedTestConcurrency).length !== 2) {
  throw new Error('Protected-Test workflow must serialize deployments without cancellation');
}
// LAYA_UAT_RECOVERY_END serialization
'''
register = '''# Register only the reviewed fixed queue name; keep paths and assertions intact.
validation_path = 'tests/validate-systemwide-enterprise-reliability.mjs'
validation = source(validation_path)
validation, count = re.subn(r'projectpulse-deploy-test(?![a-zA-Z0-9_.-])', 'projectpulse-deploy-test-recovery-20260921', validation)
require(count > 0, 'The existing exact serialization queue assertion is missing')
require(validation.replace('projectpulse-deploy-test-recovery-20260921', 'projectpulse-deploy-test') == source(validation_path), 'Unexpected serialization-validator source change')
'''
register += "validation_anchor = \"const deployment = read('.github/workflows/projectpulse-deploy-test.yml');\\n\"\n"
register += 'validation = replace(validation, validation_anchor, validation_anchor + ' + repr(serialization_guard) + ')\n'
register += 'files[validation_path] = validation\n'
register += "test_path = 'tests/protected-test-queue-recovery.test.py'\n"
register += "test_anchor = '    def test_application_provider_order_and_production_unchanged(self):\\n'\n"
register += 'files[test_path] = replace(files[test_path], test_anchor, ' + repr(negative_tests) + ' + test_anchor)\n\n'
anchor = '# Add a closed scope and inverse-normalization assertions to the new tests.'
if text.count(anchor) != 1:
    raise SystemExit('The closed candidate file-scope registration anchor changed')
text = text.replace(anchor, register + anchor, 1)
compile(text, str(path), 'exec')
exec(compile(text, str(path), 'exec'), {'__name__': '__main__', '__file__': str(path)})
