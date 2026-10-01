# Pulse service cutover candidate

This change packages the existing document scanner/OCR and the pinned Laya model
as private, non-root managed containers. It creates no VM, cluster or managed
environment. Test placement is restricted to the existing Pulse environment.
No current runtime has been switched by this source change.

The document gateway, scanner and signature updater form one Container App;
the Laya gateway and offline model worker form another in that same environment.
Replica-local Unix-socket mounts must be writable to UID 65534. There is no
privileged initializer or permission fallback: an incompatible mount blocks
readiness. All service credentials are separate from local accounts, database
credentials and model-provider credentials. They must never appear in logs.

Mandatory unprivileged kernel isolation restricts runtime file writes and prevents
new IP sockets in the parser, scanner, and Laya processes. Unsupported kernel
controls block startup rather than silently dropping isolation. The updater alone
requires signature-feed networking. Container readiness is not evidence that all
149 findings are closed.

Laya uses the existing question schema, 450-token budget and exact checkpoint
1c5edc17a7acd8701df6fc341c0d179f1c62c982. Model data and hash-pinned Python wheels are hash
pinned. The runtime loads locally and does not fetch models, contact Oracle,
perform training or execute model-generated actions. The original local-service
installer supplied by the owner is the source of the worker protocol. Its current
installed Oracle bytes were not independently re-read during this change.

The application retains the legacy routes until a reviewed Test configuration
selects the private services. Startup and monitoring must validate the selected
utilities, not continue requiring Oracle's scanning/OCR components. Inference and
embeddings remain on their existing route until a separate Foundry migration.

Before cutover: exact merged-source CI, image scans, native model/OCR/antivirus
checks, official signature bootstrap/reload, platform isolation and capacity,
private TLS and credentials, local Super Administrator positive login, scoped
API acceptance and rollback must pass. Source tests and mock transports are not
installed evidence. No Production or Oracle host change is included.

## Deployment-governance prerequisite

The new manual controller was correctly rejected by the unchanged root-of-trust
validator because it is not in its immutable manifest. The same validator also
prohibits its own modification by an ordinary PR. Therefore the proposed workflow
is retained as **inactive review material** in `proposed-test-cutover.yml`, not in
`.github/workflows`. It must not be manually executed or copied into Actions to
circumvent that boundary. A separate governance-owner registration decision is
required. No existing controller, manifest, guard digest, or release protection has
been altered, and no Test route is claimed switched.

The proposal retains one Test environment job, the required environment-wide
queue, exact merged-source and accepted-application checks, pre/post local Super
Administrator login, separately scoped service credentials, scanned images,
private native acceptance, and rollback. Its filename and hash must be registered
through the governance-owner's authorized policy-update process before dispatch.

## Canonical-job activation and credential-preservation fixes

The source now provides a server-verified in-job mode for the existing protected
Test workflow. It accepts its machine actor only when repository, exact source,
workflow path, branch, first attempt, current job and current activation step all
match, and every preceding configured release/UAT stage has succeeded. The
standalone owner-only path still requires a completed accepted deployment. This
avoids requiring an in-progress job to have already completed, without bypassing
its prior acceptance or protected environment.

The API credential update no longer sends name-only GET metadata as a replacement
secret list. It requires the explicit existing-value response, retains Key Vault
references as references, rejects missing/duplicate/racing inventories and naming
collisions, and checks that all prior credentials survived before changing routes.
The tests use synthetic values and make no cloud or real-account calls. Live
platform preservation semantics still require installed qualification.

A protected-workflow insertion was attempted in this continuation and was blocked
by the tool before execution. Readback confirmed that the canonical workflow and
all release controls remain unchanged. No alternative route was used to apply
that blocked edit. Accordingly the integration mode cannot be invoked by the
current deployed workflow and must not be executed manually. This source update
is a readiness repair, not activation, not installed acceptance, and not closure
of any original security finding.

## Canonical activation integration (PR #1229)

The current PR adds a private-service phase to the existing canonical protected
Test workflow. It does not install the separate proposed controller, edit the
trusted root validator, or disable any checks. Every pre-existing workflow step
is retained byte-for-byte around the additive block and tested accordingly.

After full existing UAT succeeds, the workflow builds the four rootless service
images, applies the existing pinned High/Critical vulnerability scan gates,
verifies each scan's local image identity, and publishes those exact images to
ACR. Registry digests are pulled back and matched to the scanned identities.
The activation phase independently verifies the live canonical job and all
preceding scan/UAT results. It stages private services in the existing managed
environment, tests the native services, then switches the two service selections.
Failures restore the prior selection before deleting only deployment-owned
resources. Temporary local administrator sessions are closed; identity, role,
password and all existing API credentials are preserved.

Subsequent application releases do not replace already-active services merely
because the application SHA changed. They require an unchanged service-source
fingerprint, rescan the installed immutable images, verify private boundaries
and both administrator/runtime checks. A genuine service-source change requires
its own reviewed upgrade rather than overwriting live services implicitly.

This implementation is not an installed result until the protected run finishes
and its new activation receipt reports success. Kernel, sockets, signature feed,
private TLS, role, credentials and rollback remain mandatory live checks. The
original 149 findings continue to require individual closure evidence.
