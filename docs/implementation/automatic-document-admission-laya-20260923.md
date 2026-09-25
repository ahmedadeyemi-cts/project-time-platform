# Automatic document admission and Laya source repair

Date: September 23, 2026
Source baseline: `4c31007053359b1cf41681d686742f6c08fbb3b6`
Status: Draft repair specification. This PR does not yet implement or activate the application-code fix. No application configuration, database migration, or deployed environment has changed.

## Required outcome

Every accepted document upload or replacement must automatically enter the existing malware, format, and text-extraction checks. Users must not need to visit Module 064, click AI Planner, or manually queue each document. Existing unprocessed documents need bounded automatic recovery instead of wholesale re-upload.

Processing starts through a durable queue as soon as the upload is registered; it does not finish instantly. The initial receipt must say received/queued, not clean, approved, indexed, or ready. Check-in means the exact version and its processing evidence are registered. It must not silently approve a SOW, contract, authoritative version, or customer handoff.

Security admission and AI retrieval permission are independent. Scan accepted uploads without changing engineering visibility, project access, AI consent, or private conversation ownership. Unsupported or prohibited formats need an explicit rejection, not forced conversion or fabricated readiness.

## Source findings

### Laya reads preview state instead of durable processing evidence

`src/backend/ProjectTime.Api/Modules/LayaDecisionModule.cs` maps the preview inventory's `ProductionAdmissionReady` to `previewAdmitted`. Classification calls `BuildProcessingPreviewAsync` and returns `decision_document_admission_required` for unsuccessful extraction, failed safety, OCR-required input, or an invalid source hash. The frontend collapses these distinct reasons into a generic warning.

The health request is independent of document admission. A ready gateway is not proof that the selected version has a successful scan, extraction, or index receipt.

### Preview readiness depends on global flags

`PulseAiPrivateDocumentPipelineService.ToInventoryItem` combines extension support, file availability, path confinement, `MalwareScanAttested`, and `ExtractionPreviewEnabled`. It does not use a completed per-version worker receipt. The preview flags default to false in `PulseAiPrivateDocumentPipelineContracts.cs`; their live values have not been inspected.

Do not flip a global attestation flag to clear the warning. Admission must require genuine scan evidence for the current bytes.

### The durable worker already owns the safety sequence

`PulseAiPrivateDocumentRuntimeService.ProcessNextAsync` reauthorizes the source, creates an immutable snapshot, runs the scanner, quarantines infected input, requires a clean result, compares source hashes, and extracts only after these checks. It supports approved private OCR and downstream indexing.

Reuse this owner and its scanner/extraction components. Do not create an independent scanner or unsafeguarded file parser inside Laya.

### Existing automatic queuing is planning-specific

`PulseAiPrivateDocumentRuntimeRepository.EnqueueNextEligibleDocumentAsync` selects active, engineering-visible, AI-eligible project documents in recognized planning categories and `not_requested` state. It excludes chat attachments and closed/archived project states and avoids duplicate active jobs.

`ProjectPlanningDocumentPreparation.QueueAssociatedAsync` is an existing transactional upload/association hook, with migration, identity, release, and planning-eligibility checks. The canonical `Program.cs` already calls it during project association, supporting-document upload, and Work Register project-document upload. The correct repair is to complete coverage and separate security admission from optional AI preparation, not assert that no upload hook exists.

Worker execution and automatic recovery have explicit configuration requirements. The recovery worker requires a dedicated authorized service principal; a human administrator must not be substituted. Source defaults are not live-state evidence.

### Preserve owner-only chat processing

`CelarAiConversationAttachmentService` already queues its uploads under the owning user's path. Chat attachments intentionally do not inherit engineering visibility or project-wide AI eligibility. A broad project backfill must not enroll, expose, or resurrect them. Preserve their owner-scoped queue, retention, revocation, and retrieval checks.

### Corrected finding: legacy .doc IS wired into the generated build

The earlier preview-source-only inspection was incomplete. The canonical `PulseAiPrivateDocumentExtractionService.cs` lacks a `.doc` switch branch, but it is not the final compiled source.

`src/backend/ProjectTime.Api/Directory.Build.props` runs `EnableFlowHiveLegacyWordExtraction` before `CoreCompile`, explicitly depending on `GenerateScopedRbacSources`. The target invokes `build/enable-flowhive-legacy-word-extraction.py`, which adds `.doc` dispatch, OLE/text-compatible signature handling, and sealed immutable-snapshot handling to the generated compiler copy. `PulseAiLegacyBinaryWordExtraction` supplies the private converter. `tests/FlowHiveDetailedPlannerTests/Program.cs` already includes compiled legacy-word regression cases.

The post-processor was executed successfully on a temporary copy of the baseline extractor during this review; the expected `.doc`, signature, and path-policy markers were present. This is a generated-source check, not a full .NET build or live file-extraction test.

Do NOT add a duplicate converter or declare `.doc` unsupported merely from its extension or the canonical switch. Inspect the running build, actual format, extractor availability, and specific per-file error. Do not rename `.doc` to `.docx` or relax macro/signature checks.

## Implementation work packages

### A. Durable admission at upload

- [ ] Enumerate actual creation/replacement/import handlers and their storage/registry owners. Include intake supporting uploads, intake request attachments, Work Register upload/save/association, generated SOW/GSD registration, approved integration imports, and private chat attachments. Review expense/template upload surfaces separately rather than assuming they share the same document registry.
- [ ] Persist the upload and its admission job atomically, or persist a transactional outbox entry with recovery.
- [ ] Bind work to document/version, source hash or immutable source identity, owning scope, and policy version.
- [ ] Deduplicate repeated events while ensuring replacement bytes receive new scan evidence.
- [ ] Keep pending files restricted from ordinary delivery, inference, and retrieval until applicable checks pass; inspect download and external-handoff paths, not just the AI panel.
- [ ] Make the queue survive closed browsers, restarts, and interrupted workers. Do not depend on an untracked fire-and-forget task.

### B. Authoritative processing state

Required progression:

`Received -> Queued -> Scanning -> Format validation -> Extracting/OCR -> Checked in -> Indexed for permitted AI use`

- [ ] Preserve actual scanner execution, immutable-snapshot checks, path confinement, size/signature/archive limits, and approved private OCR.
- [ ] Represent security admission, extraction availability/limitations, AI indexing, business approval/version authority, and optional Laya recommendation separately.
- [ ] Distinguish scanner unavailable, infected, unsupported, encrypted, corrupt, missing, awaiting OCR, retry pending, and exhausted failure.
- [ ] Preserve leases, cancellation, bounded concurrency/deadlines, and processing audit evidence.
- [ ] Keep business approval separate from technical processing success.
- [ ] Do not require successful Laya inference before scanning or permitted indexing can finish.

### C. Existing-document recovery

- [ ] Reconcile existing current versions in bounded, resumable batches without assuming they are safe.
- [ ] Prefer new upload work over historical bulk recovery and cap resource use.
- [ ] Preserve active-job deduplication and attempt limits.
- [ ] Do not endlessly retry permanent failures, clear quarantine, reactivate cancelled work, resurrect revoked/expired attachments, or reopen archived projects.
- [ ] Record actual discovered, queued, processed, excluded, failed, and quarantined counts with reasons.
- [ ] Keep security admission separate from eligibility for planning or AI retrieval.

### D. Laya processed-source adapter

- [ ] Replace the preview-only prerequisite with an authorized read of the current durable version, successful scan evidence, and extracted section.
- [ ] Match current source/version hashes against both scan and extraction receipts.
- [ ] Reauthorize before reading text, before inference, and before saving/returning a recommendation.
- [ ] Reject replacement or revocation during inference; do not publish stale results.
- [ ] Preserve the existing 300-Unicode-scalar excerpt policy, fixed model revision, labels, bounded transport, and human-review-only behavior in this repair.
- [ ] Make status/history reads inspect persisted evidence rather than reparse files.
- [ ] While preparation is pending, show the actual stage and let the queue continue automatically.
- [ ] Keep Laya busy/unavailable separate from admission failures. No cloud fallback for private source text.

### E. Consistent UI status

- [ ] Return current-version stage, stage timestamp, scan/extraction/index status, and sanitized bounded blocker codes/messages.
- [ ] Do not expose raw text, storage paths, credentials, or unrelated records.
- [ ] Use the same authoritative state in upload receipts, document lists, Module 064, and chat attachment selection.
- [ ] Use existing events or bounded polling; stop polling on hidden/unmounted pages and clear stale state on identity changes.
- [ ] Replace the generic admission warning with a specific failed stage and recovery action.
- [ ] Preserve light/dark contrast, keyboard interaction, and screen-reader status announcements.

### F. Verify Celar document answers

- [ ] Demonstrate that an authorized question retrieves and cites the newly prepared current document.
- [ ] Before readiness, report processing or missing evidence instead of guessing.
- [ ] Preserve effective-user scope, current source authorization, version precedence, and revocation.
- [ ] Keep private attachments out of broad project-document search.

Optional Laya chat-intent routing is outside this repair. It requires a separate typed contract and evaluation and must not delay document admission. Laya must not grant permissions or authorize public disclosure. Module 064 remains authoritative for generative-provider configuration and order.

## Runtime evidence to collect

Use authenticated read endpoints; never paste tokens or environment dumps into the PR:

```text
GET /api/celar-ai/v1/documents/pipeline/readiness
GET /api/celar-ai/v1/documents/runtime/readiness
GET /api/celar-ai/v1/documents/inventory?limit=100
GET /api/celar-ai/v1/documents/{documentId}/runtime-state
GET /api/celar-ai/v1/documents/runtime/jobs
```

Inspect worker/queue enablement, processing-principal authorization, shared storage, scanner configuration and actual results, migration readiness, current-version state, extractor diagnostics, and indexing. A flag, source comment, or green gateway badge is not an end-to-end processing test.

## Release-blocking acceptance

| Case | Required observation |
| --- | --- |
| New supported upload | Durable admission queued without Module 064 or AI Planner interaction. |
| Browser closed/application restarted | Recoverable work completes or exposes a specific failure. |
| DOCX, text PDF, spreadsheet | Real scanner and corresponding extraction paths produce version-bound evidence. |
| Legacy DOC | Existing generated-build converter runs safely, or exposes the precise format/runtime failure. |
| Scanned PDF | Approved private OCR produces cited text or a clear limitation/failure. |
| Invalid/oversized/encrypted/corrupt input | Controlled distinct outcome; no fabricated ready state. |
| Mock infected scanner result | Quarantine prevents extraction, Laya, indexing, ordinary download and external handoff. No malicious file is needed for unit testing. |
| Scanner unavailable | Never clean; bounded retry and named failure. |
| Duplicate event | No duplicate active job for the same version/scope. |
| Replacement | Old scan evidence cannot authorize new bytes or stale output. |
| Revocation/deletion | No stale result, citation or recommendation becomes current. |
| Interrupted backlog | Recovery resumes without duplicate work or bypassing exclusions. |
| Role/project/conversation isolation | No cross-user document, status, excerpt, cache or search leakage. |
| Laya classification | Exact clean extracted version consumed; original recommendation and human review retained. |
| UI consistency | Upload list, Module 064 and chat agree on current state. |
| Post-index chat | Grounded current-document citations within effective-user scope. |
| Regression | Provider order, business approvals, original history and release controls unchanged. |

Before release, require actual backend/frontend builds, database/queue tests, negative authorization tests, browser tests, and authenticated Protected UAT upload-to-answer receipts. No repair acceptance pass is claimed by this specification PR. The generated-source check above only corrects the legacy-Word finding.

## Release and rollback boundaries

Determine whether independent security-admission state or an outbox requires a migration after reviewing the owning schema, then reserve an unused identifier through the normal process. Do not alter applied migrations or invent approval references.

Do not enable workers, grant permissions, change service principals, or alter runtime settings merely by creating this PR. Keep it draft until implementation and validation are complete. No merge, Protected UAT deployment, Production deployment, provider change, or schema application is performed here.

A temporary read-only, exact-branch source-preparation workflow was used for repository inspection and removed from the final diff. Its run packaged tracked source only with credentials disabled and one-day artifact retention; it was not a build or a document-processing test. No branch-write or deployment authority was introduced.

Plan rollback before eventual activation and preserve scan receipts, original versions, and quarantine history. Never roll back by marking pending files clean or deleting their evidence.
