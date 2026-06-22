# React Frontend Setup

## Purpose

This runbook documents the initial React frontend skeleton for the Project Time Platform.

## Frontend Location

```text
src/frontend/project-time-web
```

## Technology

The first frontend skeleton uses:

- React
- Vite
- npm
- Node.js from Oracle Linux repositories

The frontend is intentionally local-only during this phase.

## Files Added

```text
src/frontend/project-time-web/package.json
src/frontend/project-time-web/index.html
src/frontend/project-time-web/vite.config.js
src/frontend/project-time-web/src/main.jsx
src/frontend/project-time-web/src/App.jsx
src/frontend/project-time-web/src/styles.css
src/frontend/project-time-web/.gitignore
```

## Install Node.js

From the repository root:

```bash
cd /opt/project-time-platform/app/project-time-platform

chmod +x deployment/rocky-linux/install-nodejs-oraclelinux9.sh
./deployment/rocky-linux/install-nodejs-oraclelinux9.sh
```

Validate:

```bash
node --version
npm --version
```

## Build Frontend

From the repository root:

```bash
chmod +x deployment/rocky-linux/build-frontend.sh
./deployment/rocky-linux/build-frontend.sh
```

Manual build option:

```bash
cd /opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web
npm install
npm run build
```

## Run Frontend Locally

Start the frontend development server:

```bash
cd /opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web
npm run dev
```

The frontend will listen on:

```text
http://127.0.0.1:5173
```

## API Proxy

The Vite development server proxies these paths to the backend API:

```text
/health -> http://127.0.0.1:5080/health
/api/* -> http://127.0.0.1:5080/api/*
```

The backend systemd service must be running first:

```bash
sudo systemctl status projecttime-api --no-pager
```

## Local Validation

Because the frontend is bound to localhost on the VM, validate with curl first:

```bash
curl http://127.0.0.1:5173
```

To view in a browser before public reverse proxy setup, use SSH port forwarding from your workstation:

```bash
ssh -i ~/.ssh/private_key.key -L 5173:127.0.0.1:5173 opc@167.234.223.32
```

Then open this on your local machine:

```text
http://127.0.0.1:5173
```

## Security Notes

- Do not open port `5173` publicly.
- Do not open port `5080` publicly.
- Use SSH port forwarding for temporary browser validation.
- Public HTTPS access should wait for reverse proxy, TLS, and Microsoft Entra authentication.

## Next Step

After frontend build and local browser validation succeed, publish the frontend static files and configure an internal reverse proxy foundation.
