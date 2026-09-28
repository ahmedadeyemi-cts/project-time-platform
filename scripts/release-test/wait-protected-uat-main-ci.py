#!/usr/bin/env python3
"""Read-only, bounded CI gate for the existing main-only UAT supervisor."""
import json
import os
import re
import subprocess
import time

REPOSITORY = "ahmedadeyemi-cts/project-time-platform"
REQUIRED = ("ProjectPulse CI", "ProjectPulse Repository Security Posture",
            "Validate Deployment Concurrency Governance")


def read(suffix):
    result = subprocess.run(["gh", "api", "--hostname", "github.com", "--method", "GET",
                             f"repos/{REPOSITORY}/{suffix}"],
                            capture_output=True, text=True, check=True, timeout=30)
    return json.loads(result.stdout)


def assess(runs, sha):
    pending = []
    for name in REQUIRED:
        eligible = [run for run in runs if run.get("name") == name
                    and run.get("event") == "push" and run.get("head_branch") == "main"
                    and run.get("head_sha") == sha]
        latest = max(eligible, key=lambda run: (run["id"], run.get("run_attempt", 1)), default=None)
        if latest is None or latest.get("status") != "completed":
            pending.append(name)
        elif latest.get("conclusion") != "success":
            raise RuntimeError(f"Required exact-main CI failed: {name}")
    return pending


def main():
    sha = os.environ.get("GITHUB_SHA", "")
    if (os.environ.get("GITHUB_REPOSITORY") != REPOSITORY
            or os.environ.get("GITHUB_REF") != "refs/heads/main"
            or not re.fullmatch(r"[0-9a-f]{40}", sha)):
        raise RuntimeError("Exact trusted main identity is required")
    deadline = time.monotonic() + 24 * 60
    while time.monotonic() < deadline:
        if read("git/ref/heads/main").get("object", {}).get("sha") != sha:
            raise RuntimeError("Main moved; do not deploy the superseded release")
        runs = read(f"actions/runs?head_sha={sha}&event=push&per_page=100")["workflow_runs"]
        pending = assess(runs, sha)
        if not pending:
            print("PROTECTED_UAT_EXACT_MAIN_CI=PASS source=" + sha)
            return
        print("PROTECTED_UAT_EXACT_MAIN_CI=WAIT checks=" + ", ".join(pending), flush=True)
        time.sleep(20)
    raise RuntimeError("Exact-main CI did not pass within 24 minutes")


if __name__ == "__main__":
    main()
