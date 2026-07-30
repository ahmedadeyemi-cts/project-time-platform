from pathlib import Path

path = Path('.github/workflows/pulse-ai-private-runtime-activation-ci.yml')
text = path.read_text()
old = '''          EXPECTED_BASE='56dd3df02a26aa0c07c0a92dd2ac9dd9f3a3d747'
          git cat-file -e "$EXPECTED_BASE^{commit}"
          [[ "$(git merge-base "$EXPECTED_BASE" HEAD)" == "$EXPECTED_BASE" ]]
          CHANGED="$(git diff --name-only "$EXPECTED_BASE"...HEAD)"
'''
new = '''          git fetch origin main --no-tags
          CURRENT_MAIN="$(git rev-parse origin/main)"
          CHANGED="$(git diff --name-only origin/main...HEAD)"
'''
if old not in text:
    raise SystemExit('Expected fixed-base source-isolation block was not found.')
text = text.replace(old, new, 1)
text = text.replace('          git diff --check "$EXPECTED_BASE"...HEAD\n          echo "PULSE_AI_PRIVATE_RUNTIME_BASE=$EXPECTED_BASE"\n', '          git diff --check origin/main...HEAD\n          echo "PULSE_AI_PRIVATE_RUNTIME_BASE=$CURRENT_MAIN"\n', 1)
path.write_text(text)
print('PR279_CURRENT_MAIN_SOURCE_ISOLATION=PASS')
