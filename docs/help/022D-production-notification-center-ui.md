# 022D Production Notification Center UI + Preference Controls

## Purpose
022D makes the production notification backend visible and manageable from the Project Pulse frontend.

## Scope
- Adds a Production Notification Center dashboard shortcut.
- Adds a top-bar Notifications dropdown with an unacknowledged badge.
- Adds a drawer UI for latest notifications, acknowledgment, routing rules, saved preferences, and preference updates.
- Uses the 022A notification APIs and the 022C routing/preference APIs.
- Keeps email delivery disabled.
- Preserves Administrator View-As read-only write protection.

## Validation expectations
- Dashboard shows a Production Notification Center shortcut.
- Top-bar Notifications dropdown is visible after app load.
- Notification summary and list load for authenticated users.
- Acknowledge action works outside View-As.
- Preference save works outside View-As and keeps email disabled.
- View-As write attempts remain blocked by the backend.
- Dashboard / Navigation / Registry validation remains green.
