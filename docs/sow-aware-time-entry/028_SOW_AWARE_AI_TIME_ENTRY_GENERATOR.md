# 028 SOW-Aware AI Time Entry Generator

## Status
Implemented as a complete adapter-ready module.

## Included
- Backend readiness/context/review/evidence endpoints.
- Browser-visible dashboard card.
- Browser-visible SOW-aware review center.
- Scope alignment scoring.
- Missing-context and out-of-scope-risk detection.
- Claude-ready evidence prompt/package.
- Reporting handoff structure for Module 030.
- Non-destructive database migration for future persistence.

## Webpage impact
Dashboard receives a SOW-Aware Time Entry Review card. The card opens `#sow-aware-time-entry-review`.

## Backend/process support
The backend can review pasted or adapter-supplied signed SOW, GSD, and generated time entry context. Future modules 024-027 can provide source artifacts without changing the visible review workflow.

## Validation checks
- Build backend.
- Build frontend.
- Confirm 022 dashboard/notification markers remain.
- Confirm 023 data readiness marker remains.
- Confirm 028 markers exist in source and built frontend.
- Confirm protected 028 endpoint returns 401 unauthenticated.
