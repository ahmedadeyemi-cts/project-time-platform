# Project Time Platform

This repository contains the source code, documentation, deployment scripts, database migrations, and operational runbooks for the Project Time, Utilization, Approval, Project Management, and Accounting Reconciliation Platform.

## Purpose

The platform is being designed to support:

- Engineer time entry
- Project and task assignment
- Manager approval and decline workflow
- Project Manager project/task approval workflow
- Accounting reconciliation and month-end close
- Quarterly utilization tracking
- Monthly and quarterly utilization notifications
- Microsoft Entra ID authentication
- Self-hosted deployment on Rocky Linux

## Guiding Principle

Every process must be documented, version-controlled, testable, and reproducible.

No major implementation, deployment, upgrade, database change, or configuration change should occur without documentation in the `docs/`, `database/`, or `deployment/` folders.

## Planned Technology Stack

| Layer | Planned Platform |
|---|---|
| Operating System | Rocky Linux |
| Authentication | Microsoft Entra ID |
| Backend | .NET / ASP.NET Core |
| Frontend | React |
| Database | PostgreSQL |
| Reverse Proxy | Caddy or NGINX |
| Containers | Podman |
| Background Jobs | Quartz.NET |
| Reporting | Built-in dashboards first; Apache Superset later if needed |
| Source Control | GitHub |

## Repository Structure

```text
project-time-platform/
├── docs/
├── src/
│   ├── backend/
│   └── frontend/
├── database/
│   ├── migrations/
│   ├── rollback/
│   └── seed-data/
├── deployment/
│   ├── rocky-linux/
│   ├── podman/
│   └── caddy/
└── tests/
```

## Documentation Index

- `docs/00-running-implementation-document.md`
- `docs/01-business-requirements.md`
- `docs/02-architecture.md`
- `docs/03-security-and-roles.md`
- `docs/04-database-design.md`
- `docs/05-rocky-linux-setup-runbook.md`
- `docs/06-backup-restore-runbook.md`
- `docs/07-upgrade-runbook.md`
- `docs/08-test-plan.md`

## Current Status

Status: Planning and foundation setup.

The first priority is documentation, repository structure, and reproducible deployment planning before application coding begins.
