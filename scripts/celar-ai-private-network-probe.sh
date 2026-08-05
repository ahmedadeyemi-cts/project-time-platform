#!/usr/bin/env bash
set -Eeuo pipefail

mode="${PROJECTPULSE_CELAR_PROBE_MODE:-dependencies}"
fail() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
json_field() { jq -er "$2" <<<"$1"; }

private_ip() {
  local ip="$1"
  [[ "$ip" =~ ^10\. ]] ||
    [[ "$ip" =~ ^192\.168\. ]] ||
    [[ "$ip" =~ ^172\.([1][6-9]|2[0-9]|3[01])\. ]] ||
    [[ "$ip" =~ ^fc[0-9a-f]{2}:|^fd[0-9a-f]{2}: ]]
}

host_allowlisted() {
  local host="${1,,}" entry
  IFS=',' read -r -a entries <<<"${PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST:-}"
  for entry in "${entries[@]}"; do
    entry="$(sed -E 's/^[[:space:]]+|[[:space:]]+$//g' <<<"${entry,,}")"
    [[ -n "$entry" ]] || continue
    if [[ "$entry" == .* ]]; then
      [[ "$host" == *"$entry" ]] && return 0
    elif [[ "$host" == "$entry" ]]; then
      return 0
    fi
  done
  return 1
}

probe_https_endpoint() {
  local endpoint="$1" model="$2" token="$3" label="$4"
  [[ "$endpoint" == https://* ]] || fail "$label endpoint must use HTTPS."
  local host authority port pinned_ip
  host="$(printf '%s' "$endpoint" | sed -E 's#^https://([^/:]+).*#\1#')"
  [[ -n "$host" && "$host" != "$endpoint" ]] || fail "$label endpoint hostname is invalid."
  host_allowlisted "$host" || fail "$label endpoint hostname is not in the protected private allowlist."
  authority="${endpoint#https://}"
  authority="${authority%%/*}"
  port=443
  [[ "$authority" == *:* ]] && port="${authority##*:}"
  [[ "$port" =~ ^[0-9]{1,5}$ ]] && (( port >= 1 && port <= 65535 )) || fail "$label endpoint port is invalid."
  IFS=$'\n' read -r -d '' -a ips < <(getent ahosts "$host" | awk '{print $1}' | sort -u && printf '\0')
  (( ${#ips[@]} > 0 )) || fail "$label private DNS returned no address."
  for ip in "${ips[@]}"; do private_ip "$ip" || fail "$label DNS returned a public address."; done
  pinned_ip="${ips[0]}"
  [[ "$pinned_ip" == *:* ]] && pinned_ip="[$pinned_ip]"

  body="$(jq -nc --arg model "$model" '{model:$model,messages:[{role:"user",content:"Reply with the single word READY."}],max_tokens:8,temperature:0}')"
  response_file="$(mktemp)"
  status="$(curl --silent --show-error --fail-with-body \
    --proto '=https' --tlsv1.2 --max-time 30 --max-redirs 0 \
    --resolve "$host:$port:$pinned_ip" \
    --output "$response_file" --write-out '%{http_code}' \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $token" \
    --data "$body" "$endpoint" 2>/dev/null)" || fail "$label private TLS/inference probe failed."
  [[ "$status" == 200 ]] || fail "$label returned HTTP $status."
  jq -e '(.choices | type == "array" and length > 0) or (.output | type == "array" and length > 0) or (.data | type == "array" and length > 0)' "$response_file" >/dev/null ||
    fail "$label response is not OpenAI-compatible."
  rm -f "$response_file"
  printf 'CELAR_AI_PRIVATE_DEPENDENCY_%s=VERIFIED\n' "${label^^}"
}

probe_embedding_endpoint() {
  local endpoint="$1" model="$2" token="$3"
  [[ "$endpoint" == https://* ]] || fail "embedding endpoint must use HTTPS."
  local host authority port pinned_ip
  host="$(printf '%s' "$endpoint" | sed -E 's#^https://([^/:]+).*#\1#')"
  [[ -n "$host" && "$host" != "$endpoint" ]] || fail "embedding endpoint hostname is invalid."
  host_allowlisted "$host" || fail "embedding endpoint hostname is not in the protected private allowlist."
  authority="${endpoint#https://}"
  authority="${authority%%/*}"
  port=443
  [[ "$authority" == *:* ]] && port="${authority##*:}"
  [[ "$port" =~ ^[0-9]{1,5}$ ]] && (( port >= 1 && port <= 65535 )) || fail "embedding endpoint port is invalid."
  IFS=$'\n' read -r -d '' -a ips < <(getent ahosts "$host" | awk '{print $1}' | sort -u && printf '\0')
  (( ${#ips[@]} > 0 )) || fail "embedding private DNS returned no address."
  for ip in "${ips[@]}"; do private_ip "$ip" || fail "embedding DNS returned a public address."; done
  pinned_ip="${ips[0]}"
  [[ "$pinned_ip" == *:* ]] && pinned_ip="[$pinned_ip]"

  # This payload intentionally matches PulseAiPrivateEmbeddingClient rather
  # than sending a chat-completions body to an embeddings deployment.
  body="$(jq -nc --arg model "$model" '{model:$model,input:["Celar AI private embedding activation probe."],encoding_format:"float"}')"
  response_file="$(mktemp)"
  status="$(curl --silent --show-error --fail-with-body \
    --proto '=https' --tlsv1.2 --max-time 30 --max-redirs 0 \
    --resolve "$host:$port:$pinned_ip" \
    --output "$response_file" --write-out '%{http_code}' \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $token" \
    -H 'X-Pulse-AI-Privacy-Boundary: private-enterprise-only' \
    -H 'X-Pulse-AI-Feature: private_document_embedding' \
    --data "$body" "$endpoint" 2>/dev/null)" || fail "embedding private TLS/model probe failed."
  [[ "$status" == 200 ]] || fail "embedding returned HTTP $status."
  jq -e '
    (.data | type == "array" and length == 1)
    and (.data[0].embedding | type == "array" and length > 0)
    and ([.data[0].embedding[] | select(type != "number")] | length == 0)
  ' "$response_file" >/dev/null || fail "embedding response did not contain one non-empty numeric vector."
  rm -f "$response_file"
  printf 'CELAR_AI_PRIVATE_DEPENDENCY_EMBEDDING=VERIFIED\n'
}

probe_private_transport() {
  local endpoint="$1" token="$2" label="$3"
  [[ "$endpoint" == https://* ]] || fail "$label endpoint must use HTTPS."
  local host authority port pinned_ip status
  host="$(printf '%s' "$endpoint" | sed -E 's#^https://([^/:]+).*#\1#')"
  [[ -n "$host" && "$host" != "$endpoint" ]] || fail "$label endpoint hostname is invalid."
  host_allowlisted "$host" || fail "$label endpoint hostname is not in the protected private allowlist."
  authority="${endpoint#https://}"
  authority="${authority%%/*}"
  port=443
  [[ "$authority" == *:* ]] && port="${authority##*:}"
  [[ "$port" =~ ^[0-9]{1,5}$ ]] && (( port >= 1 && port <= 65535 )) || fail "$label endpoint port is invalid."
  IFS=$'\n' read -r -d '' -a ips < <(getent ahosts "$host" | awk '{print $1}' | sort -u && printf '\0')
  (( ${#ips[@]} > 0 )) || fail "$label private DNS returned no address."
  for ip in "${ips[@]}"; do private_ip "$ip" || fail "$label DNS returned a public address."; done
  pinned_ip="${ips[0]}"
  [[ "$pinned_ip" == *:* ]] && pinned_ip="[$pinned_ip]"
  status="$(curl --silent --show-error --proto '=https' --tlsv1.2 --max-time 20 --max-redirs 0 \
    --resolve "$host:$port:$pinned_ip" \
    --request OPTIONS --output /dev/null --write-out '%{http_code}' \
    -H "Authorization: Bearer $token" "$endpoint" 2>/dev/null)" || fail "$label private TLS probe failed."
  [[ "$status" != 000 && "$status" != 3* ]] || fail "$label endpoint redirected or returned no HTTP response."
  printf 'CELAR_AI_PRIVATE_DEPENDENCY_%s=VERIFIED\n' "${label^^}"
}

case "$mode" in
  dependencies)
    : "${PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT:?missing inference endpoint}"
    : "${PROJECTPULSE_PRIVATE_INFERENCE_MODEL:?missing inference model}"
    : "${PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN:?missing inference token}"
    probe_https_endpoint "$PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT" \
      "$PROJECTPULSE_PRIVATE_INFERENCE_MODEL" "$PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN" inference
    clam_host="${PROJECTPULSE_CLAMAV_HOST:?missing private ClamAV host}"
    clam_port="${PROJECTPULSE_CLAMAV_PORT:?missing private ClamAV port}"
    [[ "$clam_port" =~ ^[0-9]{1,5}$ ]] && (( clam_port >= 1 && clam_port <= 65535 )) || fail "ClamAV port is invalid."
    IFS=$'\n' read -r -d '' -a clam_ips < <(getent ahosts "$clam_host" | awk '{print $1}' | sort -u && printf '\0')
    (( ${#clam_ips[@]} > 0 )) || fail "ClamAV private DNS returned no address."
    for ip in "${clam_ips[@]}"; do private_ip "$ip" || fail "ClamAV DNS returned a public address."; done
    nc -z -w 5 "${clam_ips[0]}" "$clam_port" >/dev/null 2>&1 || fail "Private ClamAV TCP probe failed."
    printf 'CELAR_AI_PRIVATE_DEPENDENCY_CLAMAV=VERIFIED\n'
    if [[ -n "${PROJECTPULSE_PRIVATE_OCR_ENDPOINT:-}" ]]; then
      : "${PROJECTPULSE_PRIVATE_OCR_MODEL:?missing OCR model}"
      : "${PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN:?missing OCR bearer token}"
      probe_private_transport "$PROJECTPULSE_PRIVATE_OCR_ENDPOINT" "$PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN" ocr
    fi
    if [[ -n "${PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT:-}" ]]; then
      : "${PROJECTPULSE_PRIVATE_EMBEDDING_MODEL:?missing embedding model}"
      : "${PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN:?missing embedding token}"
      probe_embedding_endpoint "$PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT" \
        "$PROJECTPULSE_PRIVATE_EMBEDDING_MODEL" "$PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN"
    else
      [[ "${PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION:-false}" == true ]] ||
        fail "Private embeddings are absent and lexical-only is not approved."
      [[ -n "${PROJECTPULSE_PULSE_AI_LEXICAL_ONLY_APPROVAL_REFERENCE:-}" ]] ||
        fail "Lexical-only approval needs a durable approval reference."
      printf 'CELAR_AI_LEXICAL_ONLY=EXPLICITLY_APPROVED\n'
    fi
    ;;
  storage-write)
    root="${PROJECTPULSE_UPLOAD_ROOT:?missing upload root}"
    [[ "$root" == /tmp || "$root" == /tmp/* || "$root" == /var/tmp || "$root" == /var/tmp/* || "$root" == /dev/shm || "$root" == /dev/shm/* || "$root" == /run || "$root" == /run/* ]] && fail "Ephemeral storage is forbidden."
    [[ "${PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT:-false}" == true ]] || fail "Shared persistent storage attestation is absent."
    canary="${PROJECTPULSE_STORAGE_CANARY:?missing storage canary}"
    [[ "$canary" =~ ^[a-z0-9][a-z0-9-]{10,80}$ ]] || fail "Storage canary identifier is invalid."
    mkdir -p "$root/.celar-activation"
    printf '%s' "$canary" > "$root/.celar-activation/$canary"
    sync
    printf 'CELAR_AI_STORAGE_CANARY_WRITTEN=%s\n' "$canary"
    ;;
  storage-read)
    root="${PROJECTPULSE_UPLOAD_ROOT:?missing upload root}"
    canary="${PROJECTPULSE_STORAGE_CANARY:?missing storage canary}"
    [[ "$canary" =~ ^[a-z0-9][a-z0-9-]{10,80}$ ]] || fail "Storage canary identifier is invalid."
    [[ "$(<"$root/.celar-activation/$canary")" == "$canary" ]] || fail "Cross-revision storage canary is unavailable."
    rm -f "$root/.celar-activation/$canary"
    printf 'CELAR_AI_STORAGE_CROSS_REVISION=VERIFIED\n'
    ;;
  application)
    base="${PROJECTPULSE_CANDIDATE_URL:?missing candidate URL}"
    cookie="${PROJECTPULSE_ACTIVATION_SESSION_COOKIE:?missing protected activation session}"
    curl_args=(--silent --show-error --fail-with-body --proto '=https' --tlsv1.2 --max-time 45 --max-redirs 0 -H "Cookie: $cookie")
    origin="$(printf '%s' "$base" | sed -E 's#(https://[^/]+).*#\1#')"
    if [[ "${PROJECTPULSE_CELAR_CONFIGURE_PROFILE:-false}" == true ]]; then
      : "${PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT:?missing protected private endpoint}"
      : "${PROJECTPULSE_PRIVATE_INFERENCE_MODEL:?missing private model}"
      : "${PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN:?missing private bearer token}"
      : "${PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST:?missing private hostname allowlist}"
      current="$(curl "${curl_args[@]}" "$base/api/ai-configuration/private-model")"
      revision="$(json_field "$current" '.profile.revision | numbers')"
      settings="$(jq -nc \
        --arg endpoint "$PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT" \
        --arg model "$PROJECTPULSE_PRIVATE_INFERENCE_MODEL" \
        --arg hosts "$PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST" \
        --argjson revision "$revision" \
        '{enabled:true, endpoint:$endpoint, model:$model, privateHostAllowlist:($hosts|split(",")|map(gsub("^\\s+|\\s+$";""))|map(select(length>0))), requirePrivateModelForDocuments:true, expectedRevision:$revision}')"
      saved="$(curl "${curl_args[@]}" -X PUT -H "Origin: $origin" -H 'Sec-Fetch-Site: same-origin' \
        -H 'Content-Type: application/json' --data "$settings" \
        "$base/api/ai-configuration/private-model/settings")"
      revision="$(json_field "$saved" '.profile.revision | numbers')"
      secret="$(jq -nc --arg token "$PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN" --argjson revision "$revision" \
        '{bearerToken:$token, expectedRevision:$revision}')"
      curl "${curl_args[@]}" -X PUT -H "Origin: $origin" -H 'Sec-Fetch-Site: same-origin' \
        -H 'Content-Type: application/json' --data "$secret" \
        "$base/api/ai-configuration/private-model/secret" >/dev/null

      routes="$(curl "${curl_args[@]}" "$base/api/ai-configuration/routes")"
      for feature in timesheet_non_project_description timesheet_project_task_description timesheet_service_request_description; do
        revision="$(jq -er --arg feature "$feature" '.routes[] | select(.featureCode == $feature) | .revision' <<<"$routes")"
        route_body="$(jq -nc --argjson revision "$revision" '{targets:["celar_ai","claude","openai","local_template"],expectedRevision:$revision}')"
        curl "${curl_args[@]}" -X PUT -H "Origin: $origin" -H 'Sec-Fetch-Site: same-origin' \
          -H 'Content-Type: application/json' --data "$route_body" \
          "$base/api/ai-configuration/routes/$feature" >/dev/null
      done
      printf 'CELAR_AI_PRIVATE_PROFILE_AND_TIMESHEET_ROUTES=CONFIGURED\n'
    fi
    test_response="$(curl "${curl_args[@]}" -X POST -H "Origin: $origin" -H 'Sec-Fetch-Site: same-origin' "$base/api/ai-configuration/private-model/test")"
    json_field "$test_response" '.status == "private_model_available" and .available == true' >/dev/null || fail "Module 064 private-model probe did not succeed."

    sow_request="${PROJECTPULSE_SOW_ACTIVATION_REQUEST_JSON:-}"
    [[ -n "$sow_request" ]] || fail "A protected exact-document SOW activation request is required."
    jq -e '
      type == "object"
      and (.documentId | type == "string" and test("^[0-9a-fA-F-]{36}$"))
      and (.expectedSourceSha256 | type == "string" and test("^[0-9a-f]{64}$"))
      and (.changeTicket | type == "string" and length >= 5)
      and ([keys[] | select(. | test("name|email|customer|text|content"; "i"))] | length == 0)
    ' <<<"$sow_request" >/dev/null || fail "SOW activation request must identify one document and exact source hash without customer/content fields."
    document_id="$(json_field "$sow_request" '.documentId')"
    expected_hash="$(json_field "$sow_request" '.expectedSourceSha256')"
    approval_ticket="$(json_field "$sow_request" '.changeTicket')"
    [[ "$approval_ticket" == "${PROJECTPULSE_CELAR_CHANGE_TICKET:-}" ]] || fail "SOW approval ticket does not match the protected activation ticket."
    state="$(curl "${curl_args[@]}" "$base/api/pulse-ai/v1/documents/$document_id/runtime-state")"
    jq -e --arg documentId "${document_id,,}" '
      (.document.documentId | ascii_downcase) == $documentId
      and (((.document.documentCategory | ascii_downcase) == "sow") or ((.document.documentCategory | ascii_downcase) == "gsd"))
    ' <<<"$state" >/dev/null || fail "The protected document is not the exact authorized SOW/GSD document."
    processing="$(json_field "$state" '.document.processingStatus')"
    if [[ "$processing" != ready ]]; then
      failed_job="$(jq -r '.document.recentJobs[]? | select(.status == "failed") | .jobId' <<<"$state" | head -n 1)"
      active_job="$(jq -r '.document.recentJobs[]? | select(.status == "queued" or .status == "scanning" or .status == "extracting" or .status == "awaiting_ocr" or .status == "embedding" or .status == "indexing" or .status == "retry_wait") | .jobId' <<<"$state" | head -n 1)"
      if [[ -n "$failed_job" ]]; then
        retry_body="$(jq -nc --arg reason "Approved activation retry under $approval_ticket" '{reason:$reason,confirmation:"RETRY-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING"}')"
        curl "${curl_args[@]}" -X POST -H "Origin: $origin" -H 'Sec-Fetch-Site: same-origin' -H 'Content-Type: application/json' \
          --data "$retry_body" "$base/api/pulse-ai/v1/documents/runtime/jobs/$failed_job/retry" >/dev/null
      elif [[ -z "$active_job" ]]; then
        queue_body='{"purpose":"approved_celar_ai_activation","priority":100,"maximumAttempts":3,"confirmation":"QUEUE-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING"}'
        curl "${curl_args[@]}" -X POST -H "Origin: $origin" -H 'Sec-Fetch-Site: same-origin' -H 'Content-Type: application/json' \
          --data "$queue_body" "$base/api/pulse-ai/v1/documents/$document_id/processing-jobs" >/dev/null
      fi
    fi

    document_ready=false
    for _ in $(seq 1 120); do
      state="$(curl "${curl_args[@]}" "$base/api/pulse-ai/v1/documents/$document_id/runtime-state")"
      if jq -e --arg hash "$expected_hash" --arg documentId "${document_id,,}" '
        (.document.documentId | ascii_downcase) == $documentId
        and ((.document.documentCategory | ascii_downcase) == "sow" or (.document.documentCategory | ascii_downcase) == "gsd")
        and .document.processingStatus == "ready"
        and (.document.activeVersionId | type == "string")
        and .document.activeVersionSourceSha256 == $hash
        and ([.document.recentJobs[]? | select(.sourceSha256 == $hash and .status == "succeeded")] | length >= 1)
      ' <<<"$state" >/dev/null; then
        document_ready=true
        break
      fi
      sleep 15
    done
    [[ "$document_ready" == true ]] || fail "The exact approved SOW hash did not reach ready state before timeout."
    version_id="$(json_field "$state" '.document.activeVersionId')"
    approve_body="$(jq -nc --arg reason "Approved for private retrieval under $approval_ticket after exact-hash processing verification." \
      --arg expectedSourceSha256 "$expected_hash" \
      '{reason:$reason,expectedSourceSha256:$expectedSourceSha256,confirmation:"APPROVE-PULSE-AI-PRIVATE-DOCUMENT-VERSION"}')"
    approval="$(curl "${curl_args[@]}" -X POST -H "Origin: $origin" -H 'Sec-Fetch-Site: same-origin' -H 'Content-Type: application/json' \
      --data "$approve_body" "$base/api/pulse-ai/v1/documents/$document_id/versions/$version_id/approve")"
    json_field "$approval" '.status == "document_version_approved" and .authorityStatus == "approved"' >/dev/null || fail "Exact SOW version approval failed."

    # Re-read the exact document after approval. This binds retrieval
    # eligibility to the protected document ID, active version, category, and
    # source hash instead of accepting an older ready SOW elsewhere.
    state="$(curl "${curl_args[@]}" "$base/api/pulse-ai/v1/documents/$document_id/runtime-state")"
    jq -e --arg documentId "${document_id,,}" --arg versionId "${version_id,,}" --arg hash "$expected_hash" '
      (.document.documentId | ascii_downcase) == $documentId
      and ((.document.documentCategory | ascii_downcase) == "sow" or (.document.documentCategory | ascii_downcase) == "gsd")
      and .document.processingStatus == "ready"
      and (.document.activeVersionId | ascii_downcase) == $versionId
      and .document.activeVersionSourceSha256 == $hash
      and .document.activeChunkCount >= 1
    ' <<<"$state" >/dev/null || fail "The exact approved SOW/GSD version is not retrieval-ready."

    runtime_ready=false
    for _ in $(seq 1 120); do
      runtime_response="$(curl "${curl_args[@]}" "$base/api/pulse-ai/v1/documents/runtime/readiness")"
      if jq -e '.status == "private_document_runtime_ready" and .readiness.readySowDocumentCount >= 1' <<<"$runtime_response" >/dev/null; then
        runtime_ready=true
        break
      fi
      sleep 15
    done
    [[ "$runtime_ready" == true ]] || fail "No authorized SOW/GSD reached ready state before the guarded timeout."
    test_response="$(curl "${curl_args[@]}" -X POST -H "Origin: $origin" -H 'Sec-Fetch-Site: same-origin' "$base/api/ai-configuration/private-model/test")"
    json_field "$test_response" '.status == "private_model_available" and .available == true' >/dev/null || fail "Final fresh Module 064 private-model probe did not succeed."
    public_health="$(curl "${curl_args[@]}" -X POST -H "Origin: $origin" -H 'Sec-Fetch-Site: same-origin' "$base/api/ai-configuration/health/refresh")"
    jq -e '
      [.providers[] | select(.provider == "claude" or .provider == "openai")] as $public
      | ($public | length) == 2
      and ($public | all(.enabled == true and .configured == true and .probeStatus == "available"))
    ' <<<"$public_health" >/dev/null || fail "Claude and OpenAI must both be enabled, configured, and live-probe available."
    external_fallback_probe="$(curl "${curl_args[@]}" -X POST -H "Origin: $origin" -H 'Sec-Fetch-Site: same-origin' \
      "$base/api/ai-configuration/sanitized-external-fallback/production-test")"
    jq -e '
      .status == "sanitized_external_fallback_production_probe_succeeded"
      and .ready == true
      and .providerOrder == ["claude", "openai"]
      and .policy.fixedServerAuthoredCapsule == true
      and .policy.callerContentAccepted == false
      and .policy.projectOrTaskContextRead == false
      and .policy.customerOrPeopleContextRead == false
      and .policy.privateDocumentContextRead == false
      and .policy.providerContentReturned == false
      and .policy.sharedRouteChanged == false
      and ([.targets[] | {provider, status}] == [
        {provider:"claude", status:"sanitized_generation_succeeded"},
        {provider:"openai", status:"sanitized_generation_succeeded"}
      ])
    ' <<<"$external_fallback_probe" >/dev/null || fail "The fixed identity-free Claude/OpenAI production fallback probe did not pass."
    private_response="$(curl "${curl_args[@]}" "$base/api/ai-configuration/private-model")"
    json_field "$private_response" '
      .productionReadiness.ready == true
      and .productionReadiness.privateModelReady == true
      and .productionReadiness.sanitizedExternalFallback.productionReady == true
      and .productionReadiness.processing.servicePrincipalAuthorized == true
    ' >/dev/null || fail "Module 064 aggregate production readiness is not green after private, document, queue-principal, and public-fallback proofs."
    rag_response="$(curl "${curl_args[@]}" "$base/api/pulse-ai/v1/rag/readiness")"
    json_field "$rag_response" '.readiness.status == "private_rag_ready"' >/dev/null || fail "Private RAG is not ready."

    request_json="${PROJECTPULSE_E2E_TIMESHEET_REQUEST_JSON:-}"
    [[ -n "$request_json" ]] || fail "A protected, non-customer test time-entry request is required for end-to-end evidence."
    jq -e '
      type == "object"
      and (.currentDescription == "Completed approved technical implementation and verification activities for the assigned task.")
      and ((.assignmentId // .timeEntryId // .taskId // .projectId) | type == "string")
      and (has("customerName") | not)
      and (has("projectName") | not)
      and (has("projectCode") | not)
      and (has("taskName") | not)
      and (has("taskCode") | not)
      and ([keys[] | select(. | test("name|email|customer"; "i"))] | length == 0)
    ' <<<"$request_json" >/dev/null || fail "End-to-end request must use IDs only and contain no customer/person/project/task names."
    e2e="$(curl "${curl_args[@]}" -X POST -H "Origin: $origin" -H 'Sec-Fetch-Site: same-origin' \
      -H 'Content-Type: application/json' --data "$request_json" \
      "$base/api/timesheets/ai-description-suggestions")"
    jq -e '
      .status == "ai_suggestion_generated"
      and .provider == "celar_ai"
      and (.suggestion | type == "string")
      and (.suggestion | length >= 120 and length <= 1500)
      and (
        .suggestion as $raw
        | ($raw
          | gsub("[\\r\\n\\t]+"; " ")
          | gsub("[ ]{2,}"; " ")
          | gsub("^[ ]+|[ ]+$"; "")) as $text
        | ($text | [scan("[^.!?]+[.!?]+")] ) as $sentences
        | ($sentences | length) >= 2
        and ($sentences | length) <= 4
        and ($sentences | join("")) == $text
        and (($raw | test("(^|\\r?\\n)[[:space:]]*([-*+][[:space:]]+|[0-9]+[.)][[:space:]]+|#{1,6}[[:space:]]+|>[[:space:]]+)"; "m")) | not)
        and (($raw | test("```|`|\\[[^]]+\\]\\([^)]+\\)|\\*\\*|__")) | not)
        and (($raw | test("(projectpulse|celar[ -]?ai|claude|openai|governed[ -]?local|local[ -]?template|ai[ -]?provider|provider[ -]?(route|target)|model[ -]?(route|deployment)|system[ -]?prompt|internal[ -]?(prompt|route|diagnostic)|module[[:space:]]*0*(1|64))"; "i")) | not)
        and (($raw | test("[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}|[0-9a-f]{32,64}|(^|[^[:alnum:]])(project|task|assignment|time[ _-]?entry|request|correlation|document|version)[ _-]?(id|identifier)([^[:alnum:]]|$)|(^|[^[:alnum:]])[A-Z]{2,10}-[0-9]{2,}([^[:alnum:]]|$)|/api/|https?://|[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}"; "i")) | not)
      )
    ' <<<"$e2e" >/dev/null ||
      fail "End-to-end timesheet evidence was not a 120-1500 character, 2-4 sentence, customer-ready Celar AI response free of Markdown, provider details, and internal identifiers."
    json_field "$e2e" '[.targetDecisions[]? | select(.target == "claude" or .target == "openai") | .outcome] | all(. == "not_attempted" or . == "skipped")' >/dev/null ||
      fail "The private-grounded end-to-end request unexpectedly used a public target."
    printf 'CELAR_AI_MODULE064_FRESH_PROBE=VERIFIED\n'
    printf 'CELAR_AI_PUBLIC_PROVIDER_HEALTH=VERIFIED providers=claude,openai\n'
    printf 'CELAR_AI_SANITIZED_EXTERNAL_FALLBACK=VERIFIED providers=claude,openai contentReturned=false\n'
    printf 'CELAR_AI_AUTHORIZED_SOW_READY=VERIFIED\n'
    printf 'CELAR_AI_END_TO_END_PRIVATE_GROUNDING=VERIFIED customerReady=true sentences=2-4 markdown=false internalIdentifiers=false\n'
    ;;
  *) fail "Unknown probe mode: $mode" ;;
esac
