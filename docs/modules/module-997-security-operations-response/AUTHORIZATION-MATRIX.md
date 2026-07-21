# Module 997 Authorization Matrix

Authority is evaluated from `ProjectPulseActualUserId` or the normal
`ProjectPulseSessionUserId`. View-As never transfers authority.

| Capability | Required authority | Additional control |
|---|---|---|
| View telemetry, sessions, alerts, and incidents | Security/admin role, `VIEW_SECURITY_OPERATIONS`, `SYSTEM_ADMINISTRATION`, or `MANAGE_ALL` | Restricted security classification |
| Declare, acknowledge, and update incidents | `MANAGE_SECURITY_RESPONSE`, `MANAGE_ALL`, Super Administrator, or Incident Commander | View-As blocked and audited |
| Prepare containment | Management authority | Creates `awaiting_approval` only |
| Approve containment | Management authority | Requester/approver separation of duties enforced in API and database |
| Execute native session revocation | Approved request plus management authority | Explicit runtime switch and active-session target required |
| Execute external containment | Approved request plus owning adapter authority | HTTP 423 until adapter is configured |

The initiating actor cannot approve their own request. All incident and response
mutations write Module 997 audit evidence.
