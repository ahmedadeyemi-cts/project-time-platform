# Module 998 — System Diagnostic & Controlled Remediation Center

## Purpose

Module 998 implements the governed ProjectPulse diagnostic and remediation
control plane defined by tracker v1.8. It unifies safe system observations,
issue classification, ownership links, runbook guidance, evidence rules, and a
complete controlled-remediation lifecycle without enabling production action.

## Governed checkpoint

| Field | Value |
|---|---|
| Task type | New module |
| Module | 998 |
| Route | `system-diagnostics` |
| Backend prefix | `/api/system-diagnostics` |
| Source base | `origin/main@3d9a3dca8af479c854dc4c4a9294bc8aad273074` |
| Required checkpoint | `48421d5ba1584d64fc3bd043304c003eff1dc27b` (verified ancestor) |
| Branch | `feature/module-998-system-diagnostic-remediation-20260720` |
| Owner | Central ProjectPulse module governance |
| Source phase | Validated complete fail-closed source checkpoint; local commit prepared, remote publication pending |
| Runtime phase | Not merged, deployed, or portal-verified |

## Included outcome

- Administrator-authorized diagnostic overview, check registry, issue contract,
  evidence policy, remediation policy, and runbook APIs.
- Direct checks limited to the authenticated session, server authorization, and
  database `SELECT 1`.
- Delegated health ownership for existing operational modules; unknown or
  delegated status is never represented as healthy.
- Sanitized issue severity and response-expectation model.
- Complete prepare → approve → stage → promote → verify → rollback → close
  remediation lifecycle contract.
- Registered AI and remediation operation endpoints that return HTTP 423 before
  reading a body or invoking any adapter.
- US Signal-branded, responsive frontend with read-only diagnostic views and
  visibly disabled execution controls.
- Module validator, production build wiring, container validation context, and
  central catalog/register/tracker records.

## Explicit fail-closed boundaries

Module 998 does not execute:

- production remediation or containment;
- telemetry, security, cloud, database, mail, or other external connectors;
- external notifications;
- AI analysis or provider routing;
- deployment promotion or rollback;
- raw-log retrieval, evidence export, or secret access;
- database writes, schema changes, Azure/Entra changes, Cloudflare changes, or
  SMTP changes.

## Relationship to Module 997

Module 998 owns diagnostic aggregation and controlled-remediation governance.
Module 997 owns the future authoritative security operations, threat
intelligence, incident, containment, and response control plane. Module 998 may
display sanitized, approved Module 997 signals later, but it cannot perform a
security containment action.

## Completion interpretation

Passing source and build validation means the module is ready for a draft PR.
It does not mean production telemetry exists or remediation is enabled. Merge,
deployment, portal acceptance, adapters, secrets, and all execution authority
remain separate gates.
