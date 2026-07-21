# Module 998 — System Diagnostic & Controlled Remediation Center

## Operational activation

Module 998 runs safe ProjectPulse-native diagnostic checks, persists sessions
and sanitized findings, correlates Module 997 incidents, ranks issues, previews
runbooks, enforces requester/approver separation, executes the native health
refresh action, and verifies the result with before/after evidence.

Migration `033_security_diagnostics_native_operations.sql` creates the session,
finding, remediation, security, and audit structures required by Modules 997 and
998. Until it is applied, operational endpoints fail closed with the migration
name.

## Available now

- Database/API path, authentication-failure, incident, containment, migration,
  session, and remediation-queue checks.
- Persistent diagnostic sessions and sanitized evidence.
- Direct incident handoff from Module 997.
- Runbook previews with target, impact, owner, adapter, and rollback guidance.
- Dual-controlled remediation workflow.
- Native `refresh_health_snapshot` execution and post-action verification.

## Adapter-gated automation

Service restart/scale, deployment rollback, integration replay, configuration
refresh, database repair, production rollback, and AI analysis require approved
owner adapters. An attempted execution returns HTTP 423 with the precise missing
configuration while retaining the plan and approval evidence.
