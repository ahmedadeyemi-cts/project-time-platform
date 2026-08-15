#!/usr/bin/env python3
from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = ROOT / ".github/workflows/projectpulse-deploy-test.yml"
VALIDATOR_PATH = ROOT / "tests/validate-systemwide-enterprise-reliability.mjs"
GENERATED_DIR = ROOT / "generated/protected-test-audit-uat-repair-20260815"
GENERATED_WORKFLOW = GENERATED_DIR / "projectpulse-deploy-test.yml"
GENERATED_VALIDATOR = GENERATED_DIR / "validate-systemwide-enterprise-reliability.mjs"
BRANCH = "fix/protected-test-audit-uat-contract-20260815"


def run(*args: str) -> None:
    subprocess.run(list(args), cwd=ROOT, check=True)


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, found {count}.")
    return source.replace(old, new, 1)


head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
expected = os.environ.get("GITHUB_SHA", "")
if not expected or head != expected:
    raise SystemExit(f"Checkout mismatch: expected {expected or '<missing>'}, got {head}.")

workflow = WORKFLOW_PATH.read_text(encoding="utf-8")
validator = VALIDATOR_PATH.read_text(encoding="utf-8")

old_assertions = '''          jq -e '[.events[]? | .eventType] | any(. == "login_succeeded" or . == "login_failed" or . == "logout_succeeded")' "$EVIDENCE_DIR/audit-authentication.json" >/dev/null || fail 'Audit history did not expose authentication outcomes.'
          jq -e '[.events[]? | .eventType] | any(. == "login_failed")' "$EVIDENCE_DIR/audit-authentication.json" >/dev/null || fail 'Failed login was not visible in Audit History.'
          jq -e '[.events[]? | .eventType] | any(. == "logout_succeeded")' "$EVIDENCE_DIR/audit-authentication.json" >/dev/null || fail 'Logout was not visible in Audit History.'
'''

new_assertions = '''          jq -e '\n            .status == "audit_history_loaded"\n            and (.centralAudit.available == true)\n            and ((.events // []) | type == "array")\n            and ((.events // []) | length > 0)\n          ' "$EVIDENCE_DIR/audit-authentication.json" >/dev/null || fail 'Audit History did not return an available authentication event collection.'
          jq -e '\n            any(.events[]?;\n              (.details.event_type // "") == "login_succeeded"\n              or (((.eventType // "") | ascii_downcase) == "login succeeded")\n            )\n          ' "$EVIDENCE_DIR/audit-authentication.json" >/dev/null || fail 'Successful login was not visible in Audit History.'
          jq -e '\n            any(.events[]?;\n              (.details.event_type // "") == "login_failed"\n              or (((.eventType // "") | ascii_downcase) == "login failed")\n            )\n          ' "$EVIDENCE_DIR/audit-authentication.json" >/dev/null || fail 'Failed login was not visible in Audit History.'
          jq -e '\n            any(.events[]?;\n              (.details.event_type // "") == "logout_succeeded"\n              or (.details.login_result // "") == "logout_success"\n              or (.details.revoked_reason // "") == "user_logout"\n              or (((.eventType // "") | ascii_downcase) == "logout succeeded")\n            )\n          ' "$EVIDENCE_DIR/audit-authentication.json" >/dev/null || fail 'Logout was not visible in Audit History.'
'''
workflow = replace_once(workflow, old_assertions, new_assertions, "legacy audit assertion block")

anchor = "]) requireText(deployment, marker, 'role-correct Opportunity Directory UAT');\n\nconst authGetStart"
validator_block = "]) requireText(deployment, marker, 'role-correct Opportunity Directory UAT');\n\nfor (const marker of [\n  '.status == \"audit_history_loaded\"',\n  '.centralAudit.available == true',\n  '.details.event_type // \"\"',\n  '.details.login_result // \"\"',\n  '.details.revoked_reason // \"\"',\n  '\"logout_success\"',\n  '\"user_logout\"'\n]) requireText(deployment, marker, 'Audit History response-contract UAT');\nrejectText(\n  deployment,\n  '[.events[]? | .eventType] | any(. == \"login_succeeded\"',\n  'display-label-only Audit History UAT'\n);\n\nconst auditResponseFixture = {\n  status: 'audit_history_loaded',\n  centralAudit: { available: true },\n  events: [\n    { eventType: 'Login Succeeded', details: { event_type: 'login_succeeded' } },\n    { eventType: 'Login Failed', details: { event_type: 'login_failed' } },\n    { eventType: 'Auth Login Events', details: { login_result: 'logout_success' } },\n    { eventType: 'Auth Sessions', details: { revoked_reason: 'user_logout' } }\n  ]\n};\nconst auditEvents = auditResponseFixture.events;\nif (!auditEvents.some((event) => event.details?.event_type === 'login_succeeded')) {\n  throw new Error('Audit response fixture is missing login_succeeded evidence.');\n}\nif (!auditEvents.some((event) => event.details?.event_type === 'login_failed')) {\n  throw new Error('Audit response fixture is missing login_failed evidence.');\n}\nif (!auditEvents.some((event) =>\n  event.details?.event_type === 'logout_succeeded'\n  || event.details?.login_result === 'logout_success'\n  || event.details?.revoked_reason === 'user_logout'\n)) {\n  throw new Error('Audit response fixture is missing logout evidence.');\n}\n\nconst authGetStart"
validator = replace_once(validator, anchor, validator_block, "validator insertion anchor")

WORKFLOW_PATH.write_text(workflow, encoding="utf-8")
VALIDATOR_PATH.write_text(validator, encoding="utf-8")

run(
    "bash",
    "-lc",
    "bash -n <(sed -n '/          login() {/,/          unset COORDINATOR_SESSION TEST_LOGIN_PASSWORD/p' .github/workflows/projectpulse-deploy-test.yml)",
)

fixture = {
    "status": "audit_history_loaded",
    "centralAudit": {"available": True},
    "events": [
        {"eventType": "Login Succeeded", "details": {"event_type": "login_succeeded"}},
        {"eventType": "Login Failed", "details": {"event_type": "login_failed"}},
        {"eventType": "Auth Login Events", "details": {"login_result": "logout_success"}},
        {"eventType": "Auth Sessions", "details": {"revoked_reason": "user_logout"}},
    ],
}
with tempfile.NamedTemporaryFile(mode="w", suffix=".json", delete=False) as handle:
    json.dump(fixture, handle)
    fixture_path = handle.name

filters = [
    '.status == "audit_history_loaded" and (.centralAudit.available == true) and ((.events // []) | type == "array") and ((.events // []) | length > 0)',
    'any(.events[]?; (.details.event_type // "") == "login_succeeded" or (((.eventType // "") | ascii_downcase) == "login succeeded"))',
    'any(.events[]?; (.details.event_type // "") == "login_failed" or (((.eventType // "") | ascii_downcase) == "login failed"))',
    'any(.events[]?; (.details.event_type // "") == "logout_succeeded" or (.details.login_result // "") == "logout_success" or (.details.revoked_reason // "") == "user_logout" or (((.eventType // "") | ascii_downcase) == "logout succeeded"))',
]
try:
    for jq_filter in filters:
        subprocess.run(["jq", "-e", jq_filter, fixture_path], check=True, stdout=subprocess.DEVNULL)
finally:
    Path(fixture_path).unlink(missing_ok=True)

run("node", "--check", "tests/validate-systemwide-enterprise-reliability.mjs")
run("node", "tests/validate-systemwide-enterprise-reliability.mjs")
run("node", "tests/validate-systemwide-image-build-controller.mjs")
run("node", "tests/validate-utilization-role-scoping.mjs")
run("git", "diff", "--check")

actual = subprocess.check_output(["git", "diff", "--name-only"], cwd=ROOT, text=True).splitlines()
expected_files = [
    ".github/workflows/projectpulse-deploy-test.yml",
    "tests/validate-systemwide-enterprise-reliability.mjs",
]
if sorted(actual) != sorted(expected_files):
    raise SystemExit(f"Unexpected modified files: {actual}; expected {expected_files}.")

GENERATED_DIR.mkdir(parents=True, exist_ok=True)
GENERATED_WORKFLOW.write_text(workflow, encoding="utf-8")
GENERATED_VALIDATOR.write_text(validator, encoding="utf-8")

run("git", "restore", "--", str(WORKFLOW_PATH.relative_to(ROOT)), str(VALIDATOR_PATH.relative_to(ROOT)))
remaining = subprocess.check_output(["git", "status", "--short"], cwd=ROOT, text=True)
allowed_status = {"?? generated/", "?? generated/protected-test-audit-uat-repair-20260815/"}
unexpected = [line for line in remaining.splitlines() if line and line not in allowed_status]
if unexpected:
    raise SystemExit(f"Unexpected worktree state after generation:\n{remaining}")

run("git", "config", "user.name", "github-actions[bot]")
run("git", "config", "user.email", "41898282+github-actions[bot]@users.noreply.github.com")
run("git", "add", "--", str(GENERATED_WORKFLOW.relative_to(ROOT)), str(GENERATED_VALIDATOR.relative_to(ROOT)))
run("git", "diff", "--cached", "--check")
run("git", "commit", "-m", "Publish validated Protected-Test audit UAT repair blobs")
run("git", "push", "origin", f"HEAD:refs/heads/{BRANCH}")

print("PROTECTED_TEST_AUDIT_UAT_GENERATED_BLOBS=PASS")
print("GENERATED_WORKFLOW=generated/protected-test-audit-uat-repair-20260815/projectpulse-deploy-test.yml")
print("GENERATED_VALIDATOR=generated/protected-test-audit-uat-repair-20260815/validate-systemwide-enterprise-reliability.mjs")
print("APPLICATION_SOURCE_CHANGE=NONE")
print("PRODUCTION_MUTATION=NONE")
