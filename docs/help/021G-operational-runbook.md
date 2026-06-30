# 021G Operational Runbook

## Scope

021G creates the operational runbook for production readiness validation.

## Covered Areas

- Runtime services.
- Deployment paths.
- Backup locations.
- Rollback sequence.
- Endpoint smoke matrix.
- Evidence capture checklist.
- Production readiness smoke script.

## Generated Artifacts

- `scripts/021-operational-runbook-report.py`
- `scripts/021-production-readiness-smoke.sh`
- `docs/production-readiness/021_OPERATIONAL_RUNBOOK.md`
- `docs/production-readiness/021_OPERATIONAL_RUNBOOK.json`

## Validation Strategy

The operational runbook supports the final 021 release-candidate validation pass by defining what must be checked, captured, and preserved before deployment closeout.
