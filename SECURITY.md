# ProjectPulse Security Policy

## Supported versions

Security fixes are developed against the current protected `main` branch and the currently deployed Test release. Production is supported only after a release has passed the full source, migration, deployment, and role-based validation gates.

## Reporting a vulnerability

Do not open a public issue containing exploit details, credentials, tokens, customer information, internal hostnames, or screenshots with sensitive data.

Use GitHub's private vulnerability reporting or a private security advisory for this repository. Include:

- the affected route, workflow, module, or deployment component;
- the minimum role needed to reproduce the issue;
- the expected and actual result;
- sanitized reproduction steps;
- whether Test or Production is affected;
- any relevant correlation ID, without session tokens or credentials.

## Response priorities

- **P0:** active credential exposure, unauthenticated compromise, arbitrary privileged write, remote code execution, or organization-wide data exposure;
- **P1:** privilege escalation, object-scope bypass, sensitive-data disclosure, or deployment-control bypass;
- **P2:** defense-in-depth weakness, dependency vulnerability, hardening gap, or security-observability defect;
- **P3:** low-risk hygiene or documentation correction.

P0 and P1 findings block deployment until corrected or explicitly risk-accepted by the security owner.

## Security boundaries

ProjectPulse security changes must preserve these invariants:

- server-side authorization is authoritative;
- Super Administrator grants and modifications are Super Administrator-only;
- View-As is read-only;
- object-level scope is checked for projects, users, documents, approvals, and reports;
- secrets are write-only and never returned by APIs;
- deployments use exact source commits and immutable image digests;
- database migrations are checksum-pinned, additive where possible, independently verified, and never executed from unreviewed feature branches;
- security-sensitive changes are auditable and fail closed when authorization dependencies are unavailable.

## Disclosure

Allow time for validation and deployment before public disclosure. Never include live secrets or personal data in a report.
