# Celar AI Oracle Test external HTTPS runtime

## Scope

This is a temporary, protected-Test-only bridge from the Azure-hosted Pulse API to the Celar AI runtime on Oracle Cloud. It does not authorize Production, a browser client, Module 064 form editing, or direct public access to Ollama, ClamAV, or the Python gateway.

The rebuilt Oracle boundary is:

- `celarai.onenecklab.com:443` — public Caddy HTTPS gateway;
- public IPv4 pin `141.148.19.235`;
- `127.0.0.1:8787` — Celar AI Python gateway;
- `127.0.0.1:11434` — Ollama;
- `127.0.0.1:3310` — ClamAV; and
- SSH TCP 22 restricted separately for administration.

The Oracle GitOps bootstrap must already report `CELAR_ORACLE_BOOTSTRAP=PASS` before Protected Test is reactivated.

## Where the endpoint URLs are added

The endpoint URLs are **not** entered on the Oracle VM, in DNS, in the Pulse frontend, or in the Module 064 provider form.

They are deployment-managed settings on the **Azure Test API Container App**. The one-time guarded reactivation workflow `.github/workflows/celar-ai-oracle-test-runtime-reactivate.yml` applies them to the Test API revision as these environment variables:

| Capability | Azure Test API environment variable | Exact value |
|---|---|---|
| Inference | `PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT` | `https://celarai.onenecklab.com/v1/chat/completions` |
| Embeddings | `PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT` | `https://celarai.onenecklab.com/v1/embeddings` |
| OCR | `PROJECTPULSE_PRIVATE_OCR_ENDPOINT` | `https://celarai.onenecklab.com/v1/extract` |
| Malware scanning | `PROJECTPULSE_PRIVATE_MALWARE_SCAN_ENDPOINT` | `https://celarai.onenecklab.com/v1/scan` |
| Startup readiness | `PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_READINESS_ENDPOINT` | `https://celarai.onenecklab.com/health` |

The normal `.github/workflows/projectpulse-deploy-test.yml` controller continues to preserve the private-runtime binding after reactivation. The deferred OpenCloud workflow remains non-mutating.

Do not enter these URLs in Module 064. The Test integration is deployment-managed so a browser user cannot silently replace the exact host or bypass the IP pin.

## Protected token placement

The five URLs are non-secret. The Oracle gateway bearer token is the one protected value the operator must synchronize separately.

In GitHub, use **Settings → Environments → test → Environment secrets** and create or update this exact secret:

```text
PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN
```

Its value must match the token stored on the rebuilt Oracle host at:

```text
/etc/celar-ai/gateway/runtime-token
```

Retrieve it only through the secured SSH session. Do not paste it into chat, a GitHub variable, a repository file, workflow output, email, DNS, or Module 064. GitHub injects it into a unique Azure Container App secret for the reactivation run; Pulse receives only `secretref:` bindings.

## Test-only security gate

The application refuses activation unless all of these agree:

- `PROJECTPULSE_ENVIRONMENT=test`;
- exact DNS host `celarai.onenecklab.com`;
- expected public IPv4 address `141.148.19.235`;
- standard HTTPS certificate validation;
- exact endpoint paths shown above;
- exact compatibility models `gemma3:4b`, `embeddinggemma`, and `tesseract-5-eng`;
- one exact host allowlist entry;
- one protected bearer token and matching protected secret provenance reference;
- explicit `ORACLE-TEST-...` approval reference;
- ClamAV scan attestation and live signature/version evidence; and
- training disabled with no training endpoint or token.

The HTTP transport performs DNS verification immediately before each request and connects only to the configured IPv4 address while TLS continues to validate the original hostname. Redirects, proxies, cookies, custom certificate validators, and insecure TLS behavior remain disabled.

Production cannot enable this mode. Any partial external-runtime configuration also fails closed.

## One-time Protected Test reactivation

After the source PR is merged, the Oracle bootstrap is green, and the protected GitHub environment secret has been synchronized:

1. Open **Actions**.
2. Select **ProjectPulse Reactivate Celar AI Oracle HTTPS Runtime in Protected Test**.
3. Run it from `main`.
4. Enter the exact current `main` commit as `release_commit`.
5. Enter an approved reference matching `ORACLE-TEST-...`.
6. Enter `REACTIVATE-CELAR-AI-ORACLE-RUNTIME-IN-PROTECTED-TEST` as the confirmation.

The controller first proves DNS equals `141.148.19.235`, then performs unauthenticated, incorrect-token, authenticated readiness, inference, strict 768-dimensional embedding, and clean-file malware tests **before** Azure mutation. It snapshots the current Test API image and relevant environment, builds an immutable Test API image from the exact current `main`, applies the private-runtime binding, and validates Pulse-to-Oracle private-model and document-runtime readiness.

If a post-change gate fails, the controller restores the prior Test API image and touched environment values and removes the run-specific Azure bearer secret. No database migration, web deployment, Production mutation, Oracle infrastructure mutation, or public opening of ports 3310, 8787, or 11434 is included.

After successful reactivation, the one-time controller should be retired again in a follow-up cleanup PR; normal Protected-Test releases must preserve rather than silently mutate this binding.

## Certificate-renewal boundary

Oracle TCP 443 remains publicly reachable because Caddy uses the TLS-ALPN ACME challenge on that port. Restricting 443 to Azure egress addresses would break automated certificate validation unless certificate issuance first moves to DNS-01 or another controlled renewal design.
