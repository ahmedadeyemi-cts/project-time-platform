# Module 071 Authorization Matrix

| Capability | Manager | Engineering Lead | Project Team Coordinator | Platform Administrator | Other authenticated users | Public client |
|---|---:|---:|---:|---:|---:|---:|
| View schedule | Yes | Yes | Yes | Yes | Yes | Yes |
| View roster and history | Yes | Yes | Yes | Yes | Yes | No |
| Add/edit/delete schedule entries | Yes | Yes | Yes | Yes | No | No |
| Change dates and assigned identities | Yes | Yes | Yes | Yes | No | No |
| Manage rotation roster | Yes | Yes | Yes | Yes | No | No |
| Generate rotation preview | Yes | Yes | Yes | Yes | No | No |
| Restore schedule history | Yes | Yes | Yes | Yes | No | No |
| View OneAssist PINs | Yes, after sign-in | Yes, after sign-in | Yes, after sign-in | Yes, after sign-in | Yes, after sign-in | No |

## Enforcement rules

- Canonical `ENGINEERING_LEAD` and legacy `ENGINEERING_TEAM_LEAD` are accepted during role-model convergence.
- `PROJECT_TEAM_COORDINATOR`, `MANAGER`, `SUPER_ADMINISTRATOR`, and legacy `ADMINISTRATOR` have management authority.
- All mutations use the actual Pulse user; View-As is read-only.
- Frontend controls mirror but never replace backend enforcement.
- The governed permission label is `MANAGE_ONCALL_SCHEDULE`.

## Public access boundary

The direct public schedule and the three versioned public On-Call GET routes expose schedule assignments only. They do not expose the Module 072 OneAssist customer directory or any OneAssist PIN.
