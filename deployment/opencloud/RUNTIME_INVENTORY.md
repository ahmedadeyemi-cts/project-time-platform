# Current-runtime inventory and recovered Laya reference

This is a preparation checklist, not a request to export credentials or a command to change the live servers. It separates information already recovered from information that must be verified against the current installation.

## Laya reference recovered from the earlier installation package

The prior `install-celar-laya-service.sh` attachment contains the original worker and local client source. It was located during this packaging review; it is not a tracked source file in the application repository. Its text is available, but its authorized raw-byte materialization was unavailable during this review, so no byte-identical vendor/worker archive has been claimed or published.

The reference implementation identifies:

- Model: `convaiinnovations/laya`.
- Checkpoint: `1c5edc17a7acd8701df6fc341c0d179f1c62c982`.
- Model manifest: `/var/lib/celar-laya/model-manifest.json`.
- Model directory: `/var/lib/celar-laya/models/1c5edc17a7acd8701df6fc341c0d179f1c62c982`.
- Worker/client: `/opt/celar-laya/local-service/worker.py` and `client.py`.
- Unix socket: `/run/celar-laya/decision.sock`.
- Original interpreter: Python 3.12; installation preflight pins Laya 0.3.4 and Transformers 4.57.6.
- Required checkpoint members: `model.safetensors`, `rl_agent_config.json`, `encoder/config.json`, `tokenizer/tokenizer.json`, and `tokenizer/tokenizer_config.json`.
- One resident CPU model, bounded request framing, nonblocking inference lock, explicit input-budget refusal, no execution of model-returned actions, and no external network fallback.

This earlier worker required systemd `NOTIFY_SOCKET`, used a managed runtime directory and stopped rather than entering an automatic restart loop on its inference deadline. The later socket-preparation operation may have changed its access permissions or implementation. Capture the current installed file hashes and unit/drop-in settings before treating the earlier source as an exact runtime replica.

The candidate Compose file's `/opt/celar-laya/models` mount is only an interface proposal for a separately qualified worker image, not the source worker's actual path. The implementation should preferably preserve the original `/var/lib/celar-laya` manifest/model layout, or explicitly adapt it with tests. Do not place the original worker in an ordinary container and assume its systemd readiness, memory limits, stale-socket cleanup or restart behavior will carry over automatically.

## Remaining Laya qualification

Recover or securely export only the required worker/client source and exact dependency inventory from the live installed environment, compare them with the earlier package and record SHA-256 hashes. Do not include environment values, tokens, customer texts or unrestricted logs. Record versions of Laya, Torch, Transformers and Safetensors plus their transitive dependency lock and platform-specific wheel hashes. A list of four package versions is not a complete lock file.

Provide the approved pinned model artifacts and provenance separately from the general infrastructure ZIP. The model manifest must match the checkpoint; file integrity and permitted redistribution require verification. No model downloads, library upgrades or live inference have been performed as part of preparing these handoffs.

Then build and test a container-native wrapper that retains the existing socket protocol, privacy boundary, request/output limits and model revision, with deliberate readiness, shutdown, deadline/restart and stale-socket behavior. Test the gateway against that real worker. Until then, `CELAR_LAYA_IMAGE` remains a required unresolved image dependency and the installation is not certified complete.

## Pulse information to capture

Record the installed application release, PostgreSQL version/extensions, database size, schema/migration ledger, active runtime role and its grants, storage roots and sizes, scheduled/hosted jobs, and the names and locations of secret providers. Keep secret VALUES out of this report. Identify which current encryption keys are needed to decrypt the restored records and transfer them only through the approved company secret channel.

Use an approved schema-only/sanitized baseline for initial development. A protected business-data migration is a later step with consistent document transfers and outbound queue isolation. Do not use an old repository SQL snapshot as proof of the current schema.

## OpenCloud information to confirm

The infrastructure team should return the selected CPU architecture and offering, persistent-storage sizes and ownership, HTTPS entry-point/certificate owner, VPN/SSH management path, permitted egress, backup destination, and company GitHub runner/registry ownership. These are installation facts, not reasons to delay configuration/package preparation. Keep internet-facing authenticated browser access separate from administrative SSH.
