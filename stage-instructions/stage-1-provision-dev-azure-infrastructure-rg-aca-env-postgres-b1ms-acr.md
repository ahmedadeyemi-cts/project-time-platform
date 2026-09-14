# Stage 1: Provision DEV Azure infrastructure (RG, ACA env, Postgres B1ms, ACR Basic)

- **Type:** chore
- **Depends on:** none

## Objectives

Stand up the single-region, scale-to-zero Azure footprint defined in ADR 0001
(`docs/adr/0001-...md`): a resource group, an ACA (Container Apps) environment with
managed ingress, a Burstable B1ms PostgreSQL Flexible Server, and a Basic-tier ACR —
nothing else. This is pure infrastructure; no application code is deployed yet.

**Confirmed by verification (2026-09-14):** no existing IaC covers this shape.
`deployment/azure/scripts/az01`–`az12a*` are ~80 imperative `az` CLI scripts hardcoded
to the team's two-region HA production topology (VNETs, NAT gateway, cross-region
Postgres replica, App Gateway+WAF) — there is nothing to parameterize down. This
stage writes new scripts/IaC from scratch; it does not touch `az01`–`az12a*`.

## What to build

- New scripts under `deployment/azure/dev/` (mirror the existing repo's script-based
  style rather than introducing Bicep/Terraform as a second IaC tool, per ADR 0001's
  "boring, well-supported" lean) that create, in westus3:
  - Resource group (name convention: confirm with existing repo naming, e.g.
    `rg-pulse-dev`)
  - ACA environment (managed/default ingress, no VNet)
  - PostgreSQL Flexible Server, Burstable B1ms, 32GB storage, public access + firewall
    rule scoped to the ACA environment's outbound IPs (not `0.0.0.0/0`)
  - ACR, Basic tier
  - All scripts idempotent (safe to re-run), following the existing repo's
    `apply-*` pattern of checking current state before creating
- **Before provisioning:** per ADR 0002's correction, confirm
  `deployment/azure/scripts/az00d-create-test-subscription-budget.sh` has been
  applied to the test subscription (or apply it) — this is the actual enforced
  budget alert, distinct from the read-only `az00c` report.
- Update `.verity/deploy-access.md` with the real resource group / ACA environment
  names once created (currently marked TBD).

## Interface contracts

- **Exposes:** the ACA environment, ACR login server, and Postgres FQDN/port that
  Stage 2 and Stage 3 deploy into. No frozen contract needed for this internal
  hand-off — record the actual resource names in `.verity/deploy-access.md` as the
  hand-off mechanism.
- **Consumes:** none (first stage).

## Testing requirements

No app-level tests (infrastructure-only stage). Validate via `az` CLI dry-run
(`az deployment group what-if` or equivalent `--what-if`/`--dry-run` flags on each
script where supported) before applying for real, and via `az resource list -g
<rg>` after applying to confirm the 4 resources exist with the expected SKUs.

## Acceptance conditions

- [ ] `az00d` budget alert confirmed active on the test subscription before any
      resource is created
- [ ] Resource group, ACA environment, Postgres Flexible B1ms, and ACR Basic all
      exist in westus3 under subscription `cd32baeb-...`
- [ ] Re-running the provisioning scripts against the already-provisioned
      environment is a no-op (idempotency confirmed)
- [ ] `.verity/deploy-access.md` updated with real resource names
- [ ] `az00c-test-subscription-cost-check.sh` run once post-provisioning to confirm
      spend is tracking toward the ~$30-45/mo target, not the $200 ceiling

## Pipeline test: NO
