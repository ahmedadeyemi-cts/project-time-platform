# Approval routing and week/month review

## Behavior

| Work | Approval requirement |
| --- | --- |
| Project with an assigned PM | Employee's Manager, then assigned PM |
| Project with a coordinator and no PM | Employee's Manager, then assigned project coordinator |
| Project without either reviewer | Employee's Manager |
| Service Request, Internal Task, Presales Task | Employee's Manager, even if stored as a project or given a PM |
| Assigned PM/coordinator's own time on that project | Their Manager or PTC; never self-approval |

PTCs retain delegated authority at both approval stages. Existing administrator authority is preserved. A PC's scoped project-review permission does not grant organization-wide PTC authority. Manager decisions remain employee-day units; project decisions affect only the required entries on the assigned project. Mixed-day request/leave/other-project entries cannot be swept into a PM decision. PTC accounting release remains a separate existing step after approval, and is excluded from the new unapproved-time list.

## Interface

The existing weekly review and return/exception tabs remain. The new bottom section lists all authorized unapproved time for a selected week or calendar month, by approval stage. It loads beyond the first API page, shows counts and hours, and allows Select all week/month, individual selection, review, and confirmation. Months use entry work dates, including partial weeks at month boundaries. A batch is capped at 2,000 explicit units; larger backlogs clearly display a partial-batch notice and can be processed in successive reviewed batches. Newly arrived records are not silently added to a reviewed selection. Manager and PM approvals are separate actions, including for a PTC acting on behalf of both.

The legacy header now uses a responsive grid so its title, scope, toolbar, and exception note do not compete for four narrow columns. The new table and controls use the platform's light/dark theme variables.

## Security and consistency

- Database-backed active roles and reporting/project relationships determine scope, not client-provided roles.
- Self-approval is blocked in both candidate selection and the write dispatcher; PM entry locking also excludes self.
- Administrator View-As remains read-only. Old approval write routes remain retired; both API aliases use the same hardened handler.
- A server-generated review token binds each selected unit to its entry content and routing metadata; changed hours/details/assignments invalidate it.
- Stage/order, date-period, explicit-selection, and stale/unauthorized selection checks run on the server. A mixed valid/invalid selected batch is rejected before writing.
- Serializable transactions reload role authority inside the approval snapshot. Concurrent conflicting changes fail closed and require a refresh; there is no silent retry against changed time.
- Existing immutable stage, batch, and platform audit evidence is retained. Monthly audit evidence includes period start, end, and kind.
- One SQL routing function governs queue/write eligibility, accounting readiness, contract pending/approved totals, and project approval notifications. Approval reminders no longer label already-approved manager-only time as overdue project approval.

## Deployment

Apply `database/migrations/131_time_approval_routing.sql` after 060c and before the matching API. The existing private-network migration packaging script includes and checksum-verifies it. The migration is additive and reapplies safely. It changes the authoritative contract usage view without rewriting time or audit history. No release-controller permissions, environment approvals, authentication controls, or protected deployment workflows are changed.

For application rollback, retain the additive function and contract view so approved/pending usage is not lost. Rolling back the API restores its previous routing behavior; therefore pause approvals and review compatibility before doing so. This PR has not been deployed.

## Validation and remaining gate

- Actual production SQL: 44 routing/scope scenarios, covering PM/PC/no reviewer, SR/internal/presales, self-time, mixed days, unauthorized users, replay, calendar boundaries, notifications, and accounting guards.
- Existing contract regression: 41 cases, including migrations, snapshots, duplicate prevention, and rollback/reapply.
- Actual C# request/write guards: 18 negative cases without database mutations.
- Component interaction tests: complete pagination, explicit selection, confirmation, month filtering, stale failure, and View-As read-only.
- Existing approval-scope security regression, private-network migration packaging, backend and full frontend build.

Authenticated live inspection is not complete: the secure sign-in attempt was rejected by Pulse. The cloud browser also blocked the isolated local preview. No live time was approved or altered. Live role-based UAT and visual checks (desktop/mobile, light/dark) remain required before deployment readiness.
