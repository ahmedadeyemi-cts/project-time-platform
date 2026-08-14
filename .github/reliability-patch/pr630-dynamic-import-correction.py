from pathlib import Path

path = Path("tests/validate-celar-ai-pr630-consolidated.mjs")
text = path.read_text()

static_import = "import './validate-celar-ai-pr630-consolidated-legacy.mjs';\n"
if text.count(static_import) != 1:
    raise SystemExit(
        f"PR630 legacy static import: expected one match, found {text.count(static_import)}."
    )
text = text.replace(static_import, "", 1)

anchor = """syncBuiltinESMExports();
if (systemwideReliabilityMode)
  console.log('CELAR_PR630_SYSTEMWIDE_RELIABILITY_COMPATIBILITY=PASS');

try {
"""
replacement = """syncBuiltinESMExports();
if (systemwideReliabilityMode)
  console.log('CELAR_PR630_SYSTEMWIDE_RELIABILITY_COMPATIBILITY=PASS');

// Load the legacy validator only after the governed child-process override is
// installed. The legacy module captures execFileSync during module evaluation.
await import('./validate-celar-ai-pr630-consolidated-legacy.mjs');

try {
"""
if text.count(anchor) != 1:
    raise SystemExit(
        f"PR630 dynamic-import anchor: expected one match, found {text.count(anchor)}."
    )
path.write_text(text.replace(anchor, replacement, 1))
