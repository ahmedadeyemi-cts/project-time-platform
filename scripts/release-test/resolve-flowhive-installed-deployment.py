#!/usr/bin/env python3
"""Resolve an installed Protected-Test release without replaying deployment.

A failed acceptance test does not undo a completed installation. Require the
installation steps, immutable images, migrations, and no-rollback evidence;
preserve the original workflow conclusion. The following authenticated identity
gate must still match the live API before any business writes are permitted.
"""
from __future__ import annotations

import hashlib
import io
import json
import os
import re
import sys
import zipfile
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse
from urllib.request import HTTPRedirectHandler, Request, build_opener

WORKFLOW_ID = 315562561
WORKFLOW_PATH = ".github/workflows/projectpulse-deploy-test.yml"
DEPLOY_JOB = "Validate, migrate, deploy, and verify protected Test"
REQUIRED_STEPS = (
    "Verify admitted controller identity before deployment mutations",
    "Admit the exact reviewed PSA candidate using trusted main controls",
    "Guard exact source and validate release",
    "Snapshot protected Test and preserve rollback contract",
    "Build immutable API, web, and migration images",
    "Apply and verify Migrations 086, 088, and 093 through 100 inside Test private network",
    "Deploy immutable Test API image",
    "Deploy immutable Test web image",
    "Seal server-confirmed deployment identity",
)
ACCEPTANCE_STEPS = {
    "Verify PSA candidate health and the live SOW-to-WBS lifecycle",
    "Run protected-Test authenticated functional UAT",
    "Run protected-Test Module 025 SOW/GSD generation lifecycle UAT",
}
ROLLBACK_STEPS = (
    "Restore exact prior Test images after application failure",
    "Rollback protected Test API configuration on failure",
)
MAX_RESPONSE = 4_000_000
MAX_ARCHIVE = 32_000_000


class ResolutionError(Exception):
    """Fixed diagnostics only; never include API bodies or authentication."""


def require(condition: bool, code: str) -> None:
    if not condition:
        raise ResolutionError(code)


def sha(value: object) -> bool:
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{40}", value) is not None


class SafeRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        require(urlparse(newurl).scheme == "https", "insecure_github_redirect")
        redirected = super().redirect_request(req, fp, code, msg, headers, newurl)
        if redirected is not None and urlparse(req.full_url).netloc != urlparse(newurl).netloc:
            redirected.remove_header("Authorization")
        return redirected


def api_read(path: str, token: str, limit: int, timeout: int) -> bytes:
    require(path.startswith("/repos/") and not path.startswith("//"), "invalid_github_path")
    request = Request("https://api.github.com" + path, headers={
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "Authorization": "Bearer " + token,
    })
    try:
        with build_opener(SafeRedirect()).open(request, timeout=timeout) as response:
            data = response.read(limit + 1)
            require(len(data) <= limit, "github_response_too_large")
            return data
    except (HTTPError, URLError, TimeoutError, OSError):
        raise ResolutionError("github_api_read_failed") from None


def api_json(path: str, token: str) -> object:
    try:
        return json.loads(api_read(path, token, MAX_RESPONSE, 45))
    except ValueError:
        raise ResolutionError("github_json_invalid") from None


def api_bytes(path: str, token: str) -> bytes:
    return api_read(path, token, MAX_ARCHIVE, 90)


def inventory(path: str, key: str, token: str) -> list[dict]:
    """Read every page; never mistake a truncated inventory for complete proof."""
    rows: list[dict] = []
    total = None
    for page in range(1, 11):
        body = api_json(f"{path}?per_page=100&page={page}", token)
        require(isinstance(body, dict) and isinstance(body.get(key), list), "github_inventory_invalid")
        count = body.get("total_count")
        require(type(count) is int and 0 <= count <= 1000, "github_inventory_count_invalid")
        require(total is None or total == count, "github_inventory_changed")
        total = count
        batch = body[key]
        require(all(isinstance(row, dict) for row in batch), "github_inventory_row_invalid")
        rows.extend(batch)
        require(len(rows) <= total, "github_inventory_overflow")
        if len(rows) == total:
            return rows
        require(len(batch) == 100, "github_inventory_incomplete")
    raise ResolutionError("github_inventory_limit")


def installation_steps(run: dict, jobs: list[dict]) -> list[str]:
    require(len(jobs) == 1 and jobs[0].get("name") == DEPLOY_JOB, "deployment_job_missing_or_ambiguous")
    job = jobs[0]
    require(job.get("run_id") == run.get("id") and job.get("status") == "completed", "deployment_job_not_complete")
    require(job.get("conclusion") == run.get("conclusion"), "deployment_job_conclusion_mismatch")
    rows = job.get("steps")
    require(isinstance(rows, list) and rows, "deployment_steps_missing")
    require(all(isinstance(row, dict) and row.get("status") == "completed" for row in rows), "deployment_steps_not_complete")
    names = [row.get("name") for row in rows]
    require(all(isinstance(name, str) and name for name in names) and len(set(names)) == len(names), "deployment_steps_ambiguous")
    steps = {row["name"]: row for row in rows}
    for name in REQUIRED_STEPS:
        require(steps.get(name, {}).get("conclusion") == "success", "installation_step_not_successful")
    positions = [names.index(name) for name in REQUIRED_STEPS]
    require(positions == sorted(positions), "installation_step_order_invalid")
    for name in ROLLBACK_STEPS:
        require(steps.get(name, {}).get("conclusion") == "skipped", "rollback_not_excluded")
    failed = [row["name"] for row in rows if row.get("conclusion") == "failure"]
    require(all(row.get("conclusion") in {"success", "failure", "skipped"} for row in rows), "deployment_step_outcome_unresolved")
    require(set(failed) <= ACCEPTANCE_STEPS, "non_acceptance_failure_unresolved")
    require((run["conclusion"] == "success" and not failed) or (run["conclusion"] == "failure" and bool(failed)), "deployment_failure_not_explained")
    require(all(names.index(name) > positions[-1] for name in failed), "failure_precedes_installation")
    return failed


def read_receipt(bundle: zipfile.ZipFile, filename: str) -> dict:
    matches = [info for info in bundle.infolist() if info.filename.rsplit("/", 1)[-1] == filename]
    require(len(matches) == 1, "deployment_receipt_missing_or_ambiguous")
    info = matches[0]
    require(not info.is_dir() and not (info.flag_bits & 1) and info.file_size <= MAX_RESPONSE, "deployment_receipt_unsafe")
    try:
        value = json.loads(bundle.read(info))
    except (ValueError, RuntimeError, zipfile.BadZipFile):
        raise ResolutionError("deployment_receipt_invalid") from None
    require(isinstance(value, dict), "deployment_receipt_invalid")
    return value


def resolve(run: dict, jobs: list[dict], artifact: dict, archive: bytes, manifest: dict,
            repository: str, verifier_sha: str) -> dict:
    """Pure validation for the same path used by the maintained entrypoint."""
    require(run.get("workflow_id") == WORKFLOW_ID and str(run.get("path", "")).split("@", 1)[0] == WORKFLOW_PATH, "selected_run_wrong_workflow")
    require(run.get("event") == "workflow_dispatch" and run.get("head_branch") == "main", "selected_run_not_canonical")
    require((run.get("repository") or {}).get("full_name") == repository, "selected_run_wrong_repository")
    controller = run.get("head_sha")
    require(sha(controller) and sha(verifier_sha), "controller_identity_invalid")
    require(run.get("status") == "completed" and run.get("conclusion") in {"success", "failure"}, "selected_run_not_terminal")
    attempt = run.get("run_attempt")
    require(type(run.get("id")) is int and run["id"] > 0 and type(attempt) is int and attempt > 0, "deployment_attempt_invalid")
    failed = installation_steps(run, jobs)
    run_id = str(run["id"])
    expected_name = f"systemwide-enterprise-reliability-test-evidence-{run_id}-{attempt}"
    provenance = artifact.get("workflow_run") or {}
    require(artifact.get("name") == expected_name and artifact.get("expired") is False, "deployment_artifact_attempt_mismatch")
    require(provenance.get("id") == run["id"] and provenance.get("head_sha") == controller and provenance.get("head_branch") == "main", "deployment_artifact_provenance_mismatch")
    require(artifact.get("digest") == "sha256:" + hashlib.sha256(archive).hexdigest(), "deployment_artifact_digest_mismatch")
    require(len(archive) <= MAX_ARCHIVE, "deployment_artifact_too_large")
    with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
        require(sum(info.file_size for info in bundle.infolist()) <= MAX_ARCHIVE, "deployment_artifact_expanded_too_large")
        identity = read_receipt(bundle, "deployment-identity.json")
        images = read_receipt(bundle, "immutable-images.json")
        migrations = read_receipt(bundle, "flowhive-psa-migrations.json")
        health = read_receipt(bundle, "deployment-health-verified.json")
        application = identity.get("applicationSha")
        require(identity.get("environment") == "test" and identity.get("productionMutation") is False, "deployment_identity_environment_invalid")
        require(str(identity.get("deploymentRunId")) == run_id and str(identity.get("deploymentAttempt")) == str(attempt) and identity.get("controllerSha") == controller, "deployment_identity_run_binding_invalid")
        require(sha(application), "deployment_identity_application_sha_invalid")
        for key in ("apiRevision", "webRevision"):
            require(isinstance(identity.get(key), str) and identity[key].strip(), "deployment_revision_missing")
        for key in ("apiImage", "webImage"):
            require(re.fullmatch(r".+@sha256:[0-9a-f]{64}", str(identity.get(key) or "")) is not None and identity[key] == images.get(key), "deployment_image_receipt_mismatch")
        require(manifest.get("repository") == repository and manifest.get("environment") == "test" and manifest.get("sha") == application and manifest.get("branch") == identity.get("applicationBranch"), "selected_deployment_not_current_approved_candidate")
        require(manifest.get("allowCustomerPublication") is False and manifest.get("allowCanonicalTaskAdoption") is False, "selected_candidate_scope_invalid")
        migration_spec = manifest.get("migrations")
        require(isinstance(migration_spec, list) and migration_spec and all(isinstance(row, dict) and isinstance(row.get("file"), str) and row["file"].endswith(".sql") for row in migration_spec), "migration_manifest_invalid")
        expected_migrations = [row["file"][:-4] for row in migration_spec]
        require(len(set(expected_migrations)) == len(expected_migrations), "migration_manifest_ambiguous")
        require(migrations.get("status") == "applied_and_verified" and migrations.get("environment") == "test" and migrations.get("productionMutation") is False and migrations.get("releaseCommit") == application and migrations.get("controlCommit") == controller and migrations.get("migrations") == expected_migrations, "deployment_migrations_not_verified")
        require(health.get("deploymentHealthVerified") is True and health.get("sourceCommit") == application and health.get("productionMutation") is False, "deployment_health_not_verified")
        # Assigned-work acceptance temporarily changes the revision, not the
        # image. Retain the successful final same-image reconciliation receipt.
        assigned = next((row for row in jobs[0]["steps"] if row["name"] == "Run protected-Test assigned-work visibility UAT"), None)
        final_api = identity["apiRevision"]
        if assigned and assigned.get("conclusion") == "success":
            cleanup = read_receipt(bundle, "module001b-revision-reconcile.json")
            final_api = cleanup.get("expectedRevision")
            require(isinstance(final_api, str) and final_api.endswith(f"--m1bd-{run_id}-{attempt}"), "fixture_disabled_revision_missing")
            require(cleanup.get("phase") == "converged" and cleanup.get("latestReadyRevision") == final_api and cleanup.get("expectedRevisionActive") is True and str(cleanup.get("trafficWeight")) == "100" and cleanup.get("activeRevisions") == [final_api] and cleanup.get("healthState") == "Healthy" and cleanup.get("productionMutation") is False and cleanup.get("expectedImage") == identity["apiImage"] and cleanup.get("observedImage") == identity["apiImage"], "fixture_cleanup_not_reconciled")
    return {
        "status": "identity_inputs_sealed", "environment": "test",
        "applicationSha": application, "applicationBranch": manifest["branch"],
        "applicationPullRequest": manifest.get("pullRequest"),
        "deploymentRunId": run_id, "deploymentAttempt": str(attempt),
        "controllerSha": controller, "verificationCodeSha": verifier_sha,
        "apiRevision": final_api, "initialApiRevision": identity["apiRevision"],
        "webRevision": identity["webRevision"], "apiImage": identity["apiImage"],
        "webImage": identity["webImage"], "productionMutation": False,
        "deploymentConclusion": run["conclusion"], "failedAcceptanceSteps": failed,
        "installationVerified": True, "liveIdentityRequired": True,
        "businessWritesPermitted": False, "functionalAcceptanceVerified": False,
        "artifactId": artifact["id"], "artifactDigest": artifact["digest"],
        "source": "server_confirmed_installation_steps_and_receipts",
    }


def main() -> int:
    output = Path(os.environ.get("OUTPUT_CONTEXT", ""))
    token = os.environ.get("GH_TOKEN", "")
    repository = os.environ.get("GITHUB_REPOSITORY", "")
    verifier_sha = os.environ.get("GITHUB_SHA", "")
    run_id = os.environ.get("DEPLOYMENT_RUN_ID", "")
    try:
        require(output.is_absolute(), "identity_context_output_missing")
        # Never leave a prior successful context available after a rejected run.
        output.unlink(missing_ok=True)
        require(bool(token) and re.fullmatch(r"[0-9]+", run_id) is not None, "deployment_run_id_invalid")
        require(sha(verifier_sha) and repository == "ahmedadeyemi-cts/project-time-platform", "trusted_verifier_identity_invalid")
        root = f"/repos/{repository}"
        run = api_json(f"{root}/actions/runs/{run_id}", token)
        require(isinstance(run, dict) and str(run.get("id")) == run_id and sha(run.get("head_sha")), "deployment_run_response_invalid")
        controller = run["head_sha"]
        if controller != verifier_sha:
            compare = api_json(f"{root}/compare/{controller}...{verifier_sha}", token)
            require(isinstance(compare, dict) and compare.get("status") == "ahead" and (compare.get("merge_base_commit") or {}).get("sha") == controller, "verifier_not_descended_from_controller")
        attempt = run.get("run_attempt")
        require(type(attempt) is int and attempt > 0, "deployment_attempt_invalid")
        jobs = inventory(f"{root}/actions/runs/{run_id}/attempts/{attempt}/jobs", "jobs", token)
        artifacts = inventory(f"{root}/actions/runs/{run_id}/artifacts", "artifacts", token)
        name = f"systemwide-enterprise-reliability-test-evidence-{run_id}-{attempt}"
        matches = [item for item in artifacts if item.get("name") == name and item.get("expired") is False]
        require(len(matches) == 1, "selected_deployment_identity_artifact_missing_or_ambiguous")
        artifact = matches[0]
        require(type(artifact.get("id")) is int and artifact["id"] > 0, "deployment_artifact_id_invalid")
        archive = api_bytes(f"{root}/actions/artifacts/{artifact['id']}/zip", token)
        manifest = json.loads(Path(".github/flowhive-psa-protected-test-candidate.json").read_text())
        require(isinstance(manifest, dict), "candidate_manifest_invalid")
        context = resolve(run, jobs, artifact, archive, manifest, repository, verifier_sha)
        latest = api_json(f"{root}/actions/runs/{run_id}", token)
        require(isinstance(latest, dict) and all(latest.get(key) == run.get(key) for key in ("id", "head_sha", "status", "conclusion", "run_attempt", "updated_at")), "deployment_changed_during_resolution")
        output.parent.mkdir(parents=True, exist_ok=True)
        temporary = output.with_name(output.name + ".tmp")
        temporary.write_text(json.dumps(context, indent=2) + "\n")
        temporary.replace(output)
        print("FLOWHIVE_INSTALLED_DEPLOYMENT_CONTEXT=SEALED")
        return 0
    except (ResolutionError, OSError, ValueError, zipfile.BadZipFile) as error:
        code = str(error) if isinstance(error, ResolutionError) else "deployment_evidence_invalid"
        print("FLOWHIVE_INSTALLED_DEPLOYMENT_CONTEXT=REJECTED:" + code, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
