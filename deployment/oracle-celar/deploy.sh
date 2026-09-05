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
RUNTIME_ENV_FILE="$GATEWAY_CONFIG_DIR/runtime.env"
FIREWALL_RULES='/etc/iptables/rules.v4'
CADDYFILE='/etc/caddy/Caddyfile'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ "$(id -u)" -eq 0 ]] || fail 'Run deploy.sh as root.'
[[ -s "$MANIFEST" ]] || fail 'release.json is missing.'
command -v jq >/dev/null 2>&1 || fail 'jq is required before deployment.'

ARCH="$(jq -r '.architecture' "$MANIFEST")"
HOSTNAME_VALUE="$(jq -r '.hostname' "$MANIFEST")"
GATEWAY_VERSION="$(jq -r '.gatewayVersion' "$MANIFEST")"
GATEWAY_BIND="$(jq -r '.gatewayBind' "$MANIFEST")"
GENERATION_MODEL="$(jq -r '.generationModel' "$MANIFEST")"
EMBEDDING_MODEL="$(jq -r '.embeddingModel' "$MANIFEST")"
EMBEDDING_DIMENSION="$(jq -r '.embeddingDimension' "$MANIFEST")"
OCR_MODEL="$(jq -r '.ocrModel' "$MANIFEST")"
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
OCR_TOTAL_TIMEOUT_SECONDS="$(jq -r '.ocrTotalTimeoutSeconds' "$MANIFEST")"
CHAT_TIMEOUT_SECONDS="$(jq -r '.chatTimeoutSeconds' "$MANIFEST")"
EMBEDDING_TIMEOUT_SECONDS="$(jq -r '.embeddingTimeoutSeconds' "$MANIFEST")"

[[ "$ARCH" == arm64 && "$(uname -m)" == aarch64 ]] || fail 'The release architecture does not match this host.'
[[ "$HOSTNAME_VALUE" == celarai.onenecklab.com ]] || fail 'The governed Oracle hostname changed unexpectedly.'
[[ "$GATEWAY_BIND" == '127.0.0.1:8787' ]] || fail 'Celar gateway must remain bound to localhost:8787.'
[[ "$GENERATION_MODEL" =~ ^[A-Za-z0-9._:/-]+$ ]] || fail 'Invalid generation model name.'
[[ "$EMBEDDING_MODEL" =~ ^[A-Za-z0-9._:/-]+$ ]] || fail 'Invalid embedding model name.'
[[ "$OCR_MODEL" == tesseract-5-eng ]] || fail 'Unexpected OCR model.'
[[ "$EMBEDDING_DIMENSION" == 768 ]] || fail 'Embedding dimension must remain 768.'
[[ "$OLLAMA_HOST_VALUE" == '127.0.0.1:11434' ]] || fail 'Ollama must remain bound to localhost.'
[[ "$CLAMAV_HOST" == '127.0.0.1' && "$CLAMAV_PORT" == 3310 ]] || fail 'ClamAV must remain on localhost:3310.'
for numeric in "$MAX_UPLOAD_BYTES" "$MAX_JSON_REQUEST_BYTES" "$MAX_GATEWAY_RESPONSE_BYTES" "$MAX_OCR_PAGES" "$OCR_TOTAL_TIMEOUT_SECONDS" "$CHAT_TIMEOUT_SECONDS" "$EMBEDDING_TIMEOUT_SECONDS"; do
  [[ "$numeric" =~ ^[0-9]+$ && "$numeric" -gt 0 ]] || fail 'Invalid positive numeric runtime limit.'
done

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y \
  ca-certificates curl jq git unzip zstd netcat-openbsd rsync openssl \
  python3 python3-venv python3-pip python3-flask python3-requests python3-pil gunicorn fonts-dejavu-core \
  tesseract-ocr tesseract-ocr-eng poppler-utils \
  clamav clamav-daemon clamav-freshclam \
  caddy restic unattended-upgrades iptables-persistent

install -d -m 0755 "$INSTALL_ROOT" "$STATE_DIR" /var/backups/celar-ai "$GATEWAY_ROOT"
install -d -m 0700 /etc/celar-ai

# Preserve Oracle-provided InstanceServices firewall rules and add only HTTPS.
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

# Configure clamd TCP protocol only on loopback. Retain the distro socket too.
if [[ ! -e /etc/clamav/clamd.conf.pre-celar-gitops ]]; then
  cp -a /etc/clamav/clamd.conf /etc/clamav/clamd.conf.pre-celar-gitops
fi
sed -i '/^TCPSocket[[:space:]]/d;/^TCPAddr[[:space:]]/d;/^StreamMaxLength[[:space:]]/d' /etc/clamav/clamd.conf
printf '\nTCPSocket %s\nTCPAddr %s\nStreamMaxLength 50M\n' "$CLAMAV_PORT" "$CLAMAV_HOST" >> /etc/clamav/clamd.conf
systemctl enable clamav-freshclam clamav-daemon >/dev/null
systemctl restart clamav-freshclam
systemctl restart clamav-daemon

# Install Ollama once. Version/model refreshes are handled by celar-ollama-update.timer.
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
systemctl enable --now ollama.service
for attempt in $(seq 1 30); do
  if curl -fsS --max-time 3 http://127.0.0.1:11434/api/version >/dev/null; then
    break
  fi
  (( attempt < 30 )) || fail 'Ollama did not become ready.'
  sleep 2
done

if ! ollama list 2>/dev/null | awk 'NR>1 {print $1}' | grep -Fxq "$GENERATION_MODEL"; then
  ollama pull "$GENERATION_MODEL"
fi
if ! ollama list 2>/dev/null | awk 'NR>1 {print $1}' | grep -Fxq "$EMBEDDING_MODEL"; then
  ollama pull "$EMBEDDING_MODEL"
fi

# Run the gateway with a dedicated non-login identity. The bearer token is
# generated only when missing and is never written to Git or command output.
getent group celar-ai >/dev/null 2>&1 || groupadd --system celar-ai
if ! id celar-ai >/dev/null 2>&1; then
  useradd --system --gid celar-ai --home-dir "$GATEWAY_STATE_DIR" --shell /usr/sbin/nologin celar-ai
fi
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

cat > "$RUNTIME_ENV_FILE" <<EOF
CELAR_RUNTIME_TOKEN_FILE=$RUNTIME_TOKEN_FILE
CELAR_GATEWAY_VERSION=$GATEWAY_VERSION
CELAR_GENERATION_MODEL=$GENERATION_MODEL
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
CELAR_OCR_TOTAL_TIMEOUT_SECONDS=$OCR_TOTAL_TIMEOUT_SECONDS
CELAR_CHAT_TIMEOUT_SECONDS=$CHAT_TIMEOUT_SECONDS
CELAR_EMBED_TIMEOUT_SECONDS=$EMBEDDING_TIMEOUT_SECONDS
EOF
chown root:celar-ai "$RUNTIME_ENV_FILE"
chmod 0640 "$RUNTIME_ENV_FILE"

install -m 0555 "$ROOT/gateway/gateway.py" "$GATEWAY_ROOT/gateway.py"
python3 -m py_compile "$GATEWAY_ROOT/gateway.py"

# Caddy owns the only public application port. Back up any pre-GitOps config,
# validate the reviewed Caddyfile, then let Caddy manage ACME/TLS state.
if [[ -s "$CADDYFILE" && ! -e /etc/caddy/Caddyfile.pre-celar-gitops ]]; then
  cp -a "$CADDYFILE" /etc/caddy/Caddyfile.pre-celar-gitops
fi
install -m 0644 "$ROOT/caddy/Caddyfile" "$CADDYFILE"
caddy validate --config "$CADDYFILE" --adapter caddyfile >/dev/null

# Install canonical deployment scripts and service definitions from this release.
install -m 0755 \
  "$ROOT/gitops.sh" \
  "$ROOT/health-check.sh" \
  "$ROOT/backup.sh" \
  "$ROOT/restore.sh" \
  "$ROOT/ollama-update.sh" \
  "$INSTALL_ROOT/"
install -m 0644 "$MANIFEST" "$INSTALL_ROOT/release.json"
install -m 0644 "$ROOT/backup.env.example" "$INSTALL_ROOT/backup.env.example"
install -m 0644 "$ROOT/systemd/"*.service "$ROOT/systemd/"*.timer /etc/systemd/system/

systemctl daemon-reload
systemctl enable --now celar-ai-gateway.service
for attempt in $(seq 1 30); do
  if ss -lnt | awk '{print $4}' | grep -Fxq '127.0.0.1:8787'; then
    break
  fi
  (( attempt < 30 )) || fail 'Celar gateway did not bind to localhost:8787.'
  sleep 2
done
systemctl enable --now caddy.service

systemctl enable --now celar-backup.timer celar-ollama-update.timer >/dev/null
systemctl enable celar-gitops.timer >/dev/null

"$INSTALL_ROOT/health-check.sh"

echo 'CELAR_ORACLE_DESIRED_STATE=APPLIED'
echo "CELAR_RUNTIME_TOKEN_FILE=$RUNTIME_TOKEN_FILE"
echo 'CELAR_RUNTIME_TOKEN_VALUE=REDACTED'
