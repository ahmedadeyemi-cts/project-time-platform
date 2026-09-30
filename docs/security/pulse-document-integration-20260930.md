# Pulse document-service integration: default-off Test candidate

Baseline: `bb2c9cccaf4d95cbfe32818e3f55ccb58cf029dc` (PR1220).
Status: source integration and placement qualification only; not an installed service.

## Implemented

The existing `PulseAiPrivateMalwareScanner` and `PulseAiPrivateOcrClient` select the new authenticated document service only when deployment configuration explicitly requests it. Missing mode or `legacy` preserves the existing path. Invalid activation is a failed scan, never a fallback or clean verdict. A separate HTTP client validates one exact internal HTTPS destination, checks private DNS addresses again at connection time, and disables redirects, cookies and proxies. Standard TLS certificate validation remains enabled.

The adapter sends an immutable-snapshot content hash and byte count. Responses must match that content, scanner identity, loaded signature version/freshness, document identity and OCR model. Duplicate JSON fields, inconsistent verdicts, oversized responses, invalid pages, excess text, incomplete results and missing scan evidence are rejected. Source hashing and response reads are bounded. Caller cancellation propagates; busy and unavailable results enter the existing retry handling rather than changing scanner provider.

Readiness for the new mode probes the actual scanner/OCR service and loaded signatures. It does not require a model provider. This separates document transport; it does not remove all legacy Oracle model/runtime configuration or perform the Foundry transition.

## Existing durable processing is retained

`PulseAiPrivateDocumentRuntimeService` already owns job claim/lease/heartbeat, cancellation, bounded retries, immutable snapshots, scan-hash validation and post-extraction integrity checks. The integration reuses those operations rather than creating another queue or changing the database. `PulseAiPrivateDocumentPipelineService` previews use the same scanner. No helper flag is accepted as evidence that a document is clean.

## Deployment-only configuration

All new fields use `PROJECTPULSE_DOCUMENT_SERVICE_`: MODE, ENVIRONMENT_DOMAIN, ORIGIN, APPROVAL_REFERENCE, TOKEN_SECRET_REFERENCE, TOKEN and CONFIGURATION_SHA256. Default MODE is `legacy`; candidate MODE is `pulse_container`. The candidate requires `PROJECTPULSE_ENVIRONMENT=test` and the exact host `ca-phd-test-documents-westus3.internal.<existing-environment-domain>` on HTTPS443. The approval reference binds a reviewed PR/source; the configuration digest binds the separate destination and credential version. These checks do not replace protected release/environment approvals.

No credential values belong in the repository, logs, reports or pull-request discussion. The service credential must not reuse a human account, database password or model-provider token. Configuration text and JSON serialization omit its bearer value.

## Local Super Administrator invariant

No authentication/account module, user row, password, role assignment, login setting, reset behavior or database migration is changed. Local Super Administrator sign-in remains a required positive installed regression before cutover. Destructive reset/deletion tests must use isolated accounts, not the owner's real account. Source preservation is not a claim that a fresh live login was performed by this PR.

## Existing-environment placement and remaining activation gates

Use the existing `cae-phd-test-westus3` managed environment, not a new VM, cluster or managed environment. This PR supplies no placement or deployment automation. A candidate aggregate of 3.5 vCPU / 7 GiB covers worker 0.5/1, scanner 2/4 and updater 1/2. Actual quota, cost and signature-reload headroom remain unverified. A legacy Consumption-only environment cannot fit this envelope; deployment must remain blocked rather than silently provisioning infrastructure or shrinking safety margins.

The Docker Compose reference is not an Azure deployment template. Container Apps replicas share network resources. Its resource schema does not directly reproduce every Compose capability, read-only-root or per-container network setting. Secret projection also needs a reviewed path to the worker's restricted secret file. Shared Unix sockets belong on replica-local storage, not an SMB file share. Persistent signature storage, ownership, bootstrap/reload, private TLS and credential rotation need explicit qualification. No control is silently removed to make deployment succeed.

The current source inventory covers shared runtime processing and processing previews. Native prepaid-contract, laboratory, expense and OneAssist import entry points still require individual scan-before-parse and legitimate-use acceptance. Do not claim that enabling the shared adapter alone covers every upload or closes all document findings.

Required before activation: publish reviewed images by immutable registry digest; qualify the approved managed-container placement and capacity; verify official signature bootstrap/update/reload and outage recovery; exercise normal and rejected documents through every applicable caller; verify the local Super Administrator and affected business workflows; run an isolated Oracle-unavailable test without shutting down the shared server; obtain exact-source protected Test acceptance. Then select the new mode through a reviewed activation change. No manual endpoint edit or direct deployment bypass is authorized by this document.

## Rollback and security closeout

Keep the existing provider selected until installed gates pass. Capture the approved document-service configuration per job attempt. An in-progress attempt retains its options; a subsequent authorized attempt may use a newly deployed revision and must scan again. Rollback selects the previously verified legacy scanner; it must never accept documents with scanning disabled. No Foundry endpoint, provider order, embedding model, local account or Production setting changes here.

The original 149-finding register, additional locations and security dispositions remain unchanged. Compiled transport-contract tests are synthetic evidence, not live antivirus feed, Azure isolation, real-account or finding-closure evidence.

## Primary platform references

- https://learn.microsoft.com/en-us/azure/container-apps/containers
- https://learn.microsoft.com/en-us/azure/container-apps/storage-mounts
- https://learn.microsoft.com/en-us/azure/templates/microsoft.app/containerapps
- https://docs.clamav.net/manual/Installing/Docker.html
