# Security and Roles

## 1. Purpose

This document defines the initial security, role, permission, and access-scope model for the platform.

## 2. Authentication

Authentication will use Microsoft Entra ID. The application will not store user passwords.

## 3. Authorization Model

The application will use both role-based and scope-based authorization.

A user's access will depend on:

- Their assigned role.
- Their access scope.
- Their assigned manager/team/project relationships.
- Their explicit permission overrides, if any.

## 4. Roles

| Role | Scope | Description |
|---|---|---|
| Engineer | Self | Enters time and views own utilization |
| Team Lead | Assigned team | Views assigned team members but cannot approve time |
| Manager | Direct reports | Approves or declines engineer time |
| Project Manager | Assigned projects | Manages project tasks and approves project/task time |
| Accounting | Reconciliation scope | Reconciles approved time and supports month-end close |
| Organizational Admin | Organization-wide | Views and manages operational data across the organization |
| System Admin | System-wide configuration | Manages app configuration, identity, roles, and settings |
| Super Admin | Full access | Emergency access with required audit trail |

## 5. Approval Separation

Approval responsibilities must remain separated:

```text
Manager = approves engineer/time accuracy
Project Manager = approves project/task/billing accuracy
Accounting = reconciles and closes billing period
```

## 6. Team Lead Restriction

Team Leads can view assigned team members but must not approve time unless they are separately assigned a Manager or Project Manager role.

## 7. Organizational Admin

Organizational Admins can view organization-wide data and make operational changes. Approval override should be configurable and should require audit comments.

## 8. Critical Audit Events

The system must audit:

- Login activity
- Role assignment changes
- Manager assignment changes
- Team Lead assignment changes
- Project Manager assignment changes
- Project/task changes
- Time entry changes
- Manager approvals/declines
- Project Manager approvals/declines
- Accounting reconciliation
- Period lock/reopen events
- Utilization rule changes
- Notification preference changes

## 9. Least Privilege Principle

Users should receive the minimum permissions required to perform their business role.
