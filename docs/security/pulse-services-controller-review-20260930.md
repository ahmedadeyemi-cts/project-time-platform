# Owner review request: private Pulse document and Laya cutover

Status: REQUEST ONLY. This document grants no deployment authority, installs no
workflow, changes no required check, and authorizes no security-finding closure.

## Requested outcome

Approve an authorized policy-update process for one new manual, Test-only service
cutover controller after reviewing PR #1222 and its exact implementation. The
existing trusted validator rejects new deployment workflows outside its immutable
manifest and also rejects ordinary PRs that modify the validator itself. Do not
work around this by running cutover.py manually, reusing an unrelated workflow,
disabling a check, or copying the proposal into Actions before registration.

Repository: ahmedadeyemi-cts/project-time-platform.
Registered CODEOWNER at review baseline: @ahmedadeyemi-cts.
Baseline: c8ac122653b2d948343727815dfc3279d98c9cc6.
Implementation PR: #1222.
Implementation source reviewed by this request: f88559965a720318ce448da8ac077374bed8e29c.
Implementation tree: ae8b623ae71c1d10da1d2a715e2f6a5609a5d9b1.

Inactive proposal: deployment/pulse-services/proposed-test-cutover.yml.
Proposal SHA-256: d78ff28a780e0288455df7f9f19b8cc99f2b5be1f42862e1f985ee9247b25eb3.
Intended future workflow path: .github/workflows/pulse-services-test-cutover.yml.
Trusted validator SHA-256 at baseline:
4b1920a20e73b10394a9fff66ca092362e57693d2f794d9396a3ca6322cecff8.

Any source/proposal revision invalidates these exact review references and requires
updated evidence. The feature PR does not approve its own release-control change.
The concrete approved policy-update mechanism and reviewer decision are still
missing; do not invent an owner approval or imply this documentation installs it.

## Authority to review

- Fixed existing Test subscription, resource group, registry and managed Container
  Apps environment. No new VM, server, cluster or managed environment.
- Two private internal Container Apps: Pulse document services and Laya. Four
  images separate the document gateway/OCR, scanner/updater, Laya gateway and
  offline Laya model. All runtime processes are non-root.
- A dedicated antivirus-signature share in the existing storage account and a
  narrowly scoped private acceptance job. Existing business shares are excluded.
- API changes limited to the two new document/Laya configuration prefixes and
  run-scoped service credentials. API image, account/password/role settings,
  application schema, existing provider keys and Production remain unchanged.
- Fixed Test environment admission, exact owner/main/source checks, successful
  merged-source CI and prior protected application acceptance. Environment-wide
  projectpulse-deploy-test concurrency with queue: max and cancellation disabled.
- Fresh scan results tied to exact built images before private registry publication;
  strict private TLS/DNS and no redirects; separate service credentials; runtime
  managed-identity access disabled; mandatory process isolation with no silent
  downgrade on an incompatible host kernel.
- Native clean/detection/OCR/model acceptance before API route selection. Old
  verified routes remain selected if staging fails. Pre/post local Super
  Administrator login and identity must match, without account modification.
- Failure rollback restores only prior selected service configuration and verifies
  local administration. Cleanup can target only services created by the exact run.
- Sanitized evidence only; private request bodies, credentials, sessions and raw
  documents are never uploaded as artifacts.

## Required review and technical gates

Owner review must identify the sanctioned way to register this specific
controller while retaining all substantive protections. The reviewer should
also resolve the remaining implementation qualifications: Azure socket-volume
permissions for UID 65534; actual support for the mandatory Landlock/seccomp
controls; identity isolation; storage-key/secret preservation semantics; quota,
resource cost and signature refresh/reload; rollback and partial-staging cleanup;
and the complete authorized application path through each selected service.

Native GitHub Linux tests are not proof of Azure runtime support. A successful
model test is not a classification-accuracy signoff. No readiness result may
mask an unscanned file or bypass document-owner, hash, lease, review, or visibility
controls. Foundry and retirement of the old Oracle host remain separate changes.

## Security remediation continues independently

The existing 149-finding register remains authoritative for original IDs, with
A001/A002 and additional controls tracked separately. This deployment is not
closure of any original finding. PR #1222 additionally records three High
candidate-image findings in the older Transformers dependency and the patched
replacement; native compatibility and fresh scan results are required before
claiming that candidate dependency issue resolved.

Remaining workstreams retain their own evidence gates: Critical release/backup
and host controls; protected local administration and credential/session lifecycle;
positive/negative role and object scope; all upload/import/preview paths; locked
time, billing and concurrency; telemetry continuity and authenticated ingestion;
exact-image assessments; historical exposure disposition; and installed
finding-specific receipts. No row is closed on the strength of this request.

## Owner decision record (not completed)

Reviewer: pending.
Authorized policy-update mechanism: pending.
Exact admitted workflow bytes and source: pending.
Decision and approval reference: pending.
Installed Test acceptance and security disposition: pending.

No live services, identities, release settings, Oracle configuration or Production
resources were changed by this documentation PR.
