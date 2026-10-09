#!/usr/bin/env bash
set -Eeuo pipefail
[[ "$GITHUB_REPOSITORY" == ahmedadeyemi-cts/project-time-platform && "$GITHUB_REF" == refs/heads/main && "$GITHUB_EVENT_NAME" == workflow_dispatch ]]
[[ "$RELEASE_SHA" =~ ^[a-f0-9]{40}$ && "$RELEASE_SHA" == "$GITHUB_SHA" && "$(git rev-parse HEAD)" == "$RELEASE_SHA" ]]
[[ -z "$(git status --porcelain)" ]]
CURRENT="$(gh api "repos/$GITHUB_REPOSITORY/git/ref/heads/main" --jq '.object.sha')"
[[ "$CURRENT" == "$RELEASE_SHA" ]]
export EXPECTED_MAIN_SHA="$CURRENT"
# Read the gate implementations from merged main; never change immutable governance.
node scripts/validate-deployment-concurrency-governance.mjs --repo-root "$PWD" --verify-repository
python3 scripts/security/validate-repository-security-posture.py
TMP_GATE="$(mktemp -d)"
trap 'rm -rf "$TMP_GATE"' EXIT
gh api "repos/$GITHUB_REPOSITORY/actions/runs?head_sha=$RELEASE_SHA&event=push&per_page=100" > "$TMP_GATE/runs.json"
gh api "repos/$GITHUB_REPOSITORY/commits/$RELEASE_SHA/check-runs?per_page=100" > "$TMP_GATE/checks.json"
gh api "repos/$GITHUB_REPOSITORY/commits/$RELEASE_SHA/status" > "$TMP_GATE/status.json"
python3 - "$TMP_GATE" <<'PY'
import json,sys
from pathlib import Path
root=Path(sys.argv[1])
runs=json.loads((root/'runs.json').read_text())['workflow_runs']
for name in ('ProjectPulse CI','ProjectPulse Repository Security Posture','Validate Deployment Concurrency Governance'):
    candidates=sorted((r for r in runs if r['name']==name),key=lambda r:r['id'],reverse=True)
    assert candidates and candidates[0]['status']=='completed' and candidates[0]['conclusion']=='success', 'Exact-main required workflow not successful: '+name
checks=json.loads((root/'checks.json').read_text())['check_runs']
for c in checks:
    assert c['status']=='completed' and c['conclusion'] in ('success','neutral','skipped'), 'Exact-main check not successful: '+c['name']
statuses=json.loads((root/'status.json').read_text())['statuses']
assert all(s['state']=='success' for s in statuses),'Exact-main status is not successful'
print('CORE_EXACT_MAIN_SECURITY_ADMISSION=PASS')
PY
printf 'EXPECTED_MAIN_SHA=%s\n' "$CURRENT" >> "$GITHUB_ENV"
