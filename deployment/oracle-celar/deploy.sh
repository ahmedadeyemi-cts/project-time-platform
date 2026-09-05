#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
MANIFEST="$ROOT/release.json"
INSTALL_ROOT='/opt/celar-ai/deploy'
GATEWAY_ROOT='/opt/celar-ai/gateway'
STATE_DIR='/var/lib/celar-ai'
GATEWAY_STATE_DIR='/var/lib/celar-ai/gateway'
GATEWAY_CONFIG_DIR='/etc/celar-ai/gateway'
RUNTIME_TOKEN_FILE="$GATEWAY_CONFIG_DIR/runtime-token"
MAINTENANCE_TOKEN_FILE="$GATEWAY_CONFIG_DIR/maintenance-token"
RUNTIME_ENV_FILE="$GATEWAY_CONFIG_DIR/runtime.env"
FIREWALL_RULES='/etc/iptables/rules.v4'
CADDYFILE='/etc/caddy/Caddyfile'
RUNTIME_MUTATION_LOCK='/run/celar-runtime-mutation.lock'
CLAMAV_SOCKET_DROPIN_DIR='/etc/systemd/system/clamav-daemon.socket.d'
CLAMAV_SOCKET_DROPIN="$CLAMAV_SOCKET_DROPIN_DIR/10-celar-tcp.conf"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ "$(id -u)" -eq 0 ]] || fail 'Run deploy.sh as root.'
[[ -s "$MANIFEST" ]] || fail 'release.json is missing.'
command -v jq >/dev/null 2>&1 || fail 'jq is required before deployment.'
command -v flock >/dev/null 2>&1 || fail 'flock is required before deployment.'

ARCH="$(jq -r '.architecture' "$MANIFEST")"
HOSTNAME_VALUE="$(jq -r '.hostname' "$MANIFEST")"
GATEWAY_VERSION="$(jq -r '.gatewayVersion' "$MANIFEST")"
GATEWAY_BIND="$(jq -r '.gatewayBind' "$MANIFEST")"
GENERATION_MODEL="$(jq -r '.generationModel' "$MANIFEST")"
REASONING_MODEL="$(jq -r '.reasoningModel' "$MANIFEST")"
FAST_GENERAL_MODEL="$(jq -r '.fastGeneralModel' "$MANIFEST")"
EMBEDDING_MODEL="$(jq -r '.embeddingModel' "$MANIFEST")"
EMBEDDING_DIMENSION="$(jq -r '.embeddingDimension' "$MANIFEST")"
OCR_MODEL="$(jq -r '.ocrModel' "$MANIFEST")"
LOCAL_GENERATION_MODELS="$(jq -r '.localGenerationModels | join(",")' "$MANIFEST")"
STRUCTURED_GENERATION_ORDER="$(jq -r '.structuredGenerationOrder | join(",")' "$MANIFEST")"
STRUCTURED_MODEL_ATTEMPT_SECONDS="$(jq -r '.structuredModelAttemptSeconds | join(",")' "$MANIFEST")"
GENERAL_GENERATION_ORDER="$(jq -r '.generalGenerationOrder | join(",")' "$MANIFEST")"
GENERAL_MODEL_ATTEMPT_SECONDS="$(jq -r '.generalModelAttemptSeconds | join(",")' "$MANIFEST")"
OLLAMA_HOST_VALUE="$(jq -r '.ollamaHost' "$MANIFEST")"
OLLAMA_KEEP_ALIVE="$(jq -r '.ollamaKeepAlive' "$MANIFEST")"
OLLAMA_MAX_LOADED_MODELS="$(jq -r '.ollamaMaxLoadedModels' "$MANIFEST")"
OLLAMA_NUM_PARALLEL="$(jq -r '.ollamaNumParallel' "$MANIFEST")"
OLLAMA_MAX_QUEUE="$(jq -r '.ollamaMaxQueue' "$MANIFEST")"
CLAMAV_HOST="$(jq -r '.clamavHost' "$MANIFEST")"
CLAMAV_PORT="$(jq -r '.clamavPort' "$MANIFEST")"
MAX_UPLOAD_BYTES="$(jq -r '.maxUploadBytes' "$MANIFEST")"
MAX_JSON_REQUEST_BYTES="$(jq -r '.maxJsonRequestBytes' "$MANIFEST")"
MAX_GATEWAY_RESPONSE_BYTES="$(jq -r '.maxGatewayResponseBytes' "$MANIFEST")"
MAX_OCR_PAGES="$(jq -r '.maxOcrPages' "$MANIFEST")"
MAX_OCR_IMAGE_PIXELS="$(jq -r '.maxOcrImagePixels' "$MANIFEST")"
MAX_OCR_IMAGE_EDGE="$(jq -r '.maxOcrImageEdge' "$MANIFEST")"
PDF_RASTER_MAX_EDGE="$(jq -r '.pdfRasterMaxEdge' "$MANIFEST")"
OCR_TOTAL_TIMEOUT_SECONDS="$(jq -r '.ocrTotalTimeoutSeconds' "$MANIFEST")"
CHAT_TIMEOUT_SECONDS="$(jq -r '.chatTimeoutSeconds' "$MANIFEST")"
EMBEDDING_TIMEOUT_SECONDS="$(jq -r '.embeddingTimeoutSeconds' "$MANIFEST")"
LOCK_WAIT_SECONDS="$(jq -r '.runtimeMutationLockWaitSeconds' "$MANIFEST")"
MAINTENANCE_ENABLED="$(jq -r '.modelMaintenance.enabled' "$MANIFEST")"
MAINTENANCE_DAY="$(jq -r '.modelMaintenance.dayOfWeek' "$MANIFEST")"
MAINTENANCE_LOCAL_TIME="$(jq -r '.modelMaintenance.localTime' "$MANIFEST")"
MAINTENANCE_TIME_ZONE="$(jq -r '.modelMaintenance.timeZone' "$MANIFEST")"
MAINTENANCE_ON_CALENDAR="$(jq -r '.modelMaintenance.systemdOnCalendar' "$MANIFEST")"

[[ "$ARCH" == arm64 && "$(uname -m)" == aarch64 ]] || fail 'The release architecture does not match this host.'
[[ "$HOSTNAME_VALUE" == celarai.onenecklab.com ]] || fail 'The governed Oracle hostname changed unexpectedly.'
[[ "$GATEWAY_BIND" == '127.0.0.1:8787' ]] || fail 'Celar gateway must remain bound to localhost:8787.'
for model in "$GENERATION_MODEL" "$REASONING_MODEL" "$FAST_GENERAL_MODEL" "$EMBEDDING_MODEL"; do
  [[ "$model" =~ ^[A-Za-z0-9._:/-]+$ ]] || fail "Invalid model name: $model"
done
[[ "$OCR_MODEL" == tesseract-5-eng ]] || fail 'Unexpected OCR model.'
[[ "$EMBEDDING_DIMENSION" == 768 ]] || fail 'Embedding dimension must remain 768.'
[[ "$OLLAMA_HOST_VALUE" == '127.0.0.1:11434' ]] || fail 'Ollama must remain bound to localhost.'
[[ "$CLAMAV_HOST" == '127.0.0.1' && "$CLAMAV_PORT" == 3310 ]] || fail 'ClamAV must remain on localhost:3310.'
[[ "$MAINTENANCE_ENABLED" == true || "$MAINTENANCE_ENABLED" == false ]] || fail 'Invalid maintenance enabled flag.'
[[ "$MAINTENANCE_DAY" == Sunday ]] || fail 'The GitOps default maintenance day must remain Sunday.'
[[ "$MAINTENANCE_LOCAL_TIME" == 01:00 ]] || fail 'The GitOps default maintenance time must remain 01:00.'
[[ "$MAINTENANCE_TIME_ZONE" == America/Chicago ]] || fail 'The GitOps maintenance time zone must remain America/Chicago.'
[[ "$MAINTENANCE_ON_CALENDAR" == 'Sun *-*-* 01:00:00 America/Chicago' ]] || fail 'The GitOps maintenance calendar changed unexpectedly.'
for numeric in "$MAX_UPLOAD_BYTES" "$MAX_JSON_REQUEST_BYTES" "$MAX_GATEWAY_RESPONSE_BYTES" "$MAX_OCR_PAGES" "$MAX_OCR_IMAGE_PIXELS" "$MAX_OCR_IMAGE_EDGE" "$PDF_RASTER_MAX_EDGE" "$OCR_TOTAL_TIMEOUT_SECONDS" "$CHAT_TIMEOUT_SECONDS" "$EMBEDDING_TIMEOUT_SECONDS" "$LOCK_WAIT_SECONDS"; do
  [[ "$numeric" =~ ^[0-9]+$ && "$numeric" -gt 0 ]] || fail 'Invalid positive numeric runtime limit.'
done
[[ "$LOCK_WAIT_SECONDS" -ge 60 ]] || fail 'Runtime mutation lock wait is too short.'

mapfile -t APPROVED_GENERATION_MODELS < <(jq -r '.localGenerationModels[]' "$MANIFEST")
(( ${#APPROVED_GENERATION_MODELS[@]} >= 3 )) || fail 'At least three approved local generation specialists are required.'
for required in "$GENERATION_MODEL" "$REASONING_MODEL" "$FAST_GENERAL_MODEL"; do
  printf '%s\n' "${APPROVED_GENERATION_MODELS[@]}" | grep -Fxq "$required" || fail "Required local model is absent from portfolio: $required"
done
jq -e '
  ([.structuredGenerationOrder[], .generalGenerationOrder[]] - .localGenerationModels | length) == 0 and
  (.structuredGenerationOrder | length) == (.structuredModelAttemptSeconds | length) and
  (.generalGenerationOrder | length) == (.generalModelAttemptSeconds | length) and
  ([.structuredModelAttemptSeconds[], .generalModelAttemptSeconds[]] | all(. >= 10)) and
  (.structuredModelAttemptSeconds | add) <= .chatTimeoutSeconds and
  (.generalModelAttemptSeconds | add) <= .chatTimeoutSeconds and
  .modelMaintenance.cadence == "weekly" and
  .modelMaintenance.timeZone == "America/Chicago" and
  .modelMaintenance.automaticEngineUpdate == true and
  .modelMaintenance.automaticModelPull == true and
  .modelMaintenance.rollbackOnValidationFailure == true
' "$MANIFEST" >/dev/null || fail 'Local model order, bounded-attempt policy, or maintenance policy is invalid.'

exec 8>"$RUNTIME_MUTATION_LOCK"
flock -w "$LOCK_WAIT_SECONDS" 8 || fail 'Timed out waiting for the Celar runtime mutation lock.'

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y \
  ca-certificates curl jq git unzip zstd netcat-openbsd rsync openssl util-linux \
  python3 python3-venv python3-pip python3-flask python3-requests python3-pil gunicorn fonts-dejavu-core \
  tesseract-ocr tesseract-ocr-eng poppler-utils \
  clamav clamav-daemon clamav-freshclam \
  caddy restic unattended-upgrades iptables-persistent

getent group celar-ai >/dev/null 2>&1 || groupadd --system celar-ai
if ! id celar-ai >/dev/null 2>&1; then
  useradd --system --gid celar-ai --home-dir "$GATEWAY_STATE_DIR" --shell /usr/sbin/nologin celar-ai
fi

install -d -m 0755 "$INSTALL_ROOT" "$STATE_DIR" /var/backups/celar-ai "$GATEWAY_ROOT"
install -d -o root -g celar-ai -m 0710 /etc/celar-ai

if [[ -s "$FIREWALL_RULES" ]]; then
  install -d -m 0700 /root/celar-firewall-backup
  if [[ ! -e /root/celar-firewall-backup/rules.v4.pre-gitops ]]; then
    cp -a "$FIREWALL_RULES" /root/celar-firewall-backup/rules.v4.pre-gitops
  fi
  if ! grep -q -- '--dport 443' "$FIREWALL_RULES"; then
    sed -i \
      '/^-A INPUT -j REJECT --reject-with icmp-host-prohibited/i\
-A INPUT -p tcp -m state --state NEW -m tcp --dport 443 -j ACCEPT' \
      "$FIREWALL_RULES"
  fi
  iptables-restore < "$FIREWALL_RULES"
  netfilter-persistent save >/dev/null
else
  fail 'Oracle iptables rules.v4 is missing; refusing to replace the host firewall blindly.'
fi

# Ubuntu's clamav-daemon package uses systemd socket activation. Recent package
# versions may expose only the Unix socket from clamav-daemon.socket even when
# TCPSocket/TCPAddr are present in clamd.conf. Install an explicit loopback TCP
# socket drop-in so the Celar gateway can reliably reach 127.0.0.1:3310 while
# preserving the distro-managed Unix socket.
if [[ ! -e /etc/clamav/clamd.conf.pre-celar-gitops ]]; then
  cp -a /etc/clamav/clamd.conf /etc/clamav/clamd.conf.pre-celar-gitops
fi
sed -i '/^TCPSocket[[:space:]]/d;/^TCPAddr[[:space:]]/d;/^StreamMaxLength[[:space:]]/d' /etc/clamav/clamd.conf
printf '\nTCPSocket %s\nTCPAddr %s\nStreamMaxLength 50M\n' "$CLAMAV_PORT" "$CLAMAV_HOST" >> /etc/clamav/clamd.conf
install -d -m 0755 "$CLAMAV_SOCKET_DROPIN_DIR"
install -m 0644 "$ROOT/systemd/clamav-daemon.socket.d/10-celar-tcp.conf" "$CLAMAV_SOCKET_DROPIN"
systemctl daemon-reload
systemctl enable clamav-freshclam clamav-daemon clamav-daemon.socket >/dev/null
systemctl restart clamav-freshclam
systemctl stop clamav-daemon.service clamav-daemon.socket
systemctl start clamav-daemon.socket
systemctl start clamav-daemon.service

CLAM_READY=false
for attempt in $(seq 1 60); do
  CLAM_PING="$(printf 'zPING\0' | nc -N -w 3 "$CLAMAV_HOST" "$CLAMAV_PORT" 2>/dev/null | tr -d '\0\r\n' || true)"
  if [[ "$CLAM_PING" == PONG ]]; then
    CLAM_READY=true
    break
  fi
  sleep 2
done
if [[ "$CLAM_READY" != true ]]; then
  systemctl --no-pager --full status clamav-daemon.socket clamav-daemon.service >&2 || true
  journalctl -u clamav-daemon.socket -u clamav-daemon.service -n 80 --no-pager >&2 || true
  fail 'ClamAV localhost TCP socket did not become ready on 127.0.0.1:3310.'
fi

if ! command -v ollama >/dev/null 2>&1; then
  INSTALLER="$(mktemp)"
  trap 'rm -f "$INSTALLER"' EXIT
  curl --fail --silent --show-error --location https://ollama.com/install.sh --output "$INSTALLER"
  test -s "$INSTALLER" || fail 'Ollama installer download was empty.'
  sh "$INSTALLER"
  rm -f "$INSTALLER"
  trap - EXIT
fi

install -d -m 0755 /etc/systemd/system/ollama.service.d
cat > /etc/systemd/system/ollama.service.d/10-celar-runtime.conf <<EOF
[Service]
Environment="OLLAMA_HOST=$OLLAMA_HOST_VALUE"
Environment="OLLAMA_KEEP_ALIVE=$OLLAMA_KEEP_ALIVE"
Environment="OLLAMA_MAX_LOADED_MODELS=$OLLAMA_MAX_LOADED_MODELS"
Environment="OLLAMA_NUM_PARALLEL=$OLLAMA_NUM_PARALLEL"
Environment="OLLAMA_MAX_QUEUE=$OLLAMA_MAX_QUEUE"
EOF

systemctl daemon-reload
systemctl enable ollama.service >/dev/null
systemctl restart ollama.service
for attempt in $(seq 1 30); do
  if curl -fsS --max-time 3 http://127.0.0.1:11434/api/version >/dev/null; then
    break
  fi
  (( attempt < 30 )) || fail 'Ollama did not become ready.'
  sleep 2
done

ollama_model_present() {
  local model="$1"
  ollama list 2>/dev/null | awk -v wanted="$model" '
    NR > 1 && ($1 == wanted || $1 == wanted ":latest") { found=1 }
    END { exit(found ? 0 : 1) }
  '
}

for model in "${APPROVED_GENERATION_MODELS[@]}" "$EMBEDDING_MODEL"; do
  if ! ollama_model_present "$model"; then
    ollama pull "$model"
  fi
done

install -d -o root -g celar-ai -m 0750 "$GATEWAY_CONFIG_DIR"
install -d -o celar-ai -g celar-ai -m 0750 "$GATEWAY_STATE_DIR"
install -d -o root -g root -m 0755 "$GATEWAY_ROOT"

if [[ ! -s "$RUNTIME_TOKEN_FILE" ]]; then
  umask 0077
  openssl rand -hex 48 > "$RUNTIME_TOKEN_FILE"
fi
[[ "$(wc -c < "$RUNTIME_TOKEN_FILE")" -ge 64 ]] || fail 'Runtime token is unexpectedly short.'
chown root:celar-ai "$RUNTIME_TOKEN_FILE"
chmod 0640 "$RUNTIME_TOKEN_FILE"

# Schedule mutation uses a separate credential from inference/read-only status.
# The value is never printed. It is synchronized to the protected Test secret
# only through an administrator-controlled secret channel after live acceptance.
if [[ ! -s "$MAINTENANCE_TOKEN_FILE" ]]; then
  umask 0077
  openssl rand -hex 48 > "$MAINTENANCE_TOKEN_FILE"
fi
[[ "$(wc -c < "$MAINTENANCE_TOKEN_FILE")" -ge 64 ]] || fail 'Maintenance token is unexpectedly short.'
chown root:celar-ai "$MAINTENANCE_TOKEN_FILE"
chmod 0640 "$MAINTENANCE_TOKEN_FILE"

cat > "$RUNTIME_ENV_FILE" <<EOF
CELAR_RUNTIME_TOKEN_FILE=$RUNTIME_TOKEN_FILE
CELAR_MAINTENANCE_TOKEN_FILE=$MAINTENANCE_TOKEN_FILE
CELAR_MAINTENANCE_DESIRED_FILE=$GATEWAY_STATE_DIR/maintenance-desired.json
CELAR_MAINTENANCE_POLICY_STATUS_FILE=$GATEWAY_STATE_DIR/maintenance-policy-status.json
CELAR_UPDATE_STATUS_FILE=$GATEWAY_STATE_DIR/update-status.json
CELAR_MAINTENANCE_ENABLED=$MAINTENANCE_ENABLED
CELAR_MAINTENANCE_CADENCE=weekly
CELAR_MAINTENANCE_DAY_OF_WEEK=$MAINTENANCE_DAY
CELAR_MAINTENANCE_LOCAL_TIME=$MAINTENANCE_LOCAL_TIME
CELAR_MAINTENANCE_TIME_ZONE=$MAINTENANCE_TIME_ZONE
CELAR_MAINTENANCE_SYSTEMD_ON_CALENDAR=$MAINTENANCE_ON_CALENDAR
CELAR_GATEWAY_VERSION=$GATEWAY_VERSION
CELAR_GENERATION_MODEL=$GENERATION_MODEL
CELAR_REASONING_MODEL=$REASONING_MODEL
CELAR_FAST_GENERAL_MODEL=$FAST_GENERAL_MODEL
CELAR_LOCAL_GENERATION_MODELS=$LOCAL_GENERATION_MODELS
CELAR_STRUCTURED_GENERATION_ORDER=$STRUCTURED_GENERATION_ORDER
CELAR_STRUCTURED_MODEL_ATTEMPT_SECONDS=$STRUCTURED_MODEL_ATTEMPT_SECONDS
CELAR_GENERAL_GENERATION_ORDER=$GENERAL_GENERATION_ORDER
CELAR_GENERAL_MODEL_ATTEMPT_SECONDS=$GENERAL_MODEL_ATTEMPT_SECONDS
CELAR_EMBEDDING_MODEL=$EMBEDDING_MODEL
CELAR_EMBEDDING_DIMENSION=$EMBEDDING_DIMENSION
CELAR_OCR_MODEL=$OCR_MODEL
CELAR_OLLAMA_BASE_URL=http://$OLLAMA_HOST_VALUE
CELAR_CLAMAV_HOST=$CLAMAV_HOST
CELAR_CLAMAV_PORT=$CLAMAV_PORT
CELAR_MAX_UPLOAD_BYTES=$MAX_UPLOAD_BYTES
CELAR_MAX_JSON_REQUEST_BYTES=$MAX_JSON_REQUEST_BYTES
CELAR_MAX_GATEWAY_RESPONSE_BYTES=$MAX_GATEWAY_RESPONSE_BYTES
CELAR_MAX_OCR_PAGES=$MAX_OCR_PAGES
CELAR_MAX_OCR_IMAGE_PIXELS=$MAX_OCR_IMAGE_PIXELS
CELAR_MAX_OCR_IMAGE_EDGE=$MAX_OCR_IMAGE_EDGE
CELAR_PDF_RASTER_MAX_EDGE=$PDF_RASTER_MAX_EDGE
CELAR_OCR_TOTAL_TIMEOUT_SECONDS=$OCR_TOTAL_TIMEOUT_SECONDS
CELAR_CHAT_TIMEOUT_SECONDS=$CHAT_TIMEOUT_SECONDS
CELAR_EMBED_TIMEOUT_SECONDS=$EMBEDDING_TIMEOUT_SECONDS
EOF
chown root:celar-ai "$RUNTIME_ENV_FILE"
chmod 0640 "$RUNTIME_ENV_FILE"

install -m 0555 "$ROOT/gateway/gateway.py" "$GATEWAY_ROOT/gateway.py"
install -m 0555 "$ROOT/gateway/wsgi.py" "$GATEWAY_ROOT/wsgi.py"
install -m 0555 "$ROOT/gateway/maintenance_gateway.py" "$GATEWAY_ROOT/maintenance_gateway.py"
python3 -m py_compile "$GATEWAY_ROOT/gateway.py" "$GATEWAY_ROOT/wsgi.py" "$GATEWAY_ROOT/maintenance_gateway.py"

if [[ -s "$CADDYFILE" && ! -e /etc/caddy/Caddyfile.pre-celar-gitops ]]; then
  cp -a "$CADDYFILE" /etc/caddy/Caddyfile.pre-celar-gitops
fi
install -m 0644 "$ROOT/caddy/Caddyfile" "$CADDYFILE"
caddy validate --config "$CADDYFILE" --adapter caddyfile >/dev/null

install -m 0755 \
  "$ROOT/gitops.sh" \
  "$ROOT/health-check.sh" \
  "$ROOT/backup.sh" \
  "$ROOT/restore.sh" \
  "$ROOT/ollama-update.sh" \
  "$ROOT/maintenance-reconcile.sh" \
  "$INSTALL_ROOT/"
install -m 0644 "$MANIFEST" "$INSTALL_ROOT/release.json"
install -m 0644 "$ROOT/backup.env.example" "$INSTALL_ROOT/backup.env.example"
install -m 0644 "$ROOT/systemd/"*.service "$ROOT/systemd/"*.timer /etc/systemd/system/

systemctl daemon-reload
systemctl enable celar-ai-gateway.service celar-maintenance-gateway.service >/dev/null
systemctl restart celar-ai-gateway.service celar-maintenance-gateway.service
for port in 8787 8788; do
  READY=false
  for attempt in $(seq 1 30); do
    if ss -lnt | awk '{print $4}' | grep -Fxq "127.0.0.1:$port"; then
      READY=true
      break
    fi
    sleep 2
  done
  [[ "$READY" == true ]] || fail "Celar localhost gateway did not bind to 127.0.0.1:$port."
done
systemctl enable caddy.service >/dev/null
systemctl restart caddy.service

# The root reconciler owns whether/when the model-update timer is active. This
# preserves an administrator-selected schedule across later GitOps deployments
# instead of blindly re-enabling the canonical default on every deployment.
systemctl enable --now celar-backup.timer celar-maintenance-reconcile.timer >/dev/null
systemctl start celar-maintenance-reconcile.service

# The GitOps timer must remain enable-only here; bootstrap starts it only after
# recording the applied tree, preventing a second deployment from racing the
# first fresh bootstrap.
systemctl enable celar-gitops.timer >/dev/null

"$INSTALL_ROOT/health-check.sh"

echo 'CELAR_ORACLE_DESIRED_STATE=APPLIED'
echo "CELAR_RUNTIME_TOKEN_FILE=$RUNTIME_TOKEN_FILE"
echo 'CELAR_RUNTIME_TOKEN_VALUE=REDACTED'
echo "CELAR_MAINTENANCE_TOKEN_FILE=$MAINTENANCE_TOKEN_FILE"
echo 'CELAR_MAINTENANCE_TOKEN_VALUE=REDACTED'
