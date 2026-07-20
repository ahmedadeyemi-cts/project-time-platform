# Module 065 Capability Matrix

| RBAC-018 capability | Source implementation | Durable/runtime gate |
|---|---|---|
| Super Administrator / explicit delegation | Server enforced against actual user | Requires database availability |
| Tenant/client/application/type/version metadata | Implemented; Module 010 metadata is primary | Runtime configuration required |
| Last rotation, expiration, health warnings | Implemented at 30/14-day thresholds | Non-secret timestamps required |
| Write-only secret entry | Raw bounded transport and zeroable lease implemented | External authorization + approved adapter + step-up required |
| Step-up authentication | Five-minute server-context contract implemented | Approved middleware not supplied by this module |
| Optional dual approval | State and adapter contract implemented | Durable approval store not authorized |
| Token-acquisition test | Sanitized adapter contract implemented | Provider adapter/Azure call not authorized |
| Explicit activation | State and adapter contract implemented | Provider adapter/Azure call not authorized |
| Bounded overlap | 1–168 hour plan validation and state contract implemented | Durable orchestration not authorized |
| Previous-version rollback | Approved target/version adapter contract implemented | Credential store adapter not authorized |
| Sanitized immutable audit | Required/prohibited contract implemented | Append-only persistence not authorized |
| Secret/browser/log boundary | Enforced by frontend and response contracts | Adapter must pass separate redaction review |

Module 065 is complete as a governed source package. Durable provider, credential-store, approval, audit, and deployment operations remain accurately locked rather than simulated.
