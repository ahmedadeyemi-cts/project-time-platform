"""Exact scope and semantics for the explicit Protected UAT runtime-recovery command."""
import os
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASE = os.environ.get("BASE_SHA", "")
ALLOWED = {
    ".github/workflows/module025-protected-uat-control.yml",
    ".github/workflows/release-supervisor-queue-ci.yml",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "tests/release-supervisor-queue/test_runtime_recovery_command_scope.py",
}

def run(*args):
    return subprocess.check_output(args, cwd=ROOT, text=True).strip()

if len(BASE) != 40:
    raise SystemExit("BASE_SHA must be the exact pull-request base commit.")

changed = {line for line in run("git", "diff", "--name-only", f"{BASE}...HEAD").splitlines() if line}
unexpected = changed - ALLOWED
if unexpected:
    raise SystemExit(f"Unexpected runtime-recovery command files: {sorted(unexpected)}")
missing = ALLOWED - changed
if missing:
    raise SystemExit(f"Missing governed runtime-recovery command files: {sorted(missing)}")

for protected in (
    ".github/workflows/projectpulse-deploy-test.yml",
    ".github/workflows/projectpulse-deploy-production.yml",
):
    subprocess.check_call(["git", "diff", "--exit-code", BASE, "HEAD", "--", protected], cwd=ROOT)

controller = (ROOT / ".github/workflows/module025-protected-uat-control.yml").read_text()
required = (
    "startsWith(github.event.comment.body, 'RECOVER MODULE025 PROTECTED UAT SHA ')",
    "^RECOVER\\ MODULE025\\ PROTECTED\\ UAT\\ SHA\\ ([0-9a-f]{40})$",
    "recover_private_runtime=false",
    "recover_private_runtime=true",
    'echo "recover_private_runtime=$recover_private_runtime" >> "$GITHUB_OUTPUT"',
    "RECOVER_PRIVATE_RUNTIME: ${{ steps.authorize.outputs.recover_private_runtime }}",
    '--arg recover_private_runtime "$RECOVER_PRIVATE_RUNTIME"',
    "recover_private_runtime:$recover_private_runtime",
    "PRODUCTION_MUTATION=NONE",
)
for marker in required:
    if marker not in controller:
        raise SystemExit(f"Missing runtime-recovery safety marker: {marker}")

# Normal push and DEPLOY behavior must remain non-recovery; only exact RECOVER may flip it.
deploy_pos = controller.index("^DEPLOY\\ MODULE025\\ PROTECTED\\ UAT\\ SHA")
recover_pos = controller.index("^RECOVER\\ MODULE025\\ PROTECTED\\ UAT\\ SHA")
true_pos = controller.index("recover_private_runtime=true")
if not (deploy_pos < recover_pos < true_pos):
    raise SystemExit("Recovery enablement is not confined to the exact RECOVER command branch.")

subprocess.check_call(["git", "diff", "--check", f"{BASE}...HEAD"], cwd=ROOT)
print("PROTECTED_UAT_RUNTIME_RECOVERY_COMMAND_SCOPE=PASS")
