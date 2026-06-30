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
