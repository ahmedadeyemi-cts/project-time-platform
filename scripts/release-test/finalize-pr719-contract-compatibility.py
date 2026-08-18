from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    target = Path(path)
    source = target.read_text()
    count = source.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected one compatibility target, found {count}: {old!r}')
    target.write_text(source.replace(old, new, 1))


replace_once(
    'src/backend/ProjectTime.Api/Modules/ModuleCatalogOwnershipModule.cs',
    'message = "Only an actual developer Super Administrator session can change module ownership."',
    'message = "Only an actual Super Administrator session can change module ownership. The session must belong to an active developer Super Administrator."'
)

replace_once(
    'src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs',
    "  'const PAGE_SIZE = 50;',",
    "  'const PAGE_SIZE = 20;',"
)

replace_once(
    'src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs',
    "  'Only an actual developer Super Administrator session can change module ownership.',",
    "  'Only an actual Super Administrator session can change module ownership.',\n  'active developer Super Administrator.',"
)

Path('scripts/release-test/finalize-pr719-contract-compatibility.py').unlink(missing_ok=True)
