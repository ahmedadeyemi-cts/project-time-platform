# US Signal Branding and Theme Toggle

## Purpose

This document records the first frontend branding update for the Project Time Platform.

## Updates Included

The React frontend now includes:

```text
US Signal-style brand header
primary navigation
light mode
dark mode
persistent theme preference
weekly timesheet shell using API data
non-project category display
work location display
utilization target cards
workflow module cards
```

## Files Updated

```text
src/frontend/project-time-web/src/App.jsx
src/frontend/project-time-web/src/styles.css
src/frontend/project-time-web/index.html
deployment/rocky-linux/serve-frontend-local.py
```

## Local Frontend Server Improvement

The temporary Python local frontend server now supports both:

```text
GET
HEAD
```

This avoids a `501 Unsupported method ('HEAD')` response when validating with:

```bash
curl -I http://127.0.0.1:5173/
```

## Logo Note

The current frontend uses a US Signal-style text lockup and signal mark for development branding.

For production, replace this with the approved official US Signal logo asset supplied by the organization.

## Light and Dark Mode

The frontend stores the selected theme in browser local storage:

```text
ptp-theme
```

The theme is applied to:

```text
document.documentElement.dataset.theme
```

Supported values:

```text
light
dark
```

## Validation Steps

After pulling the update, rebuild the frontend and restart the local frontend server:

```bash
cd /opt/project-time-platform/app/project-time-platform

git restore deployment/rocky-linux/apply-migration-002.sh || true
git restore deployment/rocky-linux/apply-migration-003.sh || true
git restore deployment/rocky-linux/apply-migration-004.sh || true
git restore deployment/rocky-linux/apply-migration-005.sh || true
git restore deployment/rocky-linux/install-api-systemd-service.sh || true
git restore deployment/rocky-linux/build-frontend.sh || true

GIT_SSH_COMMAND='ssh -i ~/.ssh/github_project_time_platform -o IdentitiesOnly=yes' \
git pull

./deployment/rocky-linux/build-frontend.sh

pkill -f 'serve-frontend-local.py' || true
chmod +x deployment/rocky-linux/serve-frontend-local.sh
./deployment/rocky-linux/serve-frontend-local.sh
```

From another SSH session:

```bash
curl -I http://127.0.0.1:5173/
curl http://127.0.0.1:5173/api/version
```

From the Mac browser through SSH tunnel:

```text
http://127.0.0.1:5173/
```
