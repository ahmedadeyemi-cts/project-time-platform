#!/usr/bin/env python3
"""Offline production preparation review. Never connects, applies SQL or deletes data."""
import argparse
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SQL_ROOTS = ('database/migrations', 'database/seed-data', 'database/demo', 'deployment/rocky-linux')
RESOURCES = ('database', 'document_storage', 'notification_queue', 'integration_queue', 'rag_index', 'secret_store')
DECISIONS = {'review_required', 'exclude_from_production', 'candidate_schema_or_reference'}
SIGNALS = {
    'sample_data_marker': r'\b(demo|sample|development engineer|development user|seeded PSA)\b|USS-PSA-2026',
    'data_mutation': r'\b(INSERT\s+INTO|UPDATE\s+|DELETE\s+FROM|TRUNCATE\s+)',
    'dynamic_sql': r'\bEXECUTE\b',
    'client_include': r'(?m)^\s*\\i(r)?\s',
}


def inventory(root):
    result = []
    for directory in SQL_ROOTS:
        for path in sorted((root / directory).rglob('*.sql')):
            content = path.read_bytes()
            source = content.decode('utf-8', errors='replace')
            result.append({
                'path': path.relative_to(root).as_posix(),
                'sha256': hashlib.sha256(content).hexdigest(),
                # These are triage hints, not a SQL parser or safety decision.
                'signals': sorted(name for name, pattern in SIGNALS.items() if re.search(pattern, source, re.I)),
                'review_default': 'review_required',
            })
    return sorted(result, key=lambda item: item['path'])


def catalog_issues(entries, catalog):
    issues = []
    expected = {entry['path']: entry for entry in entries}
    reviewed = {}
    if not isinstance(catalog, dict) or catalog.get('schema_version') != 1 or not isinstance(catalog.get('scripts'), list):
        return ['catalog: invalid schema']
    for row in catalog['scripts']:
        if not isinstance(row, dict) or not isinstance(row.get('path'), str):
            issues.append('catalog: malformed script entry')
            continue
        name = row['path']
        if name in reviewed:
            issues.append(f'catalog: duplicate path {name}')
        reviewed[name] = row
        if row.get('decision') not in DECISIONS:
            issues.append(f'catalog: invalid decision for {name}')
        if row.get('decision') != 'review_required' and not row.get('rationale', '').strip():
            issues.append(f'catalog: decision lacks rationale for {name}')
        if name not in expected:
            issues.append(f'catalog: removed or out-of-scope path {name}')
        elif row.get('sha256') != expected[name]['sha256']:
            issues.append(f'catalog: content changed; review again: {name}')
    for name in expected.keys() - reviewed.keys():
        issues.append(f'catalog: new script needs review: {name}')
    return sorted(issues)


def environment_issues(config):
    """Validate declared resource IDs only. Live separation still needs proof."""
    issues = []
    if not isinstance(config, dict) or config.get('schema_version') != 1:
        return ['environment: invalid schema']
    if config.get('phase') != 'preparation':
        issues.append('environment: foundation phase must be preparation')
    for flag in ('production_deployment_enabled', 'outbound_email_enabled', 'external_writes_enabled', 'background_jobs_enabled'):
        if config.get(flag) is not False:
            issues.append(f'environment: {flag} must explicitly be false during preparation')
    for name in RESOURCES:
        pair = config.get('resources', {}).get(name, {})
        ids = []
        for environment in ('test', 'production'):
            value = pair.get(environment) if isinstance(pair, dict) else None
            if not isinstance(value, str) or not value.strip() or value.strip().lower() in {'tbd', 'todo', 'changeme'} or '<' in value:
                issues.append(f'environment: {name}.{environment} needs a fully qualified resource identifier')
                ids.append(None)
                continue
            # Never accept DSNs, credentials, SAS/query strings, or connection strings.
            if any(token in value for token in ('://', '?', ';', '=', '@', '\n', '\r')):
                issues.append(f'environment: {name}.{environment} must be a resource identifier, not a URL or credential')
                ids.append(None)
                continue
            ids.append(value.strip().rstrip('/').casefold())
        if all(ids) and ids[0] == ids[1]:
            issues.append(f'environment: {name} is shared between Test and Production')
    return issues


def report(root, catalog, environment):
    entries = inventory(root)
    integrity = catalog_issues(entries, catalog)
    isolation = environment_issues(environment)
    decisions = {row.get('path'): row.get('decision') for row in catalog.get('scripts', []) if isinstance(row, dict)} if isinstance(catalog, dict) else {}
    pending = [item['path'] for item in entries if decisions.get(item['path'], 'review_required') == 'review_required']
    blockers = integrity + isolation
    if not entries:
        blockers.append('initialization: no SQL scripts found; verify the repository root')
    if pending:
        blockers.append(f'initialization: {len(pending)} SQL scripts still require review')
    # Explicitly no executable order is inferred from filenames or review decisions.
    return {
        'mode': 'offline_preparation_only',
        'production_ready': False,
        'foundation_review_complete': not blockers,
        'live_environment_verified': False,
        'sql_execution_supported': False,
        'catalog_integrity_issues': integrity,
        'environment_issues': isolation,
        'blockers': blockers,
        'pending_script_reviews': pending,
        'script_count': len(entries),
        'scripts': entries,
        'remaining_acceptance': [
            'Review startup/bootstrap code and operational scripts outside this SQL inventory.',
            'Define dependency-aware initialization order and treatment of mixed schema/sample-data scripts.',
            'Rehearse a fresh install and prove no sample business records or development identities are introduced.',
            'Verify live resource identities, least-privilege access, network isolation and production secret references.',
            'Complete restore, role-based UAT, capacity, notification, integration and RAG-isolation acceptance.',
            'Implement and review the real production deployment workflow before go-live.',
        ],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=ROOT)
    parser.add_argument('--catalog', type=Path)
    parser.add_argument('--environment', type=Path)
    parser.add_argument('--inventory', action='store_true', help='Print SQL metadata only; does not declare readiness')
    args = parser.parse_args()
    root = args.root.resolve()
    if args.inventory:
        print(json.dumps({'mode': 'inventory_only', 'scripts': inventory(root)}, indent=2))
        return 0
    try:
        catalog = json.loads((args.catalog or root / 'docs/production-readiness/foundation/initialization-review.json').read_text())
        environment = json.loads((args.environment or root / 'docs/production-readiness/foundation/environment.example.json').read_text())
        result = report(root, catalog, environment)
    except (OSError, ValueError, TypeError, AttributeError):
        # Do not echo file contents or parsing errors that could include credentials.
        print(json.dumps({'production_ready': False, 'error': 'Invalid or unreadable foundation input files.'}))
        return 2
    print(json.dumps(result, indent=2))
    return 0 if result['foundation_review_complete'] else 2


if __name__ == '__main__':
    raise SystemExit(main())
