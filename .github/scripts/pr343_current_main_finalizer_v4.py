from __future__ import annotations

import runpy
from pathlib import Path


V3_PATCH = Path(".github/scripts/pr343_current_main_finalizer_v3.py")
if not V3_PATCH.is_file():
    raise SystemExit("The reviewed PR #343 V3 reconciliation patch is missing.")

# Preserve every reviewed V3 reconciliation decision, including current-main
# authority for security-sensitive shared files, then correct only the one
# confirmed false-negative in the final Celar AI visible-brand validator.
runpy.run_path(str(V3_PATCH), run_name="__main__")


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    source = path.read_text(encoding="utf-8")
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one source block, found {count}")
    path.write_text(source.replace(old, new, 1), encoding="utf-8")


validator_path = Path(
    "src/frontend/project-time-web/scripts/validate-celar-ai-runtime-rebrand.mjs"
)
replace_once(
    validator_path,
    '''assert(
  'GLOBAL_CHAT_BRAND',
  help.includes('Ask Celar AI')
    && help.includes('aria-label="Celar AI system intelligence assistant"')
    && help.includes('<strong>Celar AI</strong>')
    && help.includes('Celar AI Workbench')
    && help.includes("const path = '/api/celar-ai/v1/chat';")
    && help.includes("openRoute('celar-ai')"),
  'the global chat is visibly Celar AI and submits through the new user-facing endpoint'
);''',
    '''assert(
  'GLOBAL_CHAT_BRAND',
  help.includes('Ask Celar AI')
    && help.includes('aria-label="Celar AI system intelligence assistant"')
    && help.includes('<strong>Celar AI Help & Search</strong>')
    && !help.includes('<strong>Pulse AI Help & Search</strong>')
    && help.includes('Celar AI Workbench')
    && help.includes("const path = '/api/celar-ai/v1/chat';")
    && help.includes("openRoute('celar-ai')"),
  'the global chat preserves the Group 7 Help & Search title under the Celar AI brand and submits through the user-facing endpoint'
);''',
    "Celar AI Group 7 global-chat brand contract",
)

# Fail closed unless the current-main Group 7 compatibility source still owns
# the expanded Help & Search title and the Celar injector still transforms all
# visible Pulse AI labels before the final validator executes.
group7_path = Path(
    "src/frontend/project-time-web/scripts/inject-pulse-ai-system-chat-group7-compatibility.mjs"
)
group7 = group7_path.read_text(encoding="utf-8")
if "<strong>Pulse AI Help & Search</strong>" not in group7:
    raise SystemExit("The reviewed Group 7 Help & Search title contract changed.")

injector_path = Path(
    "src/frontend/project-time-web/scripts/inject-celar-ai-runtime-rebrand.mjs"
)
injector = injector_path.read_text(encoding="utf-8")
for required in (
    "'HelpAssistant.jsx'",
    ".replaceAll('Pulse AI', 'Celar AI')",
    "content.replaceAll(`openRoute('work-task-builder')`, `openRoute('celar-ai')`)",
):
    if required not in injector:
        raise SystemExit(f"The reviewed Celar AI Help transformation changed: {required}")

print("PR343_CURRENT_MAIN_FINALIZER_V4_PATCH=APPLIED")
