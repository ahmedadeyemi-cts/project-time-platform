# FlowHive PSA — exact pre-merge Protected Test admission

This is a release-control change, not a merge or completion of successor PR #887.
It admits only the candidate pinned in `.github/flowhive-psa-protected-test-candidate.json`
through the existing `.github/workflows/projectpulse-deploy-test.yml` controller.
Production, private-runtime recovery, customer publication, baseline adoption and
canonical task mutations are not authorized by this approval.

## Admission

The repository owner may post this exact command on PR #887 after these controls
have been reviewed, tested and merged to main:

```
DEPLOY FLOWHIVE PSA PROTECTED TEST SHA 95abbb0aa2445a33fda68e9de542f9446c3e2204
```

The admission workflow executes main-owned code only. It checks the exact open PR #887,
repository, successor branch, candidate SHA, 22 required successful exact-SHA PR workflows,
current main control SHA and application-source freshness. Main changes after the
candidate's source base may contain only the reviewed control-only manifest; any
new application changes require a refreshed candidate and approval. The current
feature branch is not renamed or implicitly approved by a prefix match.

This combined candidate includes PR891 at `2ebae9f12660a3def85ba39d43fde528275cc0eb`
through integration commit `21075bc35bc1f7071d6c598f46666c7f18649449`, current-main
reconciliation `e0b8fa3f2b19009fb865eb5f50a07101461e39bf`, and the final CI/scope repair
`95abbb0aa2445a33fda68e9de542f9446c3e2204`. Its trusted source base is current main
`d2262ccbef31800883589197d03122bd51bb87cc`; migrations 103/104/105/106 retain their
reviewed SHA-256 values.

The supervisor shares the existing admission lock and refuses every unresolved
workflow run before the single dispatch write. This keeps the native Test
environment gate and existing serialization as the release transaction boundary.
The normal read-only cutover gate still requires all three
known requests (`34495606530`, `34377182662`, and `33654881418`) to be
server-confirmed terminal with completed attempts and no pending deployment.
The reviewed `.github/flowhive-psa-protected-cutover.json` provides a separate,
initially inactive exception for this exact candidate: when separately approved
and unexpired, one shared assessment verifies the exact three queued records,
every observed attempt's zero jobs, empty Test approval history and pending
deployments, empty concurrency/artifact inventories, exact historical workflow
blobs and complete native Test protection. The same assessment is passed to the
final nonterminal-run inventory, so an unknown fourth run, execution, approval,
identity change or unreadable evidence blocks admission. Queued remains queued;
the path never claims cancellation or completion and never reconstructs missing
historical dispatch inputs. A lost dispatch response is an unknown outcome to
inspect, never a reason to dispatch again automatically.

The current unresolved requests are recorded in both the trusted cutover manifest
and the live assessment. Run `34495606530` uses controller
`9f30078c2c407d4d3576ccefd663a145be50c6c4`; all three are required to remain
queued with zero jobs, no pending deployment, no approval and no artifacts. Their
raw GitHub status remains visible and distinct from the repository's protected
nonterminal disposition. Activation is a separately reviewed, bounded approval
for this candidate and current controller, followed by the existing native Test
deployment approval. When that
activation is reviewed, the maintained entrypoint performs one guarded
`disabled_manually` → `active` transition, re-reads the exact three-request
assessment, admits at most one dispatch, and reports the resulting controller
state. A successful bootstrap deliberately leaves the canonical controller
`active`; it does not silently recreate an enable → dispatch → disable cycle.
If admission fails before an external dispatch write, the wrapper restores
`disabled_manually`. If a dispatch write is uncertain, it does not disable the
workflow as a false cancellation signal: it retains `active` and reports the
uncertain outcome for inspection. Enable/restore failures preserve both the
primary admission error and cleanup error, and the final state is read back
explicitly. It is never retried automatically.

The `--inspect-only` entrypoint requires the exact `workflow_dispatch`/main
controller context, the approved candidate manifest, and a valid controller SHA;
it is GET-only and does not simulate an issue comment. The native Test rule is
compared as a complete normalized reviewer set (type and stable identity), not
just the first reviewer returned by the API.

Before the dispatch write, the admission job persists a sanitized attempt record
with the repository, candidate/controller identities, workflow, admission run ID,
admission run attempt, request fingerprint and timestamps. It records the returned run ID immediately,
then records server identity verification and the observed lifecycle phase. API
failures include stage, method, path, status and GitHub request ID; no token,
header, response body or customer content is stored. The record is uploaded as
an Actions artifact even when the admission step fails. Optional summary/comment
failures are recorded separately from the last verified deployment phase and cannot erase an accepted receipt; identity,
authorization and active-controller failures remain blocking. A single-use
reservation comment binds the candidate, controller and bounded approval
reference before the POST. Repeated commands, restarted admissions, and
uncertain dispatch responses find that reservation and stop without a second
dispatch. Its one-use key is the candidate plus approval reference; the
controller SHA is retained as execution evidence and cannot renew the same
authorization after a controller update. Only the owner-authored structured
reservation is accepted. Untrusted or malformed copies and uncertain
reservation writes fail closed without another deployment POST. Immediately
after the reservation write, the exact protected requests and complete
nonterminal-run inventory are read again. The authorization clock is checked
after those reads together with current main/controller identity and complete
native Test protection, and all are recorded with observation timestamps.

Controller-only changes no longer trigger automatic main-push deployment. All
existing application/migration source triggers remain unchanged, and the control
PR is tested not to match any automatic deployment path. Its merge therefore
does not race the explicit candidate admission.

The canonical deployment still owns `projectpulse-deploy-test` concurrency,
`environment: test`, environment protections, Test-only Azure identities and tag
checks, immutable image builds, exact source provenance and health rollback.
Candidate admission is independently repeated before its code is built. The
trusted controller revision and candidate application revision are separate
identities, both recorded in evidence.

## Migrations 103/104/105/106

Approved migration bytes are selected from the exact candidate checkout and
matched to the SHA-256 values in main approval. A separate migration image carries
only those SQL files, checksums, exact release identity and the trusted entrypoint.
For this successor approval, Module 025 migration 106 is included and verified
against its recorded SHA-256 and dependency boundary before the migration job runs.
The image is resolved to an immutable digest. The existing governed private-network
migration runner supplies the approved Test UAMI, TLS database connection and
Key Vault secret **references**, with exact job ownership and cleanup checks.

The entrypoint serializes migration application, bounds database locks/statements,
uses `ON_ERROR_STOP`, and verifies tables, execution fields, triggers, index and
migration receipts. Reapplication is supported. Migration 105 adds immutable
prior/candidate/applied plan-review receipts and refuses rollback once a receipt
exists. Application rollback does not attempt to delete immutable evidence or
destructively reverse these migrations.
Migration or deployment-health failure stops release; a healthy candidate is
retained after a functional failure for diagnosis.

## Real functional acceptance

The successor candidate lane replaces only the old long-running FlowHive/Forge acceptance
step for this exact admission. Main and the older candidate retain their existing
UAT behavior. The PSA lane does not enable or run the Module 025 authorization
fixture; it reads the project's existing SOW instead. Existing assigned-work and
utilization gates remain in the canonical job.

The new test authenticates the existing assigned PM, rejects View-As and anonymous
project access, selects the approved project, and posts exactly one generation
request. Generation stores a separate detailed Plan/Design/Implement/Validate/Release
proposal when existing work is present; it does not replace the working copy. The
test captures the current working-copy revision and dates, reads the explicit review,
previews a retain-or-map merge, and applies only the reviewed result. It preserves
existing milestone identities, task identities, assignments, progress, dependencies
and immutable history, and never retries an uncertain POST.
Status observation is bounded, checks run/project identity and the backend's
five-minute execution contract, and records stage timings. An unfinished known
operation is cancelled on test failure; a late result cannot be called successful.

On successful generation the test checks five-phase detailed cited work, effort
and schedule reconciliation, absence of automatic milestones and canonical task
adoption, and unchanged existing work before review. It then opens the actual
deployed React application with the PM session, checks the saved proposal and
working plan in AI Planner, reloads both proposal and applied views, and proves no
additional generation or publication was requested. A browser network safety filter
aborts unexpected writes; it never fabricates API responses or substitutes a test
model.

Artifacts contain fixed diagnostic codes, IDs/fingerprints and aggregate metrics.
No raw SOWs, task text, session tokens, recordings, HAR files or screenshots of
customer content are uploaded. Tests use temporary browser contexts and log out.

**A functional pass is not full AI or product acceptance.** The current candidate
API does not expose actual model names or per-provider transport call counts.
Those cannot be inferred from orchestration attempt counts or configured models.
Evidence explicitly records that correlated model-call telemetry and semantic
SOW/exclusions/estimate review remain unestablished. Full PSA integration,
financials, RAID/decisions, notifications, recordings/transcription, sharing and
all twelve export artifacts remain the feature PR's separate completion gates.

## Verification in this control PR

Node negative tests cover approval, forks, drift, stale/failed CI, dispatch identity,
receipt-before-follow-up failure, sanitized attempt evidence, unresolved-run blocking,
active-controller policy and control scope. Python tests cover false-success rejection, saved receipts,
parsed workflow safety, shell syntax, and unchanged unrelated controller steps.
A disposable PostgreSQL job executes the approved migrations and actual migration
entrypoint, reapplication, legacy-run retirement, immutable RAID evidence,
execution fences, rollback refusal and corrupt-payload/disabled-trigger detection.
None of those isolated tests is represented as live model acceptance. Live
acceptance additionally requires one real configured private-provider generation,
proposal/review/preview/apply receipts, browser display of the proposal, and reload
without another generation request.


## PR874 combined candidate and migration-image resolution repair

PR874 merged as `55ebb51fda1917f202ce6561ed5f5e635468d01c`. Candidate
`b4a976751eb2cb5bc68c6a7057ca28148f1cf58a` includes that actual merge parent,
the reviewed SOW/Oracle changes, both post-review cleanup/HTTP500 repairs, and
the reviewed PR880 control merge integrated into PR872.
The approved `sourceBase` is `4871d47fbeaad0fd5c08ddca27f193d682a0ea92`, the
reviewed PR880 main merge already contained in the candidate. The PR874 merge
remains an ancestor and is retained as historical repair evidence. Current main
`040709cdac0a940ad8feffbd730f1be35ce50280` adds only the reviewed candidate-
approval control delta; an application path is still rejected by source drift.
PR872 and its frozen candidate remain historical reference material; PR887 remains
draft and unmerged. Required exact-source CI now also includes
the PSA admission/migration contract workflow (22 total).

The failed previous candidate deployment `34068097426`, job `101580331315`,
successfully completed historical migrations and built the PSA migration image,
but its immediate registry tag lookup reported `the specified tag does not exist`
after ACR build `ds1ca` had reported a successful push. The exact job log was
recovered read-only in inspection run `34071865725`; this was not evidence of
missing database columns. No candidate API/web image had been deployed.

The migration builder now resolves its digest with at most twelve READ attempts,
a ninety-second overall budget, fifteen-second request limits and a two-second
kill grace reserved inside that budget. Recognized authorization errors and
successful but malformed digests fail immediately. Exhaustion stops before any
migration job; no tag fallback, image rebuild or migration-write retry occurs.
This handles a possible registry visibility delay without treating it as proven
until a subsequent deployment succeeds. ACR success by itself is not acceptance.

The exact follow-up diff is the separately reviewed regeneration-control set; the canonical
controller, dispatcher, private identities, environment protections, migration
bytes and twenty-path overall control boundary remain unchanged. Deterministic
Bash tests exercise immediate success, delayed visibility, missing tags,
malformed digests, authorization failure and time-budget exhaustion, while the
existing PostgreSQL fixture continues to execute migrations103/104/105 and their
failure/reapply/immutability checks. These tests are not live AI evidence.


### PR876 review closure and exact-head refresh

The seven-path digest repair is now limited to open PR876 from the reviewed
repository and release branch onto main at the exact PR874 merge base
`55ebb51fda1917f202ce6561ed5f5e635468d01c`. Validation compares the actual checkout,
resolved Git base and GitHub pull-request event, not just a seven-file count.
Missing context, a different PR/base/branch/repository, a fork, a closed PR or a
stale checked-out head is rejected. The original twenty-file control boundary is
unchanged. Negative tests exercise each identity mismatch separately.

The refreshed candidate also repairs both required controller workflows' GitHub
expression-length failure and removes static PostgreSQL CI fixture credentials.
The historical test-credential finding in GitGuardian must be classified and the
exact-head security check cleared before the combined candidate is deployed.
No failed check is waived; the application PR remains draft. This control refresh
preserves the canonical deployment/dispatcher bytes and approved migration hashes.
