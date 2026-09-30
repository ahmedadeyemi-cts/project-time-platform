# Pulse service cutover: state-preservation review

Baseline: `b974a33223b616913fe894b0525e18e22d3357df` (merged PR #1224).
This is predeployment hardening, not an installed release or service migration.
No account, resource, release protection or original finding status was changed.

## Reproduced before repair, using mocks only

1. The readiness loop accepted an old latest/ready revision before the requested
   update appeared. Matching old health is not acceptance of the new deployment.
2. Cleanup could delete a newer deployment of the same source because ownership
   was bound to source but not the exact workflow run. Job cleanup did not verify
   the live resource's ownership before deletion.

## Repairs and regression coverage

Readiness now requires the explicit requested revision name for service staging,
API switching and rollback, plus the expected image inventory where supplied.
Service/job resources carry the exact deployment run. Cleanup verifies source,
manager and run, and also the application's revision suffix. Job ownership is
read before a deletion request. An existing service name blocks initial staging
before signature storage or resource writes; upgrading an existing service is a
separate reviewed operation. Re-running an old workflow attempt is rejected
before external calls; a new explicit request remains required.

Sixteen new regression tests exercise these actual functions with mocked cloud
operations. They cover stale and partially ready revisions, mismatched images,
newer same-source resources, missing ownership, job cleanup order, resource name
collisions, and retry rejection. Existing acceptance and account-preservation
regressions remain required. No successful mocked cleanup is an actual deletion.

## Still required before deployment or security closure

The release supervisor run 36775503176 was cancelled before a job was attached;
the collected evidence does not establish the cause. The protected deployment
integration remains unregistered, tracked in PR #1223. The inactive proposal is
not installed by this PR and must not be executed to bypass that boundary.

After an authorized controller path is established: qualify actual Azure socket
permissions, mandatory process isolation, official signature loading/reloading,
private HTTPS, capacity and secret-preservation behavior; verify native services,
then the complete document/Laya application paths and local Super Administrator
login/identity, and exercise rollback. These live checks have not run here.

The existing 149 original finding records remain byte-identical and pending their
individual evidence. W01 identity/recovery, W02 deployment/backup/host controls,
W03 role/object scope, W04 all upload/import paths, W05 financial-state integrity,
W06 integration credentials, and W07 telemetry/exposure/provenance retain their
own positive, negative and installed-evidence requirements. This review closes
no original finding. Production and Foundry remain separate release decisions.
