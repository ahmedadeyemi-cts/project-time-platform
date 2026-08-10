from __future__ import annotations

from pathlib import Path

workflow_path = Path(".github/workflows/celar-ai-oracle-test-runtime-deploy.yml")
text = workflow_path.read_text(encoding="utf-8")

old_probe = r'''          AUTH=(-H "Authorization: Bearer $UAT_SESSION" -H "X-ProjectPulse-Session: $UAT_SESSION" -H 'X-ProjectPulse-Module-Number: 011' -H 'Cache-Control: no-cache')
          AVAILABLE=false
          for _attempt in $(seq 1 18); do
            PROBE="$(curl -fsS --max-time 600 "${AUTH[@]}" -H 'Content-Type: application/json' -H "Origin: $BASE" -d '{}' "$BASE/api/ai-configuration/private-model/test" || true)"
            if jq -e '.status == "private_model_available" and .configured == true and .available == true' <<<"$PROBE" >/dev/null 2>&1; then AVAILABLE=true; break; fi
            sleep 10
          done
          [[ "$AVAILABLE" == true ]]
'''

new_probe = r'''          AUTH=(-H "Authorization: Bearer $UAT_SESSION" -H "X-ProjectPulse-Session: $UAT_SESSION" -H 'X-ProjectPulse-Module-Number: 011' -H 'Cache-Control: no-cache')
          PROFILE_RAW="$RUNNER_TEMP/oracle-private-model-profile-raw.json"
          PROFILE_SAFE="$RUNNER_TEMP/oracle-private-model-profile-sanitized.json"
          PROFILE_STATUS="$(curl -sS --max-time 90 -o "$PROFILE_RAW" -w '%{http_code}' \
            "${AUTH[@]}" -H "Origin: $BASE" "$BASE/api/ai-configuration/private-model")"
          [[ "$PROFILE_STATUS" == 200 ]]
          jq -e . "$PROFILE_RAW" >/dev/null
          jq '
            (.privateModel // .profile // .) as $profile |
            {
              status: (.status // null),
              enabled: ($profile.enabled // null),
              configured: ($profile.configured // null),
              authenticationConfigured: ($profile.authenticationConfigured // null),
              ready: ($profile.ready // null),
              source: ($profile.source // null),
              endpoint: ($profile.endpoint // null),
              model: ($profile.model // null),
              authenticationMode: ($profile.authenticationMode // $profile.authMode // null),
              hostAllowlist: ($profile.hostAllowlist // []),
              resolvedHosts: ($profile.resolvedHosts // []),
              endpointPolicy: ($profile.endpointPolicy // null),
              externalHttpsRuntime: ($profile.externalHttpsRuntime // null)
            }
          ' "$PROFILE_RAW" | tee "$PROFILE_SAFE"
          echo 'ORACLE_INPROCESS_PROFILE_DIAGNOSTIC=CAPTURED'

          PROBE_RAW="$RUNNER_TEMP/oracle-private-model-probe-raw.json"
          PROBE_SAFE="$RUNNER_TEMP/oracle-private-model-probe-sanitized.json"
          printf '{}\n' > "$PROBE_SAFE"
          AVAILABLE=false
          for _attempt in $(seq 1 18); do
            PROBE_STATUS="$(curl -sS --max-time 600 -o "$PROBE_RAW" -w '%{http_code}' \
              "${AUTH[@]}" -H 'Content-Type: application/json' -H "Origin: $BASE" \
              -d '{}' "$BASE/api/ai-configuration/private-model/test" || true)"
            if [[ -s "$PROBE_RAW" ]] && jq -e . "$PROBE_RAW" >/dev/null 2>&1; then
              jq --argjson status "${PROBE_STATUS:-0}" '{
                httpStatus: $status,
                status: (.status // null),
                configured: (.configured // null),
                available: (.available // null),
                diagnostic: (.diagnostic // null),
                targetId: (.targetId // null),
                targetKind: (.targetKind // null),
                privacyBoundary: (.privacyBoundary // null)
              }' "$PROBE_RAW" \
                | tee "$PROBE_SAFE"
              if [[ "$PROBE_STATUS" == 200 ]] \
                && jq -e '.status == "private_model_available" and .configured == true and .available == true' "$PROBE_RAW" >/dev/null 2>&1; then
                AVAILABLE=true
                break
              fi
            else
              jq -n --arg status "${PROBE_STATUS:-transport_error}" \
                '{httpStatus:$status,status:"invalid_or_empty_response",configured:null,available:false,diagnostic:null}' \
                | tee "$PROBE_SAFE"
            fi
            sleep 10
          done
          if [[ "$AVAILABLE" != true ]]; then
            echo 'Oracle in-process profile diagnostic:' >&2
            cat "$PROFILE_SAFE" >&2 || true
            echo 'Oracle private-model probe diagnostic:' >&2
            cat "$PROBE_SAFE" >&2 || true
            exit 1
          fi
'''

if text.count(old_probe) != 1:
    raise SystemExit(
        f"Expected exactly one private-model readiness probe block, found {text.count(old_probe)}."
    )
text = text.replace(old_probe, new_probe, 1)

old_evidence = r'''      - name: Publish sanitized deployment evidence
        if: success()
        shell: bash
        run: |
          set -Eeuo pipefail
          mkdir -p evidence
          jq -n --arg environment test --arg releaseCommit "$TARGET_RELEASE_COMMIT" --arg approvalReference "$APPROVAL_REFERENCE" --arg apiImage '${{ steps.build.outputs.api_image }}' --arg apiRevision '${{ steps.deploy_api.outputs.revision }}' \
            '{environment:$environment,releaseCommit:$releaseCommit,approvalReference:$approvalReference,migrations:[],productionMutation:false,openCloudMutation:false,oracleInfrastructureMutation:false,apiImage:$apiImage,apiRevision:$apiRevision,secretMaterialRecorded:false}' \
            > evidence/celar-ai-oracle-test-runtime.json

      - name: Upload sanitized deployment evidence
        if: success()
'''

new_evidence = r'''      - name: Publish sanitized deployment evidence
        if: ${{ always() && steps.deploy_api.outputs.started == 'true' }}
        shell: bash
        run: |
          set -Eeuo pipefail
          mkdir -p evidence
          PROFILE_SAFE="$RUNNER_TEMP/oracle-private-model-profile-sanitized.json"
          PROBE_SAFE="$RUNNER_TEMP/oracle-private-model-probe-sanitized.json"
          [[ -s "$PROFILE_SAFE" ]] || printf '{}\n' > "$PROFILE_SAFE"
          [[ -s "$PROBE_SAFE" ]] || printf '{}\n' > "$PROBE_SAFE"
          jq -n \
            --arg environment test \
            --arg releaseCommit "$TARGET_RELEASE_COMMIT" \
            --arg approvalReference "$APPROVAL_REFERENCE" \
            --arg apiImage '${{ steps.build.outputs.api_image }}' \
            --arg apiRevision '${{ steps.deploy_api.outputs.revision }}' \
            --slurpfile privateModelProfile "$PROFILE_SAFE" \
            --slurpfile privateModelProbe "$PROBE_SAFE" \
            '{
              environment:$environment,
              releaseCommit:$releaseCommit,
              approvalReference:$approvalReference,
              migrations:[],
              productionMutation:false,
              openCloudMutation:false,
              oracleInfrastructureMutation:false,
              apiImage:$apiImage,
              apiRevision:$apiRevision,
              secretMaterialRecorded:false,
              privateModelProfile:($privateModelProfile[0] // {}),
              privateModelProbe:($privateModelProbe[0] // {})
            }' > evidence/celar-ai-oracle-test-runtime.json

      - name: Upload sanitized deployment evidence
        if: ${{ always() && steps.deploy_api.outputs.started == 'true' }}
'''

if text.count(old_evidence) != 1:
    raise SystemExit(
        f"Expected exactly one sanitized evidence block, found {text.count(old_evidence)}."
    )
text = text.replace(old_evidence, new_evidence, 1)

workflow_path.write_text(text, encoding="utf-8")
print("Oracle workflow patched with safe in-process profile and probe diagnostics.")
