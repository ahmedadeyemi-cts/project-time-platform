# PR1204 never-started Protected UAT recovery

## Recorded incident

Run 36463469253 is the attempt-1 workflow_dispatch for main b87f574b284cd2a746eaacc723f2dc36644ba3d9, created and last updated at 2026-09-28T18:11:45Z. Workflow 315562561 has no job on any attempt and no pending environment approval. The owner tried cancellation in the UI; an owner-authorized force-cancel returned HTTP409 even though the API reports Queued. Evidence and request IDs remain on PR1204. This recovery does not delete, cancel, falsify or hide that run.

## Bounded supersession

The existing supervisor can exclude only this exact verified run from its executable-run conflict list. It must run from a distinct exact current-main descendant, with the same complete deployment workflow blob c7b3c7ae88aceb33a0c77f816a21a8ad28952fc4 as the stuck run. Offline tests execute the real historical and current pre-Azure guard: the old SHA is rejected when main has advanced, while the current SHA passes that guard. No Azure login, migration, API/web rollout or message send can occur from that superseded old-main request through this unchanged workflow.

The verifier only uses GET. It checks the exact run/workflow/check-suite/repository/branch/SHA/actor/timestamp/attempt identity, zero all-attempt jobs, zero artifacts and no pending approval twice, with repeated current-main checks. Any progress, job, approval, artifact, identity drift, unrecognized controller or evidence-read failure stops recovery. No other queued run is ignored. A successful real cancellation can only be reported from GitHub's terminal result.

Native Test environment protection, exact-SHA CI, deployment concurrency, the 180-second startup check, full UAT and the supervisor's resealing behavior remain unchanged. The actual Test and Production deployment workflows, application and database files are untouched. This is not a force-merge, stale-main deployment, permanent whitelist or approval bypass.

## Procedure and success criteria

Review and merge the nine-file recovery through ordinary exact-head CI. The existing main supervisor then verifies the pinned old run and dispatches exactly the merged current-main release, which contains PR1204 and subsequent reviewed main changes. A successful recovery requires a real execution job and final Protected UAT acceptance; a newly created run record alone is not success. If the replacement also has no jobs or any other executable run is present, stop without broadening this exception or creating repeated deployments.

The old record may continue displaying Queued in GitHub. The receipt explicitly reports verified_non_executable_orphan, not Cancelled or Deployed. New scheduled notification policies remain disabled/Test-only unless separately approved. No production deployment or company-wide notification activation is part of this recovery.

Two inherited CI digest allowlists are updated to recognize only the exact normal-SA deployment workflow already on main. Their actual-workflow/source-diff checks and all offline tests remain intact; no live deployment code is changed.
