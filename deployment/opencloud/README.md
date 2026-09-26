# Pulse / Celar AI OpenCloud deployment foundation

**Delivery type: configuration and build handoff, NOT a turnkey installable release.**

Two candidate Docker Compose bundles are provided: **Pulse + PostgreSQL**, and **Celar AI**. Infrastructure may use them now to plan and provision the two development hosts. Images, live database/model assets, secrets, cloud-host acceptance and production redundancy are not included in the downloadable configuration ZIPs. Read `readiness.json` before scheduling an installation or cutover.

## Confirmed architecture

| System | Development service name | Future production service name |
|---|---|---|
| Pulse, including PostgreSQL | https://pulse-dev.ussignal.cloud | https://pulse.ussignal.cloud |
| Celar AI | https://celarai-dev.ussignal.cloud | https://celarai.ussignal.cloud |

Normal Pulse users connect over internet HTTPS, without VPN. SSH administration uses the approved VPN/management path. Celar exposes only authenticated HTTPS capabilities; raw inference, database, scanning and Docker ports are not public. GitHub remains the deployment system. Retain development when production is provisioned; do not rename dev and promote its test data.

No changes to the existing Azure/Oracle service names, deployed applications, DNS, provider order, business rules, customer data, credentials, source mirror or corporate repository were made by this packaging work.

## Start here

1. Infrastructure: read `HANDOFF.md`, then run the read-only `python3 kit.py check-host` after creating the hosts.
2. Application owner: resolve every item in `readiness.json`; use `RUNBOOK.md` for baseline/data/secrets and operational acceptance.
3. Release engineer: read `GITHUB_DEPLOYMENTS.md`. Work from the exact reviewed Git checkout, not a OneDrive copy of source. The existing personal-to-work mirror remains unchanged.
4. Packaging validation from a Git checkout: `python3 deployment/opencloud/tests.py`, then `python3 deployment/opencloud/kit.py validate` (Docker Compose required). Validation creates no containers and pulls no images.
5. Create both configuration bundles: `python3 deployment/opencloud/kit.py bundle --output /safe/output/path`.
6. Build candidate images on disposable CI/build infrastructure with `python3 deployment/opencloud/build-images.py api --output /safe/evidence/path` (also `web` and `celar`). These commands build/test images but never publish them or deploy to a server.

There is deliberately no one-click production installer and no new live deployment workflow in this change. A green packaging check cannot authorize copied queues, authenticate the new Celar host, recover a missing model or certify a restored database.

## Package contents

`pulse/compose.pulse.yaml` groups HTTPS ingress, the existing frontend/API, PostgreSQL and a read-only minimum schema check. `pulse/api-entrypoint.sh` loads the database password from a secret file and rejects conflicting legacy connection strings. The application starts only after the minimum schema check succeeds. A fresh empty database will intentionally fail that check until an approved baseline and runtime-role grants have been supplied.

`celar-ai/compose.celar-ai.yaml` groups HTTPS ingress, the existing authenticated gateway with OCR, Ollama, ClamAV, signature updates, and the required Laya worker. Its gateway/Ollama/ClamAV containers share the edge's network namespace to preserve the existing localhost-only policy. Laya uses only its Unix socket and has no network. The gateway image packages the original code and bounded native extraction tools; it does not replace the missing installed Laya worker or pull model weights automatically.

Persistence uses externally owned, environment-specific volumes. Secrets and certificates are host-supplied. Nothing attaches to the Docker socket. Only HTTPS 443 is published. Compose is a single-host development topology, not an HA implementation.

## Important qualification findings

- The current external Celar transport explicitly permits `celarai.onenecklab.com` and an Oracle/Test approval format. Merely changing an environment hostname will not authorize OpenCloud or Laya. That policy must be adapted and tested separately without weakening existing protections.
- The Laya gateway adapter is tracked; the installed worker implementation/dependency lock/checkpoint asset is not. The Laya image variable is required rather than silently omitting classification.
- The migration collection is not an independently qualified empty-database replay. A known-good current baseline and explicit migration ledger are prerequisites.
- Oracle maintenance uses host systemd/reconciliation and is not made portable by copying its files. The new edge explicitly returns 503 for maintenance calls until the container-aware implementation is qualified. Existing Oracle maintenance is untouched.
- Pulse API has no integration egress in the initial staging topology. This prevents restored queues from contacting external systems. After queue review, use the separately reviewed egress override to permit normal SSO/integration traffic. This is a staging safeguard, not a requirement that end users use VPN.

## Evidence and limitations

The CI runs on GitHub-hosted disposable runners, with read-only repository permissions. It records exact source revisions, real image builds and narrowly described smoke tests. `localImageId` is Docker's local image/config ID, NOT a registry manifest digest. Never paste it into a deployment manifest as though it were a published digest.

A build report does not establish full live API behavior, all database migrations, malware-signature freshness, live inference, installed Laya accuracy, image vulnerability qualification, target CPU support beyond the tested architecture, backups, redundancy, or OpenCloud cutover readiness. Those results must remain pending or not-run until exercised.
