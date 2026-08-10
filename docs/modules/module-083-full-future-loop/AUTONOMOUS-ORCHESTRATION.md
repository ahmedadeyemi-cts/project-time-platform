# Module 083 — Autonomous Orchestration and Persistence

## Status

This source phase adds the durable enterprise control-plane foundation behind Module 083. It is intentionally **dry-run only**. It does not install a GitHub client, Azure client, deployment dispatcher, secret-store client, observability client, process runner, or external AI client.

Migration `083_module_083_autonomous_control_plane` must be separately reviewed and applied before the new APIs persist data. Source merge does not execute the migration or deploy the platform.

## Purpose

The original Module 083 sandbox proves the Full Future Loop state machine. The autonomous orchestration layer adds the durable records required to operate that lifecycle safely at enterprise scale:

- immutable policy versions;
- a global kill switch and runtime state;
- provider-neutral adapter registration;
- idempotent automation runs;
- deterministic policy decisions;
- step plans, deadlines, retry limits, and lease fields;
- approval queues with separation of duties;
- exact release manifests;
- append-only evidence;
- a durable outbox that remains blocked until a dispatcher is separately approved.

## Runtime boundary

The database and API enforce all of the following in this phase:

- every run has `dry_run=true`;
- runtime state has `dry_run_only=true`;
- adapter mode is limited to `disabled` or `dry_run`;
- active external adapter mode is rejected with HTTP 423;
- outbox records created by a successful dry-run policy decision remain `blocked`;
- no Production execution flag is exposed;
- View-As is read-only;
- the requesting user cannot approve the same run;
- AI cannot serve as the requesting or approving authority;
- immutable policies, manifests, and evidence cannot be updated or deleted.

This lets administrators exercise policy, approval, release-manifest, evidence, and recovery behavior before any external permission is introduced.

## API root

`/api/full-future-loop/automation`

### Readiness and policy

- `GET /readiness`
- `GET /policy`
- `POST /policy/simulate`

Policy simulation is non-persistent. The request may temporarily assume that the policy is enabled and the kill switch is released so reviewers can inspect the resulting `auto_execute`, `approval_required`, or `blocked` disposition without changing runtime state.

### Adapters

- `GET /adapters`
- `POST /adapters/{adapterCode}/mode`

Only `disabled` and `dry_run` are accepted. `active` is not represented by the current database constraint and is rejected by the API.

The seeded provider-neutral catalog includes GitHub, disposable canary execution, Azure Container Apps, Azure Monitor and Application Insights, Module 076, Module 065, and Celar AI through Modules 011 and 064. No credential or endpoint is stored by migration 083.

### Durable dry runs

- `GET /runs`
- `GET /runs/{runId}`
- `POST /runs/dry-run`

A dry-run request is evaluated by the deterministic policy engine. The resulting run stores the exact repository and source commit, operation and target environment, risk and change classification, policy version, immutable request and decision snapshots, idempotency key, correlation ID, retry and deadline limits, planned steps, required approvals, and append-only evidence.

When the decision is `auto_execute`, the system records the external action that would have been requested in a blocked outbox record. No dispatcher exists in this phase.

### Release manifests

- `POST /runs/{runId}/manifest`

A manifest must match the run’s repository, exact source commit, and target environment. It requires exact `sha256:` artifact digests, SBOM references, provenance references, signature references, canary evidence, verification evidence, exact prior rollback digests, a non-secret configuration fingerprint, and a valid expiry. The stored manifest and its SHA-256 hash are append-only.

### Approvals

- `GET /approvals`
- `POST /approvals/{approvalId}/decision`

Approval requests are created from the deterministic policy decision. The requester cannot approve their own run. Approval mutations use optimistic revision checks and create append-only evidence.

### Runtime and evidence

- `POST /runtime`
- `GET /evidence`

The runtime endpoint controls only whether autonomous policy evaluation is enabled for durable dry runs and whether the global kill switch is active. It cannot disable `dry_run_only` or activate external execution.

## Persistence model

Migration 083 creates:

| Table | Purpose |
|---|---|
| `full_future_loop_automation_policies` | Immutable, versioned policy documents and SHA-256 evidence |
| `full_future_loop_automation_state` | Singleton runtime state, kill switch, active policy, and revision |
| `full_future_loop_automation_adapters` | Provider-neutral adapter catalog and disabled/dry-run state |
| `full_future_loop_automation_runs` | Idempotent, attributable, dry-run orchestration records |
| `full_future_loop_automation_steps` | Ordered run plans and dry-run outcomes |
| `full_future_loop_automation_approvals` | Gated authority decisions and optimistic revisions |
| `full_future_loop_release_manifests` | Immutable exact-release and rollback evidence |
| `full_future_loop_automation_evidence` | Append-only audit and operating evidence |
| `full_future_loop_outbox` | Durable external-action intent, blocked until activation |

## Seeded operating state

The migration seeds policy version `enterprise-default-v1`, automation disabled, global kill switch active, dry-run-only enforcement, Test and canary as the only allowed environments, Production deployment and rollback automation disabled, and all adapters disabled and not ready.

## Permissions

Migration 083 adds:

- `VIEW_FULL_FUTURE_LOOP_AUTOMATION_083`
- `OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083`
- `MANAGE_FULL_FUTURE_LOOP_AUTOMATION_083`
- `APPROVE_FULL_FUTURE_LOOP_AUTOMATION_083`

The Super Administrator, Administrator, System Administrator, and Release Manager roles receive full control when those roles exist. Operations and management roles receive view and dry-run operation authority. Engineering, support, architecture, and executive roles receive view access according to the migration’s role grants. Module 012 can refine these assignments later.

## Test procedure after source validation

### Before migration

1. Open Module 083.
2. Call `GET /api/full-future-loop/automation/readiness` with a valid Module 083 session.
3. Confirm HTTP 503 with `MODULE_083_AUTOMATION_MIGRATION_REQUIRED`.
4. Confirm the existing Module 083 sandbox remains operational.

### After Test-only migration approval

1. Apply migration 083 to the protected Test database.
2. Confirm the nine new tables and migration registration exist.
3. Confirm the state row reports automation disabled, kill switch active, and dry-run only.
4. Confirm all seven adapters are disabled and not ready.
5. Run a policy simulation with an exact source commit.
6. Create a durable dry run while the kill switch is active and confirm the run is blocked.
7. As an authorized administrator, enable automation while leaving the kill switch active; confirm runs remain blocked.
8. Release the kill switch for dry-run operation only.
9. Create a Test deployment dry run with complete evidence and confirm it reaches `dry_run_completed` and creates a blocked outbox record.
10. Create a migration-bearing dry run without migration approval and confirm an approval is required.
11. Decide the approval using a different user and confirm separation of duties.
12. Register a complete exact release manifest.
13. Confirm attempts to change an immutable policy, manifest, or evidence row fail.
14. Confirm `active` adapter mode returns HTTP 423.
15. Confirm View-As cannot change runtime, adapters, or approvals.

## Activation sequence for later phases

External activation requires separate source and operational approvals in this order:

1. Read-only GitHub and observability adapters.
2. Adapter health probes and circuit breakers.
3. Module 076 defect writes and Module 065 notifications.
4. Protected GitHub workflow dispatch for CI and disposable canary runs.
5. Protected Test deployment by exact image digest.
6. Automatic Test verification and exact-digest rollback.
7. Production manifest preparation and approval request.
8. Separately authorized Production execution.

Each adapter must have its own identity, allowlist, rate limit, timeout, retry policy, evidence contract, and kill-switch behavior. No adapter may inherit unrestricted application permissions.

## Required external access later

No external access is required for this source and dry-run persistence phase.

Later activation will require configuration—not secrets in source—for an organization-owned GitHub App; protected GitHub `test`, `canary`, and `production` Environments; separate Azure OIDC identities for Test deployment, Production deployment, and read-only observability; Azure resource identifiers for ACR, Container Apps, Application Insights, Log Analytics, and Key Vault; named approval groups and maintenance windows; and approved SLO and rollback thresholds.

Secrets and private keys must remain in GitHub Environment secrets or Azure Key Vault. They must not be entered in Module 083, committed, logged, or pasted into support conversations.
