from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
SEARCH_ROOTS = [
    ROOT / 'src/frontend/project-time-web/scripts',
    ROOT / 'tests',
]
SUFFIXES = {'.mjs', '.js', '.cjs'}
changed: list[str] = []

literal_replacements = [
    (
        "\"window.addEventListener('projectpulse:view-as-changed', refresh)\"",
        "\"window.addEventListener('projectpulse:view-as-changed', resetForIdentity)\"",
    ),
    (
        "'window.addEventListener(\\'projectpulse:view-as-changed\\', refresh)'",
        "'window.addEventListener(\\'projectpulse:view-as-changed\\', resetForIdentity)'",
    ),
]

for search_root in SEARCH_ROOTS:
    if not search_root.exists():
        continue
    for path in search_root.rglob('*'):
        if not path.is_file() or path.suffix not in SUFFIXES:
            continue
        source = path.read_text()
        original = source

        # PR 719 intentionally permits authenticated owner-catalog GET requests
        # to reach the authoritative backend. Remove only stale validator
        # expectations that the browser gate must intercept the GET and synthesize
        # an empty owner catalog. Production source is never modified here.
        if ('background-request-role-gate.js' in source
                or 'backgroundGate' in source
                or 'backgroundGateSource' in source):
            source = source.replace("  '/api/module-catalog/owners',\n", '')
            source = source.replace('  "/api/module-catalog/owners",\n', '')
            source = source.replace("  'owners: []',\n", '')
            source = source.replace('  "owners: []",\n', '')

            direct_patterns = [
                r"\s*requireText\(\s*[^,]+,\s*['\"]\/api\/module-catalog\/owners['\"]\s*,[^;]+;\s*",
                r"\s*requireMarker[s]?\(\s*[^,]+,\s*['\"]\/api\/module-catalog\/owners['\"][^;]+;\s*",
            ]
            for pattern in direct_patterns:
                source = re.sub(pattern, '\n', source, flags=re.DOTALL)

        # The corrected directory invalidates the previous user's provisional
        # list on View-As changes. Update stale marker-only expectations.
        for old, new in literal_replacements:
            if old in source and ('ModulesDirectoryPortal' in source
                                  or 'modules directory' in source.lower()):
                source = source.replace(old, new)

        # Owner metadata is layout-independent. Validators must not require the
        # obsolete table-mode early return that caused Enterprise/Classic drift.
        source = source.replace(
            "  'if (!tableMode) return undefined;\\n    void loadOwnership();',\n",
            "  'OWNER_CATALOG_READ_THROUGH_FOR_AUTHENTICATED_USERS_V1',\n",
        )
        source = source.replace(
            '  "if (!tableMode) return undefined;\\n    void loadOwnership();",\n',
            '  "OWNER_CATALOG_READ_THROUGH_FOR_AUTHENTICATED_USERS_V1",\n',
        )

        if source != original:
            path.write_text(source)
            changed.append(str(path.relative_to(ROOT)))

# Guardrail: this reconciler may update validation contracts only.
for relative in changed:
    if not (relative.startswith('src/frontend/project-time-web/scripts/')
            or relative.startswith('tests/')):
        raise SystemExit(f'Unsafe PR 719 reconciliation target: {relative}')

print('\n'.join(changed))
