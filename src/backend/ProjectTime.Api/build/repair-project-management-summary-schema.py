#!/usr/bin/env python3
from __future__ import annotations

import argparse
from pathlib import Path

LEGACY_QUERY = '''        SELECT p.project_code, pr.risk_title, pr.probability, pr.impact, pr.risk_status, pr.mitigation_plan
        FROM project_risks pr
        INNER JOIN projects p ON p.project_id = pr.project_id
        ORDER BY p.project_code, pr.created_at DESC;
'''

ENTERPRISE_QUERY = '''        SELECT
            p.project_code,
            pr.risk_title,
            CASE
                WHEN pr.probability_score <= 1 THEN 'low'
                WHEN pr.probability_score <= 3 THEN 'medium'
                ELSE 'high'
            END AS probability,
            CASE
                WHEN pr.overall_impact_score <= 1 THEN 'low'
                WHEN pr.overall_impact_score <= 3 THEN 'medium'
                ELSE 'high'
            END AS impact,
            pr.risk_status,
            COALESCE(
                NULLIF(BTRIM(pr.mitigation_actions), ''),
                NULLIF(BTRIM(pr.response_plan), '')
            ) AS mitigation_plan
        FROM project_risks pr
        INNER JOIN projects p ON p.project_id = pr.project_id
        ORDER BY p.project_code, pr.created_at DESC;
'''

DISALLOWED_AFTER_REPAIR = (
    'pr.probability,',
    'pr.impact,',
    'pr.mitigation_plan',
)

REQUIRED_AFTER_REPAIR = (
    'pr.probability_score',
    'pr.overall_impact_score',
    'pr.mitigation_actions',
    'pr.response_plan',
)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', required=True)
    args = parser.parse_args()

    target = Path(args.input)
    source = target.read_text(encoding='utf-8')
    occurrences = source.count(LEGACY_QUERY)
    if occurrences != 1:
        raise SystemExit(
            f'{target}: expected exactly one legacy project-management risk query, found {occurrences}'
        )

    repaired = source.replace(LEGACY_QUERY, ENTERPRISE_QUERY)

    for marker in DISALLOWED_AFTER_REPAIR:
        if marker in repaired:
            raise SystemExit(f'{target}: retired Module 011 risk column remains after repair: {marker}')

    for marker in REQUIRED_AFTER_REPAIR:
        if marker not in repaired:
            raise SystemExit(f'{target}: Migration 077 risk column is missing after repair: {marker}')

    target.write_text(repaired, encoding='utf-8')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
