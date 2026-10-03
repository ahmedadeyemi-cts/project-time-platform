# Preparation, installation acceptance and recovery runbook

## Stage 1 — Prepare without changing the live system

Use this configuration kit to provision hosts, storage, networking and approved access. Review the read-only inventory before selecting image architecture and disk capacity. Do not run the existing root `deploy.sh`, Oracle `bootstrap.sh` or every SQL file merely because these bundles refer to the same project. Those are different deployment paths.

Inventory from the actually running Pulse environment: exact release, PostgreSQL server version/extensions, database name/size, migration ledger, runtime roles/grants, document roots and sizes, scheduled/hosted workers, current service settings, key providers and key identifiers. Inventory Oracle's installed gateway/worker versions, Ollama engine/model digests, Laya worker source and dependency lock, checkpoint assets, service identities/socket permissions, update schedule and backup/restore behavior. Do not include secrets or customer document bodies in reports. Repository documentation alone is not proof of the current live host state.

## Stage 2 — Prepare an approved release

In the exact reviewed Git checkout, build the candidate API/web/Celar images using `build-images.py`. The API recipe preserves the canonical build and embeds the exact source revision, then adds secret-file startup handling; the web build uses the existing full frontend validators. Resolve application hostname/secret portability and the missing Laya/container-maintenance components before release qualification.

Scan the completed images and inventory their software/licenses. Publish them to the approved company registry only after its ownership and permissions are settled. Pin real registry manifest digests in the local non-secret Compose env files. A local Docker image ID is not a registry digest. Do not silently update base/model versions during a migration comparison. Qualify the selected architecture; no AMD64/ARM64 emulation is a production-performance guarantee.

The generated ZIPs contain configuration and documentation only. Image archives may be supplied as a separate offline release after qualification, with checksums and provenance. Model weights, their applicable license requirements and exact revisions require a separate asset manifest. OneDrive may deliver identical approved copies but does not become the release authority.

## Stage 3 — Baseline database and files

The SQL collection assumes existing provisioning and ordering. Capture an approved baseline from the CURRENT authoritative database, not an old migration snapshot. Prefer a schema-only or sanitized baseline for initial development. Preserve a table-by-table migration ledger and produce a reviewed explicit forward plan for that baseline. Never infer that every lexically later file is safe, nor use a single maximum migration number to imply every earlier migration is installed.

Keep source and target major versions compatible and validate extensions/collation/encoding. Preserve required roles and remap owners deliberately. The runtime application account must not be the bootstrap database owner or a superuser. Restore while the API and other workers are stopped. Establish network egress restrictions BEFORE attaching a restored operational database to the app; do not rely on an assumed global notification-disable flag.

Run the minimum schema check under the actual runtime role. Then run full migration-specific checks and business record reconciliation separately. The supplied check only proves a small required table set and read privileges; it does not prove write grants, triggers, all modules, full data consistency or migration parity.

Transfer uploaded files with hashes and stable project/document identities. Account for separately persisted artifacts, keys and model assets. Confirm files can be retrieved through normal role-based authorization after restoration. Do not apply `PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT=true` to a single local disk as fake HA evidence. Plan shared storage and its genuine attestation separately for production.

## Stage 4 — Configure new endpoints safely

Provision trusted TLS, exact Entra redirects and correct trusted proxy/public-origin settings. Review all hostname-dependent allowlists, links and Teams/connector callbacks. The source's Oracle-only Celar policy is deliberately NOT bypassed in this kit; a new explicit reviewed OpenCloud development transport must be implemented and tested, including Laya's pinned connector. Production authorization must remain separate.

Preserve Module 064's saved provider order, document privacy gates, capability state and encryption keys. Do not change the AI release phase or force false readiness to make startup pass. Verify runtime release source/configuration evidence against the image's embedded source revision.

The Pulse staging Compose has no outbound API network. After the restored queue/notification review is complete, an authorized environment release can add `compose.integration-egress.yaml`. This permits networking; it does not approve sending a message/invoice or waive any application control.

## Stage 5 — Verify, do not infer

Installation acceptance requires actual browser sign-in over the new public HTTPS name, denied unauthorized access, correct role/assignment boundaries, project/time/approval/utilization parity, document downloads, SOW/GSD exports, FlowHive updates and restricted customer links, expenses, manual Certinia receipts and duplicate-send prevention, delivery completion, acceptance and closeout.

Celar acceptance requires actual inference/embedding model digests and dimensions, clean/malware scan behavior with current signatures, extraction/OCR bounds, actual Laya socket health/classification, concurrency/resource behavior and no unauthorized external fallback. Container `/health` requires the core gateway and Laya to report ready; an absent model or worker must not be counted as passing.

The source weekly schedule is Sunday 01:00 America/Chicago. The existing Oracle updater is host/systemd-specific. Do not mount Docker's socket or grant the web gateway root simply to reuse it. Implement an approved container-aware update/reconciliation job, rollback policy and status reporting before asserting maintenance parity. Until then this candidate returns maintenance 503, not a fabricated applied status. Snapshot/model restore and off-host backup need their own qualification.

## Updates and rollback

GitHub should deploy one approved image set for the selected environment, with a target-wide lock, staged verification and retained evidence. Never run two independent personal/work deployment writers. Celar namespace-sharing containers must be recreated together when the edge namespace changes. PostgreSQL must not be reinitialized or upgraded incidentally during an application refresh.

A failed rollout can restore prior compatible application images. Database rollback is a separate data decision; do not assume every migration is additive or compatible. Once writes/external actions occur in OpenCloud, DNS reversal alone does not reconcile them. Preserve manual billing receipts and audit history. Retain evidence even when a rollback is required.

## Backup/restore acceptance

Use database-aware backups plus separate uploaded-file, runtime-state and crypto-key backups. Protect backups outside these VMs with retention and approved encryption. Rehearse restoration into isolated volumes: prevent outbound queues, restore DB/files/keys consistently, verify record counts and document hashes, then run the role and billing/utilization checks. Test failure of a host only after an HA topology exists; a successful container restart is not an HA test.

Never run `docker compose down -v`, broad Docker prune, destructive database restore or DNS cutover against the current environments as part of preparing these handoffs. The kit's own commands are validation/bundling/read-only inspection only.
