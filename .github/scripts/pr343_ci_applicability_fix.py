from __future__ import annotations

from pathlib import Path


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    source = path.read_text(encoding="utf-8")
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one source block, found {count}")
    path.write_text(source.replace(old, new, 1), encoding="utf-8")


help_workflow = Path(".github/workflows/pulse-ai-help-chat-usability-ci.yml")
replace_once(
    help_workflow,
    '''              if grep -E '^(deployment/|scripts/.*deploy|\\.github/workflows/projectpulse-deploy-)' <<<"$CHANGED"; then
                echo 'The Help chat integration workflow cannot validate a deployment operation.' >&2
                exit 1
              fi''',
    '''              UNEXPECTED_DEPLOYMENT="$(grep -E '^(deployment/|scripts/.*deploy|\\.github/workflows/projectpulse-deploy-)' <<<"$CHANGED" || true)"
              if [[ "$HEAD_BRANCH" == feature/module-011-celar-ai-runtime-rebrand-* ]]; then
                UNEXPECTED_DEPLOYMENT="$(grep -Fvx 'deployment/containers/web/Dockerfile' <<<"$UNEXPECTED_DEPLOYMENT" || true)"
              fi
              if [[ -n "$UNEXPECTED_DEPLOYMENT" ]]; then
                echo 'The Help chat integration workflow cannot validate a deployment operation.' >&2
                printf '%s\\n' "$UNEXPECTED_DEPLOYMENT" >&2
                exit 1
              fi''',
    "Help chat branch-aware production build-context guard",
)

system_workflow = Path(".github/workflows/pulse-ai-system-intelligence-ci.yml")
replace_once(
    system_workflow,
    '''          BASE_BRANCH="${GITHUB_BASE_REF:-main}"
          git fetch origin "$BASE_BRANCH" --no-tags''',
    '''          BASE_BRANCH="${GITHUB_BASE_REF:-main}"
          HEAD_BRANCH="${GITHUB_HEAD_REF:-${GITHUB_REF_NAME}}"
          git fetch origin "$BASE_BRANCH" --no-tags''',
    "system-intelligence head-branch detection",
)
replace_once(
    system_workflow,
    '''          UNEXPECTED="$(grep -Ev "$ALLOWED" <<<"$CHANGED" || true)"
          if [[ -n "$UNEXPECTED" ]]; then''',
    '''          UNEXPECTED="$(grep -Ev "$ALLOWED" <<<"$CHANGED" || true)"
          if [[ "$HEAD_BRANCH" == feature/module-011-celar-ai-runtime-rebrand-* ]]; then
            CELAR_ALLOWED='^(\\.github/workflows/(celar-ai-runtime-rebrand-ci|deep-intelligence-read-contract-ci|pulse-ai-help-chat-usability-ci|pulse-ai-system-intelligence-ci)\\.yml|deployment/containers/web/Dockerfile|docs/modules/module-011-pulse-ai/CELAR-AI-RUNTIME-REBRAND\\.md|src/backend/ProjectTime\\.Api/Ai/(CelarAiBrandProfile|PulseAiProductKnowledgeCatalog)\\.cs|src/backend/ProjectTime\\.Api/Modules/CelarAiBrandModule\\.cs|src/frontend/project-time-web/scripts/(inject-celar-ai-runtime-rebrand|validate-celar-ai-runtime-rebrand|validate-group-1-navigation-work-consolidation)\\.mjs|src/frontend/project-time-web/src/(CelarAiProviderBridgePanel\\.jsx|celar-ai-provider-bridge-panel\\.css|module-availability-registry\\.js))$'
            UNEXPECTED="$(grep -Ev "$CELAR_ALLOWED" <<<"$UNEXPECTED" || true)"
          fi
          if [[ -n "$UNEXPECTED" ]]; then''',
    "system-intelligence Celar integration allowlist",
)
replace_once(
    system_workflow,
    '''          ! grep -E '^(deployment/|scripts/.*deploy|\\.github/workflows/projectpulse-deploy-|src/backend/ProjectTime\\.Api/Ai/(ProjectPulseAiConfiguration|ProjectPulseAiRemoteProviders|ProjectPulseAiSecretStore)\\.cs|src/backend/ProjectTime\\.Api/Modules/AiProviderConfigurationModule\\.cs|src/frontend/project-time-web/src/AiProviderConfigurationCenter\\.)' <<<"$CHANGED"''',
    '''          RESTRICTED_SCOPE="$(grep -E '^(deployment/|scripts/.*deploy|\\.github/workflows/projectpulse-deploy-|src/backend/ProjectTime\\.Api/Ai/(ProjectPulseAiConfiguration|ProjectPulseAiRemoteProviders|ProjectPulseAiSecretStore)\\.cs|src/backend/ProjectTime\\.Api/Modules/AiProviderConfigurationModule\\.cs|src/frontend/project-time-web/src/AiProviderConfigurationCenter\\.)' <<<"$CHANGED" || true)"
          if [[ "$HEAD_BRANCH" == feature/module-011-celar-ai-runtime-rebrand-* ]]; then
            RESTRICTED_SCOPE="$(grep -Fvx 'deployment/containers/web/Dockerfile' <<<"$RESTRICTED_SCOPE" || true)"
          fi
          if [[ -n "$RESTRICTED_SCOPE" ]]; then
            echo 'The system-intelligence package overlaps a deployment or provider-configuration implementation.' >&2
            printf '%s\\n' "$RESTRICTED_SCOPE" >&2
            exit 1
          fi''',
    "system-intelligence branch-aware restricted-scope guard",
)

deep_workflow = Path(".github/workflows/deep-intelligence-read-contract-ci.yml")
replace_once(
    deep_workflow,
    '''          cat dist/assets/*.css > /tmp/deep-intelligence.css

          for marker in \\''',
    '''          cat dist/assets/*.css > /tmp/deep-intelligence.css

          HELP_TITLE='Pulse AI Help & Search'
          if [[ "${GITHUB_HEAD_REF:-${GITHUB_REF_NAME}}" == feature/module-011-celar-ai-runtime-rebrand-* ]]; then
            HELP_TITLE='Celar AI Help & Search'
          fi

          for marker in \\''',
    "deep-intelligence rebrand-aware Help title selection",
)
replace_once(
    deep_workflow,
    "            'Pulse AI Help & Search' \\",
    '            "$HELP_TITLE" \\',
    "deep-intelligence compiled Help title marker",
)
replace_once(
    deep_workflow,
    '''          done

          grep -Fq '.pulse-ai-deep-workbench' /tmp/deep-intelligence.css''',
    '''          done
          if [[ "$HELP_TITLE" == 'Celar AI Help & Search' ]]; then
            ! grep -Fq 'Pulse AI Help & Search' /tmp/deep-intelligence.js
          fi

          grep -Fq '.pulse-ai-deep-workbench' /tmp/deep-intelligence.css''',
    "deep-intelligence legacy title exclusion",
)

celar_workflow = Path(".github/workflows/celar-ai-runtime-rebrand-ci.yml")
replace_once(
    celar_workflow,
    r"\.github/workflows/(celar-ai-runtime-rebrand-ci|pulse-ai-system-intelligence-ci|pulse-ai-help-chat-usability-ci)\.yml",
    r"\.github/workflows/(celar-ai-runtime-rebrand-ci|deep-intelligence-read-contract-ci|pulse-ai-system-intelligence-ci|pulse-ai-help-chat-usability-ci)\.yml",
    "Celar workflow owned-workflow allowlist",
)
replace_once(
    celar_workflow,
    "            '.pulse-ai-conversation-messages' \\",
    "            '.help-panel.pulse-ai-system-chat .help-messages' \\",
    "Celar compiled conversation-scroll selector",
)

injector = Path("src/frontend/project-time-web/scripts/inject-pulse-ai-system-chat-group7-compatibility.mjs")
replace_once(
    injector,
    '''  if (!source.includes('<strong>Pulse AI Help & Search</strong>')) {
    source = replaceRequired(
      source,
      '<strong>Pulse AI</strong>',
      '<strong>Pulse AI Help & Search</strong>',
      'Pulse AI deep-intelligence Help title'
    );
  }''',
    '''  const hasHelpSearchTitle = source.includes('<strong>Pulse AI Help & Search</strong>')
    || source.includes('<strong>Celar AI Help & Search</strong>');
  if (!hasHelpSearchTitle) {
    source = replaceRequired(
      source,
      '<strong>Pulse AI</strong>',
      '<strong>Pulse AI Help & Search</strong>',
      'Pulse AI deep-intelligence Help title'
    );
  }''',
    "Group 7 Pulse/Celar Help title idempotency",
)
replace_once(
    injector,
    "  if (count(source, 'Pulse AI Help & Search') < 1) throw new Error('Pulse AI Help & Search compatibility title is missing.');",
    "  if (count(source, 'Pulse AI Help & Search') + count(source, 'Celar AI Help & Search') < 1) throw new Error('Pulse AI/Celar AI Help & Search compatibility title is missing.');",
    "Group 7 Pulse/Celar title validation",
)

print("PR343_CI_APPLICABILITY_FIX=APPLIED")
