# Frontend Port 5173 Conflict Troubleshooting

## Purpose

This document records the troubleshooting path when the frontend server cannot start because port 5173 is already in use.

## Symptom

The Python local frontend server fails with:

```text
OSError: [Errno 98] Address already in use
```

This means another process is already listening on port 5173.

## Why Browser Access Still Fails

If the existing process on port 5173 is stale or hung, the browser and curl may connect but receive no response.

Example:

```text
Connected to 127.0.0.1 port 5173
Operation timed out with 0 bytes received
```

## Identify the Process Using Port 5173

Run:

```bash
sudo ss -ltnp | grep ':5173' || true
```

The output should show a process name and PID.

## Kill the Process Using Port 5173

Run:

```bash
PID=$(sudo ss -ltnp | grep ':5173' | sed -n 's/.*pid=\([0-9]*\).*/\1/p' | head -1)
echo "PID using 5173: ${PID:-none}"
if [ -n "${PID:-}" ]; then
  sudo kill -9 "$PID"
fi
sudo ss -ltnp | grep ':5173' || true
```

After cleanup, there should be no listener on port 5173.

## Use Alternate Port 4173 if 5173 Remains Problematic

Start the local Python frontend server on port 4173:

```bash
cd /opt/project-time-platform/app/project-time-platform
python3 deployment/rocky-linux/serve-frontend-local.py --host 127.0.0.1 --port 4173
```

Keep this terminal open.

From a second SSH session, validate:

```bash
curl -I http://127.0.0.1:4173/
curl http://127.0.0.1:4173/api/version
```

From the Mac, create a tunnel for port 4173:

```bash
ssh -i ~/.ssh/private_key.key -L 4173:127.0.0.1:4173 opc@167.234.223.32
```

Then open:

```text
http://127.0.0.1:4173/
```

## Security Note

Do not open 5173 or 4173 publicly in OCI. These ports are for local development over SSH tunneling only.
