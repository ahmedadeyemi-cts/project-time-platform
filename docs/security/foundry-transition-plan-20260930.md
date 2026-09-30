# Azure/Microsoft Foundry transition: planning only

Intent: consider replacing the Oracle model host with Foundry while keeping ClamAV and Tesseract in Pulse-owned managed containers. No Azure VM, Foundry resource, endpoint, key, provider change or private-data transfer is created by this PR.

Scanning, OCR, health and scan receipts must not depend on Foundry or any other model provider. Inventory remaining Oracle inference, embeddings, Laya/classification and other consumers; moving document utilities does not automatically migrate those features.

## Separate integration decisions

Select the exact model/deployment, region, API, quota, context size, latency and cost using current documentation at implementation time. A catalog entry does not establish regional/subscription availability. Review data handling, retention and abuse-monitoring terms for the particular Azure-sold or partner offering. Do not assume universal zero retention or regional confinement.

Prefer managed identity and least privilege. Qualify private connectivity where supported, DNS, TLS and approved destinations. Preserve document visibility, current assignment authorization, approved AI use, citations, source freshness and audit receipts. Foundry is not automatically authorized to receive private documents.

Use a distinct model-provider identity, never impersonate celar_ai to satisfy an allowlist. Refusal and evidence-limited results remain explicit. Do not weaken current Oracle/private-runtime policy to activate this provider.

Embedding migration must record model/version/dimensions, build a separate index and reindex approved content before cutover. Equal dimensions do not imply compatible vector spaces. Preserve old/new index rollback and revocation propagation; never query Oracle vectors with an incompatible Foundry embedding model.

## Test gates

Off-by-default adapter; public answers; authorized cited private retrieval; SOW/WBS quality; refusals; unapproved destinations; stale/revoked documents; rate/quota failures; deadlines; content-free logs; prior-provider and matching-index restoration. Prove scanner/OCR remains available when the model path is unavailable.

Pulse local Super Administrator sign-in must not change or require Entra because a workload uses managed identity. Application accounts and workload authentication are separate. No provider migration or Production activation is authorized by this design.

References to recheck for the chosen model and region:
https://learn.microsoft.com/en-us/azure/foundry/how-to/configure-private-link
https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/openai/data-privacy
https://learn.microsoft.com/en-us/azure/container-apps/containers
