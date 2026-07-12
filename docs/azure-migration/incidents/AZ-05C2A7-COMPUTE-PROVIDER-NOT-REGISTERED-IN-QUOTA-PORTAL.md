# AZ-05C2A7 - Compute Provider Not Registered in Quota Portal

## Date
2026-07-12

## Summary
The Azure Quotas portal displayed no quota rows and disabled **New Quota Request** for provider **Compute**. The banner stated that the selected provider was not registered for one or more selected subscriptions.

## Impact
- No quota request could be submitted from the Azure portal.
- No VM or other billable compute resource was created.
- The restore-runner deployment remains blocked until the Compute provider registration is completed and quota data becomes visible.

## Root cause
The subscription was registered for `Microsoft.Quota`, but the portal indicated that the selected Compute provider was not registered. The relevant provider for Azure virtual machines is `Microsoft.Compute`.

## Remediation
1. Verify provider states for `Microsoft.Compute` and `Microsoft.Quota`.
2. Register `Microsoft.Compute` when needed.
3. Wait until the provider state is `Registered`.
4. Refresh the Quotas page.
5. Filter to East US and submit the required VM-family quota request.

## Validation commands
```bash
az provider show \
  --namespace Microsoft.Compute \
  --query '{Provider:namespace,State:registrationState}' \
  --output table

az provider show \
  --namespace Microsoft.Quota \
  --query '{Provider:namespace,State:registrationState}' \
  --output table
```

Register Compute when its state is not `Registered`:

```bash
az provider register \
  --namespace Microsoft.Compute \
  --wait
```

## Security and cost
- Resource-provider registration creates no VM and starts no compute billing.
- Only the required provider is registered, consistent with least-privilege guidance.
