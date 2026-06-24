# Project Pulse One-Time Stabilization Plan

## Purpose

This document records the one-time stabilization script created to consolidate the current patch chain, repair local duplicate patch side effects, rebuild the API/frontend, and install a restricted public frontend validation service.

## Script

```text
deployment/rocky-linux/project-pulse-one-time-stabilize.sh
```

## What the Script Does

1. Stops the existing API service and any frontend Python servers.
2. Backs up the current local `Program.cs`, `App.jsx`, and public frontend server script.
3. Resets the local repository to `origin/main` to remove duplicate local patch side effects.
4. Applies migrations 006 through 010 when present.
5. Applies the current patch and repair scripts in a controlled order.
6. Applies final source guardrails:
   - Aligns the development user identity to `ahmed.adeyemi@ussignal.com`.
   - Sets the expected API version to `0.4.8`.
   - Removes repeated duplicate `assignedOpenTasks` declarations.
   - Repairs the known `DBNull` conditional expression issue.
7. Rewrites the restricted public frontend proxy server to allow:
   - The provided public source IP.
   - `127.0.0.1`.
   - `::1`.
8. Removes stale frontend build output.
9. Publishes and restarts the API through systemd.
10. Rebuilds the frontend.
11. Configures the local OS firewall to allow the public frontend port from the approved source IP only.
12. Installs and starts a systemd service named `projecttime-frontend-public.service`.
13. Runs validation checks for:
   - API version.
   - Open Tasks.
   - Debug time entries.
   - Frontend local response.
   - Listening ports.
   - Systemd service status.

## Command

```bash
cd /opt/project-time-platform/app/project-time-platform

GIT_SSH_COMMAND='ssh -i ~/.ssh/github_project_time_platform -o IdentitiesOnly=yes' \
git pull

chmod +x deployment/rocky-linux/project-pulse-one-time-stabilize.sh
./deployment/rocky-linux/project-pulse-one-time-stabilize.sh 45.19.161.17
```

## Expected Result

The API should report version `0.4.8` after the script completes.

The restricted frontend should listen on:

```text
0.0.0.0:5173
```

The API should remain private on:

```text
127.0.0.1:5080
```

## Public Access Reminder

If the public URL still does not work after the script completes, the remaining blocker is expected to be OCI networking. The OCI security list or NSG must allow:

```text
Source CIDR: 45.19.161.17/32
Protocol: TCP
Destination Port: 5173
```

Do not expose API port `5080` publicly.

## Next Validation Flow

After the one-time stabilization completes:

1. Open `http://167.234.223.32:5173/` from the approved public source IP.
2. Confirm Open Tasks shows the seven PSA project tasks.
3. Add `Foundation & Infrastructure`.
4. Enter 8.00 hours on one day.
5. Close the modal and confirm draft save.
6. Refresh the browser and confirm the time remains.
7. Click `Submit this day`.
8. Confirm the day becomes submitted.
9. Confirm `Unlock this day` appears for the submitted day.
10. Confirm the item appears in Approval Inbox.
11. Approve it as manager.
12. Confirm the approved time remains visible as read-only.

## Status

Ready for execution.
