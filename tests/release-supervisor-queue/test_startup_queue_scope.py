"""Exact scope contract for the 2026-10-07 Protected UAT startup-queue repair."""
import os
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASE = os.environ.get("BASE_SHA", "")
ALLOWED = {
    ".github/workflows/module025-protected-uat-control.yml",
    ".github/workflows/pr1139-uat-recovery-ci.yml",
    ".github/workflows/pr1140-uat-recovery-ci.yml",
    ".github/workflows/release-supervisor-queue-ci.yml",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "tests/release-supervisor-queue/test_startup_queue_scope.py",
}

def run(*args):
    return subprocess.check_output(args, cwd=ROOT, text=True).strip()

if len(BASE) != 40:
    raise SystemExit("BASE_SHA must be the exact pull-request base commit.")

changed = {line for line in run("git", "diff", "--name-only", f"{BASE}...HEAD").splitlines() if line}
unexpected = changed - ALLOWED
if unexpected:
    raise SystemExit(f"Unexpected startup-queue repair files: {sorted(unexpected)}")

for protected in (
    ".github/workflows/projectpulse-deploy-test.yml",
    ".github/workflows/projectpulse-deploy-production.yml",
):
    subprocess.check_call(["git", "diff", "--exit-code", BASE, "HEAD", "--", protected], cwd=ROOT)

controller = (ROOT / ".github/workflows/module025-protected-uat-control.yml").read_text()
required = (
    "QUARANTINED_ZERO_JOB_RUN_ID_9: '37678070160'",
    "'3c95ccf183e45e294359defdec568ce39a144c42'",
    "'2026-10-07T19:54:49Z'",
    "startup_deadline_epoch=$(( $(date +%s) + 900 ))",
    "for attempt in $(seq 1 90); do",
    "sleep 10",\n    "has no attached job after the 900-second startup budget",
    "pending_deployments",
    "disabled_manually",
    "PRODUCTION_MUTATION=NONE",
)
for marker in required:
    if marker not in controller:
        raise SystemExit(f"Missing startup-queue safety marker: {marker}")

subprocess.check_call(["git", "diff", "--check", f"{BASE}...HEAD"], cwd=ROOT)
print("PROTECTED_UAT_STARTUP_QUEUE_SCOPE=PASS")
