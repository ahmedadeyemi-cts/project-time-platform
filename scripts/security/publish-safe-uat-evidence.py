#!/usr/bin/env python3
"""Project private UAT responses into a fixed, content-free artifact schema."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re

REPORTS = (
    "immutable-images.json", "release-boundary.json", "module001b-revision-reconcile.json",
    "flowhive-my-role-browser.json", "flowhive-installed-identity.json", "module025-installed-prerequisite.json",
    "uat-summary.json", "deployment-health-verified.json", "deployment-identity.json",
    "migrations.json", "flowhive-psa-migrations.json", "flowhive-psa-live-uat.json",
    "module025-sow-gsd-protected-test-uat.json", "module025-generation-contract.json",
    "module025-installed-sa-uat.json", "module025-normal-sa-browser.json",
    "module025-protected-test-uat-fixture-disabled.json", "utilization-role-scoping-uat.json",
    "assigned-work-protected-test-uat.json", "rollback.json",
)
STATUSES = {"passed", "failed", "disabled", "applied_and_verified", "identity_inputs_sealed"}
FLAGS = {"productionMutation", "fixtureMutation", "fixtureActive", "mockedResponses",
         "normalAuthorizedSolutionArchitect", "functionalLifecycleVerified", "passed",
         "rollbackCompleted", "repeatedDownloadsPreservedBytes", "persistentRoleAssignmentMutation"}


def summarize(source):
    reports = {}
    for name in REPORTS:
        path = source / name
        if not path.exists() and not path.is_symlink():
            continue
        if path.is_symlink() or not path.is_file() or path.stat().st_size > 4 * 1024 * 1024:
            raise ValueError("Unsafe UAT report file")
        data = path.read_bytes()
        obj = json.loads(data)
        if not isinstance(obj, dict):
            raise ValueError("Invalid UAT report")
        status = obj.get("status")
        report = {"status": status if isinstance(status, str) and status in STATUSES else "unclassified",
                  "sha256": hashlib.sha256(data).hexdigest()}
        report.update({key: obj[key] for key in FLAGS if type(obj.get(key)) is bool})
        reports[name] = report
    return {"schema": 1, "reports": reports,
            "rawResponsesPublished": False, "screenshotsPublished": False}


# Only machine-generated installation receipts are copied, field by field. The
# installed-release resolver consumes these exact names and retains all its
# provenance, digest, migration, rollback and live-identity checks.
SHA = r"[0-9a-f]{40}"
IMAGE = r"[a-z0-9.-]+(?::[0-9]+)?(?:/[a-z0-9._-]+)+@sha256:[0-9a-f]{64}"
REVISION = r"[a-z0-9][a-z0-9-]{0,127}"
BRANCH = r"(?:main|release/[a-zA-Z0-9._/-]{1,180})"
MIGRATION = r"[0-9]{3}_[a-zA-Z0-9_]{1,160}"
RECEIPTS = {
    "deployment-identity.json": {
        "status": {"identity_inputs_sealed"}, "environment": {"test"},
        "applicationSha": SHA, "applicationBranch": BRANCH, "controllerSha": SHA,
        "deploymentRunId": r"[0-9]{1,20}", "deploymentAttempt": r"[0-9]{1,6}",
        "apiRevision": REVISION, "webRevision": REVISION,
        "apiImage": IMAGE, "webImage": IMAGE, "productionMutation": bool},
    "immutable-images.json": {"apiImage": IMAGE, "webImage": IMAGE, "migrationImage": IMAGE},
    "release-boundary.json": {"applicationRelease": SHA, "environment": {"test"}, "productionMutation": bool},
    "deployment-health-verified.json": {"sourceCommit": SHA, "deploymentHealthVerified": bool, "productionMutation": bool},
    "migrations.json": {"status": {"applied_and_verified"}, "productionMutation": bool, "migrations": [MIGRATION], "image": IMAGE},
    "flowhive-psa-migrations.json": {"status": {"applied_and_verified"}, "environment": {"test"},
        "productionMutation": bool, "releaseCommit": SHA, "controlCommit": SHA, "migrations": [MIGRATION]},
    "module001b-revision-reconcile.json": {"expectedRevision": REVISION, "latestReadyRevision": REVISION,
        "phase": {"converged"}, "expectedRevisionActive": bool, "trafficWeight": {100, "100"},
        "activeRevisions": [REVISION], "healthState": {"Healthy"}, "productionMutation": bool,
        "expectedImage": IMAGE, "observedImage": IMAGE},
}


def valid(value, rule):
    if rule is bool:
        return type(value) is bool
    if isinstance(rule, str):
        return isinstance(value, str) and re.fullmatch(rule, value) is not None
    if isinstance(rule, set):
        return type(value) in (str, int) and value in rule
    return isinstance(value, list) and len(value) <= 200 and all(valid(item, rule[0]) for item in value)


def project_receipt(name, obj):
    result = {}
    for key, rule in RECEIPTS[name].items():
        if key in obj:
            if not valid(obj[key], rule):
                raise ValueError("Invalid deployment receipt field")
            result[key] = obj[key]
    return result


def publish(source, destination):
    if destination.exists() or destination.is_symlink():
        raise ValueError("Evidence destination must be new")
    summary = summarize(source)
    outputs = {"security-safe-uat-summary.json": summary}
    for name in RECEIPTS:
        if name in summary["reports"]:
            outputs[name] = project_receipt(name, json.loads((source / name).read_bytes()))
    destination.mkdir(mode=0o700, parents=False)
    for name, data in outputs.items():
        fd = os.open(destination / name, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
        with os.fdopen(fd, "w") as stream:
            json.dump(data, stream, indent=2, sort_keys=True)
            stream.write("\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    publish(args.source, args.destination)


if __name__ == "__main__":
    main()
