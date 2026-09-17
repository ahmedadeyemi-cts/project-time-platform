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

The connected GitHub app can publish and inspect this change but cannot execute
this process in the Azure Test environment. Live qualification therefore remains
an explicit operator action in that credentialed environment. This PR does not
add a deployment workflow, dispatch one, change Oracle, or invoke a live model.
