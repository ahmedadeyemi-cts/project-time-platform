# Module 997 — Security Operations, Threat Intelligence & Response Center

## Purpose

Module 997 provides the governed ProjectPulse security-operations control plane:
sanitized readiness, alert and incident contracts, threat-intelligence handling,
control ownership, incident lifecycle, reporting boundaries, and explicit future
integration adapters.

## Source checkpoint

| Field | Value |
|---|---|
| Module | 997 |
| Route | `security-operations` |
| API prefix | `/api/security-operations` |
| Base | `main@3d9a3dca8af479c854dc4c4a9294bc8aad273074` |
| Required ancestor | `48421d5ba1584d64fc3bd043304c003eff1dc27b` |
| Branch | `feature/module-997-security-operations-response-20260720` |
| Contract | `2026-07-20.1` |
| Source phase | Validated complete fail-closed source checkpoint; remote publication pending |

## Included

- Actual-session, server-authorized restricted operations center.
- Sanitized security posture and ownership map.
- Non-authoritative alert and incident contracts that never equate missing
  telemetry with health.
- Threat-source, confidence, expiry, and handling policy.
- Eight-step incident response lifecycle and separation of duties.
- Restricted reporting, redaction, and evidence rules.
- Explicit connector inventory with every adapter disabled.
- US Signal-branded responsive frontend and protected source validator.

## Fail-closed boundary

This checkpoint makes no telemetry, threat-feed, AI, mail, cloud, Entra,
endpoint, firewall, ticketing, evidence-store, or secret-store call. It performs
no incident persistence, containment, eradication, recovery, notification,
evidence export, or case closure. Every mutation-shaped route returns HTTP 423
after actual-session authorization and before request-body processing.

## Module ownership

Module 997 owns security signal governance, threat-intelligence policy, incident
command, response controls, and security reporting. Existing identity,
resilience, delivery, AI, mail, and architecture modules keep their established
ownership. Module 998 owns controlled-remediation governance and is referenced
as a future handoff only; this branch does not depend on its unmerged source.

Passing source and build validation means the module is ready for a draft PR.
It does not mean merged, deployed, connected to security products, or authorized
to take security action.
