# FlowHive PSA — exact pre-merge Protected Test admission

This is a release-control change, not a merge or completion of feature PR #872.
It admits only the candidate pinned in `.github/flowhive-psa-protected-test-candidate.json`
through the existing `.github/workflows/projectpulse-deploy-test.yml` controller.
Production, private-runtime recovery, customer publication, baseline adoption and
canonical task mutations are not authorized by this approval.

## Admission

The repository owner may post this exact command on PR #872 after these controls
have been reviewed, tested and merged to main:

```
DEPLOY FLOWHIVE PSA PROTECTED TEST SHA d0ab4380dc4b243e8914a08e719556ec71123885
```

The admission workflow executes main-owned code only. It checks the exact open PR,
repository, branch, candidate SHA, 21 required successful exact-SHA PR workflows,
current main control SHA and application-source freshness. Main changes after the
candidate's source base may contain only the reviewed control-only manifest; any
new application changes require a refreshed candidate and approval. The current
feature branch is not renamed or implicitly approved by a prefix match.

The supervisor shares the existing admission lock, refuses any executable active
Protected Test deployment, restores the admission fence if the canonical workflow
is active but idle, verifies the sealed state and repeats the idle-run check,
enables the canonical workflow for one dispatch only, and reseals it. Restoring
this fence only disables workflow admissions; it never cancels or alters a run.
The CI probe is strictly read-only and reports whether sealing is needed. The
main-owned supervisor performs and verifies that sealing before any dispatch. The previously quarantined zero-job run can be disregarded only
while it still has zero jobs. No run is cancelled. A lost dispatch response is an
unknown outcome to inspect, never a reason to dispatch again automatically.

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

## Migrations 103/104

Approved migration bytes are selected from the exact candidate checkout and
matched to the SHA-256 values in main approval. A separate migration image carries
only those SQL files, checksums, exact release identity and the trusted entrypoint.
The image is resolved to an immutable digest. The existing governed private-network
migration runner supplies the approved Test UAMI, TLS database connection and
Key Vault secret **references**, with exact job ownership and cleanup checks.

The entrypoint serializes migration application, bounds database locks/statements,
uses `ON_ERROR_STOP`, and verifies tables, execution fields, triggers, index and
migration receipts. Reapplication is supported. Application rollback does not
attempt to delete immutable evidence or destructively reverse these migrations.
Migration or deployment-health failure stops release; a healthy candidate is
retained after a functional failure for diagnosis.

## Real functional acceptance

The candidate lane replaces only the old long-running FlowHive/Forge acceptance
step for this exact admission. Main and the older candidate retain their existing
UAT behavior. The PSA lane does not enable or run the Module 025 authorization
fixture; it reads the project's existing SOW instead. Existing assigned-work and
utilization gates remain in the canonical job.

The new test authenticates the existing assigned PM, rejects View-As and anonymous
project access, selects the approved project, and posts exactly one generation
request. It captures the current working-copy revision and dates, rejects unsafe
replacement of assigned/milestone work, and never retries an uncertain POST.
Status observation is bounded, checks run/project identity and the backend's
five-minute execution contract, and records stage timings. An unfinished known
operation is cancelled on test failure; a late result cannot be called successful.

On successful generation the test checks five-phase detailed cited work, effort
and schedule reconciliation, absence of automatic milestones and canonical task
adoption, and exact atomic saved-revision readback. It then opens the actual
deployed React application with the PM session, checks task names and dates in
AI Planner work breakdown, reloads the page, and proves no additional generation
or publication was requested. A browser network safety filter aborts unexpected
writes; it never fabricates API responses or substitutes a test model.

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

Node negative tests cover approval, forks, drift, stale/failed CI, dispatch identity
and control scope. Python tests cover false-success rejection, saved receipts,
parsed workflow safety, shell syntax, and unchanged unrelated controller steps.
A disposable PostgreSQL job executes the approved migrations and actual migration
entrypoint, reapplication, legacy-run retirement, immutable RAID evidence,
execution fences, rollback refusal and corrupt-payload/disabled-trigger detection.
None of those isolated tests is represented as live model acceptance.
