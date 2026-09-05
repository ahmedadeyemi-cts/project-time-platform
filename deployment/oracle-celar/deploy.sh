#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
MANIFEST="$ROOT/release.json"
INSTALL_ROOT='/opt/celar-ai/deploy'
STATE_DIR='/var/lib/celar-ai'
FIREWALL_RULES='/etc/iptables/rules.v4'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ "$(id -u)" -eq 0 ]] || fail 'Run deploy.sh as root.'
[[ -s "$MANIFEST" ]] || fail 'release.json is missing.'
command -v jq >/dev/null 2>&1 || fail 'jq is required before deployment.'

ARCH="$(jq -r '.architecture' "$MANIFEST")"
GENERATION_MODEL="$(jq -r '.generationModel' "$MANIFEST")"
EMBEDDING_MODEL="$(jq -r '.embeddingModel' "$MANIFEST")"
OLLAMA_HOST_VALUE="$(jq -r '.ollamaHost' "$MANIFEST")"
OLLAMA_KEEP_ALIVE="$(jq -r '.ollamaKeepAlive' "$MANIFEST")"
OLLAMA_MAX_LOADED_MODELS="$(jq -r '.ollamaMaxLoadedModels' "$MANIFEST")"
OLLAMA_NUM_PARALLEL="$(jq -r '.ollamaNumParallel' "$MANIFEST")"
OLLAMA_MAX_QUEUE="$(jq -r '.ollamaMaxQueue' "$MANIFEST")"
CLAMAV_HOST="$(jq -r '.clamavHost' "$MANIFEST")"
CLAMAV_PORT="$(jq -r '.clamavPort' "$MANIFEST")"

[[ "$ARCH" == arm64 && "$(uname -m)" == aarch64 ]] || fail 'The release architecture does not match this host.'
[[ "$GENERATION_MODEL" =~ ^[A-Za-z0-9._:/-]+$ ]] || fail 'Invalid generation model name.'
[[ "$EMBEDDING_MODEL" =~ ^[A-Za-z0-9._:/-]+$ ]] || fail 'Invalid embedding model name.'
[[ "$OLLAMA_HOST_VALUE" == '127.0.0.1:11434' ]] || fail 'Ollama must remain bound to localhost.'
[[ "$CLAMAV_HOST" == '127.0.0.1' && "$CLAMAV_PORT" == 3310 ]] || fail 'ClamAV must remain on localhost:3310.'

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y \
  ca-certificates curl jq git unzip zstd netcat-openbsd rsync \
  python3 python3-venv python3-pip \
  tesseract-ocr tesseract-ocr-eng poppler-utils \
  clamav clamav-daemon clamav-freshclam \
  restic unattended-upgrades

install -d -m 0755 "$INSTALL_ROOT" "$STATE_DIR" /var/backups/celar-ai
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
  if command -v netfilter-persistent >/dev/null 2>&1; then
    netfilter-persistent save >/dev/null
  fi
else
  fail 'Oracle iptables rules.v4 is missing; refusing to replace the host firewall blindly.'
fi

# Configure clamd TCP protocol only on loopback. Retain the distro socket too.
if [[ ! -e /etc/clamav/clamd.conf.pre-celar-gitops ]]; then
  cp -a /etc/clamav/clamd.conf /etc/clamav/clamd.conf.pre-celar-gitops
fi
sed -i '/^TCPSocket[[:space:]]/d;/^TCPAddr[[:space:]]/d' /etc/clamav/clamd.conf
printf '\nTCPSocket %s\nTCPAddr %s\n' "$CLAMAV_PORT" "$CLAMAV_HOST" >> /etc/clamav/clamd.conf
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

systemctl enable --now celar-backup.timer celar-ollama-update.timer >/dev/null
systemctl enable celar-gitops.timer >/dev/null

"$INSTALL_ROOT/health-check.sh"

echo 'CELAR_ORACLE_DESIRED_STATE=APPLIED'
