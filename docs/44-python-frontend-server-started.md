# Python Frontend Server Started

## Purpose

This document records the successful start of the Python local frontend server and the follow-up validation note.

## Successful Server Start

The Python frontend server successfully started on the Oracle Linux development VM.

Observed output:

```text
Serving frontend from /opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/dist
Proxying /health and /api/* to http://127.0.0.1:5080
Local URL: http://127.0.0.1:5173/
Press CTRL+C to stop.
```

This means the previous issue with port 5173 was resolved by killing the stale Node process.

## Important Terminal Behavior

The terminal running the Python frontend server must remain open.

Do not type validation commands into the same terminal because that terminal is now occupied by the server process.

Open a second SSH session to run curl commands.

## URL Typo Observed

The server logged this request:

```text
GET /api/version~ HTTP/1.1 404
```

The trailing tilde character is not part of the endpoint.

Correct endpoint:

```text
/api/version
```

Incorrect endpoint:

```text
/api/version~
```

## Correct Local Validation Commands

From a second SSH session on the VM, run:

```bash
curl -I http://127.0.0.1:5173/
curl http://127.0.0.1:5173/api/version
curl 'http://127.0.0.1:5173/api/timesheets/week?weekStart=2026-06-21'
```

## Correct Browser Access from Mac

From a Mac terminal, keep this SSH tunnel open:

```bash
ssh -i ~/.ssh/private_key.key -L 5173:127.0.0.1:5173 opc@167.234.223.32
```

Then open this exact URL in the Mac browser:

```text
http://127.0.0.1:5173/
```

## Security Note

Port 5173 should remain bound to localhost only. Do not expose it publicly through OCI security lists or firewall rules.
