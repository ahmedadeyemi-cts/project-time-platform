# Module 997 Authorization Matrix

Authorization is evaluated on the server from the actual ProjectPulse user ID.
View-As never grants or transfers security authority.

| Capability | Role or permission contract | Source behavior |
|---|---|---|
| View security center | `SUPER_ADMINISTRATOR`, `ADMINISTRATOR`, `SECURITY_ANALYST`, `SECURITY_OPERATIONS`, `SECURITY_INCIDENT_COMMANDER`, `VIEW_SECURITY_OPERATIONS`, `MANAGE_SECURITY_RESPONSE`, `SYSTEM_ADMINISTRATION`, or `MANAGE_ALL` | Read sanitized policy and readiness contracts |
| Request future response | `SUPER_ADMINISTRATOR`, `SECURITY_INCIDENT_COMMANDER`, `MANAGE_SECURITY_RESPONSE`, or `MANAGE_ALL` | Authority is reported; execution remains locked |
| Contain, eradicate, recover | Separate incident authority plus future approved adapter | HTTP 423; no body read or action |
| Notify or export | Separate communication/evidence authority | HTTP 423; no transmission |
| Configure connectors or secrets | Separate module and infrastructure authority | No endpoint exists |

Analysts cannot self-authorize containment. Incident commanders cannot bypass
evidence review, change control, business verification, or separately authorized
production execution.
