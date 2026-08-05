#!/usr/bin/env python3
"""Fail-closed repository security-posture regression gate."""

from __future__ import annotations

import json
import hashlib
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_ROOT = ROOT / ".github" / "workflows"
ERRORS: list[str] = []

ALLOWED_CONTENT_WRITE_WORKFLOWS = {
    "publish-pulse-ai-architecture-v1-1.yml",
}
CRITICAL_PINNED_WORKFLOWS = {
    "deployment-concurrency-governance-ci.yml",
    "group5-financial-operations-recovery-ci.yml",
    "group7-ai-help-system-guide-ci.yml",
    "mirror-to-us-signal-projectpulse.yml",
    "module064-automatic-provider-health-ci.yml",
    "projectpulse-ci.yml",
    "projectpulse-rollback.yml",
    "pulse-ai-help-chat-usability-ci.yml",
    "security-posture-ci.yml",
}
PULL_REQUEST_TARGET_WORKFLOW_SHA256 = {
    # This is the sole pull_request_target workflow. It runs read-only default-branch
    # governance code and never executes PR code, consumes secrets, or receives a
    # write-capable token. Any byte change requires an explicit posture review.
    "deployment-concurrency-governance-ci.yml": (
        "f3dabd16019e54b21c7def599ef5e74bcc98cc8417f5500caf7a8dbcda531758"
    ),
}
DEPLOYMENT_CONCURRENCY_VALIDATOR_SHA256 = "4b1920a20e73b10394a9fff66ca092362e57693d2f794d9396a3ca6322cecff8"
STALE_BRANCH_WRITERS = {
    "temp-open-pr-integration.yml",
    "temp-security285-final-integration.yml",
    "temp-open-pr-integration-20260730.yml",
}
SECRET_FILE_PATTERN = re.compile(
    r"(^|/)(id_rsa|id_ed25519)(\.pub)?$|\.(p12|pfx|kdbx|jks|key|tfstate)$",
    re.IGNORECASE,
)
FULL_SHA_ACTION = re.compile(r"^\s*uses:\s*[^#\s]+@([0-9a-f]{40})(?:\s+#.*)?$", re.IGNORECASE)


def text(relative: str) -> str:
    path = ROOT / relative
    if not path.is_file():
        ERRORS.append(f"required file missing: {relative}")
        return ""
    return path.read_text(encoding="utf-8")


def require(haystack: str, token: str, label: str) -> None:
    if token not in haystack:
        ERRORS.append(f"{label}: missing {token!r}")


def forbid(haystack: str, token: str, label: str) -> None:
    if token in haystack:
        ERRORS.append(f"{label}: forbidden {token!r}")


for required in (
    ".github/CODEOWNERS",
    ".github/dependabot.yml",
    ".github/pull_request_template.md",
    ".github/workflows/security-posture-ci.yml",
    "SECURITY.md",
    "docs/security/PLATFORM-SECURITY-POSTURE-BASELINE-20260730.md",
):
    text(required)

if WORKFLOW_ROOT.is_dir():
    workflows = sorted([*WORKFLOW_ROOT.glob("*.yml"), *WORKFLOW_ROOT.glob("*.yaml")])
else:
    workflows = []
    ERRORS.append("workflow directory is missing")

governance_workflow = WORKFLOW_ROOT / "deployment-concurrency-governance-ci.yml"
if not governance_workflow.is_file() or governance_workflow.is_symlink():
    ERRORS.append("digest-pinned deployment concurrency governance workflow is missing or non-regular")
else:
    actual_workflow_digest = hashlib.sha256(governance_workflow.read_bytes()).hexdigest()
    expected_workflow_digest = PULL_REQUEST_TARGET_WORKFLOW_SHA256[
        "deployment-concurrency-governance-ci.yml"
    ]
    if actual_workflow_digest != expected_workflow_digest:
        ERRORS.append("approved pull_request_target workflow digest changed")

governance_validator = ROOT / "scripts" / "validate-deployment-concurrency-governance.mjs"
if not governance_validator.is_file():
    ERRORS.append("deployment concurrency governance validator is missing")
else:
    actual_validator_digest = hashlib.sha256(governance_validator.read_bytes()).hexdigest()
    if actual_validator_digest != DEPLOYMENT_CONCURRENCY_VALIDATOR_SHA256:
        ERRORS.append("deployment concurrency governance validator digest changed")
    else:
        try:
            subprocess.check_output(
                [
                    "node",
                    str(governance_validator),
                    "--repo-root",
                    str(ROOT),
                    "--verify-pull-request-target-policy",
                ],
                cwd=ROOT,
                text=True,
                stderr=subprocess.STDOUT,
            )
        except (OSError, subprocess.CalledProcessError) as exc:
            ERRORS.append(f"pull_request_target structural policy failed: {exc}")

for workflow in workflows:
    body = workflow.read_text(encoding="utf-8")
    name = workflow.name

    if name in STALE_BRANCH_WRITERS or name.startswith("temp-"):
        ERRORS.append(f"temporary workflow remains on protected source: {name}")

    expected_digest = PULL_REQUEST_TARGET_WORKFLOW_SHA256.get(name)
    if expected_digest is not None:
        actual_digest = hashlib.sha256(body.encode("utf-8")).hexdigest()
        if actual_digest != expected_digest:
            ERRORS.append(
                f"{name}: approved pull_request_target workflow changed; "
                "security review and digest update are required"
            )

    if re.search(r"(?m)^\s*pull_request_target\s*:", body) and expected_digest is None:
        ERRORS.append(f"{name}: pull_request_target is prohibited")

    if re.search(r"(?m)^\s*permissions\s*:\s*write-all\s*$", body):
        ERRORS.append(f"{name}: write-all permissions are prohibited")

    if re.search(r"(?m)^\s*contents\s*:\s*write\s*$", body):
        if name not in ALLOWED_CONTENT_WRITE_WORKFLOWS:
            ERRORS.append(f"{name}: unapproved contents: write permission")

    if name in CRITICAL_PINNED_WORKFLOWS:
        for line_number, line in enumerate(body.splitlines(), start=1):
            if re.match(r"^\s*uses\s*:", line) and not FULL_SHA_ACTION.match(line):
                ERRORS.append(
                    f"{name}:{line_number}: action reference is not pinned to a full commit SHA"
                )

ci = text(".github/workflows/projectpulse-ci.yml")
require(ci, "push:", "ProjectPulse CI")
require(ci, "      - main", "ProjectPulse CI")
require(ci, "dotnet list", "ProjectPulse CI")
require(ci, "--vulnerable", "ProjectPulse CI")
require(ci, "npm audit --audit-level=high", "ProjectPulse CI")
require(
    ci,
    "python3 scripts/security/validate-repository-security-posture.py",
    "ProjectPulse CI",
)
require(ci, "git diff --exit-code", "ProjectPulse CI")

mirror = text(".github/workflows/mirror-to-us-signal-projectpulse.yml")
require(mirror, "      - main", "mirror workflow")
require(mirror, ":refs/heads/main", "mirror workflow")
forbid(mirror, '      - "**"', "mirror workflow")
forbid(mirror, "+refs/heads/*:refs/heads/*", "mirror workflow")
forbid(mirror, "push --prune", "mirror workflow")

rollback = text(".github/workflows/projectpulse-rollback.yml")
require(rollback, "ROLLBACK_IMAGE_GUARD=PASS", "rollback workflow")
require(rollback, "@sha256:", "rollback workflow")
require(rollback, "project-health-dashboard-api", "rollback workflow")
require(rollback, "project-health-dashboard-web", "rollback workflow")

applicability_contracts = {
    ".github/workflows/group5-financial-operations-recovery-ci.yml": (
        "GROUP_5_VALIDATION_MODE=CENTRAL_SECURITY_CONVERGENCE",
        "GROUP_5_VALIDATION_MODE=REGRESSION",
        "CENTRAL_GROUP5_CONVERGENCE",
        "if-no-files-found: warn",
        "git -C ../../.. diff --exit-code",
    ),
    ".github/workflows/pulse-ai-help-chat-usability-ci.yml": (
        "PULSE_AI_HELP_CHAT_VALIDATION_MODE=CENTRAL_SECURITY_CONVERGENCE",
        "PULSE_AI_HELP_CHAT_VALIDATION_MODE=REGRESSION",
        "CENTRAL_HELP_CONVERGENCE",
        "git -C ../../.. diff --exit-code",
    ),
    ".github/workflows/group7-ai-help-system-guide-ci.yml": (
        "GROUP_7_VALIDATION_MODE=REGRESSION",
        "git -C ../../.. diff --exit-code",
    ),
    ".github/workflows/module064-automatic-provider-health-ci.yml": (
        "MODULE_064_VALIDATION_MODE=CENTRAL_SECURITY_CONVERGENCE",
        "MODULE_064_VALIDATION_MODE=REGRESSION",
        "CENTRAL_MODULE064_CONVERGENCE",
        "if-no-files-found: warn",
        "git -C ../../.. diff --exit-code",
    ),
}
for workflow_path, tokens in applicability_contracts.items():
    workflow_text = text(workflow_path)
    for token in tokens:
        require(workflow_text, token, f"specialized workflow applicability: {workflow_path}")

nginx = text("deployment/containers/web/default.conf.template")
for header in (
    "X-Content-Type-Options",
    "Referrer-Policy",
    "Content-Security-Policy",
    "Permissions-Policy",
    "Strict-Transport-Security",
):
    require(nginx, header, "web security headers")

project = text("src/backend/ProjectTime.Api/ProjectTime.Api.csproj")
compile_removes = project.count("<Compile Remove=")
source_transforms = project.count("<Exec Command=")
if compile_removes > 16:
    ERRORS.append(
        f"backend generated-source exclusions grew from the accepted ceiling: {compile_removes} > 16"
    )
if source_transforms > 17:
    ERRORS.append(
        f"backend build-time source transforms grew from the accepted ceiling: {source_transforms} > 17"
    )

package_path = ROOT / "src/frontend/project-time-web/package.json"
if package_path.is_file():
    package = json.loads(package_path.read_text(encoding="utf-8"))
    prebuild = str(package.get("scripts", {}).get("prebuild", ""))
    injector_count = len(re.findall(r"(?:^|&&)\s*node\s+\./scripts/inject-", prebuild))
    if injector_count > 10:
        ERRORS.append(
            f"frontend build-time source injectors grew from the accepted ceiling: {injector_count} > 10"
        )
else:
    ERRORS.append("frontend package.json is missing")

gitignore = text(".gitignore")
for ignored in (
    "*.key",
    "*.p12",
    "*.pfx",
    "*.tfstate",
    "*.tfstate.*",
    ".npmrc",
    ".pypirc",
    "secrets/",
    "credentials/",
):
    require(gitignore, ignored, ".gitignore")

try:
    tracked = subprocess.check_output(
        ["git", "ls-files"],
        cwd=ROOT,
        text=True,
        stderr=subprocess.STDOUT,
    ).splitlines()
except (OSError, subprocess.CalledProcessError) as exc:
    ERRORS.append(f"unable to enumerate tracked files: {exc}")
    tracked = []

for tracked_path in tracked:
    if SECRET_FILE_PATTERN.search(tracked_path):
        ERRORS.append(f"credential-bearing file type is tracked: {tracked_path}")

if ERRORS:
    print("REPOSITORY_SECURITY_POSTURE=FAILED")
    for error in ERRORS:
        print(f"ERROR: {error}")
    sys.exit(1)

print("REPOSITORY_SECURITY_POSTURE=PASSED")
print(f"REPOSITORY_SECURITY_WORKFLOWS_REVIEWED={len(workflows)}")
print(f"BACKEND_COMPILE_REMOVE_BASELINE={compile_removes}")
print(f"BACKEND_SOURCE_TRANSFORM_BASELINE={source_transforms}")
print("TEMPORARY_BRANCH_WRITERS_PRESENT=NO")
print("CRITICAL_ACTION_REFERENCES_PINNED=YES")
print("SPECIALIZED_REGRESSION_APPLICABILITY=ENFORCED")
