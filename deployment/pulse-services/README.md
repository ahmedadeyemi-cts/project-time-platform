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
