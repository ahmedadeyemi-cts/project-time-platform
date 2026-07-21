# Module 997 — Security Operations, Threat Intelligence & Response Center

## Operational activation

Module 997 is the ProjectPulse-native security operations center. It reads
authentication events, active/revoked sessions, platform audit events, stored
alerts, and durable incident records. Authorized analysts can declare and
acknowledge incidents, preserve a timeline, start a Module 998 diagnostic
session, and prepare a controlled containment request.

The operational schema is supplied by migration
`033_security_diagnostics_native_operations.sql`. Until that migration is
applied, the API returns `503 operational_schema_unavailable` and names the
missing migration.

## Available now

- Live ProjectPulse authentication-failure and session evidence.
- Persistent security alerts, incidents, owners, states, and timelines.
- Incident-to-diagnostic handoff to Module 998.
- Dual-controlled containment requests with immutable audit evidence.
- Native ProjectPulse session revocation when the action is approved and
  `PROJECTPULSE_SECURITY_NATIVE_SESSION_REVOCATION_ENABLED=true`.
- Exact configuration guidance for actions that still need an external adapter.

## Adapter-gated capabilities

Entra suspension, role restriction, integration quarantine, WAF/network
blocking, endpoint isolation, external notification, evidence export, and AI
analysis remain unavailable until their owning adapter, permission, redaction,
rollback, and production authority are separately configured. An unavailable
adapter returns HTTP 423 and never pretends that an action executed.

Actual-session authority is mandatory. View-As is read-only and never grants
security response authority.
