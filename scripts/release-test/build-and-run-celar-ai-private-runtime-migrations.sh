#!/usr/bin/env bash
# Builds exactly Celar AI migrations 080/081 and runs them through the existing
# protected-Test private-network migration job, UAMI and Key Vault protocol.
set -Eeuo pipefail

CONTROL_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)"
RELEASE_ROOT="${PROJECTPULSE_RELEASE_ROOT:?Exact candidate checkout is required.}"
RELEASE="${RELIABILITY_RELEASE_COMMIT:?Exact release commit is required.}"
ACR="${AZURE_ACR_NAME:?Protected Test registry is required.}"
[[ "$ACR" =~ ^[A-Za-z0-9]+$ && "$RELEASE" =~ ^[0-9a-f]{40}$ ]] || exit 1
[[ "$(git -C "$RELEASE_ROOT" rev-parse HEAD)" == "$RELEASE" ]] || {
  echo 'ERROR: Candidate checkout changed.' >&2
  exit 1
}

CONTEXT="$(mktemp -d "${RUNNER_TEMP:-/tmp}/celar-private-runtime-migrations-XXXXXX")"
trap 'rm -rf -- "$CONTEXT"' EXIT
chmod 0700 "$CONTEXT"
mkdir -p "$CONTEXT/database/migrations"

python3 - "$RELEASE_ROOT" "$CONTEXT" "$RELEASE" <<'PY'
import hashlib,pathlib,shutil,sys
source,out=map(pathlib.Path,sys.argv[1:3]); release=sys.argv[3]
migrations=['080_celar_ai_internal_data_intelligence.sql','081_celar_ai_private_runtime_activation.sql']
checks=[]
for name in migrations:
    data=(source/'database/migrations'/name).read_bytes()
    (out/'database/migrations'/name).write_bytes(data)
    checks.append(hashlib.sha256(data).hexdigest()+'  '+name)
(out/'database/migrations/SHA256SUMS').write_text('\n'.join(checks)+'\n')
(out/'.projectpulse-release-commit').write_text(release+'\n')
shutil.copy2(
    source/'scripts/release-test/apply-celar-ai-private-runtime-080-081.sh',
    out/'apply-celar-ai-private-runtime-080-081.sh')
PY

cat > "$CONTEXT/Dockerfile" <<'DOCKERFILE'
FROM postgres:16-alpine
RUN apk add --no-cache bash coreutils ca-certificates
WORKDIR /opt/projectpulse/release
COPY database/ database/
COPY .projectpulse-release-commit ./
COPY apply-celar-ai-private-runtime-080-081.sh /usr/local/bin/apply-celar-private-runtime
RUN chmod 0555 /usr/local/bin/apply-celar-private-runtime \
    && chmod 0444 .projectpulse-release-commit database/migrations/*.sql database/migrations/SHA256SUMS
ENTRYPOINT ["/usr/local/bin/apply-celar-private-runtime","/opt/projectpulse/release"]
DOCKERFILE

IMAGE="project-health-dashboard-celar-private-runtime-migrator:rel-${RELEASE:0:12}-${GITHUB_RUN_ID:?}-${GITHUB_RUN_ATTEMPT:?}"
az acr build --registry "$ACR" --image "$IMAGE" --file "$CONTEXT/Dockerfile" --timeout 1800 "$CONTEXT"

DIGEST=""
for attempt in {1..12}; do
  DIGEST="$(az acr repository show --name "$ACR" --image "$IMAGE" --query digest -o tsv --only-show-errors 2>/dev/null || true)"
  [[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] && break
  (( attempt < 12 )) && sleep 5
done
[[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] || {
  echo 'ERROR: Immutable Celar private-runtime migration digest unavailable.' >&2
  exit 1
}

export MAIN_RELEASE_EXPECTED_RELEASE_COMMIT="$RELEASE"
export MAIN_RELEASE_CONTROL_SHA="${RELIABILITY_CONTROL_SHA:?Trusted controller revision is required.}"
export MAIN_RELEASE_MIGRATION_SCOPE="celar-private-runtime-080-081-test"
export MAIN_RELEASE_MIGRATION_IMAGE="$ACR.azurecr.io/${IMAGE%%:*}@$DIGEST"
export MAIN_RELEASE_MIGRATION_JOB_NAME="cpr81-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"
export MAIN_RELEASE_MIGRATION_MODE=apply
bash "$CONTROL_ROOT/scripts/release-test/run-migration-job.sh"

mkdir -p "${EVIDENCE_DIR:?Evidence directory is required.}"
jq -n \
  --arg releaseCommit "$RELEASE" \
  --arg controlCommit "$MAIN_RELEASE_CONTROL_SHA" \
  --arg image "$MAIN_RELEASE_MIGRATION_IMAGE" \
  '{status:"applied_and_verified",environment:"test",releaseCommit:$releaseCommit,controlCommit:$controlCommit,image:$image,migrations:["080_celar_ai_internal_data_intelligence","081_celar_ai_private_runtime_activation"],productionMutation:false}' \
  > "$EVIDENCE_DIR/celar-private-runtime-migrations.json"

echo 'CELAR_AI_PRIVATE_RUNTIME_MIGRATIONS_080_081=APPLIED_AND_VERIFIED'
