# Project Health Dashboard Azure Deployment

This directory contains the version-controlled scripts used to build the Project Health Dashboard Azure environment.

## Execution environments

- Run source-discovery scripts on the current Oracle Linux application host.
- Run Azure subscription and infrastructure scripts in Azure Cloud Shell using Bash.

## Execution order

1. `scripts/az01-source-discovery.sh`
2. `scripts/az02a-subscription-discovery.sh`
3. `scripts/az03-network-foundation.sh`
4. `scripts/az03b-public-ip-egress-foundation.sh`
5. `scripts/az04-shared-services.sh`
6. `scripts/az05a-storage-foundation.sh`
7. Future PostgreSQL, Container Apps, Application Gateway, Front Door, migration, and cutover scripts

## Safety

- Read each script before running it.
- Confirm the active Azure subscription with `az account show`.
- Scripts must stop on errors with `set -Eeuo pipefail`.
- Scripts should be rerunnable or clearly state when they are continuation-only.
- Never embed secrets in scripts or arguments that are committed to GitHub.
- Do not copy generated `.env` files or unredacted logs into this repository.
- Do not create Cloudflare DNS records until the referenced Azure listener is healthy.

## Generated files

Azure scripts use this local directory:

```text
$HOME/project-health-dashboard-azure/
├── config/
└── logs/
```

Generated files are deliberately outside the repository. Commit only sanitized status updates to `docs/azure-migration/STATUS.md`.

## Naming standard

- Full product name: `Project Health Dashboard`
- Short Azure prefix: `phd`
- Test environment suffix: `test`
- Primary region: `westus3`
- Secondary region: `eastus`

Do not create new resources with the retired ProjectPulse name.
