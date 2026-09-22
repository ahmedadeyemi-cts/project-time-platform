#!/usr/bin/env bash
# Versioned cutover/rollback for the already-installed Laya service. No reinstall.
set -Eeuo pipefail
umask 077
[[ "$EUID" == 0 ]] || { echo 'Run with sudo -n.'; exit 1; }
MODE="${1:-}"
[[ "$MODE" == apply || "$MODE" == rollback ]] || { echo 'Usage: deploy-gateway.sh apply|rollback'; exit 2; }
ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
SOURCE="$ROOT/deployment/oracle-celar/gateway"
TARGET=/opt/celar-ai/gateway
DROPIN=/etc/systemd/system/celar-ai-gateway.service.d/80-laya-decisions.conf
exec 9>/run/celar-laya-deployment.lock
flock -n 9 || { echo 'Another Laya deployment is running.'; exit 1; }
[[ "$(hostname)" == vnic-celar ]] || { echo 'STOP: This release targets the approved Celar Test host.'; exit 1; }
[[ "$(systemctl show celar-ai-gateway.service -p User --value)" == celar-ai ]] || exit 1
getent group celar-laya-clients >/dev/null
[[ -f /etc/systemd/system/celar-laya.service ]] || exit 1
[[ -d "$TARGET" && ! -L "$TARGET" ]] || exit 1
for name in laya_decisions.py wsgi_decisions.py; do
    git -C "$ROOT" diff --exit-code HEAD -- "deployment/oracle-celar/gateway/$name" >/dev/null
    git -C "$ROOT" ls-files --error-unmatch "deployment/oracle-celar/gateway/$name" >/dev/null
    [[ -f "$SOURCE/$name" && ! -L "$SOURCE/$name" ]] || exit 1
done
STAGE="$(mktemp -d /run/celar-laya-release.XXXXXX)"
CHANGED=0
STARTED_LAYA=0
cleanup() {
    local status=$?
    trap - EXIT
    if [[ "$status" != 0 && "$CHANGED" == 1 && "$MODE" == apply ]]; then
        rm -f -- "$TARGET/laya_decisions.py" "$TARGET/wsgi_decisions.py" "$DROPIN"
        systemctl daemon-reload
        systemctl restart celar-ai-gateway.service || true
        [[ "$STARTED_LAYA" == 0 ]] || systemctl stop celar-laya.service || true
        echo 'Laya gateway cutover failed; prior gateway entrypoint restored.'
    fi
    rm -rf -- "$STAGE"
    exit "$status"
}
trap cleanup EXIT
cat > "$STAGE/dropin" <<'CONF'
[Service]
SupplementaryGroups=celar-laya-clients
ExecStart=
ExecStart=/usr/bin/gunicorn --workers 2 --threads 4 --bind 127.0.0.1:8787 --timeout 3660 --graceful-timeout 30 --keep-alive 5 --worker-tmp-dir /dev/shm wsgi_decisions:app
CONF
# Do not override an unreviewed service command or a newer adapter release.
python3 - "$ROOT/deployment/oracle-celar/systemd/celar-ai-gateway.service" <<'PY'
import re, shlex, subprocess, sys
from pathlib import Path
expected = next(x.split('=',1)[1] for x in Path(sys.argv[1]).read_text().splitlines() if x.startswith('ExecStart='))
actual = subprocess.check_output(['systemctl','show','celar-ai-gateway.service','-p','ExecStart','--value'], text=True)
match = re.search(r'argv\[\]=(.*?)\s*;\s*ignore_errors=', actual)
if not match or shlex.split(match[1]) not in [shlex.split(expected), shlex.split(expected.replace('wsgi:app','wsgi_decisions:app'))]:
    raise SystemExit('STOP: Gateway command differs from the reviewed release baseline.')
PY
EXISTING=0
if [[ -e "$DROPIN" || -L "$DROPIN" ]]; then
    [[ ! -L "$DROPIN" ]] && cmp -s "$STAGE/dropin" "$DROPIN" || { echo 'STOP: Different gateway drop-in exists.'; exit 1; }
    for name in laya_decisions.py wsgi_decisions.py; do
        [[ ! -L "$TARGET/$name" ]] && cmp -s "$SOURCE/$name" "$TARGET/$name" || { echo 'STOP: Different adapter version exists.'; exit 1; }
    done
    EXISTING=1
else
    for name in laya_decisions.py wsgi_decisions.py; do
        [[ ! -e "$TARGET/$name" && ! -L "$TARGET/$name" ]] || { echo 'STOP: Unmanaged adapter file exists.'; exit 1; }
    done
fi
if [[ "$MODE" == rollback ]]; then
    if [[ "$EXISTING" == 1 ]]; then
        rm -- "$DROPIN" "$TARGET/laya_decisions.py" "$TARGET/wsgi_decisions.py"
        systemctl daemon-reload
        systemctl restart celar-ai-gateway.service
    fi
    systemctl is-active --quiet celar-ai-gateway.service
    echo 'LAYA_GATEWAY_ROLLBACK=PASS; database audit history retained.'
    exit 0
fi
python3 - "$SOURCE" <<'PY'
from pathlib import Path
import sys
for p in Path(sys.argv[1]).glob('*decisions.py'):
    compile(p.read_text(), str(p), 'exec')
PY
if ! systemctl is-active --quiet celar-laya.service; then
    python3 - <<'PY'
from pathlib import Path
values = dict(line.split(':',1) for line in Path('/proc/meminfo').read_text().splitlines())
if int(values['MemAvailable'].split()[0]) < 6*1024**2:
    raise SystemExit('STOP: Less than 6 GiB available for model startup. No existing services were stopped.')
PY
    STARTED_LAYA=1
    systemctl start celar-laya.service
fi
if [[ "$EXISTING" == 0 ]]; then
    CHANGED=1
    for name in laya_decisions.py wsgi_decisions.py; do
        install -o root -g root -m 0644 "$SOURCE/$name" "$TARGET/$name"
    done
    install -d -o root -g root -m 0755 "$(dirname "$DROPIN")"
    install -o root -g root -m 0644 "$STAGE/dropin" "$DROPIN"
    systemd-analyze verify /etc/systemd/system/celar-ai-gateway.service
    systemctl daemon-reload
    systemctl restart celar-ai-gateway.service
fi
# Read the existing credential internally; never put it on the command line/log.
python3 - <<'PY'
import json, time, urllib.request, urllib.error
from pathlib import Path
base = 'http://127.0.0.1:8787'
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
secret = Path('/etc/celar-ai/gateway/runtime-token').read_text().strip()
headers = {'Authorization':'Bearer '+secret, 'X-Pulse-AI-Privacy-Boundary':'private_pulse_runtime_only'}
def call(path, auth, body=None):
    request = urllib.request.Request(base+path, headers=auth, data=None if body is None else json.dumps(body).encode())
    if body is not None: request.add_header('Content-Type','application/json')
    try:
        with opener.open(request, timeout=15) as response:
            data = response.read(16385)
            if len(data)>16384: raise SystemExit('Gateway verification response exceeded the limit.')
            return response.status, json.loads(data)
    except urllib.error.HTTPError as error:
        return error.code, {}
for attempt in range(15):
    try:
        status, health = call('/v1/decisions/health', headers)
        if status == 200: break
    except (OSError, ValueError): pass
    time.sleep(1)
else: raise SystemExit('Authenticated gateway readiness did not pass.')
assert health.get('runtime_connected') is True
assert call('/v1/decisions/health', {})[0] == 401
assert call('/v1/decisions/health', {'Authorization':'Bearer '+secret})[0] == 403
status, result = call('/v1/decisions/document-type', headers, {'text':'INVOICE. Total amount due USD 2500. Payment terms net 30 days.'})
assert status == 200 and result.get('document_type') == 'invoice'
assert result.get('review_required') is True and result.get('automation_approved') is False
assert result.get('workflow_actions_performed') == 0 and result.get('input_truncated') is False
assert result.get('model_revision') == '1c5edc17a7acd8701df6fc341c0d179f1c62c982'
print(json.dumps({'gateway_integration_test':'PASS','authenticated_health':True,'unauthenticated_denied':True,
                  'privacy_header_required':True,'classification':'invoice','latency_ms':result['latency_ms'],
                  'workflow_actions_performed':0}))
PY
for service in celar-laya celar-ai-gateway ollama caddy; do
    systemctl is-active --quiet "$service.service" || { echo "STOP: $service is not active."; exit 1; }
done
CHANGED=0
printf 'LAYA_GATEWAY_CUTOVER=PASS\nRelease commit: '
git -C "$ROOT" rev-parse HEAD
