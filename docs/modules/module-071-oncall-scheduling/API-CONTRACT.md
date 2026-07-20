# Module 071 API Contract

## Authenticated read endpoints

| Method | Route | Result |
|---|---|---|
| `GET` | `/api/oncall-scheduling/capabilities` | Authorization, schedule, notification, public API, and persistence metadata |
| `GET` | `/api/oncall-scheduling/schedule` | Complete schedule for every authenticated ProjectPulse user |
| `GET` | `/api/oncall-scheduling/roster` | Rotation roster for every authenticated ProjectPulse user |
| `GET` | `/api/oncall-scheduling/history` | Schedule snapshot history; restore capability is returned separately |
| `GET` | `/api/oncall-scheduling/identity-options` | Active engineering identities; restricted to Module 071 managers |

## Protected management endpoints

Every mutation uses the actual ProjectPulse session and requires canonical `MANAGER` or `ENGINEERING_TEAM_LEAD`. View-As never transfers authority.

| Method | Route | Result |
|---|---|---|
| `PUT` | `/api/oncall-scheduling/schedule` | Validates identity IDs and America/Chicago windows, then saves through the compatibility adapter |
| `PUT` | `/api/oncall-scheduling/roster` | Saves the department rotation roster |
| `POST` | `/api/oncall-scheduling/autogenerate` | Returns an unsaved Friday rotation preview |
| `POST` | `/api/oncall-scheduling/history/restore` | Restores a selected upstream schedule snapshot |

Auto-generation never persists automatically. A manager must review the generated entries and explicitly call the schedule save endpoint.

## Public routing endpoints

| Method | Route | Result |
|---|---|---|
| `GET` | `/api/public/v1/oncall/current` | Current window and all assigned departments |
| `GET` | `/api/public/v1/oncall/current?department=collaboration` | Current assignment for one normalized department |
| `GET` | `/api/public/v1/oncall/schedule` | Public routing schedule |

Public endpoints are GET-only, set a short cache policy, allow cross-origin routing clients, and expose no mutation or retired external compatibility service credential.

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
          "userId": "stable-projectpulse-user-guid",
          "name": "Current identity display name",
          "email": "current identity email",
          "phone": "routing contact number"
        }
      }
    }
  ]
}
```

Legacy assignments without `userId` remain readable. Every newly selected identity carries a stable ProjectPulse GUID and is server-validated as active before save.

## Error boundary

Raw upstream responses, secrets, connection strings, and exception text are not returned. Upstream failures are normalized to `dependency_unavailable` or `oncall_source_unavailable` responses.

## PROJECTPULSE_NATIVE_POSTGRESQL_MIGRATION_031

- Source parent: `603538ad408b70b3e6a26ff2f4f162599fa1cabf`
- Migration source: `database/migrations/031_modules_071_072_native_persistence.sql`
- Rollback source: `database/rollback/031_modules_071_072_native_persistence_rollback.sql`
- Module 071 persistence: ProjectPulse PostgreSQL schedule, roster, acknowledgement, and history tables
- Module 072 persistence: ProjectPulse PostgreSQL routing directory and immutable revision tables
- Platform Administrator authority: explicit
- View-As write authority: blocked
- External compatibility runtime dependency: removed
- Migration applied: no
- Database changed: no
- Deployment performed: no
