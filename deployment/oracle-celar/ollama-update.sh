#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
MANIFEST="$ROOT/release.json"
[[ -s "$MANIFEST" ]] || MANIFEST='/opt/celar-ai/deploy/release.json'
HEALTH_CHECK="$ROOT/health-check.sh"
[[ -x "$HEALTH_CHECK" ]] || HEALTH_CHECK='/opt/celar-ai/deploy/health-check.sh'
ROLLBACK_ROOT='/var/lib/celar-ai/ollama-rollback'
STATUS_FILE='/var/lib/celar-ai/gateway/update-status.json'
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
ATTEMPT_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
PREVIOUS_SUCCESS="$(jq -r '.lastSuccessfulUpdateAt // empty' "$STATUS_FILE" 2>/dev/null || true)"
PREVIOUS_FAILURE="$(jq -r '.lastFailedUpdateAt // empty' "$STATUS_FILE" 2>/dev/null || true)"
INSTALLER="$(mktemp)"
OLLAMA_COMMAND="$(command -v ollama)"
OLD_BINARY="$(readlink -f -- "$OLLAMA_COMMAND")"
[[ -n "$OLD_BINARY" && -f "$OLD_BINARY" && -x "$OLD_BINARY" ]] || fail 'Could not resolve the installed Ollama executable.'
OLD_BINARY_MODE="$(stat -Lc '%a' -- "$OLD_BINARY")"
OLD_VERSION="$(ollama --version 2>/dev/null | head -1 | tr -d '\r' || true)"
ORIGINAL_COMMAND_WAS_SYMLINK=false
[[ -L "$OLLAMA_COMMAND" ]] && ORIGINAL_COMMAND_WAS_SYMLINK=true
BACKUP_BINARY="$ROLLBACK_ROOT/ollama-$STAMP"
declare -A ROLLBACK_ALIAS=()

write_update_status() {
  local result="$1"
  local success_at="$2"
  local failure_at="$3"
  local rollback_performed="$4"
  local rollback_available="$5"
  local completed_at="$6"
  local current_version="$7"
  local tmp
  install -d -o celar-ai -g celar-ai -m 0750 /var/lib/celar-ai/gateway
  tmp="$(mktemp /var/lib/celar-ai/gateway/update-status.XXXXXX)"
  jq -nc \
    --arg attemptAt "$ATTEMPT_AT" \
    --arg successAt "$success_at" \
    --arg failureAt "$failure_at" \
    --arg result "$result" \
    --arg previousVersion "$OLD_VERSION" \
    --arg currentVersion "$current_version" \
    --arg completedAt "$completed_at" \
    --argjson rollbackPerformed "$rollback_performed" \
    --argjson rollbackAvailable "$rollback_available" \
    '{schema:1,lastAttemptAt:$attemptAt,lastSuccessfulUpdateAt:(if $successAt == "" then null else $successAt end),lastFailedUpdateAt:(if $failureAt == "" then null else $failureAt end),lastResult:$result,rollbackPerformed:$rollbackPerformed,rollbackAvailable:$rollbackAvailable,previousEngineVersion:$previousVersion,currentEngineVersion:(if $currentVersion == "" then null else $currentVersion end),completedAt:(if $completedAt == "" then null else $completedAt end),rawErrorReturned:false}' \
    > "$tmp"
  chown celar-ai:celar-ai "$tmp"
  chmod 0640 "$tmp"
  mv -f "$tmp" "$STATUS_FILE"
}

write_update_status running "$PREVIOUS_SUCCESS" "$PREVIOUS_FAILURE" false false '' "$OLD_VERSION"

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
write_update_status running "$PREVIOUS_SUCCESS" "$PREVIOUS_FAILURE" false true '' "$OLD_VERSION"

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
    systemctl restart celar-ai-gateway.service celar-maintenance-gateway.service >/dev/null 2>&1 || true
    FAILED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    RESTORED_VERSION="$(ollama --version 2>/dev/null | head -1 | tr -d '\r' || true)"
    write_update_status rolled_back "$PREVIOUS_SUCCESS" "$FAILED_AT" true true "$FAILED_AT" "$RESTORED_VERSION" || true
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

systemctl restart celar-ai-gateway.service celar-maintenance-gateway.service
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

COMPLETED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
NEW_VERSION="$(ollama --version 2>/dev/null | head -1 | tr -d '\r' || true)"
write_update_status success "$COMPLETED_AT" "$PREVIOUS_FAILURE" false true "$COMPLETED_AT" "$NEW_VERSION"

rm -f "$INSTALLER"
trap - EXIT INT TERM

echo 'CELAR_OLLAMA_UPDATE=PASS'
printf 'CELAR_LOCAL_MODELS_UPDATED=%s\n' "${GENERATION_MODELS[*]} $EMBEDDING_MODEL"
ollama --version
