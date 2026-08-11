# Celar AI Oracle Protected-Test Activation Evidence — 2026-08-11

## Outcome

The protected-Test Pulse API is running the exact merged Celar AI Oracle correction, and the Oracle-backed private model is active. The final live verifier confirmed the exact immutable API and web images, exact active revisions, source marker, HTTP health, Module 064 private-model availability, and Module 011 Oracle component configuration.

This evidence is for protected Test only. Production was not changed.

## Release lineage

| Pull request | Purpose | Merge or release commit |
|---|---|---|
| #600 | Oracle Celar AI protected-Test runtime contracts | `c5915f6714224e8f952521db142ce7502c316dc7` |
| #601 | Enterprise UI, governance, document, and module corrections | `3ce9f003dd1fa5195559623d96d08c55c2f36c34` |
| #602 | Protected-Test release controller for #600/#601 | `c9a0091452a2e966896a4b24ba68f6fb1b5c0076` |
| #603 | Module 083 Autonomous Control Plane | `08d7961017613143b679a08fca6cbdbdf99715bc` |
| #619 | Oracle embedding response compatibility | `0afe1cbe1be550484b96e93012480ad362661c54` |
| #627 | Celar AI runtime routing, DLP, and systemwide theme hardening | `016810826d122b400950e9463f4221c9a024be6f` |
| #631 | External HTTPS runtime policy correction | `eeaad4abe5b0f730f3f4e78ed6162a347fa0602c` |
| #632 | Same-origin Module 064 UAT correction | `0bdf2c739307fe605b1713164ad3a9fa3d5c4a21` |
| #634 | Canonical Pulse privacy-boundary headers for all private inference | `1b667086dbaffff61890ce36d5680dadc8349bfa` |
| #639 | Deterministic Module 064 Oracle readiness attestation | `b204b002286d2a9fe2958073c7d0a99168d75401` |
| #640 | Exact PR #639 API-only protected-Test controller | `ba4d4f7fe0ae784b3f9d781f2c739ff4a26112a5` |

Controller-only intermediate PRs #629, #635, and #637 preserved rollback and diagnostic evidence but are not the final active application source.

## Root causes corrected

### Private-boundary HTTP 403

The Oracle gateway accepted the canonical request contract:

```text
X-Pulse-AI-Privacy-Boundary: private_pulse_runtime_only
```

The older Module 064 and private-generation callers still used:

```text
X-Celar-AI-Private-Boundary: true
```

The gateway correctly rejected the legacy request with HTTP 403 `privacy_boundary_rejected`. PR #634 aligned both normal private generation and Module 064 attestation with the canonical Pulse contract while preserving bearer authentication, feature and correlation headers, and `X-Pulse-AI-External-Escalation: false`.

### Non-deterministic readiness response

After the HTTP contract was corrected, the model returned HTTP 200 and the exact model identity but did not reliably reproduce the content-derived token sequence used by the release-candidate SOW attestation. A safe diagnostic proved that a fixed identity-free readiness phrase was deterministic.

PR #639 therefore separated the two purposes:

- Module 064 ordinary provider readiness uses an exact fixed phrase and exact model identity.
- Release-candidate exact-SOW verification retains the content-derived private evidence challenge.

The two controls remain independent; the ordinary readiness probe does not weaken or replace exact-SOW release evidence.

## Final protected-Test runs

| Stage | Run | Result |
|---|---|---|
| Exact PR #639 API-only deployment and authenticated UAT | [31462401989](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/31462401989) | Success |
| Oracle Celar AI activation v2 | [31462607846](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/31462607846) | Success |
| Final read-only live-state verification | [31463174497](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/31463174497) | Success |

## Final active protected-Test state

### API

- Source marker: `b204b002286d2a9fe2958073c7d0a99168d75401`
- Image: `acrphdtest7825cc.azurecr.io/project-health-dashboard-api@sha256:b3c50fcd33a96ac477482bd5c500cc811bf91a1300f52f9bcb50fff93b85b2e5`
- Active revision: `ca-phd-test-api-westus3--caorv2-31462607846-1`
- Active revision count: `1`
- `/health`: HTTP `200`, payload status `healthy`

### Web

The Oracle correction was API-only and did not rebuild or replace the validated PR #627 web release.

- Image: `acrphdtest7825cc.azurecr.io/project-health-dashboard-web@sha256:205a1d0ee77bf56db3a5f3635c14cbfd2403da1469349dae007d43059fcbcf68`
- Active revision: `ca-phd-test-web-westus3--pr627web-31452398717-1`
- Active revision count: `1`

## Oracle component verification

The activation and final live verifier confirmed:

- Private inference configured and available.
- Model: `gemma3:4b`.
- Diagnostic: `readiness_phrase_and_model_verified`.
- Private embeddings configured with `embeddinggemma`.
- Oracle embedding response compatibility remains fail-closed and accepts the approved 768-dimensional response without logging vector values.
- OCR configured with `tesseract-5-eng`.
- Tesseract version reported by readiness: `5.3.4`.
- Malware scanning configured through the authenticated Celar HTTPS gateway.
- ClamAV clean-file validation passed.
- Private vector index configured as `projectpulse_postgresql_hybrid`.
- Oracle readiness endpoint reports `ready`.
- Raw document content logging reports `false`.
- Module 064 returned neither endpoint values nor bearer-token values.

## Module 011 private-document readiness

The Oracle components are active, but the complete private document processing runtime remains **partially ready**. This is a separate storage and operating-readiness phase, not an Oracle connectivity failure.

Verified active components:

- ClamAV gateway configured.
- OCR endpoint configured and accepted by endpoint policy.
- Embedding endpoint configured and accepted by endpoint policy.
- Private vector index configured.
- Required migrations already present.
- Lexical and embedding storage schemas available.

Remaining blockers:

- `PROJECTPULSE_UPLOAD_ROOT` currently resolves to an ephemeral location instead of a verified shared persistent mount.
- `PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT=true` cannot be asserted until that mount is verified across replicas.
- The private document worker is disabled.
- Automatic admission of authorized AI-eligible documents is disabled.
- The dedicated document service principal is not configured and authorized.
- No AI-authorized SOW or GSD is currently in the ready state.
- Ready document, active chunk, and embedded chunk counts remain zero.

These blockers must remain visible; Oracle activation must not be represented as complete private document ingestion readiness.

## Migration statement

No database migration was applied or reapplied by the PR #634 or PR #639 Oracle correction sequence. Existing Migration 083 and Module 083 state were left unchanged.

## Evidence artifacts

| Artifact | Artifact ID | SHA-256 digest |
|---|---:|---|
| `pr639-readiness-api-test-31462401989-1` | `9090326820` | `sha256:10bf503397c5ee7a7d85789e5a4144cfe8388b0b58e81be17e3416ef8c5bbb13` |
| `celar-ai-oracle-activation-v2-31462607846-1` | `9090361754` | `sha256:4b69e1f623f6fc6e5ef7201fc024589c97032bf2720f08bb10ebeffc74c732ec` |
| `live-celar-oracle-test-final-31463174497-1` | `9090537511` | `sha256:fa24398bb281331fa25f9061a56a718c9271f8d47f1323d94109406442149c39` |

## Security and infrastructure boundary

- No Oracle VM configuration was changed.
- No firewall or network rule was changed.
- No DNS or certificate was changed.
- No additional port was opened.
- Public ingress remains TCP 443 through Caddy.
- The gateway remains loopback-only on `127.0.0.1:8787`.
- Ollama remains loopback-only on `127.0.0.1:11434`.
- ClamAV remains loopback-only on `127.0.0.1:3310`.
- No secret value, session token, bearer token, prompt, source document, model output, or embedding vector was written to repository evidence.
- Production was untouched.

## Permanent and temporary controls

Retained permanent control:

- `.github/workflows/celar-ai-oracle-test-runtime-activation-v2.yml`

Removed after successful live verification:

- `.github/workflows/projectpulse-deploy-pr627-celar-ai-enterprise-test.yml`
- `.github/workflows/projectpulse-deploy-pr634-oracle-boundary-api-test.yml`
- `.github/workflows/retry-pr634-api-uat-oracle-v2.yml`
- `.github/workflows/deploy-pr639-readiness-api-test.yml`

The final diagnostic observer is closed without merge after its artifact is recorded.
