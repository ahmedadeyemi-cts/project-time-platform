# Pulse document-processing container foundation

Status: opt-in source candidate, not integrated or deployed. Existing local accounts, Super Administrator access, Pulse configuration, database, Oracle services and deployment controllers remain unchanged.

## No new Azure server

Use companion containers in the existing Pulse Container Apps environment or existing approved container platform. No VM, Kubernetes cluster, new managed environment or new server is created by this PR. Additional managed compute/memory still has a cost.

`compose.yml` is a portable topology reference, not an Azure deployment template. Azure placement, capacity, private ingress, TLS, identity, storage and equivalent isolation require a later reviewed PR. Compose settings do not automatically transfer to Azure.

## Components

The worker image supplies an authenticated HTTP adapter, Tesseract English, Poppler and Pillow. The scanner image supplies ClamAV and FreshClam; separate scanner and signature-updater containers use that image. Neither image contains an Oracle, model-inference, embedding or database client.

The scanner has no network and uses a shared Unix socket, not publicly exposed TCP. The worker network is internal. The updater alone has outbound signature-feed access and database write access; it has no documents or application token. Target egress restrictions remain a deployment qualification requirement.

All services run as existing non-root UID/GID 65534 with read-only root filesystems, dropped capabilities, no-new-privileges and resource limits. No host ports, host networking, privileged mode or Docker socket is included.

## Request and content boundaries

Requests need a restricted service-secret file and the exact private-boundary header. Liveness returns only a content-free status. Readiness checks the loaded scanner database and OCR binary without requiring AI availability.

Uploaded bytes receive a generated temporary filename, SHA-256 and byte count. Scan evidence must match the saved content. OCR scans its own upload before parsing; caller-supplied clean status is ignored. Errors omit original names, text, credentials and parser stderr. Successful extraction returns text only to the authenticated internal caller.

Unknown verdicts, scanner failures, stale/unknown signatures, changed signatures, encrypted inputs and scan-limit results are not clean. Signature age is limited to 48 hours as a candidate policy subject to security review.

OCR uses a separate process, fixed arguments without a shell, a minimal environment, total deadline and CPU/memory/file/output limits. PDFs are scanned first, then rendered one bounded page at a time. Single-frame PNG/JPEG/BMP/TIFF/WebP are supported; multiframe images, SVG, image-list inputs, encrypted/invalid PDFs and oversized inputs are rejected. Only English is packaged initially.

Limits: 32 MiB input, 50 PDF pages, 16,777,216 raster pixels and one million output characters. One active job per worker returns 429 under load; this is not a durable application queue.

## Capacity and ongoing maintenance

Candidate resource envelopes: worker 1 GiB/1 CPU, scanner 4 GiB/1 CPU, updater 2 GiB/0.5 CPU. Qualify the aggregate against the existing environment. Do not crowd these into the API allocation, silently create a VM, or reduce scanner memory just to fit. Signature reloads require headroom; the numbers are not performance guarantees.

FreshClam validates updates in a persistent signature volume and notifies clamd. No fresh database means not ready. Initial software/image distribution and signature updates still need approved outbound paths or an internal mirror; document content need not leave for these updates. Moving containers does not eliminate patching or signature maintenance.

The base image is pinned by registry digest, but native packages resolve from signed Debian repositories at build time. This is not a complete bit-for-bit dependency lock. CI records package inventories and local image IDs and scans candidates. Local Docker config IDs are not registry digests. Fixable High/Critical gates do not waive unfixed or lower-severity findings.

## Separate integration and cutover PR

Current Pulse runtime policy binds OCR/scanning and models to Oracle. This foundation leaves that policy untouched. A later adapter PR must separate document-service destination, credentials, health and authorization from model-provider configuration while preserving approved private/TLS boundaries. Private HTTP in this Compose reference does not authorize insecure Azure traffic.

Inventory all upload/import/processing-preview callers. Preserve authorization before parsing, scan/version linkage, document visibility and safe downloads. Implement durable pending/retry/idempotence behavior in Pulse. Keep the existing provider default until representative normal/rejected inputs and end-to-end acceptance pass.

Test official signature bootstrap/update/reload, worker restart, secret rotation, clean and rejected documents and an isolated Oracle-unavailable path. Do not stop the shared Oracle service merely to run a test. Retire old scanner/OCR services only after a separate approved cutover and recovery check. Rollback returns to a verified scanner, never a scan-disabled mode.

## Test scope

Boundary tests use synthetic dependency fixtures. Native OCR uses generated PNG/PDF files. Native clamd uses a harmless custom signature to prove transport and detection and rejects unknown freshness. It does not prove official signature-feed freshness. No real accounts, tenant secrets or user documents enter CI. Builds/tests run only on disposable GitHub runners; this PR publishes no image or release and deploys nothing.

References: https://docs.clamav.net/manual/Installing/Docker.html ; https://docs.clamav.net/manual/Usage/Scanning.html ; https://tesseract-ocr.github.io/tessdoc/InputFormats.html ; https://learn.microsoft.com/en-us/azure/container-apps/containers .
