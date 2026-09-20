# Module 025 SA workspace: prepared Protected UAT rollout

PR 1123 prepares the next SA workspace increment. Passing PR checks demonstrates source and disposable-fixture behavior; it does not authorize deployment or demonstrate that a live tenant is configured correctly. Obtain approval for the exact reviewed commit before merge or dispatch. The existing Module025 supervisor listens to matching changes on `main`, so merging can initiate the protected UAT workflow.

## Existing release authority

The existing `projectpulse-deploy-test.yml` lane calls `build-and-run-module025-retention-migration-106.sh` only for `sow_role` or `sow_exports`. The runner still requires `workflow_dispatch`, `refs/heads/main`, target branch `main`, the exact checked-out release SHA, a Test-tagged API application, an immutable ACR image digest, and the existing private-network migration job. The controller, candidate manifest, environment approvals, credentials, notification transport policy, and Production infrastructure are unchanged.

This PR extends only the runner's migration payload, apply sequence, schema postconditions, and recorded migration list. Its original release authority is pinned byte-for-byte to main commit `373b37e9c76e430355ed2dc5f71ec6a133ead3aa` by `tests/module025-sa-rollout.test.py`. The production-readiness inventory keeps the new SQL entries at `review_required`; registration is not production admission.

## Migration sequence

The existing lane establishes the foundational application schema, including Module025 workspace099, retained documents106, project name109, Module065 notification orchestration064, and the CRM provider/source-authority prerequisites. The retained-document runner applies the following sequence, including safe replay of its existing prerequisites:

| Migration | Purpose | Required verification |
| --- | --- | --- |
| 106 | Confirmed versions, generation snapshots and SELL evidence | Retained tables and existing guard triggers |
| 110 | Deletion exception for untouched, ungenerated drafts | Existing evidence-mutation guard still recognizes the exception |
| 111 | Correct ConnectWise SELL identity | Correct provider row; retired connector remains disabled |
| 116 | Governed in-place ownership transfer | Same-transaction transfer evidence and retained transfer audit guard |
| 117 | Immutable manager template candidates | Candidate table and immutable-original trigger |
| 118 | Independent SA work tracking | Snapshot table, append-only and current-identity insert triggers |
| 119 | Temporary coverage and acknowledgement | Handoff table, metadata validation and mutation/truncate guards |
| 120 | Module065 handoff event policies | Four policies retain Module065 ownership and the Module025 producer contract |

Each SQL file is copied from the exact release source into the migration image and included in its checksum manifest. The entrypoint checks release identity and checksums before SQL execution, stops at the first failure, and checks the final schema before reporting success. Evidence is written to `module025-retention-migration.json` with the full applied set, source commit and immutable image digest. Existing per-migration transactions remain independent; an interrupted chain may leave earlier migrations applied. Resolve the failure and replay the same reviewed chain rather than assuming the whole chain rolled back.

Migration120 creates only policies, with the existing `test_only` delivery boundary. Replaying it preserves an administrator's existing policy choices. Applying migrations queues no notification events and sends no email or Teams message. A later ownership action creates a durable Module065 event; actual delivery follows Module065 configuration and recipient restrictions.

## PR verification

The preparation workflow uses an isolated PostgreSQL container with a unique generated password and loopback-only port. Every database fixture creates and drops its own database. It runs:

- Work tracking, transfer/coverage and notification backend behavior with the actual API implementation and real migrations.
- Template package validation and migration immutability/retention checks.
- Browser behavior for task review, work tracking, manager queues, handoffs and retained document actions.
- The actual generated migration entrypoint twice, including policy preservation and a zero-notification-event assertion. Negative cases reject the wrong release, wrong operation mode, changed SQL bytes, and a disabled audit trigger.

The export workflow separately verifies high-level SOW content, detailed task allocations, both GSD profiles, after-hours arithmetic and the existing download lifecycle. These fixtures use synthetic data and no business or delivery credentials.

## Review before approval

1. Confirm all required checks belong to the final PR head and pass; re-run the exact-source registration if the head changes.
2. Review migrations116–120, their retained-evidence behavior, and the rollout diff. Verify the Test database already has the prerequisites above. Do not use this runner to initialize an unrelated tenant.
3. Confirm the existing Protected UAT controller can admit the exact main release under its normal controls. This PR does not grant itself candidate or deployment approval.
4. Ask the user to approve the concrete final change for Protected UAT. Do not merge or dispatch before that approval.

## Installed UAT acceptance after approval

Use synthetic packages and the real configured Systems and Collaboration/Networking reporting scopes. Verify each manager sees only the intended team and each SA sees authorized packages. The following checks require installed behavior; a green local fixture cannot substitute for them:

- Save and reload target date, priority, blocker owner and remaining authoring effort. Confirm these changes retain the document revision, and a competing scheduling edit returns a conflict.
- Transfer an editable package, acknowledge receipt as its current assignee, and return temporary coverage explicitly. Try a stale owner, an unrelated manager and View As; verify each prohibited write is rejected. Confirm overdue coverage does not automatically change ownership.
- Generate/review tasks, reconcile regular plus after-hours with total labor, confirm, download both formats, reopen/reconfirm and archive/return to active. Previously issued documents and SELL receipts must remain readable and unchanged. Existing untouched-draft deletion must still work.
- Stage an approved template original and inspect its preview. Confirm it is labeled awaiting mapping and has not silently become an export template.
- Inspect the Module065 queued handoff event and resolved recipients. Send live email/Teams only when the user has authorized those recipients and the delivery configuration is ready. A test database policy is not proof of live transport permission.

## Recovery

If entrypoint verification fails, stop the rollout and retain the source, image, job and failure evidence. Do not disable database triggers or broaden release admission to force a pass. The new migration rollback scripts preserve retained evidence; review them against the installed state before any database reversal. An application rollback does not delete audit rows, originals, confirmed versions or notification history. Keep Module065 policies locked or disabled if delivery must be paused; never erase delivery evidence to simulate a successful handoff.

Approved Standard and Toyota/Hyundai source templates, reviewed cell/field mappings, formula validation and controlled activation remain a separate completion requirement. This release prepares review and preview; it does not activate arbitrary uploaded templates.
