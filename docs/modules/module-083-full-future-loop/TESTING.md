# Module 083 Testing Guide

## What is required from the product owner

For source validation, nothing additional is required.

For browser-based Test/UAT, the following approvals are required after the PR is green:

1. Apply migration `082_module_083_full_future_loop.sql` to the **Test** database.
2. Deploy the reviewed Module 083 application commit to the **Test** site.
3. Test with a real Super Administrator session first.
4. Keep Production, secrets, cloud resources, and external GitHub/deployment adapters unchanged.

No API key, GitHub token, AI-provider credential, cloud credential, production permission, or new infrastructure is required for the sandbox.

## Source validation

From the repository root:

```bash
node src/frontend/project-time-web/scripts/validate-module-083-full-future-loop.mjs
```

Expected final output:

```text
MODULE_083_CONTRACT=PASSED
```

The validator checks the endpoint contract, state machine, sandbox boundary, RBAC, migration, rollback ownership, immutable evidence, App integration, module registry, Module Availability registration, light/dark styling, failure-path controls, and Agent Keep restrictions.

## Frontend build

```bash
cd src/frontend/project-time-web
npm ci
npm run build
```

The existing Module 077 validator imports the Module 083 validator. That ensures the idempotent Module 083 injection runs after the generated App source is finalized and before Vite compiles it.

## Backend build

From the repository root, run the injector first so Module Availability contains Module 083, then build:

```bash
node src/frontend/project-time-web/scripts/inject-module-083-full-future-loop.mjs
dotnet build src/backend/ProjectTime.Api/ProjectTime.Api.csproj --configuration Release
```

## Database migration validation

Apply only in Test after review:

```bash
psql "$PROJECTPULSE_CONNECTION_STRING" \
  --set ON_ERROR_STOP=1 \
  --file database/migrations/082_module_083_full_future_loop.sql
```

Verify:

```sql
SELECT migration_id, applied_at
FROM schema_migrations
WHERE migration_id='082_module_083_full_future_loop';

SELECT to_regclass('public.full_future_loop_items'),
       to_regclass('public.full_future_loop_events'),
       to_regclass('public.full_future_loop_artifacts');
```

All three table names must be returned and the migration record must exist.

## Browser UAT — complete loop

1. Sign in to the Test site as the actual Super Administrator.
2. Open **More → Platform Operations → Full Future Loop**, or navigate to `#full-future-loop`.
3. Confirm the header states **Safe persistent sandbox**.
4. Confirm the access response shows `dataReady=true`.
5. Select **Create test loop**.
6. Keep `Major` and `Require STEER-IT governance` selected.
7. Create the loop.
8. Select **Run complete loop**.
9. Confirm the loop ends at `verified closed`.
10. Confirm the timeline contains governance, private build, initial canary, sandbox promotion, production signal, private repair issue, repair evidence, repair canary, re-promotion, and final verification events.
11. Confirm evidence cards exist for the same stages.
12. Ask Agent Keep: `What is the current status and next governed action?`
13. Confirm Agent Keep states that it has no private-source, deployment, secret, or production mutation authority.

## Browser UAT — step-by-step loop

Create a second test loop and execute one action at a time:

1. Approve STEER-IT packet.
2. Complete private build.
3. Run passing canary.
4. Promote to sandbox production.
5. Record production signal.
6. Relay private repair issue.
7. Complete review and fix.
8. Run passing repair canary.
9. Promote repair again.
10. Verify and close.

At each step, verify that only the actions valid for the current state are shown.

## Browser UAT — failure and retry

Create a third test loop:

1. Approve governance and complete the private build.
2. Choose **Run failing canary**.
3. Confirm status changes to `attention required` and stage becomes `canary failed`.
4. Choose **Prepare canary retry**.
5. Run a passing canary and continue.
6. After the repair step, choose **Run failing repair canary**.
7. Confirm `repair canary failed` and an immutable failure artifact.
8. Prepare the retry, run a passing repair canary, and finish the loop.

## View-As and role testing

1. Enter Administrator View-As for a user with Module 083 view access.
2. Confirm the module can be viewed.
3. Confirm create, transition, reset, and support-issue actions are disabled or rejected.
4. Exit View-As.
5. Test an Engineer role: read-only unless explicitly granted sandbox execution.
6. Test an authorized Manager or Release Manager: sandbox actions allowed according to RBAC.

## Data integrity checks

```sql
SELECT loop_number,title,current_stage,current_status,iteration_number,revision_number
FROM full_future_loop_items
ORDER BY updated_at DESC;

SELECT loop_id,event_code,from_stage,to_stage,outcome,occurred_at
FROM full_future_loop_events
ORDER BY occurred_at;

SELECT loop_id,artifact_type,artifact_code,status,is_read_only,created_at
FROM full_future_loop_artifacts
ORDER BY created_at;
```

Attempting to update or delete an event or artifact must fail with the Module 083 append-only evidence message.

## Reset test

1. Select a completed loop.
2. Choose **Reset iteration**.
3. Confirm `iteration_number` increases.
4. Confirm the current stage returns to governance pending or private development.
5. Confirm prior events and artifacts remain unchanged and visible.

## Acceptance criteria

Module 083 passes UAT when:

- the source validator and frontend build pass;
- the backend compiles;
- migration 082 applies cleanly in Test;
- the complete loop can be run in one click;
- every stage can also be exercised manually;
- passing, failing, and retry canary paths work;
- Agent Keep remains read-only and evidence-scoped;
- View-As is read-only;
- reset creates a new iteration without deleting history;
- no GitHub, production deployment, cloud, secret, or external AI mutation occurs.
