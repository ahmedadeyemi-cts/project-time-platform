# Celar AI Oracle Runtime — GitOps Rebuild and Recovery

This directory is the desired-state source for the disposable Oracle Celar AI runtime used by Protected Test. The VM is replaceable infrastructure: configuration lives in GitHub, reproducible software/models are reinstalled, and dynamic state is backed up separately.

## Target

- `celarai.onenecklab.com`
- Ubuntu 24.04 LTS, ARM64 / Ampere
- 4 vCPU, ~11 GiB RAM, ~45 GiB root disk
- Public: SSH 22 restricted by OCI NSG; Caddy HTTPS 443
- Local only: Ollama `127.0.0.1:11434`, ClamAV `127.0.0.1:3310`, Celar gateway `127.0.0.1:8787`
- Structured/private specialist: `gemma3:4b`
- General reasoning/coding/tool-use specialist: `qwen3:4b-instruct`
- Fast general/multilingual/summarization fallback: `llama3.2:3b`
- Private embeddings: `embeddinggemma` / 768 dimensions
- OCR: `tesseract-5-eng`

Production is outside this package.

## Answer architecture

Ollama is the local execution runtime, not the source of truth.

1. **Pulse/connected-system facts:** the permission-scoped database, API, system tool, connector, or private RAG source is queried first. Models synthesize evidence; they must not invent internal records from model memory.
2. **Private documents:** ClamAV → extraction/OCR → private embeddings → retrieval/citations → private synthesis. Raw private documents do not become public-provider input just because a local model is unavailable.
3. **General knowledge:** the governed platform provider route remains available; time-sensitive claims require current authoritative evidence when freshness has not already been verified.
4. **Local structured work:** `gemma3:4b` → `qwen3:4b-instruct` → `llama3.2:3b`.
5. **Local general/reasoning work:** `qwen3:4b-instruct` → `llama3.2:3b` → `gemma3:4b`.

The platform target order remains **DeepSeek v4 → Celar AI → Claude → OpenAI → governed local template**. The Ollama models are specialists inside the Celar target; they are not extra external-provider slots. Local failover occurs only for runtime/server failures. A 4xx/policy refusal is terminal.

## Public runtime contract

Caddy is the only public application service. The gateway requires both the protected bearer token and `X-Pulse-AI-Privacy-Boundary: private_pulse_runtime_only`.

The exact public routes are:

- `GET /health`
- `POST /v1/chat/completions`
- `POST /v1/embeddings`
- `POST /v1/extract`
- `POST /v1/scan`

There is no training route and no external-escalation route. Raw prompt/document content is not written to application logs. OCR has bounded upload, page, decoded-pixel, raster, output, memory, file-size, temporary-storage, and execution-time limits.

## Recovery layers

1. **GitHub desired state:** packages, model manifest/routing, Caddy, gateway, systemd, firewall logic, maintenance, and acceptance checks.
2. **Encrypted Restic state backup:** external object storage when `/etc/celar-ai/backup.env` is configured; local daily rollback archives are also retained.
3. **OCI boot-volume backup:** scheduled whole-machine point-in-time recovery. GitHub remains authoritative after restore.

Ollama model blobs and ClamAV signatures are reproducible and are not treated as irreplaceable backup data. Staged recovery trees are excluded from later backups.

## One-time bootstrap after VM replacement

After DNS/NSG are attached to a fresh Ubuntu 24.04 ARM64 VM:

```bash
sudo apt-get update
sudo apt-get install -y git ca-certificates
sudo rm -rf /tmp/project-time-platform-bootstrap
git clone --depth 1 https://github.com/ahmedadeyemi-cts/project-time-platform.git /tmp/project-time-platform-bootstrap
sudo bash /tmp/project-time-platform-bootstrap/deployment/oracle-celar/bootstrap.sh
```

The bootstrap enables pull-based GitOps. GitHub-hosted runners do not need inbound SSH access.

The first bootstrap creates `/etc/celar-ai/gateway/runtime-token` when missing. The deployment never prints the value. Retrieve it only through secured administrative SSH and synchronize it with the protected GitHub `test` environment secret `PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN` before reconnecting Protected Test. Never commit or paste the token.

## Automatic maintenance

- GitOps reconciliation: every 5 minutes.
- Local/Restic state backup: daily.
- Ollama engine + every approved local model: weekly pull/update with engine and per-model rollback copies.
- Promotion requires direct model probes plus the complete HTTPS/auth/model-routing/embedding/ClamAV/OCR acceptance suite.
- FreshClam updates malware signatures automatically.
- Ubuntu unattended upgrades apply security updates.

## Restore

1. Restore/create the Oracle VM and attach the governed NSG/DNS.
2. Bootstrap from GitHub.
3. Configure external Restic credentials if used.
4. Stage `sudo /opt/celar-ai/deploy/restore.sh latest` when dynamic state must be recovered.
5. Review staged state before applying it.
6. Let GitOps reapply canonical configuration and Ollama/FreshClam rebuild reproducible artifacts.
7. Run `sudo /opt/celar-ai/deploy/health-check.sh`.
8. Synchronize the Oracle runtime token with the protected GitHub `test` environment secret.
9. Reconnect Protected Test only after the separate Azure-to-Oracle activation/acceptance workflow passes.
