#!/usr/bin/env bash
set -Eeuo pipefail

REPOSITORY_URL='https://github.com/ahmedadeyemi-cts/project-time-platform.git'
WORK_DIR='/tmp/celar-oracle-emergency-recovery'
HOST='celarai.onenecklab.com'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ "$(id -u)" -eq 0 ]] || fail 'Run this recovery with sudo/root.'
[[ -r /etc/os-release ]] || fail '/etc/os-release is missing.'
# shellcheck disable=SC1091
. /etc/os-release
[[ "${ID:-}" == ubuntu && "${VERSION_ID:-}" == 24.04* ]] || fail 'Ubuntu 24.04 LTS is required.'
[[ "$(uname -m)" == aarch64 ]] || fail 'ARM64/aarch64 is required.'

echo 'CELAR_EMERGENCY_RECOVERY_BEGIN=true'
echo 'PRODUCTION_MUTATION=NONE'

# Record safe diagnostics before changing services. Do not print environment
# files, tokens, or process arguments that may contain credentials.
systemctl --no-pager --full status   caddy.service   celar-ai-gateway.service   celar-maintenance-gateway.service   celar-gitops.service   celar-gitops.timer 2>&1 | tail -n 160 || true

ss -lnt | awk 'NR == 1 || $4 ~ /:(443|8787|8788|11434|3310)$/' || true

command -v git >/dev/null 2>&1 || {
  apt-get update
  apt-get install -y git ca-certificates curl
}

rm -rf "$WORK_DIR"
git clone --depth 1 --branch main "$REPOSITORY_URL" "$WORK_DIR"
TARGET_COMMIT="$(git -C "$WORK_DIR" rev-parse HEAD)"
[[ "$TARGET_COMMIT" =~ ^[0-9a-f]{40}$ ]] || fail 'Could not resolve exact current main.'

test -x "$WORK_DIR/deployment/oracle-celar/bootstrap.sh"   || fail 'Canonical Oracle bootstrap is missing or not executable.'

# The canonical bootstrap drains the current GitOps service, reapplies the
# reviewed desired state, restarts runtime services, records the applied tree,
# and restarts the pull-based reconciler.
bash "$WORK_DIR/deployment/oracle-celar/bootstrap.sh"

for service in   caddy.service   celar-ai-gateway.service   celar-maintenance-gateway.service   celar-gitops.timer; do
  systemctl is-active --quiet "$service"     || fail "Required service is not active after recovery: $service"
done

for port in 443 8787 8788 11434 3310; do
  case "$port" in
    443)
      ss -lnt | awk '{print $4}' | grep -Eq '(^|:)443$'         || fail 'Caddy is not listening on TCP 443 after recovery.'
      ;;
    *)
      ss -lnt | awk '{print $4}' | grep -Fxq "127.0.0.1:$port"         || fail "Required localhost service is not listening on 127.0.0.1:$port."
      ;;
  esac
done

# Verify the public auth boundary locally without exposing any bearer token.
# A healthy unauthenticated endpoint must fail closed with HTTP 401.
LOCAL_HTTPS_STATUS="$(
  curl --resolve "$HOST:443:127.0.0.1"     --noproxy "$HOST"     -sS --max-time 30 -o /dev/null -w '%{http_code}'     "https://$HOST/health" || true
)"
[[ "$LOCAL_HTTPS_STATUS" == 401 ]]   || fail "Recovered local HTTPS boundary returned HTTP $LOCAL_HTTPS_STATUS instead of 401."

# Run the canonical full acceptance suite. It reads the runtime token from the
# protected local file and never prints the token value.
test -x /opt/celar-ai/deploy/health-check.sh   || fail 'Installed Celar health check is missing.'
/opt/celar-ai/deploy/health-check.sh

echo "CELAR_EMERGENCY_RECOVERY_COMMIT=$TARGET_COMMIT"
echo 'CELAR_EMERGENCY_RECOVERY_HTTPS=PASS'
echo 'CELAR_EMERGENCY_RECOVERY=PASS'
echo 'PRODUCTION_MUTATION=NONE'
