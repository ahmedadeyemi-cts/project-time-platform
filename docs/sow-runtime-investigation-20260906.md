# SOW generation investigation — Protected Test

Status: local PR candidate; not pushed, merged, or deployed. Oracle evidence now supports memory exhaustion as a likely cause; successful generation under the proposed repair is not yet proven. Hold publication until the owner finishes other deployments.

## Evidence and limits

- PRs 870 and 871 introduced phase generation and runtime diagnostics/recovery.
- Run 34048643195, source `139d63fc276e8dcfb4dd126c30bf196dc34c24a9`, passed authenticated functional UAT but failed SOW generation after approximately 56 minutes. The terminal diagnostic was `private_model_http_502_private_runtime_unavailable`. The saved draft stayed at revision 1.
- The Oracle gateway produces `private_runtime_unavailable` when its localhost Ollama request raises a Requests transport exception. This does **not** prove missing SOW evidence, an invalid token, an OOM, or a specific model defect.
- A separate synthetic two-task Plan request previously completed in 376 seconds. That probe did not use the complete application request path and did not prove the full five-phase lifecycle.
- PR 873 corrected an application transport instruction that contradicted phase-specific generation. Its required checks passed, but complete authenticated acceptance was still outstanding when this investigation began.
- The owner supplied Oracle evidence captured at 2026-09-06 20:50:58 UTC: 8 kernel entries matched OOM patterns, 2 Ollama entries matched OOM patterns, 2 runner-exit signals, and 2 daemon restarts. These are matched log-entry counts, not counts of unique OOM incidents. Peak Ollama memory was 10,513,330,176 bytes (about 9.79 GiB), against total host RAM of 12,213,176 KiB (about 11.65 GiB); swap was absent. Gateway peak memory was about 84 MiB with no restarts.
- Ollama cgroup OOM counters were zero at collection time. This does not erase kernel OOM evidence; global OOMs and prior service instances require timestamp/PID correlation for exact attribution. The supplied aggregate report does not establish which individual SOW request coincided with a kill.
- The repository runtime configuration allows two models resident concurrently and one request per model. A local candidate changes model residence to one, retaining all specialist models, one parallel request, full 16,384-token context, and provider order. The release version advances to 1.1.7 so the existing preflight rejects the prior runtime before API rollout.
- The deployment health check verifies selected settings from the actual running Ollama daemon, without emitting the rest of its environment. This detects a stale or overridden runtime configuration.
- Official reference: https://docs.ollama.com/faq documents model residence and request concurrency as memory controls. Single-model residence can add model-switch latency; live acceptance and post-run memory evidence are still required.

## Confirmed defects addressed in this candidate

1. A terminal gateway failure could restart the full local-model fallback chain at the phase layer. Gateway-classified exhaustion is now terminal; only an unclassified proxy 502/503/504 may receive the existing bounded retry.
2. Five phases and their retries had no shared application generation deadline. There is now a 40-minute composition deadline across providers, a 40-minute phase-orchestration deadline, and a 10-minute shared deadline per phase. The same deadline includes repairs and transport retries. Caller shutdown is preserved as cancellation. These limits contain failure; they do not make an unavailable model healthy.
3. Runtime faults were labeled as `module025_ai_evidence_limited`. Recognized runtime faults now produce the existing temporary-service-unavailability status while evidence/completeness failures remain 422. No partial draft is adopted.
4. Terminal diagnostics lacked the failing phase. Phase lifecycle logs now include correlation ID, phase, completed-phase count, package count, and elapsed time, without source or completion text.
5. SOW failure prevented independent assigned-work and utilization UAT from running. Those gates now precede SOW, run independently after successful functional UAT, and still fail the overall job on failure. Cancellation and fixture cleanup remain enforced.
6. Generation polling could outlive the 45-minute authorization fixture and make archival cleanup fail. Polling now ends within 42 minutes and reserves three minutes before the actual fixture expiry. Each HTTP poll is bounded by the remaining time. Authorization duration is unchanged.

## Validation required before publication

- Detailed planner tests: five-phase assembly, required citations and completeness, semantic repair, refusal/authentication terminal behavior, no duplicate exhausted runtime chain, non-cooperative provider timeout, caller cancellation, failure classification, and no partial draft output.
- Controller and scope checks, plus UAT ordering/cancellation and cleanup-reserve checks.
- Evidence collector privacy, malformed/truncated input, and denied-read handling.
- Rebase and rerun required checks after the other owner's changes land. No PR number has been allocated for this candidate.

## Runtime evidence and publication hold

The first Oracle evidence report has been received. Keep the read-only collector for post-change comparison using the same event window format. It emits selected counters and closed fields; missing or denied reads remain incomplete.

No more live deployments or inference probes were launched during preparation. The Oracle changes remain local with the application changes. Publishing and merging the candidate will let the existing Oracle GitOps controller apply the memory setting and restart Ollama, so wait for the owner's deployment hold to be released. Rebase against the completed parallel work, confirm the runtime version, then run required PR checks.

Do not treat shorter deadlines or changed error labels as proof of generation success. Do not add swap, resize the VM, change model weights, quantize the cache, or shorten the SOW context based on these aggregate counters alone.

## Acceptance still required

A successful authenticated run on the final combined source must create a technology-specific SOW with at least ten complete work packages across Plan, Design, Implement, Validate, and Release, read it back as review_ready, and archive its fixture. Functional, assigned-work, and utilization gates must also pass. Verify the earlier failed test draft's cleanup separately; the previous run reported archival failure even though temporary authorization was disabled. Do not call the SOW generator fixed based only on deployment health or unit tests.


## Provider selection and acceptance (follow-up)

Recommend DeepSeek v4 on the existing approved private DGX endpoint as the
primary SOW author, with Celar as private fallback. This is a deployment-specific
recommendation, not a claim of measured superiority or completed SOW acceptance.
The earlier provider smoke artifact records DeepSeek generation success; the
SOW artifact retained only the last Celar runtime failure. Neither establishes a
complete DeepSeek SOW. Claude/OpenAI currently receive the fixed generic
scope-quality capsule and cannot author the project-grounded SOW under that
contract. Changing their order alone does not change this limitation.

The next candidate now checks the persisted Module 064 SOW order explicitly:
DeepSeek, Celar, Claude, OpenAI, local. Other capability orders remain
administrator-controlled. No route or credential has been changed live.
Generation completion and missing-draft failures retain router target decisions
in the existing event JSON; status polling exposes them and successful draft
metadata retains them. Failure selection considers both private providers,
preserves refusals, and classifies DeepSeek transport, timeout and queue failures
as temporary infrastructure failures. UAT requires DeepSeek to be considered
first and a private provider to report generation success alongside the existing
full review-ready scope checks. Its summary names the actual draft provider;
a Celar fallback success must not be reported as a proven DeepSeek SOW.

The existing 120-second DeepSeek budget includes up to 60 seconds waiting for
the shared database advisory-lock slot. That is a concrete contention risk with
concurrent work, but no captured SOW evidence yet identifies a DeepSeek timeout.
Do not inflate deadlines or disable concurrency controls without that evidence.
If the overall generation deadline interrupts the router before it returns,
terminal target decisions can still be empty; the timeout remains explicitly
incomplete rather than claiming a provider attempt that was not observed.

Publication remains on hold until the user's other deployment is complete.
Required live acceptance remains: saved scope -> five technology-specific
phases and plausible reviewable LOE -> persisted review_ready readback ->
archive fixture, with actual provider decisions retained. Assigned-work and
utilization execute independently before this long-running SOW test.


## Combined FlowHive release reconciliation

PR #874 is being integrated with the already merged #875 Protected Test control
plane, at base `8c84d91b8a4f7d1583118f6d701185b355c9684f`, before inclusion in the
unmerged #872 candidate. The independent assigned-work and utilization checks
remain ahead of Module 025 composition and also run when the PSA lane establishes
deployment health, even if its AI acceptance fails. The PSA candidate must never
enable Module 025 authorization fixtures. The fixture expiration output and
cleanup reserve remain enforced for normal main releases.

The Oracle validator now checks the intended `1.1.7` gateway manifest, one loaded
model and one inference lane, and executes the memory-policy and privacy tests.
Its former `1.1.6` equality was a stale test, not evidence that the new runtime
policy was invalid. No runtime setting, provider order or timeout was relaxed to
pass that check. FlowHive's separate five-minute durable operation limit is not
changed by Module 025's longer multi-phase SOW budget.

The exact integration scope is 25 files. The original release permissions,
triggers, environment, concurrency, image ownership and rollback are compared
against the merged controller. Local scope, parsed workflow, false-success and
Oracle shell contracts passed; final combined CI and authenticated live provider
acceptance must still be recorded. Repository merge does not itself prove the
Oracle GitOps host applied the new manifest or that either live AI flow passed.
