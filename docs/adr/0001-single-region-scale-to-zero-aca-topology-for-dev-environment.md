# 0001. Single-region scale-to-zero ACA topology for DEV environment

- **Status:** Accepted
- **Date:** 2026-09-14

## Context

`project-time-platform` ("Pulse") is a .NET 10 API + nginx-served React web app +
PostgreSQL 13 (DB ~31MB) monolith. Ahmed's team already committed to container-first
Azure as the production target: Azure Container Apps (ACA) + PostgreSQL Flexible
Server + ACR, documented in `docs/azure-migration/` as a two-region, production-HA
build (Front Door Premium+WAF, App Gateway WAF_v2 per region, zone-redundant +
cross-region-replica Postgres, RA-GZRS storage, geo-replicated Premium ACR) costing
an estimated $1,500-3,000+/mo (Azure Retail Prices API, westus3, PAYG, pulled
2026-09-09). That design is correct for production but is the wrong shape for a
personal/team DEV environment, and the team's test subscription has a hard $200/mo
budget cap (warn $150 / critical $180 / emergency $195).

The `stack-and-topology` guide recommends boring, well-supported managed services,
starting as a modular monolith, and proving a walking skeleton before feature work —
it does not mandate HA shape for non-production environments.

## Decision

Stand up a **scale-to-zero, single-region DEV environment**, target **~$30-45/mo**,
reusing the team's already-chosen ACA runtime rather than picking a different
platform:

- **Single region** (westus3), no Front Door / no Application Gateway — ACA's
  built-in managed ingress is sufficient for a DEV environment with no HA
  requirement.
- **PostgreSQL Flexible Server, Burstable B1ms** (~$12/mo + ~$4/mo for 32GB storage)
  instead of zone-redundant/cross-region-replica.
- **ACR Basic** (~$5/mo) instead of Premium geo-replicated.
- **ACA consumption plan, min-replicas=0** — the app scales to zero when idle,
  which is the primary cost lever versus the always-on production design.
- **Public access + firewall rules on the DB** (no VNet/NAT gateway) — acceptable
  for DEV, not for production.

## Alternatives considered

- **Mirror production's two-region HA design at smaller SKUs** — rejected: still
  carries the fixed cost of two regions, Front Door, and App Gateway even at the
  smallest available tier; doesn't hit the ~$30-45/mo target and provides HA
  guarantees a DEV environment doesn't need.
- **A different hosting platform entirely (e.g. App Service, AKS)** — rejected:
  the team has already standardized on ACA for this app in production; introducing
  a second platform for DEV would mean maintaining two sets of IaC/deploy tooling
  for no benefit, and violates the guide's "boring, well-supported" lean by adding
  novelty rather than removing it.
- **VNet-isolated DB with private endpoints even in DEV** — rejected for now: adds
  NAT gateway cost and complexity with no corresponding DEV-environment benefit;
  revisit if DEV ever handles sensitive data it doesn't today.

## Consequences

- **Easier:** DEV environment cost stays well under the $200/mo subscription budget
  even with headroom for the AI runtime (see ADR 0003); infra stays close enough to
  production's ACA/Postgres/ACR shape that lessons and IaC patterns transfer forward
  without a rewrite when it's time to harden toward production.
- **Harder:** DEV does not exercise HA, cross-region failover, or private
  networking, so those failure modes won't be caught until a later
  production-shaped environment (or a dedicated staging tier) is built. Public DB
  access means firewall rules must be kept tight and DEV must never hold real
  customer data.
- **Follow-on:** this ADR intentionally leaves the AI runtime (Ollama/Tesseract/
  ClamAV for FlowHive/SOW) out of scope — see ADR 0003.
