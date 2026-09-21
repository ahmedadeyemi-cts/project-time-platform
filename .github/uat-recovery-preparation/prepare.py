"""Prepare exact historical-layout proofs while retaining existing CI cleanup."""
from pathlib import Path
import hashlib
import subprocess

# Read the actual immutable Git object, not a connector's rendered file view.
for revision in ('c15ef12d5ce1bc54c15d8b31c87a50daa94bad17', 'af060cbc311dcc7cce89c3ae2cc0bff040f5f6ea'):
    raw = subprocess.check_output(['git', 'show', revision + ':.github/workflows/projectpulse-deploy-test.yml'])
    identity = hashlib.sha1(b'blob ' + str(len(raw)).encode() + b'\0' + raw).hexdigest()
    begin = raw.index(b'      - name: Guard exact source and validate release\n')
    end = raw.index(b'          for required in ', begin)
    print('IMMUTABLE_CONTROLLER=' + revision + ' blob=' + identity + ' sha256=' + hashlib.sha256(raw).hexdigest(), flush=True)
    print(raw[begin:end].decode(), flush=True)

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
compile(text, str(path), 'exec')
exec(compile(text, str(path), 'exec'), {'__name__': '__main__', '__file__': str(path)})
