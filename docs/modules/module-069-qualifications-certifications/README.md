# Module 069 — Qualifications & Certification Matrix

Module 069 provides the ProjectPulse workforce-capability matrix and a governed
self-service experience for each authenticated user’s own qualifications and
certifications. It uses the existing ProjectPulse identity and resource-profile
foundation; it does not create another employee directory.

## Package scope

- Role-scoped people, skills, and certification matrix.
- Self, team, and organization visibility derived server-side.
- Identity-backed name, email, team, department, and primary function.
- Qualification category, name, competency, experience, and effective dates.
- Current, expiring-within-90-days, expired, and unrecorded states.
- Search, category, and lifecycle filters.
- Coverage view for identities without qualification records.
- Authenticated self-service creation and update of the user’s own records.
- No delete endpoint and no write authority over another user’s records.

## API ownership

The original matrix endpoints remain read-only:

```text
GET /api/qualifications/capabilities
GET /api/qualifications/matrix
```

The self-service extension adds:

```text
GET  /api/qualifications/self-service
POST /api/qualifications/self-service
PUT  /api/qualifications/self-service/{qualificationId}
```

The POST and PUT endpoints always bind `user_id` to the actual authenticated
ProjectPulse user. A caller cannot supply a different user ID. Administrator
View-As remains read-only, and the underlying Super Administrator session does
not transfer write authority to the selected effective identity.

## Project Management role

Project Managers and Project Management Leads may:

- view Module 069 within their normal role scope;
- add their own qualification or certification;
- update their own record; and
- record an issue/effective date, optional expiration date, competency, and
  years of experience.

This does not grant organization-wide qualification administration. Editing
another user remains separately governed.

## Governance

| Field | Value |
|---|---|
| Module | `069` |
| Route | `qualifications-certifications` |
| Identity key | Stable `app_users.user_id` |
| Matrix scope | Organization, team, or self from server authorization |
| Self-service scope | Own authenticated user only |
| View-As | Read-only |
| Delete | Not exposed |
| Migration | `062_project_management_billing_role_access_repair` adds role permissions; existing qualification storage remains authoritative |
| External systems | No Azure, Entra, provider, or notification operation |

## Security boundaries

- Every request requires a valid ProjectPulse session.
- The read matrix remains parameterized and role scoped.
- Self-service POST and PUT ignore client-selected identity and use the actual
  session user ID.
- Mutation is denied whenever View-As is active or actual and effective users
  differ.
- Input length, experience range, and date ordering are validated server-side.
- Audit evidence is written when the shared module-audit table is available.
- Secret, token, credential, and provider values are never accepted or returned.

## Still deferred

Issuer evidence, credential identifiers, document upload, renewal
acknowledgement, scheduled expiration notifications, organization-wide edit,
and delete require separately reviewed persistence and workflow controls.
