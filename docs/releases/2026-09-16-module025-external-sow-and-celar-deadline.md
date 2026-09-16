# Module 025 external SOW generation and Celar phase deadlines

Run 35155142339 installed e632c8d in Protected Test. Its first Plan phase exhausted
DeepSeek and Celar's 120-second application budgets. Claude/OpenAI were skipped
because no structured adapter existed. No phase completed. The gateway's manifest
advertised a 3,000-second first model attempt inside a 3,600-second SOW deadline.
These observations do not establish whether model loading, CPU throughput, queueing,
or another host condition caused the latency.

## Implementation

- Only server-loaded Module 025 evidence, after effective-user permission checks,
  can create the new cloud capsule. The existing sanitized-external policy must
  be enabled and each provider must be registered, configured, enabled and healthy.
- Cloud input consists of closed public technology names and operation categories.
  A narrow numeric grammar preserves an unambiguous version transition and common
  quantities for a single technology. Raw overview prose, files, customer names,
  contacts, hostnames, secrets, rates and commercial terms are never cloud input.
- Supported technologies are listed in Module025ExternalSowAdapter. Unsupported
  technologies and negated/excluded scope stay on the private path. Arbitrary environment constraints are
  not sent externally; the saved overview remains authoritative and SA review is
  mandatory. This initial adapter does not claim arbitrary-prose extraction.
- For an eligible Module 025 capsule, Claude/OpenAI are tried first in their
  relative configured order; other features retain Module 064's current order and
  private-document controls. Two attempts per phase and the persisted 20-minute
  generation deadline are unchanged. No generic local result can complete a SOW.
- Responses must pass privacy checks and the same detailed phase parser used by
  private providers. Source citations and customer identity are bound locally.
  Each phase is persisted before the next phase. A failed phase stops generation.
- Structured cloud requests use the 6,144-token ceiling, a 120-second deadline,
  and zero hidden transport retries. Truncated/incomplete responses fail closed.
  Other request types retain their existing transport budgets. OpenAI response
  storage is disabled. No provider key or model configuration changes are shipped.
- Celar gateway 1.1.8 honors the Module 025 phase workload header and a maximum
  110-second caller budget, with one local model per durable attempt. FlowHive's
  and other workloads' existing gateway budgets are unchanged.
- Gateway logs and the read-only collector retain only closed model/status/budget
  fields and Ollama load/prompt/evaluation durations and counts. No prompt or
  generated content is logged. Provider completion status, elapsed time and
  available cloud usage are persisted in Module 025 progress.

## Required live verification

Code tests use synthetic transports and do not contact model APIs. They cannot
prove provider performance. Before calling this release successful:

1. Review the exact PR and its CI results. Before another deployment, qualify a
   cloud provider from an authorized environment using its existing provider key
   and enabled/sanitized-external policy configuration:
   `dotnet run --project tests/FlowHiveDetailedPlannerTests -- --qualify-sow-provider claude`
   (or `openai`). This explicit command makes exactly one bounded synthetic Plan
   request, no fallback, no business writes, and reports the complete reviewable
   phase plus latency and available usage. It fails before a call if credentials
   or policy are absent. Never put the key in command-line arguments or reports.
   A passed phase is not full lifecycle acceptance.

   Keep Production and FlowHive generation out of scope. Confirm Oracle has gateway 1.1.8 through the existing authenticated
   runtime gate before the application rollout.
2. Qualify the first detailed Plan phase: verify selected model/provider, positive
   elapsed time, available usage, full required fields, correct source versions and
   quantities, and no unsupported invented environment facts. Stop if it fails.
3. Pass all remaining phases and the normal-SA edit/save/confirm/reopen/immutable
   SOW/GSD download lifecycle, then My Role. Deployment health alone is insufficient.
4. For Celar diagnosis, run the existing read-only collector on the Oracle host:
   `python3 scripts/release-test/collect-celar-runtime-evidence.py --since '30 minutes ago'`.
   Compare model load duration, prompt evaluation and output evaluation, host load,
   memory, and service error counts. A successful deadline test is not proof of
   acceptable generation speed or of live cancellation after a client disconnect.

No workflow dispatch, production mutation, live model request, migration, or
change to deployment approval/rollback authority is performed by this PR.
