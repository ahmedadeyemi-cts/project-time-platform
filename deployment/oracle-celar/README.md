# Celar AI Oracle Runtime — GitOps Rebuild and Recovery

This directory is the desired-state source for the disposable Oracle Celar AI runtime used by protected Test. The VM is replaceable infrastructure: configuration lives in GitHub, reproducible software/models are reinstalled, and dynamic state is backed up separately.

## Current target

- Hostname: `celarai.onenecklab.com`
- Operating system: Ubuntu 24.04 LTS
- Architecture: ARM64 / Ampere
- Current capacity: 4 vCPU, ~11 GiB RAM, ~45 GiB root disk
- Public services: SSH 22 restricted by OCI NSG; Caddy HTTPS 443
- Local-only services: Ollama `127.0.0.1:11434`; ClamAV `127.0.0.1:3310`; Celar gateway `127.0.0.1:8787`
- Structured/private generation: `gemma3:4b`
- General reasoning/coding/tool-use specialist: `qwen3:4b`
- Fast general/multilingual/summarization fallback: `llama3.2:3b`
- Embeddings: `embeddinggemma` / 768 dimensions
- OCR: `tesseract-5-eng`

Production is outside this package.

## Celar answer architecture

Ollama is the local model runtime, not the source of truth. The platform answer path is capability- and evidence-driven:

1. **Internal Pulse or connected-system facts** — the owning database, permission-scoped API, system tool, connector, or private document/RAG source is queried first. Models synthesize retrieved evidence; they must not invent internal records from training memory.
2. **Private document questions** — malware scan, extraction/OCR, private embeddings, retrieval/citations, then private model synthesis. Raw private documents are not sent to a public model merely because a local model is unavailable.
3. **General public knowledge** — DeepSeek remains the platform primary route. The Celar target can answer with the local specialist portfolio before later configured provider fallbacks when the governed route reaches Celar. Current/time-sensitive facts require current authoritative evidence when the platform has not already verified them.
4. **Structured planning/output** — Gemma remains first locally because the existing protected contract is proven around `gemma3:4b`; Qwen3 and Llama are local runtime fallbacks only for server/model failure, never to bypass a safety/policy refusal.
5. **General reasoning, coding, explanation, troubleshooting, summarization** — Qwen3 is the first local specialist, followed by Llama 3.2 and Gemma.

The platform-wide governed target order remains **DeepSeek v4 → Celar AI → Claude → OpenAI → governed local template**. The three Ollama generation models are specialists *inside the Celar AI target*; they do not create three new external provider slots and do not weaken the final deterministic fallback.

## Public runtime contract

Caddy is the only public application service. It terminates TLS for `celarai.onenecklab.com` and proxies only to the localhost Celar gateway. The gateway requires both the protected bearer token and `X-Pulse-AI-Privacy-Boundary: private_pulse_runtime_only` for every endpoint.

The governed public endpoints are exactly:

- `GET /health`
- `POST /v1/chat/completions`
- `POST /v1/embeddings`
- `POST /v1/extract`
- `POST /v1/scan`

There is no training endpoint and no external-escalation route. Raw prompt/document content is not written to application logs. Chat generation is non-streaming for the protected gateway contract. Upload, OCR page/pixel/raster/output limits, request/response sizes, memory, temporary storage, and provider timeouts are bounded.

## Local specialist routing

`generationModel` remains `gemma3:4b` as the stable public compatibility contract. The gateway then selects an approved local specialist without changing what Pulse is configured to call:

- Structured JSON / SOW / FlowHive / Project Forge / closeout / timesheet work: `gemma3:4b` → `qwen3:4b` → `llama3.2:3b`.
- Plain/general/help/reasoning work: `qwen3:4b` → `llama3.2:3b` → `gemma3:4b`.

Local failover occurs only for server/runtime failures. A local 4xx/policy refusal is terminal and is not bypassed by trying another model.

## Recovery model

There are three recovery layers:

1. **GitHub desired state** — scripts, Caddy config, authenticated gateway, model portfolio/routing manifest, service definitions, firewall logic, and live acceptance checks.
2. **Encrypted state backup** — `backup.sh` can write encrypted Restic snapshots to external object storage once `/etc/celar-ai/backup.env` is configured. Local rotating archives are retained for quick rollback.
3. **OCI boot-volume backup** — configure a scheduled Oracle boot-volume backup policy as a whole-machine point-in-time layer. GitHub remains authoritative after a boot-volume restore.

Ollama model blobs and ClamAV signatures are deliberately not disaster-recovery data; they are reproducible via `ollama pull` and FreshClam. Staged recovery trees are excluded from later backups so recovery tests cannot recursively consume the ~45 GiB root disk.

## One-time bootstrap after VM replacement

After creating a fresh Ubuntu 24.04 ARM64 VM, point DNS at the new public IP, allow TCP 22 only from approved admin CIDRs, allow TCP 443, and connect by SSH. Then run:

```bash
sudo apt-get update
sudo apt-get install -y git ca-certificates
sudo rm -rf /tmp/project-time-platform-bootstrap
git clone --depth 1 https://github.com/ahmedadeyemi-cts/project-time-platform.git /tmp/project-time-platform-bootstrap
sudo bash /tmp/project-time-platform-bootstrap/deployment/oracle-celar/bootstrap.sh
```

The bootstrap installs the full desired state and enables pull-based GitOps. No GitHub-hosted runner needs inbound SSH access to Oracle.

The first bootstrap creates `/etc/celar-ai/gateway/runtime-token` if one is not already present. The deployment never prints the value. Retrieve it only from a secured administrative SSH session and place the same value in the protected GitHub `test` environment secret `PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN` before reconnecting Azure Protected Test. Never put it in a repository file, PR, issue, workflow output, chat, or email.

The runtime token is excluded from both local and Restic backups. Recovery credentials must live independently of the VM; a fresh rebuild can generate a new token and the protected environment secret is then rotated to match.

## GitOps behavior and live acceptance

`celar-gitops.timer` polls `origin/main` over outbound HTTPS and compares only `deployment/oracle-celar`. When that tree changes, the exact main commit is applied. The tree is recorded as applied only after the complete live check passes.

Acceptance requires:

- SSH/HTTPS firewall invariants and localhost-only Ollama/ClamAV/gateway listeners;
- valid Caddy HTTPS using the 443/TLS-ALPN boundary;
- unauthenticated/wrong-token 401 and missing-privacy-boundary 403;
- all three local generation models plus `embeddinggemma` installed;
- Qwen3 selected for the general route and Gemma selected for structured work;
- authenticated 768-dimensional embeddings;
- exact-byte clean-file ClamAV scanning;
- authenticated Tesseract OCR;
- bounded OCR pixels/raster memory/temp storage; and
- sufficient disk/RAM headroom.

The bearer secret is passed to health-test curl processes through root-only temporary config files, not process command-line arguments.

## Automatic maintenance

- `celar-gitops.timer`: checks GitHub every 5 minutes.
- `celar-backup.timer`: daily local snapshot plus encrypted external Restic when configured.
- `celar-ollama-update.timer`: weekly Ollama engine **and every approved local model** refresh. The previous engine and every model receive rollback copies; promotion requires direct generation/embedding tests and the complete authenticated live acceptance suite.
- Ubuntu `unattended-upgrades`: security patching.
- `clamav-freshclam`: malware-signature updates.
- Caddy updates arrive through normal Ubuntu package/security maintenance.

## External Restic backup

Copy `backup.env.example` to `/etc/celar-ai/backup.env`, root-owned mode `0600`, and populate an external Restic repository. Oracle Object Storage's S3-compatible interface is suitable; the scripts intentionally keep the backend generic.

Never commit `backup.env`, private SSH keys, Restic passwords, object-storage credentials, bearer tokens, TLS private keys, or API keys.

## Restore

1. Create/restore the Oracle VM, attach the governed NSG, and point `celarai.onenecklab.com` at it.
2. Bootstrap from GitHub.
3. Configure `/etc/celar-ai/backup.env` when external Restic backups are used.
4. Stage `sudo /opt/celar-ai/deploy/restore.sh latest` when dynamic state must be recovered.
5. Review staged state under `/var/lib/celar-ai/recovery/` before selectively applying it.
6. Let GitOps reapply canonical configuration and Ollama/FreshClam rebuild reproducible artifacts.
7. Run `sudo /opt/celar-ai/deploy/health-check.sh`.
8. Synchronize the Oracle runtime token with the protected GitHub `test` environment secret.
9. Reconnect Protected Test only after the independent Azure-to-Oracle acceptance workflow passes.

The restore script intentionally stages data instead of blindly overwriting the live OS.
