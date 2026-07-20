# Module 066 — Authorization and Security

## Current source behavior

- All endpoints require an authenticated ProjectPulse session.
- Canonical portfolio rows are filtered on the backend using effective-user role,
  assignment, project-manager, reporting, team, and delegated-scope data.
- View-As never expands the dataset beyond the effective user's server scope.
- Computational plan/schedule/AI-request/artifact previews store nothing.
- Persistence and baselines always return HTTP 423.
- Customer artifact/link requests always return HTTP 423.

## Required future mutation authority

Before `IProjectFlowHivePlanRepository.WritesEnabled` can become true:

1. The server must load the actual actor and effective subject separately.
2. View-As must remain non-mutating.
3. Administrators and Project Team Coordinators may administer authorized scope.
4. Project Managers may edit plans only for projects they manage.
5. Engineering Team Leads may update authorized team scope.
6. Engineers may update only assigned execution fields, never approve/baseline.
7. Baseline establishment must route through Module 002 approval authority.
8. Every write must include actor, subject, project, plan version, reason,
   correlation ID, old/new checksum, and timestamp.

Role names alone are insufficient: project/assignment/team scope must be checked
for every mutation.

## AI security

- Module 066 must never instantiate `HttpClient` for an AI provider.
- Module 066 must never read Claude/OpenAI secrets or provider URLs.
- Only Module 064 may decide provider availability and routing.
- Claude is first, OpenAI second, and governed local fallback last.
- A safety refusal stops routing; it is not provider unavailability.
- Generated content remains a draft and cannot approve/baseline itself.

## Customer sharing

No sharing implementation is active. A future design must include customer and
plan isolation, approved-baseline checksum, restricted-field redaction,
single-purpose expiring token, revocation, access log, rate limiting, and explicit
delivery authorization. PINs, opaque links, or frontend filtering alone are not
an authorization boundary.
