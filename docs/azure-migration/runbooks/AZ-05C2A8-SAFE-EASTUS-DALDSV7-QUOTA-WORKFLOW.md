# AZ-05C2A8 - Safe East US Daldsv7 Quota Workflow

## Purpose

Avoid Azure portal filter ambiguity and avoid guessing a Microsoft.Compute quota resource name.

The canonical script:

- resolves `Standard_D2alds_v7` from the East US SKU catalog;
- reads all East US Microsoft.Compute quota records through the Microsoft.Quota REST API;
- matches the SKU family to exactly one quota resource name;
- performs discovery only by default;
- submits only when explicitly run with `--submit`;
- uses `--no-wait` and does not poll;
- never creates a virtual machine.

## Script

`deployment/azure/scripts/az05c2a8-eastus-daldsv7-quota-safe.sh`

## Discovery

Run without arguments. This mode makes no Azure change.

Expected final marker:

`EASTUS DALDSV7 EXACT QUOTA RESOURCE DISCOVERED`

The output includes:

- `SKU_FAMILY`
- `EXACT_QUOTA_RESOURCE_NAME`
- `CURRENT_QUOTA_LIMIT`
- `QUOTA_APPLICABLE`
- `QUOTA_DECISION`

## Submission

After discovery succeeds, run the same script with `--submit`.

The script re-runs discovery and submits only the exact matching quota record. It requests a limit of 2 vCPUs and exits without polling.

Expected marker:

`EASTUS DALDSV7 QUOTA REQUEST SUBMITTED`

## Safety

- No VM is created.
- No existing VM is changed.
- No network, database, storage, or Key Vault resource is changed.
- The default mode is read-only.
- An ambiguous or missing quota match stops the script without submitting.
