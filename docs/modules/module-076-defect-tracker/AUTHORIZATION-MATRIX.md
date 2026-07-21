# Module 076 Authorization Matrix

Module 076 separates visibility, reporting, assignment, triage, resolution, integration, and external delivery. Server authority comes from the actual ProjectPulse session. Administrator View-As is read-only and never grants a write.

| Actor | View scope | Report | Comment | Update assigned defect | Reassign | Resolve/close | Configure integration |
|---|---|---:|---:|---:|---:|---:|---:|
| Authenticated user | Own reported or assigned | Yes after activation | Own/assigned after activation | Assigned only after activation | No | Assigned only after activation | No |
| Ahmed/default owner | All | Yes | Yes | Yes | Yes | Yes | No implicit secret access |
| Manager / Engineering Manager | All | Yes | Yes | Yes | Yes | Yes | No implicit secret access |
| Project Manager / Project Management | All | Yes | Yes | Yes | Yes | Yes | No implicit secret access |
| Project Team Coordinator | All | Yes | Yes | Yes | Yes | Yes | No implicit secret access |
| Administrator / Super Administrator | All | Yes | Yes | Yes | Yes | Yes | Source configuration only under separate authority |
| Administrator View-As | Effective-user read scope | No | No | No | No | No | No |
| Signed GitHub adapter | Allowlisted event scope | Intake/reconcile only after activation | Reconcile only | Reconcile only | Assignment event only | Close/reopen event only | No |
| Claude or ChatGPT | No direct application authority | GitHub issue only | GitHub issue only | No direct authority | No | No | No |

Canonical permissions for a future role seed are `VIEW_ALL_DEFECTS` and `MANAGE_DEFECTS`. This source does not modify role, permission, or database tables. Established administrator permissions (`SYSTEM_ADMINISTRATION`, `MANAGE_ALL`) remain recognized.

Notification recipients are resolved server-side from active manager roles. The browser never selects or supplies the manager distribution list.
