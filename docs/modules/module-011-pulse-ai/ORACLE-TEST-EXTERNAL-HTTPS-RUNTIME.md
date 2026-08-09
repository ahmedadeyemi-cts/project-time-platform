# Celar AI Oracle Test external HTTPS runtime

## Scope

This is a temporary, protected-Test-only bridge from the Azure-hosted Pulse API to the Celar AI runtime on Oracle Cloud. It does not authorize Production, a browser client, Module 064 form editing, or direct public access to Ollama, ClamAV, or the Python gateway.

The verified Oracle boundary is:

- `celarai.onenecklab.com:443` — public Caddy HTTPS gateway;
- `127.0.0.1:8787` — Celar AI Python gateway;
- `127.0.0.1:11434` — Ollama;
- `127.0.0.1:3310` — ClamAV; and
- SSH TCP 22 restricted separately for administration.

## Where the endpoint URLs are added

The endpoint URLs are **not** entered on the Oracle VM, in DNS, in the Pulse frontend, or in the Module 064 provider form.

They are deployment-managed settings on the **Azure Test API Container App**. The guarded GitHub Actions workflow `.github/workflows/projectpulse-deploy-celar-ai-private-runtime-test.yml` applies them to the API revision as these environment variables:

| Capability | Azure Test API environment variable | Exact value |
|---|---|---|
| Inference | `PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT` | `https://celarai.onenecklab.com/v1/chat/completions` |
| Embeddings | `PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT` | `https://celarai.onenecklab.com/v1/embeddings` |
| OCR | `PROJECTPULSE_PRIVATE_OCR_ENDPOINT` | `https://celarai.onenecklab.com/v1/extract` |
| Malware scanning | `PROJECTPULSE_PRIVATE_MALWARE_SCAN_ENDPOINT` | `https://celarai.onenecklab.com/v1/scan` |
| Startup readiness | `PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_READINESS_ENDPOINT` | `https://celarai.onenecklab.com/health` |

Do not enter these URLs in Module 064. The Test integration is deployment-managed so a browser user cannot silently replace the exact host or bypass the IP pin.

## One protected value the operator must add

The five URLs are non-secret and are pinned in the deployment workflow. The Oracle gateway bearer token is the only value that must be supplied separately.

In GitHub, go to:

```text
Repository
→ Settings
→ Environments
→ test
→ Environment secrets
→ Add secret
```

Create this exact secret name:

```text
PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN
```

Its value is the existing Oracle token stored at:

```text
/etc/celar-ai/gateway/runtime-token
```

Retrieve it only through the secured SSH session. Do not paste it into chat, a GitHub variable, a repository file, workflow output, email, DNS, or Module 064. GitHub injects it into a short-lived Azure Container App secret; Pulse receives only `secretref:` environment bindings.

## Test-only security gate

The application refuses activation unless all of these agree:

- `PROJECTPULSE_ENVIRONMENT=test`;
- exact DNS host `celarai.onenecklab.com`;
- one expected public IPv4 address, initially `129.213.82.144`;
- standard HTTPS certificate validation;
- exact endpoint paths shown above;
- exact models `gemma3:4b`, `embeddinggemma`, and `tesseract-5-eng`;
- one exact host allowlist entry;
- one protected bearer token and one matching protected secret reference;
- explicit `ORACLE-TEST-...` approval reference;
- ClamAV scan attestation and signature evidence; and
- training disabled with no training endpoint or token.

The HTTP transport performs DNS verification immediately before each request and then connects only to the configured IPv4 address while TLS continues to validate the original hostname. Redirects, proxies, cookies, custom certificate validators, and `--insecure` behavior remain disabled.

Production cannot enable this mode. Any external-runtime variables left behind while the enable flag is false also fail closed, preventing a partial or ambiguous configuration.

## Manual deployment after merge

After the source PR is merged and the protected GitHub environment secret exists:

1. Open **Actions**.
2. Select **ProjectPulse Deploy Celar AI Oracle HTTPS Runtime to Test**.
3. Run it from `main`.
4. Enter the exact current `main` commit as `release_commit`.
5. Enter an approved reference matching `ORACLE-TEST-...`.
6. Enter `DEPLOY-CELAR-AI-ORACLE-RUNTIME-TO-TEST` as the confirmation.

The workflow performs live unauthenticated, incorrect-token, authenticated health, inference, embedding, clean-file malware, and OCR tests before changing Azure. It then deploys an immutable API image, applies only the protected Test API environment and secret bindings, validates Pulse-to-Oracle inference and readiness, and restores the prior API image, environment values, and secret references if any post-change gate fails.

No database migration, web deployment, Production mutation, Oracle infrastructure mutation, or public opening of ports 3310, 8787, or 11434 is included.

## Certificate-renewal boundary

Oracle TCP 443 remains publicly reachable because Caddy currently uses the TLS-ALPN ACME challenge on that port. Restricting 443 to Azure egress addresses would break automated certificate validation unless certificate issuance first moves to DNS-01 or another controlled renewal design.
