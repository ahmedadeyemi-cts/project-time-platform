# Module 071 API Contract

## Authenticated read endpoints

| Method | Route | Result |
|---|---|---|
| `GET` | `/api/oncall-scheduling/capabilities` | Authorization, governed links, schedule, notification, public API, and persistence metadata |
| `GET` | `/api/oncall-scheduling/schedule` | Complete schedule for every authenticated Pulse user |
| `GET` | `/api/oncall-scheduling/roster` | Rotation roster for every authenticated Pulse user |
| `GET` | `/api/oncall-scheduling/history` | Schedule snapshot history |
| `GET` | `/api/oncall-scheduling/identity-options` | Active engineering identities; restricted to approved editors |

## Protected management endpoints

Every mutation uses the actual Pulse session. Approved editors are `MANAGER`, `PROJECT_TEAM_COORDINATOR`, canonical `ENGINEERING_LEAD`, legacy `ENGINEERING_TEAM_LEAD`, `SUPER_ADMINISTRATOR`, and legacy `ADMINISTRATOR`. View-As never transfers authority.

| Method | Route | Result |
|---|---|---|
| `PUT` | `/api/oncall-scheduling/schedule` | Validate and save assignment/date changes |
| `PUT` | `/api/oncall-scheduling/roster` | Save the department rotation roster |
| `POST` | `/api/oncall-scheduling/autogenerate` | Return an unsaved Friday rotation preview |
| `POST` | `/api/oncall-scheduling/history/restore` | Restore a selected schedule snapshot |

Auto-generation never persists automatically. An approved editor must review the generated entries and explicitly save the schedule.

## Public schedule access

- Direct unauthenticated page: `https://oncall.onenecklab.com/`
- Engineer payment form: `https://forms.cloud.microsoft/Pages/ResponsePage.aspx?id=2kFZU3Lai0qDeJg6VL7DQtvfUo2dqAlEkjfnG3izqQFUQ0NXTlQ5TEtERzE0RzNHN0tNMjJNWThWRSQlQCN0PWcu`
- `GET /api/public/v1/oncall/current`
- `GET /api/public/v1/oncall/current?department=collaboration`
- `GET /api/public/v1/oncall/schedule`

Public schedule surfaces are GET-only and briefly cacheable. They expose on-call assignments only and never expose OneAssist customer PIN data.

## Schedule shape

```json
{
  "version": 1,
  "tz": "America/Chicago",
  "entries": [
    {
      "id": "stable-entry-id",
      "startISO": "2026-07-24T16:00:00",
      "endISO": "2026-07-31T07:00:00",
      "departments": {
        "collaboration": {
          "userId": "stable-pulse-user-guid",
          "name": "Current identity display name",
          "email": "current identity email",
          "phone": "routing contact number"
        }
      }
    }
  ]
}
```

Legacy assignments without `userId` remain readable. Newly selected identities carry active stable Pulse GUIDs.

## Error and data boundary

Raw exceptions, connection strings, provider secrets, and OneAssist PINs are excluded from public schedule responses. Authenticated mutations fail closed when the actual session lacks an approved editor role or when View-As is active.
