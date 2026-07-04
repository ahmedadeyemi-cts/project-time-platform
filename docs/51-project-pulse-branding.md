# Project Pulse Branding

## Purpose

This document records the branding decision to call the system `Project Pulse`.

## Date

2026-06-23

## Branding Decision

The system name is now:

```text
Project Pulse
```

The name is intended to be short, memorable, and easy for engineers and project stakeholders to reference.

## Brand Meaning

Project Pulse reflects the system's purpose:

- Time entry heartbeat
- Project-task assignment visibility
- Utilization awareness
- Approval workflow status
- Accounting reconciliation readiness
- Operational reporting signals

## Files Updated

- `src/frontend/project-time-web/index.html`
- `src/frontend/project-time-web/package.json`
- `src/frontend/project-time-web/src/HelpAssistant.jsx`

## Patch Script Added

- `deployment/rocky-linux/apply-project-pulse-branding-patch.sh`

The patch script updates the main React app display labels to use Project Pulse.

## UI Label Direction

The preferred display should be:

```text
Project Pulse
Time • Approval • Utilization
```

For longer descriptions, use:

```text
Project Pulse: time, approval, utilization, and accounting workflow
```

## Status

Ready for validation.
