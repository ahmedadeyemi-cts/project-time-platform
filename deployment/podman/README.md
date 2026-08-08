# Celar AI OpenCloud Private Runtime

## Decision

The additional Azure private-runtime deployment is paused to avoid temporary infrastructure cost before OpenCloud migration. Do not configure placeholder private endpoints or bearer tokens in GitHub or Pulse.

The OpenCloud Test/UAT starting topology uses one private Linux VM. Ollama, the Tesseract OCR adapter, and ClamAV run as separate OCI/Podman containers on that host. A small gateway container terminates TLS and enforces bearer authentication for the HTTP services.

Pulse Web/API and PostgreSQL do not have to reside on the shared runtime VM.

## Files

- `compose.yml` - portable Podman Compose definition.
- `private-runtime.env.example` - non-secret deployment inputs and secret-reference names.
- `../environments/opencloud-template.yml` - architecture and activation-state contract.

## Test/UAT host baseline

- Rocky Linux or another supported enterprise Linux distribution.
- 8-16 vCPU.
- 32-64 GiB RAM.
- 250 GiB SSD for the initial model, signatures, working files, images, and logs.
- Private network address only.
- Podman with Compose support.
- GPU optional. Start Ollama with a small quantized model on CPU when functional validation matters more than latency.

## Runtime boundaries

- Ollama owns private inference and embeddings.
- Tesseract performs OCR only for documents whose native extraction is insufficient.
- ClamAV scans every document before extraction.
- The gateway owns HTTPS, bearer authentication, request-size limits, and private routing to Ollama and Tesseract.
- Persistent volumes isolate Ollama models, ClamAV signatures, OCR work files, and gateway configuration from replaceable containers.
- Raw SOW content never goes to Claude or OpenAI.

## Activation sequence

1. Provision the private OpenCloud VM and persistent storage.
2. Pin reviewed OCI image digests and fill `private-runtime.env` from the example.
3. Create the runtime bearer secret in the approved secret store; do not commit it.
4. Configure private DNS, firewall rules, TLS, and the Pulse source allowlist.
5. Start the containers and verify health, model availability, OCR, current ClamAV signatures, and a clean malware scan.
6. Configure Pulse with the private endpoints and secret references.
7. Reprocess the selected SOW, approve its active version, and verify citation-ready chunks.
8. Run the end-to-end FlowHive test and require a SOW-grounded plan with citations before enabling the feature.

## Production evolution

When concurrency or latency requires it, move Ollama to dedicated GPU-capable compute. Tesseract and ClamAV may remain together on CPU compute. Preserve the same private service contracts so Pulse configuration does not depend on physical placement.

## Fail-closed rule

Do not weaken the FlowHive evidence gate. If malware scanning, extraction/OCR, retrieval, approval, or citations are unavailable, FlowHive must not generate or save a generic plan.
