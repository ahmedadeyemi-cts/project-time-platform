# Module 998 Authorization Matrix

Authority always comes from `ProjectPulseActualUserId`, falling back to the
normal `ProjectPulseSessionUserId`. `ProjectPulseEffectiveUserId` is not an
authority source, so View-As never transfers diagnostic or remediation access.

| Capability | Super Administrator | Administrator | Delegated `VIEW_SYSTEM_DIAGNOSTICS` | Delegated `MANAGE_SYSTEM_REMEDIATION` | Other authenticated roles |
|---|---:|---:|---:|---:|---:|
| View safe diagnostic APIs | Yes | Yes | Yes | Yes | No |
| View evidence and remediation policy | Yes | Yes | Yes | Yes | No |
| View guidance-only runbooks | Yes | Yes | Yes | Yes | No |
| Request remediation | Contract only | Contract only | No | Contract only | No |
| Approve, stage, promote, or rollback | Locked | Locked | Locked | Locked | Locked |
| Run AI diagnostic analysis | Locked | Locked | Locked | Locked | Locked |
| Access secrets or raw logs | No | No | No | No | No |

`SYSTEM_ADMINISTRATION` and `MANAGE_ALL` also grant read access. Only a Super
Administrator, `MANAGE_SYSTEM_REMEDIATION`, or `MANAGE_ALL` may be represented
as a future remediation requester. That does not enable execution.

All production actions still require a separately approved adapter,
authorization record, separated approval, durable audit, staging evidence, and
explicit production authority.
