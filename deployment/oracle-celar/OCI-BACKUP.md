# Oracle Cloud backup layer for Celar AI

GitHub provides the canonical rebuild source, but Git alone is not a point-in-time image of the VM. Keep an OCI-native boot-volume backup policy in addition to the encrypted application-state backup.

## Required layers

### 1. Boot-volume backup

In Oracle Cloud, assign a scheduled backup policy to the boot volume attached to the Celar VM. Use a schedule and retention appropriate for protected Test; a practical starting point is daily backups plus longer-retained weekly copies.

The boot-volume layer protects the complete filesystem and is the fastest path to reproducing an exact point in time. It must not replace GitOps: after an OCI restore, run the GitOps health check so configuration converges back to the reviewed GitHub state.

### 2. Encrypted external state backup

`backup.sh` uses Restic when `/etc/celar-ai/backup.env` exists. Store the Restic repository outside the VM. Oracle Object Storage's S3-compatible interface is one option.

Backed-up state may include Celar runtime state, Caddy state, firewall history, ClamAV configuration, and system configuration evidence. Reproducible model blobs and virus signatures are deliberately excluded.

### 3. Local rotating archive

A small local `.tar.zst` archive is produced daily under `/var/backups/celar-ai`. This is for quick rollback of accidental local changes. It is not disaster recovery because it lives on the same boot volume.

## Restore sequence after deletion

1. Restore an OCI boot-volume image if an exact machine recovery is desired, or create a fresh Ubuntu 24.04 ARM64 instance.
2. Attach the expected NSG and public IP/DNS.
3. Run the GitHub bootstrap from `deployment/oracle-celar/bootstrap.sh`.
4. If rebuilding fresh, configure `/etc/celar-ai/backup.env` and stage the latest Restic snapshot with `restore.sh latest`.
5. Review staged dynamic state, then selectively apply only what is not reproducible from GitHub.
6. Pull approved Ollama models, refresh ClamAV signatures, and run `/opt/celar-ai/deploy/health-check.sh`.
7. Reconnect protected Test only after HTTPS gateway, malware, OCR, embedding, inference, and authentication checks pass.

## Secret handling

Do not put any of these in Git:

- SSH private keys;
- Oracle customer secret keys;
- Restic repository passwords;
- Celar bearer/runtime tokens;
- Caddy/TLS private keys;
- API keys.

Keep recovery credentials in a protected password/secret store independent of the Oracle VM so deletion of the VM does not delete the ability to restore it.
