# Module 071 Authorization Matrix

| Capability | Manager | Engineering Team Lead | Administrator | Project Team Coordinator | All other authenticated users | Public client |
|---|---:|---:|---:|---:|---:|---:|
| View schedule | Yes | Yes | Yes | Yes | Yes | Through public GET API |
| View roster | Yes | Yes | Yes | Yes | Yes | No |
| View history | Yes | Yes | Yes | Yes | Yes | No |
| Add/edit/delete schedule entries | Yes | Yes | No | No | No | No |
| Change dates and identities | Yes | Yes | No | No | No | No |
| Manage rotation roster | Yes | Yes | No | No | No | No |
| Auto-generate schedule preview | Yes | Yes | No | No | No | No |
| Restore schedule history | Yes | Yes | No | No | No | No |

## Enforcement rules

- Management authorization is calculated from the actual ProjectPulse user, never the View-As identity.
- Only exact canonical role codes `MANAGER` and `ENGINEERING_TEAM_LEAD` grant management authority.
- `ADMINISTRATOR`, `SUPER_ADMINISTRATOR`, `SYSTEM_ADMINISTRATION`, and `MANAGE_ALL` do not implicitly grant Module 071 management authority.
- Frontend controls reflect the server result but never replace backend enforcement.
- The governed permission label is `MANAGE_ONCALL_SCHEDULE`.
