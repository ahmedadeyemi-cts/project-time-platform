# Infrastructure handoff: two OpenCloud development hosts

## Purpose and current delivery status

Provision infrastructure for Pulse (application plus PostgreSQL) and Celar AI (private processing services). This handoff defines the proposed deployment and build inputs before the servers exist. It is suitable for infrastructure planning now, but `readiness.json` lists unresolved prerequisites for a complete installation. Do not advertise the configuration ZIPs as containing runnable application images, business data or model weights.

## Server request

| Requirement | Pulse + PostgreSQL | Celar AI |
|---|---|---|
| Service DNS | pulse-dev.ussignal.cloud | celarai-dev.ussignal.cloud |
| OS starting point | Supported Ubuntu 24.04 LTS Linux VM | Supported Ubuntu 24.04 LTS Linux VM |
| Planning compute | 8 vCPU / 32 GiB RAM | 16 vCPU / 64 GiB RAM preferred; qualify smaller offerings |
| CPU architecture | linux/amd64 candidate build lane; confirm actual offering | linux/amd64 candidate build lane; source Oracle runtime is ARM64; qualify any ARM64/GPU variant separately |
| System disk | Separate from persistent business/model data | Separate from model/runtime data |
| Persistent capacity | Size from current DB + documents + growth + backup staging | 250 GiB initial planning allowance; size from locked models, rollback copies and growth |
| GPU | Not required for application | Optional for initial installation; performance acceptance decides production need |
| User access | Internet HTTPS 443, authenticated application | Internet-routable authenticated HTTPS gateway; only approved callers |
| Administration | VPN/management SSH | VPN/management SSH |

Sizing figures are estimates, not measured workload requirements or latency guarantees. No VM purchase or resource allocation is performed by this PR. The operator must reconcile the source runtime's models and concurrency with the selected capacity.

## Network and service boundaries

Pulse uses HTTPS 443 only at the edge. Its API and PostgreSQL have no host-published ports. The web/API networks preserve an internal backend path. The API initially has no external egress: do not enable the optional egress override until copied notification/billing/retry queues and integration settings have been reviewed. After that approved step, normal internet-facing sign-in and integrations can function without user VPN.

Celar also publishes only HTTPS 443. Gateway, Ollama and ClamAV remain on 127.0.0.1 inside a shared container network namespace. Do not change them to public 0.0.0.0 listeners to work around connectivity. Laya has no network and communicates through the controlled Unix socket. The edge namespace must be recreated together with all sharing containers during an update; do not replace only the namespace owner.

Permit required outbound HTTPS to the approved image registry, GitHub/Actions artifact services, signature/model sources and explicitly enabled business integrations. The actual allowlist belongs to the company network team. DNS and time synchronization must work. Validate OpenCloud network controls together with Docker forwarding rules, not only ufw. No public SSH or Docker API is needed for GitHub deployments.

## Certificates and hostnames

Reserve the new development names. Do not modify the existing onenecklab names during preparation. Install trusted certificate chains and private keys matching each new hostname through the approved secret channel. The supplied Caddy configuration uses manually provisioned TLS, disables its admin API, and does not request certificates or open HTTP port 80. Assign a certificate-renewal owner and a controlled edge restart process.

Pulse public-origin configuration is supplied explicitly. The exact Entra callback paths must be registered before sign-in testing. Review Teams allowed domains/manifest content, notification URLs, customer FlowHive links, connector callbacks, trusted proxies and cross-origin checks. Do not assume old links are rewritten by changing DNS. Current Celar hostname authorization remains a separate blocking software change.

## Durable storage ownership

Create the named external volumes in the Compose definition only after confirming the correct environment. The application must never mount production data for development testing. Do not point two independent PostgreSQL instances at the same writable data directory.

Pulse volumes: `pulse-dev-database`, `pulse-dev-uploads`, `pulse-dev-state`, `pulse-dev-keys`, `pulse-dev-edge-data`, `pulse-dev-edge-config`. Celar volumes: `celarai-dev-models`, `celarai-dev-signatures`, `celarai-dev-gateway-state`, `celarai-dev-laya-models`, `celarai-dev-laya-socket`, `celarai-dev-edge-data`, `celarai-dev-edge-config`.

Determine the actual PostgreSQL and API numeric identities from the selected images before setting directory ownership. Celar custom services and edges use UID/GID 10001; source API uses its base image's APP_UID. Mount permissions must permit only required read/write access. Do not solve permissions with recursive world-writable modes. All business files and crypto keys need restore-tested off-host backups. Named volumes alone are neither backups nor redundancy.

## Secure runtime material, provided separately

The download does not include secrets. Provision the exact approved runtime settings in `PULSE_LOCAL_DIR/pulse-runtime.env`; database owner and API passwords are separate secret files. Preserve keys needed to decrypt existing integration/provider records. Use the current active key source, not a newly guessed encryption password. API secret files must be readable by its actual runtime identity, not world-readable.

For Celar, provision a strong runtime bearer token and coordinate it securely with Pulse. Supply the reviewed Laya worker image and model/checkpoint assets; it must expose `/run/celar-laya/decision.sock`, revision `1c5edc17a7acd8701df6fc341c0d179f1c62c982`, the existing bounded contract and restricted socket permissions. The models mount convention in the candidate is `/opt/celar-laya/models`; adapt/validate the worker image against that convention before acceptance. Do not assume the legacy worker already uses it.

Do not put a live runtime env file, certificate private key, database dump or model credential in the source tree, GitHub release or general OneDrive handoff.

## Handoff back to the application owner

Return the host architecture/resources, working service DNS/TLS, approved SSH access procedure, exact image versions, volume/backup ownership, certificate renewal owner, and completed read-only preflight report. Provide the approved runner/deployment connection and image-pull access. A host that can browse GitHub is not automatically a configured Actions deployment runner.

Record pending items honestly: data baseline, hostname policy, Laya assets, secret provider, container maintenance, integration isolation, corporate deployment authority and target acceptance. No production traffic cutover is authorized by this document.

## Production redundancy later

Keep the two logical packages but qualify a separate production topology: load-balanced application instances, shared durable document storage, appropriate per-environment session/crypto-key coordination, a PostgreSQL replication/failover/backup design, coordinated background-job ownership and redundant AI capacity. Never create two unrelated writable databases and call them HA. Never activate two independent notification or billing workers against copied queues. Keep dev and production credentials and data separate.
