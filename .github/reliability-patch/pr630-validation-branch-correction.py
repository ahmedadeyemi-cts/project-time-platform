from pathlib import Path

path = Path("tests/validate-celar-ai-pr630-consolidated.mjs")
text = path.read_text()
old = "const branchName = process.env.GITHUB_HEAD_REF || process.env.GITHUB_REF_NAME || '';\n"
new = (
    "const branchName = process.env.CELAR_PR630_VALIDATION_BRANCH "
    "|| process.env.GITHUB_HEAD_REF || process.env.GITHUB_REF_NAME || '';\n"
)
if text.count(old) != 1:
    raise SystemExit(
        f"PR630 validation branch anchor: expected one match, found {text.count(old)}."
    )
path.write_text(text.replace(old, new, 1))
