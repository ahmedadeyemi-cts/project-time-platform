# 028 SOW-Aware AI Time Entry Generator

## Purpose
Module 028 reviews generated time entries against signed SOW and GSD scope context before PM/Manager approval or export.

## Webpage
`#sow-aware-time-entry-review`

## Backend endpoints
- `GET /api/sow-aware-time-entry/readiness`
- `GET /api/sow-aware-time-entry/context`
- `GET /api/sow-aware-time-entry/reviews`
- `POST /api/sow-aware-time-entry/review`
- `POST /api/sow-aware-time-entry/review/evidence`

## Workflow
1. User provides or selects signed SOW context.
2. User provides or selects GSD / delivery handoff context.
3. User provides generated time entries.
4. Module 028 reviews alignment against scope.
5. The review returns alignment score, outcome, checks, recommendation, reporting handoff, and Claude-ready evidence.

## Dependency handling
This module is adapter-ready for Modules 024-027. It does not require their final tables to exist before build validation.
