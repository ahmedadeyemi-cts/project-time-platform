# Module 076 API Contract

Contract version: `2026-07-20.1`

## Authenticated read endpoints

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/defect-tracker/overview` | Summary, access, lifecycle, default assignee, ID, and activation policy |
| `GET` | `/api/defect-tracker/defects` | Server-scoped defect inventory contract and table columns |
| `GET` | `/api/defect-tracker/assignee-options` | Active Module 062 identities for an authorized reassignment dropdown |
| `GET` | `/api/defect-tracker/intake-policy` | Required fields, sources, date rules, and validation limits |
| `GET` | `/api/defect-tracker/notification-policy` | Manager-open and reporter-resolution mail policy |
| `GET` | `/api/defect-tracker/integration-policy` | Help, GitHub, Claude-through-GitHub, and ChatGPT-through-GitHub contract |

Until a durable repository is authorized, `/defects` returns an explicit `durable_defect_store_not_authorized` state and no live records. This is not evidence that production has zero defects.

## Registered fail-closed lifecycle routes

| Method | Route | Future purpose | Current result |
|---|---|---|---|
| `POST` | `/api/defect-tracker/report` | Create and number an internal report | `423 Locked` |
| `PATCH` | `/api/defect-tracker/defects/{defectId}` | Update governed fields and status | `423 Locked` |
| `POST` | `/api/defect-tracker/defects/{defectId}/reassign` | Change identity-backed assignee | `423 Locked` after authority check |
| `POST` | `/api/defect-tracker/defects/{defectId}/comments` | Append a comment | `423 Locked` |
| `POST` | `/api/defect-tracker/defects/{defectId}/resolve` | Record resolution and resolution date | `423 Locked` |
| `POST` | `/api/defect-tracker/integrations/github/events` | Reconcile signed GitHub events | `423 Locked` |

Locked routes do not read request bodies, allocate a defect ID, change an assignment, write a comment, set a resolution date, queue email, call GitHub, or execute AI.

## Durable record contract

```json
{
  "defectId": "DEF-2026-000001",
  "status": "Open",
  "title": "Dropdown does not load",
  "description": "Identity options do not appear.",
  "category": "User Interface",
  "priority": "High",
  "assignee": {
    "userId": "stable-projectpulse-user-guid",
    "displayName": "Ahmed Adeyemi",
    "email": "ahmed.adeyemi@ussignal.com"
  },
  "raisedBy": {
    "userId": "stable-projectpulse-user-guid",
    "displayName": "Reporter",
    "email": "reporter@ussignal.com"
  },
  "sourceChannel": "help",
  "affectedModule": "070",
  "affectedRoute": "capacity-pipeline-forecast",
  "dateAdded": "2026-07-20T18:00:00Z",
  "dateResolved": null,
  "resolutionMinutes": null,
  "comments": [],
  "githubIssue": null,
  "version": 1
}
```

`dateAdded`, `dateResolved`, and `resolutionMinutes` are server-owned. A future client must use optimistic concurrency and cannot submit or override these values.
