"""Keep historical cleanup unchanged; add cleanup owned by the repair fixture."""
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
compile(text, str(path), 'exec')
exec(compile(text, str(path), 'exec'), {'__name__': '__main__', '__file__': str(path)})
