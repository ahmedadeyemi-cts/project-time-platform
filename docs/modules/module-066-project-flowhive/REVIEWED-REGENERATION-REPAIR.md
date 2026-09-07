# Milestone-preserving regeneration repair

## Source and publication boundary

Prepared against application candidate `1538548d5bc85c83c5ceb14795e1cfc8536d721c`
from existing draft PR #872. That candidate includes the actual merged PR #874.
The latest verified release-controller base is
`7e5c378dcb15b2b2a00511fa69f90d2411eec336` (merged PR #876).

This is a local implementation and validation checkpoint, not a remote commit,
deployment, live-model result or full enterprise PSA acceptance. Do not update the
approved main-branch candidate pin to this source until the complete new head has
passed real CI and the required release-control changes have been reviewed.
Production and real-customer publication/notifications remain excluded.

## Repair

Existing milestones no longer prevent the native model result from being built.
The builder produces a **detached proposal** rather than silently dropping the
old milestone graph from an adopted working copy. Existing persisted drafts,
activities or milestones require review. A genuinely new, empty project retains
the automatic working-copy save path. Persisted-version detection prevents a
caller from bypassing review simply by omitting tasks from its request.

The worker stores `candidate_review_required` with the generated plan and schedule
inside its existing durable run. This is a terminal inference outcome: the
original five-minute deadline, attempt budget, cancellation and immutable input
fences remain intact. Browser refresh and status reads never restart inference.
The response explicitly distinguishes a persisted proposal from a saved working
copy; the UI renders the proposal inside **AI Planner work breakdown**.

The reviewer loads the current project-authorized working copy and offers an
explicit disposition for every prior activity: retain separately, or map to one
new AI activity. Nothing is silently deleted. Mapping preserves the prior stable
and canonical identity, progress, comments, constraints, effort and assignments.
Retained milestones preserve identity, committed dates and acceptance evidence;
references and dependency edges are reconciled. Cycles, missing predecessors,
multiple mappings to one activity and invalid project identities fail closed.
Retain-all exposes additive hours, rather than concealing possible duplicate scope.

Preview is deterministic and bound to the run, current row version, merged graph,
decisions and review note. A changed note or disposition invalidates the preview.
Apply requires explicit acknowledgement in the UI. The API rechecks project edit
permission, actual/effective identity, document access and source fingerprints.
Concurrent changes reject the apply; a late AI result never replaces PM work.

## Persistence and audit

Migration `105_flowhive_reviewed_regeneration.sql` adds the append-only review
receipt with complete prior, candidate and applied snapshots. Working-copy save,
audit insert and terminal saved-revision receipt share one transaction. A failure
in finalization rolls back every write. An identical replay returns its original
receipt; a changed decision cannot reuse it. The rollback refuses to erase review
evidence after use. The API does not enable apply before the migration receipt.

Human review is not another inference attempt and does not extend the AI deadline.
The browser does not retry uncertain writes. It verifies nested project identities,
run identity, preview validity and the exact working-copy receipt before adoption.
Read-only review recovery is available after a known conflict. An ambiguous save
must be observed through existing working-copy/run reads, not reposted blindly.

## Validation included

- Actual API compilation and executable builder/merge/replay tests.
- Actual React reviewer in desktop and mobile/light and dark fixtures, including
  explicit decisions, preview invalidation, duplicate click, concurrent-edit
  rejection, unknown saves, nested identity mismatches and late project responses.
- Actual FlowHive center fixture: proposal appears in Planner while old working
  tasks/milestone remain; reopening and reloading do not regenerate.
- Existing saved-readback, edited-date, identity-race and detailed-planner tests.
- Real PostgreSQL test definitions for migration apply/reapply, staging without
  working-copy mutation, atomic review receipts, rollback and immutable history.
  These database definitions must execute in disposable CI PostgreSQL; compilation
  is not an executed database test.

All model/API data in the local behavioral/browser tests are synthetic. The live
private model and authenticated application remain separate acceptance gates.

## Required release-control follow-through

The separate one-line Module 001B controller patch projects
`RELIABILITY_RELEASE_COMMIT: ${{ env.TARGET_RELEASE_COMMIT }}` into the assigned-work
UAT step. Its exact before-image blob is
`aa84a76117c9beabf4966a276ae071847c1c8067`. It does not alter authorization, target
selection, cleanup, concurrency, fixture lifetime or rollback behavior. Executable
Bash tests reproduce the original controller-SHA fallback and verify the fix for
both main and candidate releases. Live reallocation still needs verification.

Before this application candidate can be admitted, extend the **reviewed**
main-owned migration approval, builder and entrypoint from migrations 103/104 to
103/104/105, preserving existing migration hashes and the bounded registry read
repair from #876. Bind the new source SHA only after its CI is successful. The
current main control cannot deploy this source merely because old CI was green.

The old live verifier intentionally stops on existing milestones because the
previous application replaced predecessor work. Replace that stop only with a
new-contract-aware test that proves proposal generation, saved proposal/browser
readback and byte-equivalent preservation of the current working plan. An unapplied
proposal is not a successfully applied working copy. Do not remove milestones,
auto-map customer work by positional WBS, fabricate model output, or weaken the
full functional acceptance result to make a pipeline appear green.

The existing broader PSA integration, financial, RAID, reminder, recording,
transcription, sharing and twelve branded-export gates remain open and unchanged.


## Local validation results for this repair

The final local run completed with exit code zero for the actual API build, the
82-assertion executable WBS/merge/replay suite, the 329-assertion existing detailed
planner and Module 025 regressions, all 45 Node operation/admission/scope tests,
SOW auto-admission regression, parsed workflow tests and the full frontend build.
All seven parsed-workflow tests passed with an explicit ancestral controller
base whose full controller blob matches the fetched current main controller.
This comparison uses the exact local ancestry, not a fabricated remote CI run.

The actual React reviewer passed 107 assertions across its synthetic API cases,
including 390-pixel light/dark layouts. The complete FlowHive center passed
19 browser assertions, including persisted proposal reopening and reload without
starting generation again. No local browser response is represented as a live
provider or customer-project response.

The database execution test project compiled successfully. PostgreSQL was not
available in this local runtime, so the new transaction, migration and rollback
checks were not executed here. The API build reports warnings (482 in the final
local build), and the frontend still reports large bundle chunks. A successful
build is not a claim that all warnings or performance goals have been resolved.

There are no remote commits, deployment dispatches or live acceptance results
from this repair checkpoint. The local patch preserves the previously committed
feature work rather than replacing or rewriting the repository history.
