# Automatic first AI draft

Open **FlowHive → select a project → Planner or AI Planning Workspace → AI Planner settings**, beneath document readiness.

The assigned PM, an authorized PM lead, or an administrator can enable **Automatically create the first AI plan**. Other project members see status and timers. View-As sessions cannot change this setting.

Administrators also see **Administrator default for new projects → Enable for new projects**. This is initially off. Enabling it records the administrator and a cutoff timestamp; only projects created after that cutoff can inherit the default. Existing projects and explicit project opt-outs are preserved. Turning the default off stops future enrollment; use the project setting to stop a project already enrolled.

## What happens after opting in

1. The durable worker waits for an active project, an assigned active PM, a start date, and an approved, prepared current SOW and supporting documents. Missing documents or dates do not consume the generation deadline. Existing document association preparation remains the first preparation path; automation can also enqueue eligible unprepared documents through that same service.
2. Once ready, one transaction claims the first run. Concurrent API instances cannot create duplicate first-draft runs. The real authorizing user's current permission is checked before queueing and before subsequent phases and saving.
3. The existing sequential planner uses pinned evidence for Plan, Design, Implement, Validate, and Release. Overall and phase timers use persisted timestamps. Users can leave and return while generation continues.
4. The result is a working draft for PM review, never an automatically approved baseline. A pre-existing plan or run prevents automatic generation. A plan saved during generation is preserved.
5. Completion queues one `FLOWHIVE_FIRST_DRAFT_READY` event for Module 065. Module 065 retains channel, recipient, retry and delivery-boundary authority. Pending notices are suppressed if the PM, saved draft or project status is no longer current. This does not enable Teams or production email delivery; the new policy defaults to the existing Test-only boundary.

Turning the project setting off cancels its active automatic run and prevents late phases from being applied. Re-enabling after cancellation or failure does not silently start another run. Use the explicit AI Planner action for a reviewed retry. Closure uses the existing archive and late-write guards.

## Installation and evidence

Migration 122 requires sequential checkpoint migration 121. It adds project preferences, prospective defaults and an audit log; installation opts no project in. The protected project-planning migration job applies and verifies both migrations. Rollback preserves consent, audit and plan history.

Coverage includes actual API compilation, real PostgreSQL migration/permission/concurrency/notification tests in CI, and an actual React component browser fixture for controls, role visibility, timers, stale responses and mobile geometry. Synthetic tests do not constitute live model-provider or Teams delivery qualification. Protected UAT deployment must be verified separately at its installed revision.
