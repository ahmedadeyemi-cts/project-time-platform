# 022C Production Notification Preferences + Routing Rules

## Purpose
022C adds production notification preference and routing foundations for Project Health Dashboard.

## Scope
- Adds role-aware production notification routing rules.
- Adds user-level notification preferences by module and severity.
- Keeps email delivery disabled.
- Preserves Administrator View-As read-only enforcement.
- Adds API validation coverage for Dashboard / Navigation / Registry workflows.

## Endpoints
- `GET /api/production/notifications/preferences/summary`
- `GET /api/production/notifications/routing-rules`
- `POST /api/production/notifications/preferences`
- `POST /api/production/notifications/routing-rules/toggle`

## Email posture
022C is in-app only. Any future email delivery must use the shared global email provider and recipient safety gate.
