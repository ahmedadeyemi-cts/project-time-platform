# Module 025 provider diagnostics and one-phase qualification

## Recorded failure

Protected Test run [35165706292](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/35165706292)
installed dd6403e successfully. The Plan phase failed: Claude output was rejected by
identity validation after 40 seconds; OpenAI returned an incomplete response after
59 seconds. DeepSeek and Celar were skipped after the two reserved attempts. No
phase completed, My Role did not run, and rollback was skipped.

The existing evidence does not retain Claude's precise rejection category or
OpenAI's incomplete reason. The backend already emits requested model and token
counts, but the acceptance-report allowlist dropped them. This change repairs
those diagnostics; it does not claim to fix the original provider output.

## Evidence contract

- Existing privacy decisions, detailed phase requirements, provider ordering,
  6,144-token ceiling, 120-second request deadline and two-attempt limit remain.
- Preserve OpenAI response status, closed incomplete reason and reasoning-token
  usage, including when incomplete text is discarded. Unknown protocol values
  are reported as `other`; missing values remain `missing`.
- Preserve Claude stop reason, and the rejecting privacy category and JSON field
  path. Paths contain only known schema names and array indices. Unknown keys
  become `unknown_field`. Matched identities and rejected text are never emitted.
- Record the returned text length before rejection, rather than reporting zero
  because the rejected content was cleared.
- Preserve this metadata through the router, journal, API and acceptance report.
  Old journal records with no new fields remain readable.
- The qualification command uses the same validation function as Module 025.

OpenAI documents `incomplete_details.reason` and reasoning-token usage in its
[reasoning guide](https://developers.openai.com/api/docs/guides/reasoning).
A token-limit failure is one possible explanation, not an established cause of
this recorded failure. No token limit or reasoning setting is changed here.

## Run exactly one qualification

This CLI requires a checked-out, validated revision and .NET 10. Build first so
build output does not contaminate the JSON report:

```bash
dotnet build tests/FlowHiveDetailedPlannerTests --configuration Release
```

From an authorized Test diagnostic environment that already has the Test API's
Module 064 database, encryption and runtime policy configuration, run:

```bash
dotnet run --project tests/FlowHiveDetailedPlannerTests --configuration Release --no-build --   --qualify-sow-provider claude --use-module064-store > module025-claude-qualification.json
```

Select only one provider per invocation. Substitute `openai` only when that is the
chosen provider; this command does not cascade or retry. It makes at most one
synthetic CUCM Plan request, with no customer data or business writes. No provider
key is printed or exported. Store mode requires the existing
`PROJECTPULSE_ENVIRONMENT=test` setting and uses the same read-only configuration
hydration as the API. It does not create tables or change provider settings.
Do not change a Production process's environment label to run this command.

Without `--use-module064-store`, the existing environment-only configuration mode
remains available for an already authorized credentialed diagnostic process. A
missing key, disabled provider or disabled external policy fails before inference.
Never place keys in command arguments, reports, Git, or chat.

On failure, inspect `SowDiagnostics`, `Usage`, requestedModel and elapsed time.
Keep the failure report; do not automatically issue another request. On success,
review the returned detailed Plan before authorizing the remaining lifecycle.
A successful single phase is not full SOW/GSD or My Role acceptance.

The connected GitHub app can publish and inspect this change but cannot dispatch
a workflow or approve the protected Test environment. The manual qualification
workflow below runs this process with the existing credentials after native Test
approval. Authoring this PR does not itself invoke any live model.


## Visible documents and SELL handoff

The authoring page previously hid its SOW/GSD download links until confirmation
and required finding the same record again in the Register to see SELL readiness.
The workspace now displays all four actions (SOW, GSD, SELL, version history)
from the start, with state-specific prerequisites. Downloads use the authenticated
session bridge; opening SELL selects this exact record while preserving the editor.
No unconfirmed draft is mislabeled as a confirmed document. Historical retained
versions stay accessible after reopening or archiving.

The current SELL publisher is a deliberate non-writing implementation. The
[public Zendesk Sell Documents API](https://developer.zendesk.com/api-reference/sales-crm/resources/documents/)
documents retrieval, not document upload. This change does **not** implement or
claim automatic SELL publication; the blocker and manual-download option remain
visible. A supported upload integration and verified two-document receipts are
still required for that acceptance criterion.

## Credentialed one-phase qualification in GitHub

After this PR is merged, open the existing **Protected Test deployment workflow**,
select `main`, use the exact merged current-main SHA and `release_branch=main`,
set `acceptance_scope=sow_role`, leave recovery unchecked and controller SHA empty,
and select `qualification_provider=claude` (or `openai`). Approve its existing
`test` environment. In this mode, every deployment/migration/acceptance/rollback
step is skipped. The normal `qualification_provider=none` mode preserves the
existing deployment behavior. No new privileged workflow is added. Re-running an old run is rejected; each request
requires a new explicit manual run.

It publishes an isolated runner image and creates one temporary job in the Test
API's private Container Apps environment. The runner reads the configured model,
enabled state, and provider key from the same encrypted Module 064 store. Only the
required Test DB/encryption and chosen cloud-provider settings are carried into
the job. Credentials are not printed, returned as artifacts, or requested from
the owner. The job has one replica, no automatic retries, and a 240-second process
limit; inference remains bounded to one 120-second, 6,144-token Plan request.
There are no migrations, API/web deployments, business-record writes, FlowHive
requests, Celar runtime changes, or Production changes.

`qualification.json` contains the chosen provider/model, elapsed time, available
usage and closed diagnostics, and the validated synthetic plan only if it passed.
The script verifies job cleanup and unchanged API deployment identity. A failed
or incomplete report is not permission to run another request automatically.
A one-phase pass is qualification only; full SOW/GSD lifecycle and My Role remain
separate live acceptance requirements.
