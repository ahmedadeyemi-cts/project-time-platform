#!/usr/bin/env python3
"""Resolve a server-confirmed Protected-Test deployment into verifier input.

The verifier never accepts an application SHA, image, or revision from a
workflow input. It accepts only the selected successful deployment run's
sanitized artifact and the current trusted-main candidate manifest.
"""
from __future__ import annotations

import io
import json
import os
import re
import sys
import zipfile
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


class ResolutionError(Exception):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise ResolutionError(code)


def api_json(path: str, token: str) -> object:
    request = Request(
        "https://api.github.com" + path,
        headers={
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "Authorization": "Bearer " + token,
        },
    )
    try:
        with urlopen(request, timeout=45) as response:
            return json.loads(response.read(4_000_001))
    except (HTTPError, URLError, TimeoutError, OSError, ValueError):
        raise ResolutionError("github_api_read_failed") from None


def api_bytes(path: str, token: str) -> bytes:
    request = Request(
        "https://api.github.com" + path,
        headers={
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "Authorization": "Bearer " + token,
        },
    )
    try:
        with urlopen(request, timeout=90) as response:
            data = response.read(32_000_001)
            require(len(data) <= 32_000_000, "deployment_artifact_too_large")
            return data
    except (HTTPError, URLError, TimeoutError, OSError):
        raise ResolutionError("deployment_artifact_read_failed") from None


def main() -> int:
    output = Path(os.environ.get("OUTPUT_CONTEXT", ""))
    token = os.environ.get("GH_TOKEN", "")
    repository = os.environ.get("GITHUB_REPOSITORY", "")
    current_sha = os.environ.get("GITHUB_SHA", "")
    run_id = os.environ.get("DEPLOYMENT_RUN_ID", "")
    try:
        require(output.is_absolute(), "identity_context_output_missing")
        require(bool(token) and re.fullmatch(r"[0-9]+", run_id) is not None, "deployment_run_id_invalid")
        require(bool(re.fullmatch(r"[0-9a-f]{40}", current_sha)), "trusted_main_sha_invalid")
        require(bool(re.fullmatch(r"[^/]+/[^/]+", repository)), "repository_invalid")

        run = api_json(f"/repos/{repository}/actions/runs/{run_id}", token)
        require(isinstance(run, dict), "deployment_run_response_invalid")
        require(run.get("workflow_id") == 315562561, "selected_run_wrong_workflow")
        require(run.get("event") == "workflow_dispatch", "selected_run_not_manual_dispatch")
        require(run.get("head_branch") == "main" and run.get("head_sha") == current_sha,
                "selected_run_wrong_controller")
        require(run.get("status") == "completed" and run.get("conclusion") == "success",
                "selected_run_not_successful")

        artifacts = api_json(f"/repos/{repository}/actions/runs/{run_id}/artifacts?per_page=100", token)
        require(isinstance(artifacts, dict) and isinstance(artifacts.get("artifacts"), list),
                "deployment_artifacts_response_invalid")
        expected_prefix = f"systemwide-enterprise-reliability-test-evidence-{run_id}-"
        matches = [item for item in artifacts["artifacts"]
                   if isinstance(item, dict) and str(item.get("name", "")).startswith(expected_prefix)
                   and item.get("expired") is False]
        require(len(matches) == 1, "selected_deployment_identity_artifact_missing_or_ambiguous")
        archive = api_bytes(f"/repos/{repository}/actions/artifacts/{matches[0]['id']}/zip", token)
        with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
            names = [name for name in bundle.namelist() if name.endswith("deployment-identity.json")]
            require(len(names) == 1, "deployment_identity_file_missing_or_ambiguous")
            try:
                identity = json.loads(bundle.read(names[0]))
            except (KeyError, ValueError):
                raise ResolutionError("deployment_identity_json_invalid") from None

        require(isinstance(identity, dict), "deployment_identity_invalid")
        require(identity.get("environment") == "test" and identity.get("productionMutation") is False,
                "deployment_identity_environment_invalid")
        require(str(identity.get("deploymentRunId")) == run_id
                and str(identity.get("controllerSha")) == current_sha,
                "deployment_identity_run_binding_invalid")
        application_sha = str(identity.get("applicationSha") or "")
        require(re.fullmatch(r"[0-9a-f]{40}", application_sha) is not None,
                "deployment_identity_application_sha_invalid")
        for key in ("apiRevision", "webRevision"):
            require(isinstance(identity.get(key), str) and identity[key].strip(),
                    f"deployment_identity_{key}_missing")
        for key in ("apiImage", "webImage"):
            require(re.fullmatch(r".+@sha256:[0-9a-f]{64}", str(identity.get(key) or "")) is not None,
                    f"deployment_identity_{key}_not_immutable")

        manifest_path = Path(".github/flowhive-psa-protected-test-candidate.json")
        manifest = json.loads(manifest_path.read_text())
        require(manifest.get("environment") == "test" and manifest.get("sha") == application_sha,
                "selected_deployment_not_current_approved_candidate")
        require(manifest.get("allowCustomerPublication") is False
                and manifest.get("allowCanonicalTaskAdoption") is False,
                "selected_candidate_scope_invalid")
        context = {
            "status": "identity_inputs_sealed",
            "environment": "test",
            "applicationSha": application_sha,
            "applicationBranch": manifest.get("branch"),
            "applicationPullRequest": manifest.get("pullRequest"),
            "deploymentRunId": run_id,
            "deploymentAttempt": identity.get("deploymentAttempt"),
            "controllerSha": current_sha,
            "apiRevision": identity["apiRevision"],
            "webRevision": identity["webRevision"],
            "apiImage": identity["apiImage"],
            "webImage": identity["webImage"],
            "productionMutation": False,
            "source": "server_confirmed_successful_dispatch_artifact_and_current_main_candidate_manifest",
        }
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(context, indent=2) + "\n")
        print("FLOWHIVE_INSTALLED_DEPLOYMENT_CONTEXT=SEALED")
        return 0
    except (ResolutionError, OSError, ValueError) as error:
        print(f"FLOWHIVE_INSTALLED_DEPLOYMENT_CONTEXT=REJECTED:{error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
