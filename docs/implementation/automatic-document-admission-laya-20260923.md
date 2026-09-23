# Automatic document admission and Laya source repair

Date: September 23, 2026
Source baseline: `4c31007053359b1cf41681d686742f6c08fbb3b6`
Status: Draft repair specification. This commit does not implement or activate document processing. No application code, configuration, migrations, or deployed environments have changed.

## Owner-requested outcome

Every accepted document upload or replacement must automatically enter the existing malware, format, and text-extraction checks. A user must not have to open Module 064, click AI Planner, or manually submit each document for routine preparation. Existing unprocessed documents need automatic, bounded recovery rather than wholesale re-upload.

An upload starts processing immediately through a durable queue; it does not finish instantly. The initial receipt must say received/queued, not clean, approved, indexed, or ready. Successful check-in means the exact document version and its processing evidence are registered. It is not business approval of a SOW, contract, authoritative version, or customer handoff.

Security admission and AI retrieval permission are independent. Scan accepted uploads without turning on engineering visibility, widening project access, setting AI-consent flags, or exposing a private conversation attachment to anyone else. Unsupported or prohibited formats must be rejected with an explicit reason; they must not be converted or marked ready to satisfy a dashboard.

## Source findings to repair

### 1. Laya consumes the preview path instead of durable processing evidence

`src/backend/ProjectTime.Api/Modules/LayaDecisionModule.cs` maps the inventory's `ProductionAdmissionReady` to `previewAdmitted`. Classification then calls `BuildProcessingPreviewAsync` and returns `decision_document_admission_required` for failed extraction, failed safety, OCR-required input, or an invalid source hash. These are distinct failures but the UI reports one generic message.

The health request is separate from document admission. A ready Laya gateway does not prove that a particular source version has been scanned or extracted.

### 2. Preview state depends on global flags, not a current scan receipt

`PulseAiPrivateDocumentPipelineService.ToInventoryItem` combines extension support, file availability, path confinement, `MalwareScanAttested`, and `ExtractionPreviewEnabled`. It does not derive its Boolean from a completed per-version processing receipt. The two preview flags default to false in `PulseAiPrivateDocumentPipelineContracts.cs`; their live values have not been inspected.

Do not flip a global malware-attestation flag to manufacture success. Require genuine scan evidence bound to the current file bytes.

### 3. The durable document worker already owns the safety sequence

`PulseAiPrivateDocumentRuntimeService.ProcessNextAsync` reauthorizes the source, creates an immutable snapshot, calls the scanner, quarantines an infected result, requires a clean result, compares the scan hash with the snapshot hash, and only then extracts text. It supports approved private OCR and downstream indexing.

Reuse this owner and its scanner/extraction components. Do not put an independent scanner or unsafeguarded parser inside the Laya panel.

### 4. Automatic queuing is planning-specific, not universal upload admission

`PulseAiPrivateDocumentRuntimeRepository.EnqueueNextEligibleDocumentAsync` selects active, engineering-visible, AI-eligible project documents in recognized planning categories and `not_requested` state. It excludes chat attachments and closed/archived project states and deduplicates active jobs.

`ProjectPlanningDocumentPreparation.QueueAssociatedAsync` is an existing transaction-bound upload/association hook with similar eligibility restrictions, migration checks, identity checks, and release boundaries. Some preparation wording still instructs users to click AI Planner to initiate preparation.

The worker and automatic queue have explicit runtime enablement requirements. The recovery worker requires a dedicated authorized service principal, not a substituted human administrator. Source defaults are not proof of live configuration.

### 5. Preserve the separate owner-only chat-attachment path

Chat attachment upload already creates queued processing under its existing owner. Such files intentionally do not inherit engineering visibility or project-wide AI eligibility. A general backlog sweep must not enroll, expose, or resurrect them. Their existing owner-scoped queue, retention, revocation, and retrieval rules remain authoritative.

### 6. Legacy Word support is inconsistent

`.doc` is in `PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions`, and `PulseAiLegacyBinaryWordExtraction` exists. However, the inspected `PulseAiPrivateDocumentExtractionService.ExtractAsync` dispatch has no `.doc` branch. A file reaching that switch falls into unsupported-format handling.

Verify both signature recognition and execution wiring before declaring support. Use the intended bounded, private text-only converter only after the same safety gates. Do not rename a `.doc` file to `.docx`, execute macros or embedded objects, or silently relax the format policy. This is a source defect, not proof of every live document's individual failure reason.

## Implementation work packages

### A. Inventory and connect upload paths

- [ ] Enumerate actual document creation and replacement handlers, including project upload/association, intake/handoff, generated SOW/GSD registration, approved integration imports, and owner-only chat attachments.
- [ ] Confirm which handlers persist into the shared document registry and which require a separately scoped adapter.
- [ ] Persist the upload and its admission job atomically, or persist a transactional outbox entry with recovery.
- [ ] Bind each job to the document/version, source hash or immutable source identity, owning scope, and policy version.
- [ ] Deduplicate repeated events without reusing an older version's clean receipt for replacement bytes.
- [ ] Keep pending uploads restricted from ordinary delivery, inference, or retrieval until the applicable checks succeed. Review download and external-handoff paths, not only the AI panel.
- [ ] Do not make browser lifetime or a best-effort untracked task responsible for completing preparation.

### B. Use one authoritative admission lifecycle

Required user-facing progression:

`Received -> Queued -> Scanning -> Format validation -> Extracting/OCR -> Checked in -> Indexed for permitted AI use`

- [ ] Preserve actual scanner execution, immutable-snapshot verification, path confinement, size limits, signature checks, archive-expansion protection, and private extraction/OCR.
- [ ] Represent security status, extraction status, indexing eligibility, business approval/version authority, and optional Laya classification separately.
- [ ] Keep scanner unavailable, malicious, unsupported, encrypted, corrupt, missing, awaiting OCR, and retry pending as distinct outcomes.
- [ ] Retain limits, deadlines, cancellation, leases, and immutable processing evidence.
- [ ] Do not mark absent or partially processed evidence as a complete document.
- [ ] Do not make successful Laya inference a prerequisite for security scanning or permitted indexing.

### C. Recover existing uploads

- [ ] Add a bounded, restartable backlog reconciliation that discovers unprocessed current versions without assuming they are safe.
- [ ] Prefer new upload work over historical bulk recovery and bound concurrent CPU/memory pressure.
- [ ] Keep active-job deduplication, attempt limits, and per-version idempotency.
- [ ] Do not endlessly retry permanent failures or silently revive quarantine, cancelled work, revoked files, expired attachments, or archived projects.
- [ ] Record actual discovered, queued, processed, excluded, failed, and quarantined counts with reasons.
- [ ] Preserve the distinction between security admission and whether a document may be used for project planning or AI retrieval.

### D. Connect Laya to processed source evidence

- [ ] Replace the preview-only prerequisite with a server-owned read of the current durable document version, successful scan evidence, and extracted section.
- [ ] Verify current source/version hashes against scan and extraction receipts.
- [ ] Reauthorize before reading text, before inference, and before persisting or returning a recommendation.
- [ ] Reject replacement or revocation during inference and prevent stale results from being published.
- [ ] Preserve the existing 300-Unicode-scalar excerpt policy, fixed checkpoint, bounded transport, labels, and human-review-only behavior for this repair.
- [ ] Make history/status reads inspect persisted evidence rather than reparsing file content.
- [ ] Continue preparation automatically while a user sees a pending state; do not require a classification request to discover that admission has not happened.
- [ ] Keep Laya busy/unavailable separate from scan/extraction failure. No cloud fallback for private source text.

### E. Show actionable state across the application

- [ ] Return sanitized current-version stage, stage timestamp, scan/extraction/index states, and bounded blocker codes/messages.
- [ ] Preserve raw-text, storage-path, credential, and unrelated-record privacy.
- [ ] Use the same authoritative status in upload receipts, document lists, Module 064, and chat attachment selection.
- [ ] Update pending state using existing events or bounded polling that stops on hidden/unmounted pages and handles identity changes.
- [ ] Replace the generic admission warning with the actual failed stage and recovery action.
- [ ] Keep good contrast in light/dark modes and keyboard/screen-reader access.

### F. Verify chat retrieval after indexing

- [ ] Demonstrate that an authorized question retrieves and cites the newly prepared current document.
- [ ] Before readiness, return a processing or evidence-limited answer rather than guessing.
- [ ] Preserve actual/effective-user scope, current source authorization, version precedence, and revocation.
- [ ] Keep private chat attachments isolated from broad project-document search.

Optional Laya chat-intent routing is NOT part of this repair. It would require a separate bounded typed contract, evaluation, privacy enforcement, and application integration. Document admission must not wait for that work. Laya must never grant permissions or authorize public disclosure. Module 064 remains the generative-provider configuration and ordering authority.

## Runtime evidence needed before selecting configuration changes

Use existing authenticated read endpoints; do not paste credentials or environment dumps into the PR:

```text
GET /api/celar-ai/v1/documents/pipeline/readiness
GET /api/celar-ai/v1/documents/runtime/readiness
GET /api/celar-ai/v1/documents/inventory?limit=100
GET /api/celar-ai/v1/documents/{documentId}/runtime-state
GET /api/celar-ai/v1/documents/runtime/jobs
```

Inspect worker/queue enablement, processing principal authorization, shared storage, scanner configuration and actual results, migration readiness, current-version state, extraction error codes, and index readiness. A configuration flag, green gateway badge, or source-code comment is not a completed live processing test.

## Release-blocking acceptance tests

| Case | Required observation |
| --- | --- |
| New supported upload | Durable admission queued without visiting Module 064 or clicking AI Planner. |
| Browser closed or application restarted | Work remains queued/recoverable and completes or exposes a specific failure. |
| Native DOCX, text PDF, spreadsheet | Real scanner and corresponding extraction paths produce version-bound evidence. |
| Genuine legacy DOC | Approved signature and bounded converter path work, or a precise unsupported/converter failure is shown. |
| Scanned PDF | Approved private OCR returns cited text or an explicit OCR failure/limitation. |
| Invalid/oversized/encrypted/corrupt input | Controlled, distinct failure; no fabricated clean/ready state. |
| Simulated infected scanner result | Quarantine; no extraction, Laya call, indexing, normal download, or external handoff. Use mocks rather than malicious files. |
| Scanner unavailable | Never interpreted as clean; bounded retry and named failure. |
| Duplicate upload event | No duplicate active job for the same version and scope. |
| Replacement during processing | Old receipt cannot authorize new bytes or publish stale extracted text. |
| Revocation/deletion during processing | No stale result, citation, or classification is returned or retained as current. |
| Backlog interruption | Recovery resumes without duplicate work or bypassing exclusions. |
| Role/project/conversation isolation | No cross-user document, status, excerpt, cache, or search leakage. |
| Laya use | Exact clean extracted version is consumed; human review and original recommendation are retained. |
| Status consistency | Upload list, Module 064, and chat agree on the current stage. |
| Chat after indexing | Answer has grounded current-document citations in the effective user's scope. |
| Unrelated behavior | Provider order, business approval, original history, and release controls are unchanged. |

Required evidence: actual backend/frontend builds, database/queue tests, negative authorization tests, browser tests, and authenticated Protected UAT end-to-end receipts. None of the new repair acceptance tests is claimed to have passed in this specification commit.

## Migration, deployment, and rollback

Determine whether independent security-admission state or a durable outbox needs a migration after reviewing the owning schema. Reserve an unused migration identifier through the repository's normal process. Do not mutate existing applied migrations or invent an approval reference.

Do not enable workers, change service principals, grant permissions, or alter runtime settings merely by creating this PR. Implementation and validation should occur on this isolated branch. Keep the PR draft until it contains tested code and its release gates are met. No merge, Protected UAT deployment, Production deployment, provider change, or schema application is authorized or performed by this specification commit.

Document eventual rollback boundaries before activation, preserving processing receipts and quarantine history. Never roll back by marking all pending documents clean or deleting their evidence.
