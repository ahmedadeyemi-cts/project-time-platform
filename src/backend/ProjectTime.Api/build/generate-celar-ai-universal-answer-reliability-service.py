#!/usr/bin/env python3
from __future__ import annotations

import argparse
from pathlib import Path

OLD = '''        if (plan.ClarificationsToRequest.Count > 0)
        {
            findings.Add(Review(
                "clarification_recommended",
                "The planner identified missing scope that could change the answer.",
                "Ask the listed clarification when authoritative resolution cannot be completed safely."));
        }'''

NEW = '''        // Planner clarifications are pre-retrieval safeguards. Once the
        // required authoritative evidence has been retrieved, cited, and has
        // passed every blocker-class gate, that evidence has resolved the
        // missing scope and must not keep an otherwise verified answer in a
        // review-required state. Missing, stale, unauthorized, or incomplete
        // evidence still retains the clarification review.
        if (plan.ClarificationsToRequest.Count > 0
            && (successfulSources.Length == 0
                || findings.Any(finding => finding.Severity == "blocker")))
        {
            findings.Add(Review(
                "clarification_recommended",
                "The planner identified missing scope that could change the answer and authoritative evidence did not fully resolve it.",
                "Ask the listed clarification when authoritative resolution cannot be completed safely."));
        }'''


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()

    source_path = Path(args.input)
    output_path = Path(args.output)
    source = source_path.read_text(encoding='utf-8')
    occurrences = source.count(OLD)
    if occurrences != 1:
        raise SystemExit(
            f'{source_path}: expected exactly one canonical clarification gate, found {occurrences}'
        )

    generated = source.replace(OLD, NEW)
    if 'findings.Any(finding => finding.Severity == "blocker")' not in generated:
        raise SystemExit('generated reliability source is missing the blocker-preserving gate')

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(generated, encoding='utf-8')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
