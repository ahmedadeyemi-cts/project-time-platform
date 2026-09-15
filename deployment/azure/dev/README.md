# Pulse DEV Azure environment

Scripts in this directory provision the single-region, scale-to-zero DEV
environment defined in
[`docs/adr/0001-single-region-scale-to-zero-aca-topology-for-dev-environment.md`](../../../docs/adr/0001-single-region-scale-to-zero-aca-topology-for-dev-environment.md):
a resource group, an Azure Container Apps (ACA) environment, a PostgreSQL
Flexible Server (Burstable B1ms), and a Basic-tier Azure Container Registry
(ACR) — nothing else. No application code is deployed by these scripts.

This is a separate, new IaC surface from `deployment/azure/scripts/az01`
through `az12a*`, which build the team's two-region, production-HA topology
against the same subscription. Do not run the `az0N` scripts and the `devN`
scripts against overlapping resource groups.

Access to the target subscription is interactive `az login` per
[ADR 0002](../../../docs/adr/0002-deploy-dev-environment-to-azure-container-apps-via-interactive.md) —
there is no service principal or CI credential for this environment. That
means these scripts are meant to be run by a human at a terminal, not from
an agent or a pipeline.

## Before you run anything

1. **Confirm the budget alert is active.** Run
   `deployment/azure/scripts/az00d-create-test-subscription-budget.sh` (safe
   to re-run; it creates/updates one Azure Budget resource, no billable
   infrastructure). This is the actual enforced alert on the test
   subscription — distinct from `az00c`, which is a read-only report a human
   has to act on.
2. **Log in and select the subscription:**
   ```bash
   az login
   az account set --subscription cd32baeb-7b71-4bc0-8ea3-9f23a50903fe
   ```
   Every script in this directory re-checks this and fails loudly if the
   active subscription doesn't match, but it's worth setting explicitly
   first so you're not surprised by which tenant/account you're in.

## Running the scripts

Run these in order — `dev04` depends on the resource group from `dev01`
(all four scripts independently verify the resource group exists rather
than assuming order was followed):

```bash
bash dev01-resource-group.sh
bash dev02-container-apps-environment.sh
bash dev03-container-registry.sh
bash dev04-postgresql-flexible-server.sh
```

Each script:

- Checks whether its resource already exists in the expected shape before
  creating anything, and exits cleanly (not a no-op silently pretending to
  succeed — it prints "confirmed" and re-validates the shape) if it does.
- Fails loudly (non-zero exit, an `ERROR:` line) if a resource exists in an
  *unexpected* shape (wrong location, wrong SKU, etc.) rather than papering
  over the mismatch.
- Writes a timestamped log to `$HOME/project-health-dashboard-azure/logs/`
  and records the resource names/IDs it created into
  `$HOME/project-health-dashboard-azure/config/dev-environment.env`, mirroring
  the existing `az0N` scripts' generated-files convention
  (see `deployment/azure/README.md`).

Resources created (see `.verity/deploy-access.md` for the canonical
record once this stage has actually been run):

| Resource | Name |
| --- | --- |
| Resource group | `rg-project-health-dashboard-dev-westus3` |
| ACA environment | `cae-phd-dev-westus3` |
| ACR (Basic) | `acrphddev<subscription-derived suffix>` |
| PostgreSQL Flexible Server (Burstable B1ms) | `pg-phd-dev-w3-<subscription-derived suffix>` |

## Known limitation: the PostgreSQL firewall rule is broader than "ACA only"

`dev04-postgresql-flexible-server.sh` was asked to scope the Postgres
firewall to just the ACA environment's outbound IP(s). That turned out not
to be achievable as specified, and the script documents why in a comment
above the firewall-rule step — summarized here for whoever runs it:

- A Consumption-plan Container Apps environment **without a VNet** (which
  is what ADR 0001 calls for, to avoid NAT Gateway cost) has no fixed,
  per-environment outbound IP. Microsoft's own docs say outbound IPs "might
  change over time," and a dedicated static egress IP is only available if
  you add a workload-profile VNet + Azure NAT Gateway — the exact
  cost/complexity ADR 0001 rejected for DEV.
- The script instead creates the standard "allow all Azure services and
  resources within Azure" firewall rule (start IP = end IP = `0.0.0.0`),
  which is Microsoft's own documented pattern for this combination
  (Consumption ACA + PostgreSQL Flexible Server, no VNet). This is **not**
  the open-internet `0.0.0.0/0` range — the server still requires a valid
  password — but it is broader than "just this ACA environment": any
  Azure-hosted resource in any subscription can attempt a connection.

This is a real gap relative to the stage spec, not a shortcut taken
silently. Two ways to close it later, if it matters before this environment
handles anything sensitive:

1. Accept it as a documented DEV-only risk — ADR 0001 already accepts
   "public DB access... firewall rules must be kept tight" as a
   consequence of the no-VNet design.
2. Reopen ADR 0001 to add a workload-profile VNet + NAT Gateway, which
   reintroduces the cost/complexity that ADR explicitly avoided.

## No Key Vault in this stage

ADR 0001 scopes DEV to exactly resource group + ACA environment + Postgres
+ ACR — no Key Vault. `dev04` generates the Postgres administrator password
and stores it only in a local file:

```text
$HOME/project-health-dashboard-azure/config/dev-postgres-admin-password.txt
```

(`chmod 600`, never printed to stdout or the log). Before Stage 2/3 wire the
application up to this database, move this credential into a proper secret
store (Key Vault, or whatever this workstream ends up using) — don't leave
it as a bare file for longer than necessary.

## Verifying after you run the scripts

```bash
az resource list -g rg-project-health-dashboard-dev-westus3 --output table
```

Expect exactly four resources: the Container Apps environment, the
PostgreSQL Flexible Server, the ACR registry, and (if Azure surfaces it as
a separate resource in the list) the server's default database.

**Confirm idempotency:** re-run all four scripts a second time. Every one
should print "confirmed"/"existing ... confirmed" lines and exit 0 without
creating or changing anything. If a re-run tries to create a resource that
already exists, or errors out instead of confirming, that's a bug — file it
rather than working around it.

**Check spend:** once resources exist, run
`deployment/azure/scripts/az00c-test-subscription-cost-check.sh` to confirm
actual/forecast spend is tracking toward the ~$30-45/mo target from ADR
0001, not the $150/$180/$195/$200 warning/critical/emergency/ceiling
thresholds.

## Tearing down

There is no `dev-teardown.sh` yet. To remove everything from this stage:

```bash
az group delete --name rg-project-health-dashboard-dev-westus3 --yes
```

That also removes the ACA environment, ACR, and Postgres server in one
call. It does **not** remove `az00d`'s budget resource (which is
subscription-scoped and worth keeping regardless) or the local files under
`$HOME/project-health-dashboard-azure/`.
