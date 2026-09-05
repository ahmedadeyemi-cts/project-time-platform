#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
MANIFEST="$ROOT/release.json"
[[ -s "$MANIFEST" ]] || MANIFEST='/opt/celar-ai/deploy/release.json'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

command -v jq >/dev/null 2>&1 || fail 'jq is required.'
GENERATION_MODEL="$(jq -r '.generationModel' "$MANIFEST")"
EMBEDDING_MODEL="$(jq -r '.embeddingModel' "$MANIFEST")"
CLAMAV_HOST="$(jq -r '.clamavHost' "$MANIFEST")"
CLAMAV_PORT="$(jq -r '.clamavPort' "$MANIFEST")"

[[ "$(uname -m)" == aarch64 ]] || fail 'Architecture is not aarch64.'
[[ -s /etc/iptables/rules.v4 ]] || fail 'Oracle firewall rules are missing.'
grep -q -- '--dport 22' /etc/iptables/rules.v4 || fail 'SSH firewall rule is missing.'
grep -q -- '--dport 443' /etc/iptables/rules.v4 || fail 'HTTPS firewall rule is missing.'

TESSERACT_VERSION="$(tesseract --version 2>/dev/null | head -1 || true)"
[[ "$TESSERACT_VERSION" == tesseract\ 5.* ]] || fail "Tesseract 5 is not ready: $TESSERACT_VERSION"
tesseract --list-langs 2>/dev/null | grep -Fxq eng || fail 'Tesseract English language data is missing.'
command -v pdftoppm >/dev/null 2>&1 || fail 'Poppler pdftoppm is missing.'

systemctl is-active --quiet clamav-daemon || fail 'clamav-daemon is not active.'
systemctl is-active --quiet clamav-freshclam || fail 'clamav-freshclam is not active.'
CLAM_PING="$(printf 'zPING\0' | nc -N -w 5 "$CLAMAV_HOST" "$CLAMAV_PORT" 2>/dev/null | tr -d '\0\r\n' || true)"
[[ "$CLAM_PING" == PONG ]] || fail "ClamAV TCP health failed: ${CLAM_PING:-no-response}"

systemctl is-active --quiet ollama.service || fail 'Ollama is not active.'
OLLAMA_VERSION_JSON="$(curl -fsS --max-time 5 http://127.0.0.1:11434/api/version)" || fail 'Ollama version endpoint failed.'
jq -e '.version | strings | length > 0' <<<"$OLLAMA_VERSION_JSON" >/dev/null || fail 'Ollama version response is invalid.'

MODEL_LIST="$(ollama list 2>/dev/null || true)"
grep -Fq "$GENERATION_MODEL" <<<"$MODEL_LIST" || fail "Generation model is missing: $GENERATION_MODEL"
grep -Fq "$EMBEDDING_MODEL" <<<"$MODEL_LIST" || fail "Embedding model is missing: $EMBEDDING_MODEL"

GENERATION_RESULT="$(curl -fsS --max-time 120 http://127.0.0.1:11434/api/generate \
  -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg model "$GENERATION_MODEL" '{model:$model,prompt:"Reply with OK.",stream:false,options:{num_predict:8}}')")" || fail 'Ollama generation probe failed.'
jq -e '.response | strings | length > 0' <<<"$GENERATION_RESULT" >/dev/null || fail 'Ollama generation response is empty.'

EMBED_RESULT="$(curl -fsS --max-time 120 http://127.0.0.1:11434/api/embed \
  -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg model "$EMBEDDING_MODEL" '{model:$model,input:"Celar AI health check"}')")" || fail 'Ollama embedding probe failed.'
jq -e '.embeddings[0] | arrays | length > 0' <<<"$EMBED_RESULT" >/dev/null || fail 'Ollama embedding response is empty.'

ROOT_FREE_KB="$(df -Pk / | awk 'NR==2 {print $4}')"
[[ "$ROOT_FREE_KB" =~ ^[0-9]+$ && "$ROOT_FREE_KB" -ge 8388608 ]] || fail 'Less than 8 GiB free space remains on root.'

MEM_AVAILABLE_KB="$(awk '/MemAvailable:/ {print $2}' /proc/meminfo)"
[[ "$MEM_AVAILABLE_KB" =~ ^[0-9]+$ && "$MEM_AVAILABLE_KB" -ge 1048576 ]] || fail 'Less than 1 GiB available memory remains.'

echo "CELAR_ORACLE_HEALTH=PASS"
echo "TESSERACT_VERSION=$TESSERACT_VERSION"
echo "CLAMAV_TCP=$CLAM_PING"
echo "GENERATION_MODEL=$GENERATION_MODEL"
echo "EMBEDDING_MODEL=$EMBEDDING_MODEL"
echo "OLLAMA_VERSION=$(jq -r '.version' <<<"$OLLAMA_VERSION_JSON")"
