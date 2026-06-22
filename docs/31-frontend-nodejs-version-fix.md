# Frontend Node.js Version Fix

## Purpose

This document records the first frontend build issue and the correction.

## Issue

The frontend build failed because the VM installed Node.js 16 from the default Oracle Linux package stream.

Observed version:

```text
node v16.20.2
npm 8.19.4
```

Vite reported that the current Node.js version was unsupported and required Node.js 18 or higher.

## Error

```text
npm WARN EBADENGINE Unsupported engine
required: node 18 or higher
current: node v16.20.2
```

The build then failed with:

```text
TypeError: crypto.getRandomValues is not a function
```

## Fix

The Node.js install script was updated to reset the Node.js module stream and enable a newer stream:

```text
deployment/rocky-linux/install-nodejs-oraclelinux9.sh
```

Preferred stream:

```text
nodejs:20
```

Fallback stream:

```text
nodejs:18
```

The frontend build script was also updated to stop early when Node.js is below version 18:

```text
deployment/rocky-linux/build-frontend.sh
```

## Apply Steps

From the repository root:

```bash
cd /opt/project-time-platform/app/project-time-platform

git restore deployment/rocky-linux/apply-initial-schema.sh || true

GIT_SSH_COMMAND='ssh -i ~/.ssh/github_project_time_platform -o IdentitiesOnly=yes' \
git pull
```

Run the Node.js installer again:

```bash
chmod +x deployment/rocky-linux/install-nodejs-oraclelinux9.sh
./deployment/rocky-linux/install-nodejs-oraclelinux9.sh
```

Clean the previous Node.js 16 dependency install:

```bash
cd /opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web
rm -rf node_modules package-lock.json
```

Rebuild:

```bash
cd /opt/project-time-platform/app/project-time-platform
chmod +x deployment/rocky-linux/build-frontend.sh
./deployment/rocky-linux/build-frontend.sh
```

## Validation

Confirm Node.js is version 18 or higher:

```bash
node --version
npm --version
```

Expected:

```text
v18.x or v20.x or higher
```

## Security Note

Do not open Vite's development port publicly. Keep frontend development bound to localhost and use SSH port forwarding for browser testing.
