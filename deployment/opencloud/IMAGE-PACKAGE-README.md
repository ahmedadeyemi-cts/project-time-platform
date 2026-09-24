# Docker image packages — Linux AMD64 candidate

These archives contain real Docker images exported using `docker image save`, not only Dockerfiles or Compose instructions. Each image was loaded back into Docker and its identity rechecked in disposable CI. This corrects the earlier configuration-only handoff. The `PACKAGE-MANIFEST.json` in this package supersedes the older configuration-only manifest.

**This is a loadable software candidate, not a complete installation or migration release. Do not begin a cloud cutover or represent it as a replacement for every installed Oracle service.** See `readiness.json` for the outstanding installation requirements. No current environments have been changed.

## Contents

The Pulse package includes the built application API, built web frontend, PostgreSQL software, and Caddy HTTPS proxy. The application images derive from the exact `sourceCommit` in the manifest. PostgreSQL 16 Bookworm is the disposable packaging baseline, not an assertion that the current Azure database is version 16. Verify the deployed major version, extensions, schema, permissions and approved restoration plan before using it with business data.

The Celar AI package includes the built gateway and its native OCR/extraction/ClamAV tools, the upstream Ollama engine, and Caddy. The gateway image also provides the ClamAV daemon and signature updater invoked by Compose. **The separately qualified Laya worker image and all AI model weights are still absent.** The existing full Celar Compose definition intentionally refuses to start until its required Laya image is supplied. Do not remove that service or its health/privacy checks simply to make startup pass. The provided Ollama version is a recorded packaging candidate, not a verified match to the existing Oracle engine.

Upstream image tags are resolved during the CI build; their resulting registry digests and version-probe output are recorded under `evidence/`. The installation uses uniquely named, locally loaded release tags in `images.env`, verified against each image's configuration ID. These local image IDs are not registry manifest digests. Future CI rebuilds may resolve newer upstream bytes; use these exact archives and checksums for this candidate.

No database contents, customer document volume, live credentials, TLS private keys, model weights, or current host virtual environment is included. Operating-system dependencies and licenses supplied by the upstream software images remain within their image layers. No private sandbox fonts or unrelated host files are added.

## Verify and load — does not start the application

First verify the downloaded package ZIP against the separately provided SHA-256 checksum and obtain both through the authorized GitHub artifact or a trusted company distribution copy. Checksums detect a mismatch; they do not authenticate an untrusted download source.

Extract the package to a new directory on the intended Linux AMD64 Docker host. Python 3.11 or newer is required for these scripts (Ubuntu 24.04 supplies Python 3.12).

```sh
python3 load-images.py
python3 load-images.py --load
```

The first command verifies all packaged file and image-archive checksums. The second also checks the Docker server architecture, refuses conflicting release tags, loads the image archives, and verifies their resulting image IDs. Loading affects the selected Docker daemon's local image store only. It does not run any application containers, create volumes, restore a database, configure credentials, send billing, or deploy a release. Confirm your Docker context is the intended host before running it.

Individual `.tar.gz` files in `images/` are standard Docker-save archives and can also be imported using `docker image load --input images/<component>.tar.gz`. The loader is preferred because it checks their expected identities.

## Before starting services

The infrastructure team can use these actual images without compiling Pulse, but the remaining prerequisites must still be resolved: approved database baseline and role grants; volume creation/ownership and document recovery; protected configuration and decryption keys; certificates and sign-in callbacks; new Celar hostname authorization; real Laya worker/model provisioning; container-native maintenance/backup; and complete target acceptance.

The original `HANDOFF.md`, `RUNBOOK.md`, and `RUNTIME_INVENTORY.md` remain included. After those prerequisites are satisfied, supply the configuration environment file and then `images.env` so the exact loaded tags override example image values. An eventual offline-start configuration must prohibit implicit image pulling and continue validating expected image IDs. This package deliberately contains no automatic `compose up` shortcut while the full installation requirements remain unresolved.

Internet-facing authenticated HTTPS remains `pulse-dev.ussignal.cloud` and `celarai-dev.ussignal.cloud`; normal browser access does not require VPN. Administrative SSH remains through the approved VPN/management path. The initial API staging network blocks integration egress so a copied queue cannot send real business actions; restore and outbound-policy review must precede enabling normal external integrations.

## Release status and future updates

Actual archive export and Docker-load round trips are tested. Complete application/database startup, live AI/Laya inference, vulnerability qualification, source-to-target data parity, sign-in, recoverability, new-hostname policy, corporate runner/registry connectivity and OpenCloud acceptance remain unverified. All additional limits in `readiness.json` still apply. Development/production redundancy is not provisioned here.

The packaging PR remains separate from the current deployment. No registry push, production tag, corporate-main edit, mirror change, DNS change or live deployment is performed by the archive workflow. GitHub remains the intended ongoing build/deployment system after the company environment and registry are configured. Downloaded image packages can be copied to an approved OneDrive folder without changing their version or checksums.
