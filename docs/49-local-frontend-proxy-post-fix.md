# Local Frontend Proxy POST Fix

## Purpose

This document records the fix for the browser-based timesheet save and submit issue.

## Date

2026-06-23

## Issue

The backend API and PostgreSQL persistence were confirmed working directly from the server using `curl` against `http://127.0.0.1:5080`.

However, when using the browser through the local frontend server at `http://127.0.0.1:5173`, timesheet entries did not persist after clicking Save draft or Submit and refreshing the page.

## Root Cause

The local frontend test server served the React build and proxied `/api/*` requests to the backend API. It supported `GET` and `HEAD`, but it did not support `POST`.

Because the React frontend uses `POST` for these endpoints:

- `/api/timesheets/week/draft`
- `/api/timesheets/week/submit`

browser-based save and submit calls were not being proxied correctly to the backend API.

The direct API test worked because it bypassed the frontend proxy and called the backend API directly on port `5080`.

## File Updated

- `deployment/rocky-linux/serve-frontend-local.py`

## Fix

The local frontend server now supports proxying these HTTP methods to the backend API:

- `GET`
- `HEAD`
- `POST`
- `PUT`
- `DELETE`

For `POST`, `PUT`, and `DELETE`, the server reads the request body from the browser and forwards it to the backend API with the original `Content-Type` header.

## Validation Steps

1. Pull the latest repository updates on the OCI VM.
2. Restart the local frontend server.
3. Open `http://127.0.0.1:5173/` through the SSH tunnel.
4. Enter timesheet hours from the browser.
5. Click Save draft.
6. Refresh the browser.
7. Confirm saved time remains visible.
8. Click Submit.
9. Refresh again.
10. Confirm submitted time remains visible and the row is locked.

## Expected Server Log Behavior

The frontend server terminal should now show browser-originated POST requests such as:

```text
127.0.0.1 - "POST /api/timesheets/week/draft HTTP/1.1" 200 -
127.0.0.1 - "POST /api/timesheets/week/submit HTTP/1.1" 200 -
```

## Status

Ready for validation.
