# Deploy access — Pulse Azure DEV Environment

**Target:** Azure Container Apps, US Signal test subscription (`cd32baeb-...`, tenant `535941da-...`), region westus3.

**How to reach it:**
- `az login` (interactive/device-code) as yourself — no stored secret.
- `az account set --subscription cd32baeb-...`
- Resource group / ACA environment names: defined by the Stage 1 provisioning
  scripts in `deployment/azure/dev/` (see `deployment/azure/dev/README.md`).
  These names are not live yet — an operator still needs to run the scripts
  against the subscription; this file records what will exist once they do:
  - Resource group: `rg-project-health-dashboard-dev-westus3`
  - ACA environment: `cae-phd-dev-westus3`
  - ACR (Basic): `acrphddev<subscription-derived suffix>`
  - PostgreSQL Flexible Server (Burstable B1ms): `pg-phd-dev-w3-<subscription-derived suffix>`

**Budget guardrail:** $200/mo cap on this subscription, warn $150 / critical $180 / emergency $195 — enforced by `deployment/azure/scripts/az00c-*` in this repo. Check current spend before large changes.

**If you don't have access:** ask the subscription admin, rpmcds12 (Ryan McDonald).

See `~/.verity/deployment-methods.md` (method id `azure-aca-dev`) for the canonical, cross-project version of this access info.
