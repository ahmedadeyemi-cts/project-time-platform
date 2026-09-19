"""Allow only the two PR #1103 acceptance timing changes for the known orphan."""
import subprocess

BASE = "245b0915d895d83f1ceaed32460ad95a4a3d79be"
CONTROLLER = ".github/workflows/projectpulse-deploy-test.yml"
REPLACEMENTS = (
    (b"        timeout-minutes: 35", b"        timeout-minutes: 50"),
    (b"MODULE025_GENERATION_TIMEOUT_SECONDS: '1500'", b"MODULE025_GENERATION_TIMEOUT_SECONDS: '2520'"),
)


def permitted(original, current):
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
        raise SystemExit("Deployment controller differs beyond the two reviewed acceptance timeouts")
    print("MODULE025_QUARANTINE_CONTROLLER=PASS")


if __name__ == "__main__":
    main()
