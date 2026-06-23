# Frontend Vite SSH Tunnel Troubleshooting

## Purpose

This document captures troubleshooting steps for accessing the local Vite React frontend running on the Oracle Linux development VM.

## Expected Local Frontend Runtime

The Vite frontend development server should run on the VM at:

```text
http://127.0.0.1:5173/
```

Start it from the frontend project directory:

```bash
cd /opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web
npm run dev
```

Expected Vite output:

```text
VITE ready
Local: http://127.0.0.1:5173/
```

Keep this terminal open. Pressing `CTRL+C` stops the frontend server.

## Verify the Frontend Is Listening on the VM

From a second SSH session into the VM, run:

```bash
ss -ltnp | grep 5173 || true
```

Expected listener:

```text
127.0.0.1:5173
```

Then test with curl:

```bash
curl -v --max-time 5 http://127.0.0.1:5173/
```

Expected result:

```text
HTTP/1.1 200 OK
```

and HTML content for the Vite application.

## Browser Access from Mac

Because Vite is bound to localhost on the VM, access it through SSH port forwarding from the Mac:

```bash
ssh -i ~/.ssh/private_key.key -L 5173:127.0.0.1:5173 opc@167.234.223.32
```

Keep this SSH session open while testing in the browser.

Open this exact URL in the Mac browser:

```text
http://127.0.0.1:5173/
```

Do not use:

```text
http:///127.0.0.1:5173
```

The extra slash after `http:` makes the URL invalid.

## Common Causes

### Curl hangs or returns nothing

Possible causes:

- Vite is no longer running.
- The curl command is running in the same terminal that is occupied by Vite instead of a second SSH session.
- Local port forwarding is not active.
- Browser URL is malformed.
- The Vite process is accepting connections but not serving a response.

### Browser does not load

Check:

```bash
ss -ltnp | grep 5173 || true
```

Then confirm the tunnel is open from the Mac:

```bash
ssh -i ~/.ssh/private_key.key -L 5173:127.0.0.1:5173 opc@167.234.223.32
```

Then open:

```text
http://127.0.0.1:5173/
```

## If Curl Connects but Times Out

If curl shows:

```text
Connected to 127.0.0.1 port 5173
Operation timed out with 0 bytes received
```

then the port is open, but the Vite process is not returning a response. Restart the frontend process cleanly.

From a second SSH session:

```bash
pkill -f 'vite' || true
pkill -f 'node.*5173' || true
ss -ltnp | grep 5173 || true
```

Then start Vite again from the frontend directory:

```bash
cd /opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web
npm run dev -- --host 127.0.0.1 --port 5173 --clearScreen false
```

Test again:

```bash
curl -v --max-time 10 http://127.0.0.1:5173/
```

## Production Preview Fallback

If the development server continues to accept connections but returns no response, use the production preview server instead.

```bash
cd /opt/project-time-platform/app/project-time-platform
./deployment/rocky-linux/build-frontend.sh

cd /opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web
npm run preview -- --host 127.0.0.1 --port 5173
```

Then test:

```bash
curl -v --max-time 10 http://127.0.0.1:5173/
```

Use the same SSH tunnel from the Mac:

```bash
ssh -i ~/.ssh/private_key.key -L 5173:127.0.0.1:5173 opc@167.234.223.32
```

Open:

```text
http://127.0.0.1:5173/
```

## Security Note

Do not open port 5173 publicly in OCI. The Vite server should remain bound to localhost during development. Use SSH tunneling for browser testing.
