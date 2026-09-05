# Celar AI Oracle Runtime — GitOps Rebuild and Recovery

This directory is the desired-state source for the disposable Oracle Celar AI runtime used by protected Test. The VM should be treated as replaceable infrastructure: configuration lives in GitHub, reproducible software is reinstalled, models are re-pulled, and dynamic state is backed up separately.

## Current target

- Hostname: `celarai.onenecklab.com`
- Operating system: Ubuntu 24.04 LTS
- Architecture: ARM64 / Ampere
- Current capacity: 4 vCPU, ~11 GiB RAM, ~45 GiB root disk
- Public services: SSH 22 restricted by OCI NSG; Caddy HTTPS 443
- Local-only services: Ollama `127.0.0.1:11434`; ClamAV `127.0.0.1:3310`; Celar gateway `127.0.0.1:8787`
- Generation model: `gemma3:4b`
- Embedding model: `embeddinggemma` / 768 dimensions
- OCR model: `tesseract-5-eng`

Production is outside this package.

## Public runtime contract

Caddy is the only public application service. It terminates normal public TLS for `celarai.onenecklab.com` and proxies only to the localhost Celar gateway. The gateway requires both the protected bearer token and `X-Pulse-AI-Privacy-Boundary: private_pulse_runtime_only` for every endpoint.

The governed public endpoints are exactly:

- `GET /health`
- `POST /v1/chat/completions`
- `POST /v1/embeddings`
- `POST /v1/extract`
- `POST /v1/scan`

There is no training endpoint and no external-escalation route. Raw prompt/document content is not written to application logs. Chat generation is deliberately non-streaming for the protected gateway contract. OCR and upload sizes, page count, JSON request size, provider response size, and provider timeouts are bounded in `release.json`.

## Recovery model

There are three recovery layers:

1. **GitHub desired state** — scripts, Caddy config, authenticated gateway source, service definitions, model manifest, firewall configuration logic, and live acceptance checks. This is the primary rebuild mechanism.
2. **Encrypted state backup** — `backup.sh` can write encrypted Restic snapshots to external object storage once `/etc/celar-ai/backup.env` is configured. Local rotating archives are also retained for quick rollback.
3. **OCI boot-volume backup** — configure a scheduled Oracle boot-volume backup policy as a full-machine point-in-time recovery layer. GitHub remains authoritative for configuration even when a boot-volume image is restored.

Do not back up Ollama model blobs or ClamAV signatures as disaster-recovery data. They are reproducible and are restored by `ollama pull` and FreshClam.

## One-time bootstrap after a VM replacement

After creating a fresh Ubuntu 24.04 ARM64 VM, point DNS at the new public IP, allow TCP 22 only from approved admin CIDRs, allow TCP 443, and connect by SSH. Then run:

```bash
sudo apt-get update
sudo apt-get install -y git ca-certificates
sudo rm -rf /tmp/project-time-platform-bootstrap
git clone --depth 1 https://github.com/ahmedadeyemi-cts/project-time-platform.git /tmp/project-time-platform-bootstrap
sudo bash /tmp/project-time-platform-bootstrap/deployment/oracle-celar/bootstrap.sh
```

The bootstrap installs the full desired state and enables a pull-based GitOps timer. No GitHub-hosted runner needs inbound SSH access to Oracle.

The first bootstrap creates `/etc/celar-ai/gateway/runtime-token` if a token is not already present. The deployment never prints that token. Retrieve it only from a secured administrative SSH session and place the same value in the protected GitHub `test` environment secret `PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN` before reconnecting Azure Protected Test. Do not put the token in a repository file, issue, PR, workflow log, chat, or email.

The runtime token is intentionally excluded from local and Restic backup archives. Recovery credentials must live independently of the Oracle VM. If the VM is rebuilt without restoring the old token, a new token is generated and the protected GitHub environment secret must be rotated to match before Azure activation.

## GitOps behavior

`celar-gitops.timer` polls `origin/main` over outbound HTTPS. It compares only the Git tree for `deployment/oracle-celar`; unrelated application commits do not redeploy the VM. When that tree changes, the exact main commit is checked out into a temporary worktree and `deploy.sh` is executed. The applied tree and commit are recorded under `/var/lib/celar-ai` only after the complete health check passes.

The live health check proves more than process state. It requires:

- Oracle host firewall retains SSH and HTTPS rules;
- Ollama, ClamAV, and the Python gateway have localhost-only listeners;
- Caddy presents valid HTTPS for the governed hostname;
- unauthenticated and wrong-token requests return 401;
- the bearer token without the privacy-boundary header returns 403;
- authenticated health reports all private services/models ready;
- authenticated chat generation succeeds;
- authenticated 768-dimensional embeddings succeed;
- a clean-file ClamAV scan succeeds with exact byte count; and
- a generated image fixture passes the authenticated Tesseract OCR adapter.

A failed deployment is not recorded as the applied GitOps tree.

This pull model deliberately avoids exposing SSH to GitHub's changing hosted-runner address ranges.

## Automatic maintenance

- `celar-gitops.timer`: checks GitHub every 5 minutes.
- `celar-backup.timer`: creates a local state archive daily and, when Restic is configured, an encrypted external backup.
- `celar-ollama-update.timer`: weekly Ollama engine + approved model refresh with local rollback copies and post-update inference/embedding tests.
- Ubuntu `unattended-upgrades`: security patching.
- `clamav-freshclam`: malware-signature updates.
- Caddy package updates arrive through the normal Ubuntu package/security maintenance path.

## Approved models

The model names live in `release.json`. The current defaults are:

- generation: `gemma3:4b`
- embeddings: `embeddinggemma`

DeepSeek v4 remains the primary platform provider; this Oracle host is the private document-processing, retrieval, embedding, and local-fallback runtime.

## External Restic backup

Copy `backup.env.example` to `/etc/celar-ai/backup.env`, root-owned mode `0600`, and populate it with an external Restic repository and credentials. Oracle Object Storage's S3-compatible API is suitable, but the scripts intentionally keep the backend generic.

Never commit `backup.env`, private SSH keys, Restic passwords, object-storage credentials, bearer tokens, TLS private keys, or runtime tokens.

## Restore

On a new server:

1. create/restore the Oracle VM, attach the governed NSG, and point `celarai.onenecklab.com` at it;
2. bootstrap from GitHub;
3. configure `/etc/celar-ai/backup.env` if external Restic backups are used;
4. run `sudo /opt/celar-ai/deploy/restore.sh latest` when dynamic state needs recovery;
5. review the staged recovery tree under `/var/lib/celar-ai/recovery/` before selectively applying dynamic state;
6. let GitHub reapply canonical configuration and let Ollama/FreshClam restore reproducible artifacts;
7. run `sudo /opt/celar-ai/deploy/health-check.sh`;
8. synchronize the Oracle runtime token with the protected GitHub `test` environment secret; and
9. reconnect Protected Test only after the independent Azure-to-Oracle acceptance workflow passes.

The restore script intentionally stages data instead of overwriting a running system blindly.
