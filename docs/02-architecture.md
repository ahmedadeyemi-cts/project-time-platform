# Architecture Document

## 1. Architecture Goal

The platform will be built as a self-hosted web application that can run on Rocky Linux using open-source or free software where possible while leveraging Microsoft Entra ID for authentication.

## 2. Target Architecture

```text
User Browser
   ↓
Cloudflare DNS / Optional Proxy
   ↓
Caddy or NGINX Reverse Proxy
   ↓
React Frontend
   ↓
.NET / ASP.NET Core API
   ↓
PostgreSQL Database
```

Scheduled jobs will run through a background worker service using Quartz.NET or a similar open-source scheduler.

## 3. Logical Components

| Component | Purpose |
|---|---|
| React Frontend | User interface for engineers, managers, PMs, accounting, and admins |
| .NET API | Business logic, workflow rules, RBAC, and data access |
| PostgreSQL | Stores users, roles, projects, tasks, time entries, approvals, utilization, notifications, and audit logs |
| Background Worker | Runs monthly/quarterly utilization calculations and notification jobs |
| Microsoft Entra ID | Authentication and SSO |
| Reverse Proxy | HTTPS, routing, and inbound web traffic management |
| Podman | Container runtime on Rocky Linux |

## 4. Deployment Model

The preferred end-state deployment is:

```text
Rocky Linux Server
├── Podman containers
│   ├── frontend
│   ├── backend-api
│   ├── background-worker
│   └── postgres
├── Caddy or NGINX
└── systemd service definitions
```

## 5. Environments

| Environment | Purpose |
|---|---|
| Development | Local development and early testing |
| Staging | Upgrade and release validation using production-like data |
| Production | Live system |

## 6. Upgrade Safety Architecture

The system must be designed so upgrades are tested before production. This requires:

- Containerized application components.
- Version-pinned dependencies.
- Database migration scripts.
- Regression tests.
- Staging environment.
- Backup and restore runbook.
- Upgrade runbook.
- Post-upgrade validation checklist.

## 7. Authentication Architecture

Authentication will use Microsoft Entra ID through OpenID Connect.

The application will maintain local application roles and permission scopes. Entra ID will verify identity; the application will decide what each authenticated user can access.

## 8. Authorization Architecture

Authorization will be role-based and scope-based.

Examples:

- Engineer: self only.
- Team Lead: assigned team visibility only.
- Manager: assigned direct reports.
- Project Manager: assigned projects.
- Accounting: reconciliation scope.
- Organizational Admin: organization-wide operational access.
- System Admin: configuration access.

## 9. Data Flow Summary

1. User signs in using Microsoft Entra ID.
2. Application validates the token.
3. Application maps the user to local profile and roles.
4. User performs role-specific actions.
5. Business events are stored in PostgreSQL.
6. Critical actions are logged to the audit table.
7. Background jobs send notifications and utilization summaries.
