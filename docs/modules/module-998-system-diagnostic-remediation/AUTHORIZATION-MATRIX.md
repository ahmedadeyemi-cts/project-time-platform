# Module 998 Authorization Matrix

| Capability | Required authority | Additional control |
|---|---|---|
| View checks, sessions, findings, and runbooks | Admin/security role, `VIEW_SYSTEM_DIAGNOSTICS`, `SYSTEM_ADMINISTRATION`, or `MANAGE_ALL` | Restricted operational metadata |
| Run and persist a diagnostic session | `MANAGE_SYSTEM_REMEDIATION`, Administrator, Super Administrator, or `MANAGE_ALL` | View-As blocked |
| Prepare or stage remediation | Management authority | Preview and audit evidence required |
| Approve remediation | Management authority | Requester/approver separation of duties |
| Execute native health refresh | Approved/staged request | Changes evidence only, then verification required |
| Execute production-changing runbook | Approved request plus owning adapter authority | HTTP 423 until adapter is configured |

Actual-session identity is mandatory. A View-As effective user is never an
authority source.
