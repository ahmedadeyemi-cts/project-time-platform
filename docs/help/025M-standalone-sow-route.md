# 025M Standalone SOW Route

## Purpose
Fix the SOW Generator route so it no longer renders on top of the dashboard or User Administration page.

## Changes
- Standalone full-page SOW Generator route shell.
- Dashboard card remains on the dashboard.
- `#sow-generator` hides the underlying application by using a route shell.
- Prevents the visual endless-scroll behavior.
- Keeps research-backed SOW generation and Word download.
