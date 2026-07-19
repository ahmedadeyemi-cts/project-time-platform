# Module 065 Authorization Matrix

| Capability | Super Administrator | Delegated `MANAGE_ENTRA_SECRET` | Administrator | Other role | View-As |
|---|---:|---:|---:|---:|---:|
| View non-secret metadata | Yes | Yes | No | No | Actual user's authority only |
| View readiness/workflow/audit contracts | Yes | Yes | No | No | Actual user's authority only |
| View usable client secret | Never | Never | Never | Never | Never |
| Prepare/approve/stage/test/activate/rollback | Only after all external and step-up gates | Only after all external and step-up gates | No | No | Blocked |

## Separation of duties

- A required second approval cannot be supplied by the initiating actor.
- Adapter persistence must record actual actor identifiers; effective View-As identity is informational only.
- `MANAGE_ALL`, `SYSTEM_ADMINISTRATION`, and the general `ADMINISTRATOR` role do not substitute for explicit `MANAGE_ENTRA_SECRET` delegation.
- Step-up is accepted only from server context items populated by approved authentication middleware. A request header cannot create step-up authority.
- The approved adapter must re-evaluate operation state, approver eligibility, and actor separation atomically before activation.
