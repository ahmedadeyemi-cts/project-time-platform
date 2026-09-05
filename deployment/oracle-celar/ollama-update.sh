#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
MANIFEST="$ROOT/release.json"
[[ -s "$MANIFEST" ]] || MANIFEST='/opt/celar-ai/deploy/release.json'
ROLLBACK_ROOT='/var/lib/celar-ai/ollama-rollback'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ "$(id -u)" -eq 0 ]] || fail 'ollama-update.sh requires root.'
command -v jq >/dev/null 2>&1 || fail 'jq is required.'
command -v ollama >/dev/null 2>&1 || fail 'Ollama is not installed.'

GENERATION_MODEL="$(jq -r '.generationModel' "$MANIFEST")"
EMBEDDING_MODEL="$(jq -r '.embeddingModel' "$MANIFEST")"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
INSTALLER="$(mktemp)"
OLD_BINARY="$(command -v ollama)"
BACKUP_BINARY="$ROLLBACK_ROOT/ollama-$STAMP"
GEN_ALIAS="${GENERATION_MODEL}-rollback-$STAMP"
EMB_ALIAS="${EMBEDDING_MODEL}-rollback-$STAMP"

install -d -m 0700 "$ROLLBACK_ROOT"
cp -a "$OLD_BINARY" "$BACKUP_BINARY"
chmod 0700 "$BACKUP_BINARY"

have_model() {
  ollama list 2>/dev/null | awk 'NR>1 {print $1}' | grep -Fxq "$1"
}

if have_model "$GENERATION_MODEL"; then
  ollama cp "$GENERATION_MODEL" "$GEN_ALIAS"
fi
if have_model "$EMBEDDING_MODEL"; then
  ollama cp "$EMBEDDING_MODEL" "$EMB_ALIAS"
fi

rollback() {
  local status=$?
  trap - EXIT INT TERM
  if [[ "$status" -ne 0 ]]; then
    echo 'Ollama update validation failed; restoring the previous engine and model aliases.' >&2
    systemctl stop ollama.service || true
    cp -a "$BACKUP_BINARY" "$OLD_BINARY" || true
    systemctl start ollama.service || true
    sleep 3
    if have_model "$GEN_ALIAS"; then
      ollama rm "$GENERATION_MODEL" >/dev/null 2>&1 || true
      ollama cp "$GEN_ALIAS" "$GENERATION_MODEL" || true
    fi
    if have_model "$EMB_ALIAS"; then
      ollama rm "$EMBEDDING_MODEL" >/dev/null 2>&1 || true
      ollama cp "$EMB_ALIAS" "$EMBEDDING_MODEL" || true
    fi
  fi
  rm -f "$INSTALLER"
  exit "$status"
}
trap rollback EXIT INT TERM

curl --fail --silent --show-error --location https://ollama.com/install.sh --output "$INSTALLER"
test -s "$INSTALLER" || fail 'Ollama installer download was empty.'
sh "$INSTALLER"
systemctl daemon-reload
systemctl restart ollama.service

for attempt in $(seq 1 30); do
  if curl -fsS --max-time 3 http://127.0.0.1:11434/api/version >/dev/null; then
    break
  fi
  (( attempt < 30 )) || fail 'Updated Ollama did not become ready.'
  sleep 2
done

ollama pull "$GENERATION_MODEL"
ollama pull "$EMBEDDING_MODEL"

GENERATION_RESULT="$(curl -fsS --max-time 120 http://127.0.0.1:11434/api/generate \
  -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg model "$GENERATION_MODEL" '{model:$model,prompt:"Reply with OK.",stream:false,options:{num_predict:8}}')")"
jq -e '.response | strings | length > 0' <<<"$GENERATION_RESULT" >/dev/null

EMBED_RESULT="$(curl -fsS --max-time 120 http://127.0.0.1:11434/api/embed \
  -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg model "$EMBEDDING_MODEL" '{model:$model,input:"Celar AI update validation"}')")"
jq -e '.embeddings[0] | arrays | length > 0' <<<"$EMBED_RESULT" >/dev/null

# Keep only the two newest rollback aliases per approved model.
prune_aliases() {
  local prefix="$1"
  mapfile -t aliases < <(ollama list 2>/dev/null | awk 'NR>1 {print $1}' | grep -F "${prefix}-rollback-" | sort -r || true)
  if (( ${#aliases[@]} > 2 )); then
    for alias in "${aliases[@]:2}"; do
      ollama rm "$alias" >/dev/null 2>&1 || true
    done
  fi
}
prune_aliases "$GENERATION_MODEL"
prune_aliases "$EMBEDDING_MODEL"

find "$ROLLBACK_ROOT" -maxdepth 1 -type f -name 'ollama-*' -mtime +30 -delete

rm -f "$INSTALLER"
trap - EXIT INT TERM

echo "CELAR_OLLAMA_UPDATE=PASS"
ollama --version
