#!/usr/bin/env bash
# Reuses the governed private-network migration job, UAMI and cleanup protocol.
set -Eeuo pipefail
CONTROL_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)"
RELEASE_ROOT="${PROJECTPULSE_RELEASE_ROOT:?Exact candidate checkout is required.}"
RELEASE="${RELIABILITY_RELEASE_COMMIT:?Exact release commit is required.}"
ACR="${AZURE_ACR_NAME:?Protected Test registry is required.}"
[[ "$ACR" =~ ^[A-Za-z0-9]+$ && "$RELEASE" =~ ^[0-9a-f]{40}$ ]] || exit 1
[[ "$(git -C "$RELEASE_ROOT" rev-parse HEAD)" == "$RELEASE" ]] || { echo 'ERROR: Candidate checkout changed.' >&2; exit 1; }
CONTEXT="$(mktemp -d "${RUNNER_TEMP:-/tmp}/flowhive-psa-migrations-XXXXXX")"
trap 'rm -rf -- "$CONTEXT"' EXIT
chmod 0700 "$CONTEXT"
mkdir -p "$CONTEXT/database/migrations"
python3 - "$CONTROL_ROOT" "$RELEASE_ROOT" "$CONTEXT" "$RELEASE" <<'PY'
import hashlib,json,pathlib,shutil,sys
control,source,out=map(pathlib.Path,sys.argv[1:4]); release=sys.argv[4]
approval=json.loads((control/'.github/flowhive-psa-protected-test-candidate.json').read_text())
if approval['sha']!=release or approval['environment']!='test': raise SystemExit('Unapproved migration candidate')
expected=['103_module_066_flowhive_enterprise_psa_revamp.sql','104_flowhive_bounded_ai_execution.sql']
if [x['file'] for x in approval['migrations']]!=expected: raise SystemExit('Unexpected migration set')
checks=[]
for item in approval['migrations']:
    relative='database/migrations/'+item['file']; data=(source/relative).read_bytes()
    if hashlib.sha256(data).hexdigest()!=item['sha256']: raise SystemExit('Unreviewed migration bytes')
    (out/relative).write_bytes(data); checks.append(item['sha256']+'  '+relative)
entry=(control/'scripts/release-test/apply-flowhive-psa-migrations.sh').read_bytes()
(out/'entrypoint.sh').write_bytes(entry); checks.append(hashlib.sha256(entry).hexdigest()+'  entrypoint.sh')
(out/'release-commit').write_text(release+'\n');(out/'SHA256SUMS').write_text('\n'.join(checks)+'\n')
PY
cat > "$CONTEXT/Dockerfile" <<'DOCKERFILE'
FROM postgres:16-alpine
RUN apk add --no-cache bash coreutils ca-certificates
WORKDIR /opt/projectpulse/release
COPY database/ database/
COPY release-commit SHA256SUMS entrypoint.sh ./
RUN chmod 0555 entrypoint.sh && chmod 0444 release-commit SHA256SUMS database/migrations/*.sql
ENTRYPOINT ["/opt/projectpulse/release/entrypoint.sh"]
DOCKERFILE
IMAGE="project-health-dashboard-flowhive-psa-migrator:rel-${RELEASE:0:12}-${GITHUB_RUN_ID:?}-${GITHUB_RUN_ATTEMPT:?}"
az acr build --registry "$ACR" --image "$IMAGE" --file "$CONTEXT/Dockerfile" --timeout 1800 "$CONTEXT"
DIGEST="$(az acr repository show --name "$ACR" --image "$IMAGE" --query digest -o tsv --only-show-errors)"
[[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] || { echo 'ERROR: Immutable migration digest unavailable.' >&2; exit 1; }
export MAIN_RELEASE_EXPECTED_RELEASE_COMMIT="$RELEASE"
export MAIN_RELEASE_CONTROL_SHA="${RELIABILITY_CONTROL_SHA:?Trusted controller revision is required.}"
export MAIN_RELEASE_MIGRATION_SCOPE=flowhive-enterprise-psa-103-104-test
export MAIN_RELEASE_MIGRATION_IMAGE="$ACR.azurecr.io/${IMAGE%%:*}@$DIGEST"
export MAIN_RELEASE_MIGRATION_JOB_NAME="fhpsa-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"
export MAIN_RELEASE_MIGRATION_MODE=apply
# The generic runner verifies subscription/UAMI/DB identity, immutable digest,
# exact temporary-job ownership, no retries, TLS, and cleanup before returning.
bash "$CONTROL_ROOT/scripts/release-test/run-migration-job.sh"
mkdir -p "${EVIDENCE_DIR:?Evidence directory is required.}"
jq -n --arg releaseCommit "$RELEASE" --arg controlCommit "$MAIN_RELEASE_CONTROL_SHA" --arg image "$MAIN_RELEASE_MIGRATION_IMAGE" \
  '{status:"applied_and_verified",environment:"test",releaseCommit:$releaseCommit,controlCommit:$controlCommit,image:$image,migrations:["103_module_066_flowhive_enterprise_psa_revamp","104_flowhive_bounded_ai_execution"],productionMutation:false}' \
  > "$EVIDENCE_DIR/flowhive-psa-migrations.json"
