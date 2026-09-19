"""Recognize exact reviewed controllers for the known zero-job orphan."""
import subprocess
import hashlib

BASE = "245b0915d895d83f1ceaed32460ad95a4a3d79be"
CONTROLLER = ".github/workflows/projectpulse-deploy-test.yml"
REPLACEMENTS = (
    (b"        timeout-minutes: 35", b"        timeout-minutes: 50"),
    (b"MODULE025_GENERATION_TIMEOUT_SECONDS: '1500'", b"MODULE025_GENERATION_TIMEOUT_SECONDS: '2520'"),
)


# PR #1106 changes acceptance only. The exact-main pre-Azure guard remains
# byte-identical to the stale run's controller. No arbitrary future controller
# receives this exception; the supervisor still verifies run identity, zero jobs,
# unchanged timestamps, ancestry and absence of pending environment approvals.
ORIGINAL_SHA256 = "5e20ba22c8f135393172b8110802094f7bfd742b54b0fda19df4ec7c51a2bfb7"
EXPORT_SHA256 = "276d23806215df92246a8b37eb7291f55f1dd1b7938ec38cdaff45570bebd2c3"


def permitted(original, current):
    if (hashlib.sha256(original).hexdigest() == ORIGINAL_SHA256
            and hashlib.sha256(current).hexdigest() == EXPORT_SHA256):
        return True
    reviewed = original
    for old, new in REPLACEMENTS:
        if reviewed.count(old) != 1:
            return False
        reviewed = reviewed.replace(old, new, 1)
    return current in (original, reviewed)


def main():
    original = subprocess.check_output(["git", "show", BASE + ":" + CONTROLLER])
    current = subprocess.check_output(["git", "show", "HEAD:" + CONTROLLER])
    if not permitted(original, current):
        raise SystemExit("Deployment controller is not an exact reviewed timing or export acceptance controller")
    print("MODULE025_QUARANTINE_CONTROLLER=PASS")


if __name__ == "__main__":
    main()
