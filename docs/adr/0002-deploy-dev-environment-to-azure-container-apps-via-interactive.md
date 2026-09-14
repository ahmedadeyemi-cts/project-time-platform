# 0002. Deploy DEV environment to Azure Container Apps via interactive az login

- **Status:** Accepted
- **Date:** 2026-09-14

## Context

This workstream needs a deployment target for the DEV environment defined in
ADR 0001, and a way for the Release/Deploy Operator (`/verity:ship`) to reach it.
Ryan's personal Verity deployment catalog (`~/.verity/deployment-methods.md`) had
no configured methods — only shipped SSH examples. The actual target is not a new
choice: Ahmed's team has already standardized production on Azure Container Apps,
and Ryan has a US Signal Azure **test subscription** (`cd32baeb-...`, tenant
`535941da-...`) with a $200/mo budget cap earmarked for this.

## Decision

Deploy to **Azure Container Apps in the existing US Signal test subscription**,
region westus3, using **interactive `az login`** (device-code / browser) as the
access method — no stored secret or service principal for now. Registered in the
deployment catalog as method id `azure-aca-dev`. Per-app access details (subscription
ID, budget guardrail, admin contact) are recorded in this repo's
`.verity/deploy-access.md` (gitignored) with the canonical copy in the global
catalog.

## Alternatives considered

- **Service principal / federated credential from the start** — rejected for now:
  a DEV environment stood up and operated by one person doesn't need an unattended
  identity yet, and every stored credential is something to rotate and secure.
  Deferred until `/verity:ship` needs to run deploys from CI without a human present
  — at that point, add a *separate* deployment-method entry rather than reusing this
  interactive one, so the two access paths don't get conflated.
- **A different Azure subscription** (e.g. a brand-new one) — rejected: the test
  subscription already exists and is the one the team intends for non-production
  work.

## Consequences

- **Easier:** zero secret-management overhead for a solo DEV environment; anyone
  with tenant access and the right RBAC role can reach it the same way.
- **Harder:** deploys require a human at a terminal to `az login` — this environment
  cannot yet be deployed unattended from GitHub Actions or any other CI runner.
  That's an acceptable trade for a DEV environment but must be revisited before this
  pattern is reused for anything closer to production.

## Correction (2026-09-14, post-verification)

The original text of this ADR described `deployment/azure/scripts/az00c-*` as
"budget-guardrail tooling," implying automated enforcement. Verified against the
repo: `az00c-test-subscription-cost-check.sh` is explicitly **read-only reporting**
(its own header states it creates/updates/deletes no resources) — it prints
WARNING/CRITICAL/EMERGENCY/CEILING labels at $150/$180/$195/$200 based on Cost
Management data, but a human must act on the output; nothing stops spend
automatically. A separate script, `az00d-create-test-subscription-budget.sh`,
creates an actual Azure Budget/alert resource and was not previously referenced
here. **Action for the walking-skeleton stage:** confirm `az00d`'s budget/alert is
applied to the test subscription (or apply it) before provisioning DEV resources,
and treat `az00c` as a manual check to run periodically — not as a safety net that
fires on its own.
