# Celar AI production-hardening contract

Migration `071_ai_runtime_production_hardening` must be applied after migrations
052, 053, and 061. The API never creates or alters Module 064 tables at runtime;
it validates the migration ledger and required columns and fails closed if the
release runner has not applied Migration 071.

## Encryption key ring

`PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY` is the base64 encoding of the active
32-byte AES-256 key. `PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID` is its stable,
non-secret identifier. `PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_RING` is an
optional JSON object containing prior key IDs and their base64 keys during a
rotation window. Key material belongs in the approved secret store and must
never be pasted into tickets, chat, logs, workflow inputs, or deployment output.

Rotation is performed only by a non-View-As Module 064 administrator through
`POST /api/ai-configuration/encryption-key/rotate`, from the same origin, with:

- `expectedCurrentKeyId`: the current persisted key ID;
- `expectedActiveKeyId`: the newly configured active key ID;
- `confirmation`: `ROTATE-PROJECTPULSE-AI-ENCRYPTION-KEY`.

The old and new keys must both be present in the running key ring. The operation
locks and re-encrypts public-provider secrets and every private profile in one
serializable PostgreSQL transaction. It audits only key IDs, versions, actor,
and time. Remove the old key from the ring only after the operation succeeds,
all replicas have restarted with the new active key, and read/probe validation
passes. The guarded rollback refuses to remove key-ID metadata while encrypted
rows exist.

## Shared probe evidence

Normal active-revision Celar readiness is not process-local. The private-model test writes one
database evidence row containing provider, environment, profile revision,
success/failure, sanitized diagnostic code, request ID, model fingerprint,
replica ID, test time, and expiry. It stores no endpoint, token, prompt,
response, customer identity, or document content. Production readiness accepts
only a successful, unexpired row for the exact current profile revision. The
default TTL is 15 minutes; a settings or secret revision change invalidates old
evidence immediately.

Release behavior is explicit and fail closed:

- `PROJECTPULSE_AI_RELEASE_PHASE=disabled` uses the ordinary database-managed
  configuration;
- `PROJECTPULSE_AI_RELEASE_PHASE=candidate` is an intrinsically read-only,
  zero-traffic verification appliance; and
- `PROJECTPULSE_AI_RELEASE_PHASE=active` uses the normal application data plane
  while keeping the AI provider profile and all eight routes deployment-managed.

Candidate and active release phases require the exact 40-character
`PROJECTPULSE_AI_RELEASE_SOURCE_COMMIT`, `PROJECTPULSE_SOURCE_COMMIT`, immutable
`ProjectPulseSourceRevision` assembly metadata, 40-character
`PROJECTPULSE_AI_RELEASE_CONTROL_COMMIT`, and protected
`PROJECTPULSE_AI_RELEASE_CONFIG_SHA256`. The runtime recomputes that digest from
an ordered, non-secret configuration manifest. The manifest contains exact
provider endpoints, models, approved-model lists, enabled flags, private-host
allowlist, OCR/embedding/scanner/worker settings, storage attestation, exact SOW
evidence, and immutable secret-version resource identifiers; it never contains
secret values. Missing, `latest`/`current`, malformed, or mismatched evidence
fails startup. `PROJECTPULSE_AI_RELEASE_ROUTE_ORDER` must be exactly
`celar_ai,claude,openai,local_template`.

The candidate exposes only `GET|HEAD /health` and exact
`POST /api/ai-configuration/release-candidate/verify`; every other request
returns HTTP 423 before authentication or module middleware. Session validation
does not update `last_seen_at`. The combined operation runs the private Celar
probe, fixed identity-free Claude/OpenAI probe, Module 064 readiness, exact
SOW/source-hash check, storage attestation, and database-identity check in one
request on one replica. It writes no route, profile, session, probe evidence,
audit, queue, conversation, document, or file state. Candidate database evidence
must show default read-only transactions and no elevated role, database/schema/
temporary-create, table-mutation, or sequence-write privileges.

Candidate storage reads and hashes one existing protected canary named by
`PROJECTPULSE_UPLOAD_ROOT_ATTESTATION_FILE` and compares it with
`PROJECTPULSE_UPLOAD_ROOT_ATTESTATION_SHA256`, while proving the file metadata is
unchanged before and after inspection. It never creates or deletes a probe.
Active and ordinary runtimes keep
the write/delete storage probe. Background provider probes, document workers,
automatic admission, feedback, answer/retrieval audit, System Intelligence
persistence, and Project Forge draft persistence do not run in candidate phase.
They run normally in active phase. Database secret/model synchronizers remain
frozen in both release phases so shared configuration cannot replace protected
revision configuration.

## Private worker lease fencing

Every document-job claim receives a random `lease_token` and a monotonically
increasing `lease_generation`. A heartbeat renews the current claim while
scanner, extraction, OCR, embedding, and indexing work is in progress. Stage,
terminal, and index-publication writes require matching owner, token,
generation, and an unexpired lease. Loss of the fence cancels processing and a
stale replica cannot publish results. Expired claims are requeued or failed by
the existing maximum-attempt policy without logging document content.

## Production readiness

Module 064 reports production ready only when all of the following are true:

- migrations 052, 053, 061, and 071 are recorded and their runtime structures
  are available;
- all eight central capability routes exactly equal the governed configured
  order, with the governed local template last;
- the encrypted private profile is persisted, enabled, and requires private
  inference for document-grounded answers;
- the HTTPS endpoint hostname is allowlisted and resolves only to approved
  private addresses; loopback and IPv4/IPv6 link-local addresses are rejected,
  and the TCP connection is pinned to a connect-time revalidated private IP
  while normal TLS hostname/certificate validation remains enabled;
- a shared persistent upload root (read-only protected-canary attestation in
  candidate phase; runtime write/delete probe in active phase), worker service
  principal, private malware scanning, OCR when needed, and private embeddings
  or approved lexical-only operation are ready;
- the automatic-queue service principal currently exists, is active, and has
  `QUEUE_PULSE_AI_DOCUMENT_PROCESSING` through an active role assignment;
- at least one authorized approved/canonical SOW or GSD is processed and ready;
- a successful shared private-model probe for the exact profile revision is
  unexpired; and
- when sanitized external fallback is enabled, both policy switches are on and
  Claude and OpenAI are configured, enabled, and backed by fresh successful
  probes. Activation must additionally exercise the fixed-input deidentified
  fallback probe; provider connectivity alone is insufficient.

Document-version approval is optimistic and content-bound: the request carries
both the active version ID and its expected source SHA-256. The repository
checks both values in the same update, so a concurrently reprocessed SOW cannot
cause an operator or activation workflow to approve a different version.

The exact-head CI workflow executes Migration 071 twice, verifies the single
ledger row and business-row preservation, proves a mid-rotation cryptographic
failure rolls the entire transaction back, proves the former key cannot decrypt
rotated ciphertext, proves guarded rollback refusal, rolls back after evidence
is retired, reapplies the migration, builds the API, and checks generated-source
convergence.

## Required two-stage release sequence

Production hardening and environment activation are deliberately separated by
the repository's ownership and deployment controls:

1. Merge a source-only pull request containing the application, Migration 071,
   backend/frontend validators, focused CI, tests, and this runbook. Its exact
   head must pass the real 052 → 053 → 061 → 071 migration lifecycle and the
   backend/frontend build gates. It must not contain `deployment/` changes or an
   activation workflow.
2. Before candidate creation, verify that migrations 052, 053, 061, and 071 are
   already applied and that the exact authorized SOW ID/source SHA is already
   canonical and ready. Candidate activation must not run migrations, upload or
   reprocess SOWs, alter provider routes/profiles, or create a storage probe.
3. From the exact merged source SHA, open a second deployment-control
   pull request containing only the guarded activation workflow, activation
   scripts, and purpose-built container definitions. The controller must be
   invoked with two distinct immutable authorities: `expected_source_sha` is
   the merged PR1 application commit, while `expected_main_sha` is the exact
   current-main PR2 control commit. PR1 must be an ancestor of PR2, and the
   complete PR1-to-PR2 diff is rejected unless every path is in the narrow
   activation-control allowlist. The API and migrations are built from a
   detached checkout of PR1; only reviewed control Dockerfiles needed for
   digest-addressed base-image arguments may be overlaid. Both SHAs are
   revalidated and recorded as evidence. The controller builds the detached
   source with `-p:ProjectPulseSourceRevision=<expected_source_sha>`, resolves
   the immutable image digest, and creates a zero-traffic candidate using a
   dedicated read-only database identity, read-only storage canary, exactly one
   replica, and the exact AI configuration digest. It fingerprints database and
   storage state before and after the single combined verification request and
   fails if either changes. Missing source/control/config metadata, a stale SHA,
   a replica-count or traffic mismatch, or an intervening release fails closed.
4. The candidate revision is never promoted and never receives traffic. At the
   explicit protected-environment authorization boundary, create a new active
   revision from the exact same immutable image digest and AI configuration
   digest, with `PROJECTPULSE_AI_RELEASE_PHASE=active` and approved active
   database/storage identities. Shift traffic only to that new active revision,
   deactivate the candidate, then run active customer-ready Timesheet/SOW
   verification. Test and Production require separate approvals. A traffic
   rollback must be reported as traffic-only; it cannot claim document or
   database state was reversed.

The two stages must not be combined or bypassed by widening the source CI path
guard. A successful source build is not runtime activation evidence. Likewise,
the activation controller cannot make an unreviewed application tree eligible
for deployment.

Production activation remains a separate, explicitly authorized action. It
must use version-pinned secret references, immutable image digests, distinct
least-privilege job identities, a persistent shared upload mount, private-only
dependency probes, an exact SOW/GSD ID plus source hash, and a zero-traffic
candidate before creation of a separate active revision. Additive AI migrations
remain applied during image rollback; destructive database rollback is never
automatic.

The activation SOW request must identify one exact document and exact source
SHA-256 whose category is `sow` or `gsd`. Activation cannot be satisfied by a
different older ready document or by an aggregate count alone.
