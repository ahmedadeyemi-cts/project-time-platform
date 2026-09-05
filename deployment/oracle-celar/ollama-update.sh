#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
MANIFEST="$ROOT/release.json"
[[ -s "$MANIFEST" ]] || MANIFEST='/opt/celar-ai/deploy/release.json'
HEALTH_CHECK="$ROOT/health-check.sh"
[[ -x "$HEALTH_CHECK" ]] || HEALTH_CHECK='/opt/celar-ai/deploy/health-check.sh'
ROLLBACK_ROOT='/var/lib/celar-ai/ollama-rollback'
RUNTIME_MUTATION_LOCK='/run/celar-runtime-mutation.lock'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ "$(id -u)" -eq 0 ]] || fail 'ollama-update.sh requires root.'
command -v jq >/dev/null 2>&1 || fail 'jq is required.'
command -v ollama >/dev/null 2>&1 || fail 'Ollama is not installed.'
command -v readlink >/dev/null 2>&1 || fail 'readlink is required.'
[[ -x "$HEALTH_CHECK" ]] || fail 'Celar Oracle health-check.sh is missing.'

LOCK_WAIT_SECONDS="$(jq -r '.runtimeMutationLockWaitSeconds' "$MANIFEST")"
[[ "$LOCK_WAIT_SECONDS" =~ ^[0-9]+$ && "$LOCK_WAIT_SECONDS" -ge 60 ]] || fail 'Invalid runtime mutation lock wait.'
exec 8>"$RUNTIME_MUTATION_LOCK"
flock -w "$LOCK_WAIT_SECONDS" 8 || fail 'Timed out waiting for the Celar runtime mutation lock.'

mapfile -t GENERATION_MODELS < <(jq -r '.localGenerationModels[]' "$MANIFEST")
EMBEDDING_MODEL="$(jq -r '.embeddingModel' "$MANIFEST")"
(( ${#GENERATION_MODELS[@]} >= 3 )) || fail 'The governed local generation portfolio is incomplete.'
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
INSTALLER="$(mktemp)"
OLLAMA_COMMAND="$(command -v ollama)"
OLD_BINARY="$(readlink -f -- "$OLLAMA_COMMAND")"
[[ -n "$OLD_BINARY" && -f "$OLD_BINARY" && -x "$OLD_BINARY" ]] || fail 'Could not resolve the installed Ollama executable.'
OLD_BINARY_MODE="$(stat -Lc '%a' -- "$OLD_BINARY")"
ORIGINAL_COMMAND_WAS_SYMLINK=false
[[ -L "$OLLAMA_COMMAND" ]] && ORIGINAL_COMMAND_WAS_SYMLINK=true
BACKUP_BINARY="$ROLLBACK_ROOT/ollama-$STAMP"
declare -A ROLLBACK_ALIAS=()

[[ "$OLD_BINARY_MODE" =~ ^[0-7]{3,4}$ ]] || fail 'Could not determine the current Ollama executable mode.'
install -d -m 0700 "$ROLLBACK_ROOT"
# Archive the resolved executable bytes, never the launcher symlink. This keeps
# the rollback copy independent from whatever target the updater later replaces.
cp --dereference --preserve=mode,timestamps -- "$OLD_BINARY" "$BACKUP_BINARY"
[[ -f "$BACKUP_BINARY" && ! -L "$BACKUP_BINARY" ]] || fail 'Ollama executable rollback copy is invalid.'
chmod "$OLD_BINARY_MODE" "$BACKUP_BINARY"

resolve_model_name() {
  local wanted="$1"
  ollama list 2>/dev/null | awk -v model="$wanted" '
    NR > 1 && ($1 == model || $1 == model ":latest") { print $1; exit }
  '
}

have_model() {
  [[ -n "$(resolve_model_name "$1")" ]]
}

for model in "${GENERATION_MODELS[@]}" "$EMBEDDING_MODEL"; do
  source_name="$(resolve_model_name "$model")"
  if [[ -n "$source_name" ]]; then
    alias_name="${model}-rollback-$STAMP"
    ollama cp "$source_name" "$alias_name"
    ROLLBACK_ALIAS["$model"]="$alias_name"
  fi
done

rollback() {
  local status=$?
  trap - EXIT INT TERM
  if [[ "$status" -ne 0 ]]; then
    echo 'Ollama/model update validation failed; restoring the previous engine and model aliases.' >&2
    systemctl stop ollama.service || true

    # Restore the exact pre-update executable bytes to the pre-update resolved
    # target. If the launcher was originally a symlink, restore that launcher as
    # an absolute symlink too, because an installer may have repointed/replaced it.
    install -m "$OLD_BINARY_MODE" "$BACKUP_BINARY" "$OLD_BINARY" || true
    if [[ "$ORIGINAL_COMMAND_WAS_SYMLINK" == true ]]; then
      ln -sfnT "$OLD_BINARY" "$OLLAMA_COMMAND" || true
    elif [[ "$OLLAMA_COMMAND" != "$OLD_BINARY" ]]; then
      install -m "$OLD_BINARY_MODE" "$BACKUP_BINARY" "$OLLAMA_COMMAND" || true
    fi

    systemctl start ollama.service || true
    sleep 3
    for model in "${GENERATION_MODELS[@]}" "$EMBEDDING_MODEL"; do
      alias_name="${ROLLBACK_ALIAS[$model]:-}"
      if [[ -n "$alias_name" ]] && have_model "$alias_name"; then
        ollama rm "$model" >/dev/null 2>&1 || true
        ollama cp "$alias_name" "$model" || true
      fi
    done
    systemctl restart celar-ai-gateway.service >/dev/null 2>&1 || true
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
  if curl -fsS --max-time 3 http://127.0.0.1:11434/api/version >/dev/null; then break; fi
  (( attempt < 30 )) || fail 'Updated Ollama did not become ready.'
  sleep 2
done

for model in "${GENERATION_MODELS[@]}" "$EMBEDDING_MODEL"; do
  ollama pull "$model"
done

for model in "${GENERATION_MODELS[@]}"; do
  GENERATION_RESULT="$(curl -fsS --max-time 180 http://127.0.0.1:11434/api/generate \
    -H 'Content-Type: application/json' \
    -d "$(jq -nc --arg model "$model" '{model:$model,prompt:"Reply with OK.",stream:false,options:{num_predict:8}}')")"
  jq -e '.response | strings | length > 0' <<<"$GENERATION_RESULT" >/dev/null || fail "Updated local model failed generation: $model"
done

EMBED_RESULT="$(curl -fsS --max-time 180 http://127.0.0.1:11434/api/embed \
  -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg model "$EMBEDDING_MODEL" '{model:$model,input:"Celar AI update validation"}')")"
jq -e '.embeddings[0] | arrays | length > 0' <<<"$EMBED_RESULT" >/dev/null || fail 'Updated embedding model failed.'

systemctl restart celar-ai-gateway.service
"$HEALTH_CHECK"

prune_aliases() {
  local prefix="$1"
  mapfile -t aliases < <(ollama list 2>/dev/null | awk 'NR>1 {print $1}' | grep -F "${prefix}-rollback-" | sort -r || true)
  if (( ${#aliases[@]} > 2 )); then
    for alias in "${aliases[@]:2}"; do
      ollama rm "$alias" >/dev/null 2>&1 || true
    done
  fi
}
for model in "${GENERATION_MODELS[@]}" "$EMBEDDING_MODEL"; do
  prune_aliases "$model"
done

find "$ROLLBACK_ROOT" -maxdepth 1 -type f -name 'ollama-*' -mtime +30 -delete

rm -f "$INSTALLER"
trap - EXIT INT TERM

echo 'CELAR_OLLAMA_UPDATE=PASS'
printf 'CELAR_LOCAL_MODELS_UPDATED=%s\n' "${GENERATION_MODELS[*]} $EMBEDDING_MODEL"
ollama --version
