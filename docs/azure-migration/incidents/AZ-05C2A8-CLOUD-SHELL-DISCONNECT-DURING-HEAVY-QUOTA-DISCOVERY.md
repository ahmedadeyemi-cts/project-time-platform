# AZ-05C2A8 - Cloud Shell Disconnect During Heavy Quota Discovery

## Date
2026-07-12

## Status
Open workaround; original heavy discovery workflow is not to be used in Cloud Shell.

## Summary
The `az05c2a8-eastus-daldsv7-quota-safe.sh --discover` workflow repeatedly disconnected the browser-hosted Azure Cloud Shell session before producing a result.

The script does not create a VM and was run in discovery mode only. No quota request or billable compute resource was created by the failed attempts.

## Technical cause and contributing factors
The workflow performs several comparatively long operations in one foreground process:

1. Queries Azure VM SKUs.
2. Reads the entire East US Microsoft.Quota collection.
3. Follows pagination for as many as 25 pages.
4. Performs multiple local Python parsing steps.

This exposed the already observed browser/Cloud Shell WebSocket instability. The script is executed as a child process, so its internal `exit` statements do not terminate the parent interactive Bash shell. The visible session closure is therefore treated as a Cloud Shell connection interruption rather than intentional shell termination by the script.

## Decision
Do not use the full quota-discovery script in Azure Cloud Shell.

Use short, independent commands instead:

1. Save `az vm list-usage --location eastus` to a local temporary JSON file.
2. Parse only quota rows containing `daldsv7`.
3. Use the exact returned `name.value` for a single `az quota show` request.
4. Submit a quota request only after the exact name and current limit are confirmed.

Each command must return to the prompt before the next command is run. No polling loops are permitted.

## Safety controls
- Discovery commands are read-only.
- No VM deployment is attempted until quota is approved.
- No quota request is submitted without displaying the exact Azure quota name first.
- No source files or existing Azure application resources are modified.

## Resume point
Run the lightweight East US usage discovery commands documented in the associated PR update. Capture the exact Daldsv7 quota row and then validate it with one `az quota show` call.
