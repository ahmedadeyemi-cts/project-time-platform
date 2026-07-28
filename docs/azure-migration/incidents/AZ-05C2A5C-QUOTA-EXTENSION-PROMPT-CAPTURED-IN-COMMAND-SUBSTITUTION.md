# AZ-05C2A5C: Quota Extension Prompt Captured in Command Substitution

## Status

Resolved operationally on 2026-07-12.

## What happened

A short Cloud Shell quota-status command used `az quota show` inside a Bash command substitution before the Azure CLI `quota` extension was installed.

Azure CLI prompted interactively:

`The command requires the extension quota. Do you want to install it now?`

Because the prompt occurred inside `CURRENT_LIMIT="$(...)"`, the prompt text was captured as the variable value instead of producing a numeric quota limit. The subsequent quota-request status command also did not execute meaningfully because the extension was still unavailable.

The user also experienced a Firefox Cloud Shell disconnect near the end of the earlier run. The browser UI explicitly reported that Firefox default settings can cause early disconnects.

## Impact

- No VM was created.
- No compute billing started.
- No quota status was obtained from the affected command.
- The quota request may or may not already have been submitted by an earlier disconnected session.

## Root cause

1. The `quota` Azure CLI extension was not installed explicitly before invoking `az quota`.
2. Dynamic extension installation attempted to prompt interactively inside Bash command substitution.
3. Firefox Cloud Shell connectivity remained unstable.

## Resolution

Before any quota lookup or quota request:

```bash
az extension add \
    --name quota \
    --upgrade \
    --yes \
    --only-show-errors
```

Then run quota commands directly, outside command substitution, to confirm the extension and request status:

```bash
az extension show --name quota --query version --output tsv
```

Use the existing canonical short status script after the extension is installed:

`deployment/azure/scripts/az05c2a5c-check-eastus-daldsv7-quota.sh`

For browser stability during the remaining migration steps, use Microsoft Edge or Google Chrome with Cloud Shell opened directly rather than the Firefox embedded portal shell.

## Required next result

One of:

- `EASTUS DALDSV7 COMPUTE QUOTA READY`
- `EASTUS DALDSV7 QUOTA NOT READY`
