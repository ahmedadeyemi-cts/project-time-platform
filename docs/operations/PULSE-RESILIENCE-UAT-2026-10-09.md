# Pulse Protected UAT resiliency and January 2027 release gate

## Incident reference
The October 9 outage was caused by the external Celar AI startup guard requiring a reachable Oracle endpoint. When `celarai.onenecklab.com:443` refused the connection, the API did not start and the web-to-API health proxy returned failures. Azure Application Gateway marked the web backend unhealthy and served HTTP 502.

## Required guardrails
- A missing **optional** AI runtime must degrade only AI capabilities. Never send internal user data to unapproved fallback providers.
- Invalid external AI policy, unapproved endpoints, unauthorized domains, and missing required secrets must continue to fail closed **for the AI capability**. Do not weaken release candidate fences, RBAC, SSO, or Production boundaries.
- Distinguish process liveness, core readiness, and individual dependency health. No external AI probe may gate core readiness.
- Deploy changes through reviewed pull requests and existing governed Protected Test release controllers only. Unhealthy candidate revisions must not receive traffic.
- Preserve last-known-good image digest and revision; verify rollback against independent core readiness endpoints before traffic promotion.

## UAT failure simulations (run only in Test with approval)
1. Disconnect the Celar provider in a candidate revision; confirm `/health/live`, `/health/ready`, `/health` and core authenticated API scenarios return expected responses. Verify AI features reject unavailable provider requests without unsafe fallback.
2. Restore the provider; confirm AI readiness state recovers without requiring a Pulse restart.
3. Submit an intentionally unhealthy candidate: confirm no production or Test live traffic is routed to it and the last healthy revision remains available.
4. Kill a replica: verify replacement while requests continue to succeed with multiple ready replicas.
5. Validate PostgreSQL access failure behavior, backup restoration, and transaction safety separately. Database availability is a core dependency and must not be masked as healthy.
6. Exercise App Gateway backend-health probe failure and recovery; confirm notifications and evidence.
7. Simulate 502 spikes and container restarts to validate alert delivery, ownership, acknowledgment, and closure.

## Operational release acceptance
- No core outage when any optional AI provider is unavailable.
- Candidate deployment cannot replace healthy live revisions on failed readiness, security, or data-integrity tests.
- Documented RTO/RPO backed by recovery drills, restore validation, and application/DB failover testing.
- Alerts for 5xx errors, backend unhealthy, API restart/crash-loop, and dependency unavailability with an identified escalation owner.
- Evidence of representative performance/load test, end-to-end business regression, security testing, and sign-off.
- January 2027 go-live remains conditional on business, security, and operational approvals.

## Status
PR #1284 is an application-level reliability change under CI review. The repository-wide release-scope validators currently require an approved, narrowly defined scope path for shared runtime and web proxy files. Never remove or bypass these checks just to merge this patch.
