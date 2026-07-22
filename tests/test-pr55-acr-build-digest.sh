#!/usr/bin/env bash
set -Eeuo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HELPER="$REPO_ROOT/scripts/build-pr55-acr-image.sh"
TEMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TEMP_DIR"' EXIT

FAKE_BIN="$TEMP_DIR/bin"
BUILD_CONTEXT="$TEMP_DIR/context"
AZ_ARGS_LOG="$TEMP_DIR/az-args.log"
VALID_DIGEST='sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'

mkdir -p "$FAKE_BIN" "$BUILD_CONTEXT"
printf 'FROM scratch\n' > "$BUILD_CONTEXT/Dockerfile"

cat > "$FAKE_BIN/az" <<'AZ'
#!/usr/bin/env bash
set -Eeuo pipefail
printf '%s\n' "$*" >> "$AZ_ARGS_LOG"
case "${FAKE_AZ_MODE:-valid}" in
  valid)
    printf '%s\n' "$FAKE_AZ_DIGEST"
    ;;
  invalid)
    printf '%s\n' 'sha256:not-a-valid-digest'
    ;;
  failure)
    exit 17
    ;;
  *)
    exit 18
    ;;
esac
AZ
chmod 0755 "$FAKE_BIN/az"

export PATH="$FAKE_BIN:$PATH"
export AZ_ARGS_LOG
export FAKE_AZ_DIGEST="$VALID_DIGEST"

ACTUAL_DIGEST="$(
  "$HELPER" \
    testregistry \
    project-health-dashboard-api:test-release \
    "$BUILD_CONTEXT/Dockerfile" \
    "$BUILD_CONTEXT"
)"
[[ "$ACTUAL_DIGEST" == "$VALID_DIGEST" ]] || {
  echo 'The helper did not return the digest from the ACR build result.' >&2
  exit 1
}

grep -Fq 'acr build' "$AZ_ARGS_LOG"
grep -Fq -- '--registry testregistry' "$AZ_ARGS_LOG"
grep -Fq -- '--image project-health-dashboard-api:test-release' "$AZ_ARGS_LOG"
grep -Fq -- "--file $BUILD_CONTEXT/Dockerfile" "$AZ_ARGS_LOG"
grep -Fq -- '--no-logs' "$AZ_ARGS_LOG"
grep -Fq -- '--query outputImages[0].digest' "$AZ_ARGS_LOG"
grep -Fq -- '--output tsv' "$AZ_ARGS_LOG"
grep -Fq -- "$BUILD_CONTEXT" "$AZ_ARGS_LOG"
if grep -Fq 'repository show' "$AZ_ARGS_LOG"; then
  echo 'The helper must not query a newly pushed tag after the build.' >&2
  exit 1
fi

export FAKE_AZ_MODE='invalid'
if "$HELPER" testregistry project-health-dashboard-web:test-release "$BUILD_CONTEXT/Dockerfile" "$BUILD_CONTEXT" >/dev/null 2>&1; then
  echo 'The helper accepted an invalid ACR digest.' >&2
  exit 1
fi

export FAKE_AZ_MODE='failure'
if "$HELPER" testregistry project-health-dashboard-pr55-migrator:test-release "$BUILD_CONTEXT/Dockerfile" "$BUILD_CONTEXT" >/dev/null 2>&1; then
  echo 'The helper accepted a failed ACR build.' >&2
  exit 1
fi

echo 'PR55_ACR_BUILD_DIGEST_TEST=PASS'
