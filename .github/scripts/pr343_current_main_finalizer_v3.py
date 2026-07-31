from __future__ import annotations

import json
from pathlib import Path


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    source = path.read_text(encoding="utf-8")
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one source block, found {count}")
    path.write_text(source.replace(old, new, 1), encoding="utf-8")


# Register the public Celar compatibility endpoints while retaining the complete
# reviewed Pulse AI technical endpoint family.
project = Path("src/backend/ProjectTime.Api/ProjectTime.Api.csproj")
project_text = project.read_text(encoding="utf-8")
pulse_registration = 'print &quot;app.MapPulseAiSystemIntelligenceEndpoints();&quot;;'
celar_registration = 'print &quot;app.MapCelarAiBrandEndpoints();&quot;;'
if project_text.count(pulse_registration) != 1:
    raise SystemExit("Expected one Pulse AI system endpoint registration anchor.")
if celar_registration not in project_text:
    project_text = project_text.replace(
        pulse_registration,
        f"{pulse_registration} {celar_registration}",
        1,
    )
if project_text.count(celar_registration) != 1:
    raise SystemExit("Celar AI endpoint registration must appear exactly once.")
project.write_text(project_text, encoding="utf-8")


# Run the deterministic visible-name injector in development and immediately
# before the production Vite compile, then validate the generated Celar state.
package_path = Path("src/frontend/project-time-web/package.json")
package = json.loads(package_path.read_text(encoding="utf-8"))
scripts = package.setdefault("scripts", {})
injector = "node ./scripts/inject-celar-ai-runtime-rebrand.mjs"
validator_command = "npm run validate:celar-ai-runtime-rebrand"

predev = scripts["predev"]
if injector not in predev:
    predev = f"{predev} && {injector}"
scripts["predev"] = predev

build = scripts["build"]
celar_build = f"{injector} && {validator_command}"
if celar_build not in build:
    marker = " && vite build"
    if build.count(marker) != 1:
        raise SystemExit("Expected one Vite build anchor in package.json.")
    build = build.replace(marker, f" && {celar_build}{marker}", 1)
scripts["build"] = build
scripts["validate:celar-ai-runtime-rebrand"] = (
    "node ./scripts/validate-celar-ai-runtime-rebrand.mjs"
)
package_path.write_text(json.dumps(package, indent=2) + "\n", encoding="utf-8")


# The lean production web image executes source validators. Include the backend
# Celar endpoint module that the validator reads without broadening the image
# context to unrelated repository source.
dockerfile = Path("deployment/containers/web/Dockerfile")
docker_text = dockerfile.read_text(encoding="utf-8")
celar_copy = """COPY src/backend/ProjectTime.Api/Modules/CelarAiBrandModule.cs \\
     src/backend/ProjectTime.Api/Modules/CelarAiBrandModule.cs

"""
docker_anchor = """COPY src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs \\
     src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs

"""
if "Modules/CelarAiBrandModule.cs" not in docker_text:
    if docker_text.count(docker_anchor) != 1:
        raise SystemExit("Expected one Module 064 Dockerfile copy anchor.")
    docker_text = docker_text.replace(docker_anchor, docker_anchor + celar_copy, 1)
if docker_text.count("Modules/CelarAiBrandModule.cs") != 2:
    raise SystemExit(
        "Celar AI module Dockerfile copy must have source and destination exactly once."
    )
dockerfile.write_text(docker_text, encoding="utf-8")


# The original stacked workflow compared against PR #323. Rebase its source
# isolation contract onto authoritative current main while retaining strict
# ownership and protected-deployment guards.
workflow_path = Path(".github/workflows/celar-ai-runtime-rebrand-ci.yml")
workflow = workflow_path.read_text(encoding="utf-8")
start_marker = "      - name: Verify exact stacked source isolation\n"
end_marker = "      - uses: actions/setup-dotnet@v4\n"
start = workflow.find(start_marker)
end = workflow.find(end_marker, start)
if start < 0 or end < 0:
    raise SystemExit("Celar AI workflow isolation step anchors are missing.")

replacement = r'''      - name: Verify exact current-main source isolation
        shell: bash
        run: |
          set -Eeuo pipefail
          BASE_BRANCH="${GITHUB_BASE_REF:-main}"
          git fetch origin "$BASE_BRANCH" --no-tags
          BASE_REF="$(git merge-base "origin/$BASE_BRANCH" HEAD)"
          test -n "$BASE_REF"
          test "$BASE_REF" = "$(git rev-parse "origin/$BASE_BRANCH")"
          CHANGED="$(git diff --name-only "$BASE_REF"...HEAD)"
          printf '%s\n' "$CHANGED"

          ALLOWED='^(\.github/workflows/(celar-ai-runtime-rebrand-ci|pulse-ai-system-intelligence-ci|pulse-ai-help-chat-usability-ci)\.yml|docs/modules/module-011-pulse-ai/(CELAR-AI-RUNTIME-REBRAND|SYSTEM-INTELLIGENCE-AND-TROUBLESHOOTING)\.md|src/backend/ProjectTime\.Api/Ai/(CelarAiBrandProfile|PulseAiProductKnowledgeCatalog|ProjectPulseAiServiceCollectionExtensions|PulseAiSystemIntelligenceService|PulseAiSystemToolExecutor)\.cs|src/backend/ProjectTime\.Api/Modules/CelarAiBrandModule\.cs|src/backend/ProjectTime\.Api/ProjectTime\.Api\.csproj|src/frontend/project-time-web/package\.json|src/frontend/project-time-web/scripts/(inject-celar-ai-runtime-rebrand|inject-pulse-ai-system-chat-group7-compatibility|validate-celar-ai-runtime-rebrand|validate-module-011-pulse-ai|validate-group-1-navigation-work-consolidation|validate-module-011-private-document-pipeline|validate-module-011-pulse-ai-deep-intelligence|validate-module-011-system-intelligence-package|validate-pulse-ai-help-chat-usability)\.mjs|src/frontend/project-time-web/src/(CelarAiProviderBridgePanel\.jsx|celar-ai-provider-bridge-panel\.css|module-availability-registry\.js|HelpAssistant\.jsx|PulseAiSystemIntelligenceWorkbench\.jsx|WorkTaskBuilderPanel\.jsx|pulse-ai-system-chat\.css|pulse-ai-system-intelligence-workbench\.css)|deployment/containers/web/Dockerfile)$'
          UNEXPECTED="$(grep -Ev "$ALLOWED" <<<"$CHANGED" || true)"
          if [[ -n "$UNEXPECTED" ]]; then
            echo 'Unexpected Celar AI current-main source scope:' >&2
            printf '%s\n' "$UNEXPECTED" >&2
            exit 1
          fi

          for protected in \
            '.github/workflows/projectpulse-deploy-runtime-direct-timer-recovery-test.yml' \
            '.github/workflows/validate-runtime-direct-timer-recovery-deployment.yml' \
            'scripts/validate-runtime-direct-timer-recovery-test-deployment.sh'; do
            ! grep -Fxq "$protected" <<<"$CHANGED"
          done

          OTHER_DEPLOYMENT="$(grep -E '^deployment/' <<<"$CHANGED" | grep -Fvx 'deployment/containers/web/Dockerfile' || true)"
          if [[ -n "$OTHER_DEPLOYMENT" ]]; then
            echo 'Unexpected deployment source changed by the Celar AI package:' >&2
            printf '%s\n' "$OTHER_DEPLOYMENT" >&2
            exit 1
          fi

          ! grep -E '^(database/|scripts/.*deploy|\.github/workflows/projectpulse-deploy-|src/backend/ProjectTime\.Api/Ai/(ProjectPulseAiConfiguration|ProjectPulseAiRemoteProviders|ProjectPulseAiSecretStore)\.cs|src/backend/ProjectTime\.Api/Modules/AiProviderConfigurationModule\.cs)' <<<"$CHANGED"
          git diff --check "$BASE_REF"...HEAD -- \
            '.github/workflows/celar-ai-runtime-rebrand-ci.yml' \
            'deployment/containers/web/Dockerfile' \
            'src/backend/ProjectTime.Api/ProjectTime.Api.csproj' \
            'src/frontend/project-time-web/package.json' \
            'src/frontend/project-time-web/scripts/validate-module-011-pulse-ai.mjs'
          echo "CELAR_AI_RUNTIME_REBRAND_BASE=$BASE_REF"
          echo 'CELAR_AI_RUNTIME_REBRAND_SOURCE_ISOLATION=PASSED'

'''
workflow = workflow[:start] + replacement + workflow[end:]
workflow_path.write_text(workflow, encoding="utf-8")


# The foundation validator remains authoritative for technical compatibility,
# security boundaries, legacy recovery, and build wiring. It now accepts either
# the historical public Pulse label or the approved public Celar identity, but
# the Celar path must explicitly retain technicalIdentity: Pulse AI.
validator_path = Path(
    "src/frontend/project-time-web/scripts/validate-module-011-pulse-ai.mjs"
)
replace_once(
    validator_path,
    '''assert(
  'REGISTRY_IDENTITY',
  module011Block.includes("displayName: 'Pulse AI'")
    && module011Block.includes("group: 'AI & Automation'")
    && module011Block.includes("lifecycle: 'source_foundation'"),
  'Module 011 is registered as the Pulse AI source foundation'
);''',
    '''assert(
  'REGISTRY_IDENTITY',
  (
    module011Block.includes("displayName: 'Pulse AI'")
      && module011Block.includes("group: 'AI & Automation'")
      && module011Block.includes("lifecycle: 'source_foundation'")
  ) || (
    module011Block.includes("displayName: 'Celar AI'")
      && module011Block.includes("group: 'AI & Automation'")
      && module011Block.includes("lifecycle: 'active_operational_intelligence'")
      && module011Block.includes("technicalIdentity: 'Pulse AI'")
      && module011Block.includes("publicAlias: 'celar-ai'")
  ),
  'Module 011 uses the approved Celar AI public identity or the preserved Pulse AI foundation identity'
);''',
    "dual public/technical Module 011 registry identity",
)

replace_once(
    validator_path,
    '''assert(
  'GROUP_ONE_RECONCILED',
  groupOneValidator.includes("assert('MODULE_011_PULSE_AI'")
    && groupOneValidator.includes('GROUP1_MODULE_011_DISPOSITION=REUSED_AS_PULSE_AI')
    && groupOneValidator.includes('LEGACY_WORK_TASK_BUILDER_RECOVERABLE'),
  'the earlier navigation consolidation contract recognizes the approved Module 011 reuse'
);''',
    '''assert(
  'GROUP_ONE_RECONCILED',
  (
    groupOneValidator.includes("assert('MODULE_011_PULSE_AI'")
      && groupOneValidator.includes('GROUP1_MODULE_011_DISPOSITION=REUSED_AS_PULSE_AI')
      && groupOneValidator.includes('LEGACY_WORK_TASK_BUILDER_RECOVERABLE')
  ) || (
    groupOneValidator.includes("assert('MODULE_011_CELAR_AI'")
      && groupOneValidator.includes('GROUP1_MODULE_011_DISPOSITION=REBRANDED_AS_CELAR_AI')
      && groupOneValidator.includes('GROUP1_MODULE_011_TECHNICAL_IDENTITY=PULSE_AI_COMPATIBILITY_RETAINED')
      && groupOneValidator.includes('LEGACY_WORK_TASK_BUILDER_RECOVERABLE')
  ),
  'the navigation consolidation contract recognizes either the approved Pulse reuse or the Celar public rebrand with Pulse compatibility retained'
);''',
    "dual Pulse/Celar Group 1 reconciliation contract",
)

print("PR343_CURRENT_MAIN_FINALIZER_V3_PATCH=APPLIED")
