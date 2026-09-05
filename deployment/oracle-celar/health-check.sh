#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
MANIFEST="$ROOT/release.json"
[[ -s "$MANIFEST" ]] || MANIFEST='/opt/celar-ai/deploy/release.json'
TOKEN_FILE='/etc/celar-ai/gateway/runtime-token'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

command -v jq >/dev/null 2>&1 || fail 'jq is required.'
HOSTNAME_VALUE="$(jq -r '.hostname' "$MANIFEST")"
GATEWAY_VERSION="$(jq -r '.gatewayVersion' "$MANIFEST")"
GENERATION_MODEL="$(jq -r '.generationModel' "$MANIFEST")"
EMBEDDING_MODEL="$(jq -r '.embeddingModel' "$MANIFEST")"
EMBEDDING_DIMENSION="$(jq -r '.embeddingDimension' "$MANIFEST")"
OCR_MODEL="$(jq -r '.ocrModel' "$MANIFEST")"
CLAMAV_HOST="$(jq -r '.clamavHost' "$MANIFEST")"
CLAMAV_PORT="$(jq -r '.clamavPort' "$MANIFEST")"

[[ "$(uname -m)" == aarch64 ]] || fail 'Architecture is not aarch64.'
[[ "$HOSTNAME_VALUE" == celarai.onenecklab.com ]] || fail 'Unexpected governed hostname.'
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

# The private services must never acquire wildcard/public listeners.
SOCKETS="$(ss -lntH)"
grep -Eq '127\.0\.0\.1:11434([[:space:]]|$)' <<<"$SOCKETS" || fail 'Ollama localhost listener is missing.'
grep -Eq '127\.0\.0\.1:3310([[:space:]]|$)' <<<"$SOCKETS" || fail 'ClamAV localhost listener is missing.'
grep -Eq '127\.0\.0\.1:8787([[:space:]]|$)' <<<"$SOCKETS" || fail 'Celar gateway localhost listener is missing.'
! grep -Eq '(0\.0\.0\.0|\[::\]|\*):11434([[:space:]]|$)' <<<"$SOCKETS" || fail 'Ollama is publicly bound.'
! grep -Eq '(0\.0\.0\.0|\[::\]|\*):3310([[:space:]]|$)' <<<"$SOCKETS" || fail 'ClamAV is publicly bound.'
! grep -Eq '(0\.0\.0\.0|\[::\]|\*):8787([[:space:]]|$)' <<<"$SOCKETS" || fail 'Celar gateway is publicly bound.'

systemctl is-active --quiet celar-ai-gateway.service || fail 'Celar gateway service is not active.'
systemctl is-active --quiet caddy.service || fail 'Caddy service is not active.'
[[ -s "$TOKEN_FILE" ]] || fail 'Runtime token file is missing.'
RUNTIME_TOKEN="$(tr -d '\r\n' < "$TOKEN_FILE")"
[[ ${#RUNTIME_TOKEN} -ge 32 ]] || fail 'Runtime token is too short.'
AUTH=(
  -H "Authorization: Bearer $RUNTIME_TOKEN"
  -H 'X-Pulse-AI-Privacy-Boundary: private_pulse_runtime_only'
)
RESOLVE=(--resolve "$HOSTNAME_VALUE:443:127.0.0.1")
BASE="https://$HOSTNAME_VALUE"
TMP="$(mktemp -d)"
trap 'unset RUNTIME_TOKEN; rm -rf "$TMP"' EXIT

# Wait for Caddy automatic HTTPS/TLS-ALPN issuance on a freshly rebuilt VM.
TLS_READY=false
for attempt in $(seq 1 60); do
  STATUS="$(curl -sS --max-time 10 "${RESOLVE[@]}" -o /dev/null -w '%{http_code}' "$BASE/health" 2>/dev/null || true)"
  if [[ "$STATUS" == 401 ]]; then
    TLS_READY=true
    break
  fi
  sleep 5
done
[[ "$TLS_READY" == true ]] || fail 'Caddy HTTPS did not become ready with unauthenticated HTTP 401.'

WRONG_STATUS="$(curl -sS --max-time 15 "${RESOLVE[@]}" -o /dev/null -w '%{http_code}' \
  -H 'Authorization: Bearer intentionally-wrong-celar-health-token-value' \
  -H 'X-Pulse-AI-Privacy-Boundary: private_pulse_runtime_only' "$BASE/health" || true)"
[[ "$WRONG_STATUS" == 401 ]] || fail "Incorrect-token readiness must return 401, got $WRONG_STATUS."
BOUNDARY_STATUS="$(curl -sS --max-time 15 "${RESOLVE[@]}" -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $RUNTIME_TOKEN" "$BASE/health" || true)"
[[ "$BOUNDARY_STATUS" == 403 ]] || fail "Missing privacy boundary must return 403, got $BOUNDARY_STATUS."

curl -fsS --max-time 30 "${RESOLVE[@]}" "${AUTH[@]}" "$BASE/health" > "$TMP/health.json"
jq -e \
  --arg gateway "$GATEWAY_VERSION" \
  --arg generation "$GENERATION_MODEL" \
  --arg embedding "$EMBEDDING_MODEL" \
  --arg ocr "$OCR_MODEL" \
  --argjson dimension "$EMBEDDING_DIMENSION" '
    .status == "ready" and
    .gatewayVersion == $gateway and
    .ollamaReady == true and
    .generationModelReady == true and
    .embeddingModelReady == true and
    .tesseractReady == true and
    .clamavReady == true and
    .generationModel == $generation and
    .embeddingModel == $embedding and
    .embeddingDimension == $dimension and
    .ocrModel == $ocr and
    .rawDocumentContentLogged == false and
    .trainingEnabled == false and
    .externalEscalationEnabled == false
  ' "$TMP/health.json" >/dev/null || fail 'Authenticated health contract failed.'

curl -fsS --max-time 840 "${RESOLVE[@]}" "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg model "$GENERATION_MODEL" '{model:$model,messages:[{role:"user",content:"Return only: CELAR ORACLE OK"}],stream:false,temperature:0,max_tokens:32}')" \
  "$BASE/v1/chat/completions" > "$TMP/chat.json"
jq -e '.choices[0].message.content | strings | length > 0' "$TMP/chat.json" >/dev/null || fail 'Authenticated chat-completion gateway probe failed.'

curl -fsS --max-time 240 "${RESOLVE[@]}" "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg model "$EMBEDDING_MODEL" '{model:$model,input:["Celar AI embedding health proof"],encoding_format:"float"}')" \
  "$BASE/v1/embeddings" > "$TMP/embed.json"
jq -e --argjson dimension "$EMBEDDING_DIMENSION" \
  '.data | length == 1 and (.data[0].embedding | length) == $dimension' \
  "$TMP/embed.json" >/dev/null || fail 'Authenticated embedding gateway probe failed.'

printf 'Celar AI protected Test clean-file validation.\n' > "$TMP/clean.txt"
CLEAN_SIZE="$(stat -c '%s' "$TMP/clean.txt")"
curl -fsS --max-time 90 "${RESOLVE[@]}" "${AUTH[@]}" \
  -F "file=@$TMP/clean.txt;filename=celar-clean.txt" \
  "$BASE/v1/scan" > "$TMP/scan.json"
jq -e --argjson size "$CLEAN_SIZE" \
  '.status == "clean" and .clean == true and .infected == false and .scanner == "clamav" and .sizeBytes == $size' \
  "$TMP/scan.json" >/dev/null || fail 'Authenticated clean-file malware gateway probe failed.'

# Produce a deterministic, non-sensitive image fixture locally and prove the
# Tesseract multipart adapter through the same authenticated HTTPS boundary.
python3 - "$TMP/ocr.png" <<'PY'
from PIL import Image, ImageDraw, ImageFont
import sys
image = Image.new('RGB', (1000, 180), 'white')
draw = ImageDraw.Draw(image)
font = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf', 64)
draw.text((35, 45), 'CELAR OCR OK', fill='black', font=font)
image.save(sys.argv[1], 'PNG')
PY
curl -fsS --max-time 300 "${RESOLVE[@]}" "${AUTH[@]}" \
  -F "file=@$TMP/ocr.png;filename=celar-ocr-health.png" \
  -F "model=$OCR_MODEL" \
  -F 'documentId=00000000-0000-0000-0000-000000000001' \
  -F 'documentCategory=health-check' \
  "$BASE/v1/extract" > "$TMP/ocr.json"
jq -e '([.pages[].text] | join(" ") | ascii_upcase) as $text | ($text | contains("CELAR")) and ($text | contains("OCR"))' \
  "$TMP/ocr.json" >/dev/null || fail 'Authenticated OCR gateway probe failed.'

ROOT_FREE_KB="$(df -Pk / | awk 'NR==2 {print $4}')"
[[ "$ROOT_FREE_KB" =~ ^[0-9]+$ && "$ROOT_FREE_KB" -ge 8388608 ]] || fail 'Less than 8 GiB free space remains on root.'
MEM_AVAILABLE_KB="$(awk '/MemAvailable:/ {print $2}' /proc/meminfo)"
[[ "$MEM_AVAILABLE_KB" =~ ^[0-9]+$ && "$MEM_AVAILABLE_KB" -ge 1048576 ]] || fail 'Less than 1 GiB available memory remains.'

unset RUNTIME_TOKEN

echo 'CELAR_ORACLE_HEALTH=PASS'
echo 'PUBLIC_HTTPS_AUTH_BOUNDARY=PASS'
echo 'CHAT_GATEWAY=PASS'
echo "EMBEDDING_GATEWAY=PASS:$EMBEDDING_DIMENSION"
echo 'MALWARE_GATEWAY=PASS'
echo 'OCR_GATEWAY=PASS'
echo "TESSERACT_VERSION=$TESSERACT_VERSION"
echo "CLAMAV_TCP=$CLAM_PING"
echo "GENERATION_MODEL=$GENERATION_MODEL"
echo "EMBEDDING_MODEL=$EMBEDDING_MODEL"
echo "OLLAMA_VERSION=$(jq -r '.version' <<<"$OLLAMA_VERSION_JSON")"
