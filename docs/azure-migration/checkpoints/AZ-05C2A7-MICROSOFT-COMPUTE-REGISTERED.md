# AZ-05C2A7 - Microsoft.Compute Registered

Date: 2026-07-12
Subscription: Azure subscription 1
Subscription ID: cd32baeb-7b71-4bc0-8ea3-9f23a50903fe

## Status

- `Microsoft.Quota`: Registered
- `Microsoft.Compute`: Registered
- The Azure Quotas portal blocker caused by an unregistered Compute provider is cleared.
- No VM or other billable compute resource was created by this registration step.

## Next action

1. Refresh or reopen Azure Portal > Quotas > Compute > My quotas.
2. Filter to East US.
3. Search for the Daldsv7 family quota associated with `Standard_D2alds_v7`.
4. Request a minimum limit of 2 vCPUs.
5. Optionally submit the West US 3 FXmds family request for 4 vCPUs as a backup path.
6. Do not start the Rocky Linux restore-runner deployment until one quota request is approved.
