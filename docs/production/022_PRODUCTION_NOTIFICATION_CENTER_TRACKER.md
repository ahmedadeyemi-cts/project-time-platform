# 022 Production Notification Center Tracker

## 022A Production Notification Center Foundation

Status: In progress

Purpose:
- Add role-aware in-app production notifications.
- Support production readiness alerts, workflow notices, export/audit notices, and future email/in-app pairing.
- Keep notification delivery separate from external email provider sends.

Production guardrails:
- No email is sent by this module.
- Signed-out users receive session_required.
- Administrator View-As can read notifications as a selected user but cannot acknowledge or create notifications.
- System notification creation is restricted to administrators and production operators.
- Dashboard / Navigation / Registry checks remain required.

## 022B Production Readiness Route Isolation + Dashboard Entry

Status: Applied pending visual validation and commit.

Scope:
- Isolate `#production-readiness` route content from dashboard/landing content.
- Add visible Production Readiness Center dashboard shortcut.
- Preserve existing View-As, notification, and production readiness APIs.
- Include Dashboard / Navigation / Registry validation.

Validation:
- Frontend build required.
- Public dashboard route should show Production Readiness Center shortcut.
- Public production-readiness route should not continue into general landing/dashboard content.
- API checks required for readiness command center, navigation registry, dashboard module visibility, and 022A notifications.
