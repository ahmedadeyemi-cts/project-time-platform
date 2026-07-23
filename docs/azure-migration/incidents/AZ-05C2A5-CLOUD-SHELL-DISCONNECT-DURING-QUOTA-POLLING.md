# AZ-05C2A5: Cloud Shell Disconnect During Quota Polling

## Status

Resolved on 2026-07-13 by replacing the long-running quota workflow with separate submission and status-check scripts.

## What happened

The East US `StandardDaldsv7Family` quota-request script failed or disconnected the Azure Cloud Shell session while it waited for quota approval.

## Contributing factors

1. The original script polled quota status 40 times with 30-second sleeps, which could keep a Cloud Shell operation active for approximately 20 minutes.
2. Azure Cloud Shell uses a temporary per-session host and can time out after 20 minutes without interactive activity.
3. The original JSON helper supplied the Python program through standard input while also attempting to read quota JSON from that same standard-input stream. A successful quota response could therefore cause a JSON parsing failure.
4. Long-running polling was unnecessary because `az quota create` supports `--no-wait`.

## Impact

- The quota-request workflow was unreliable in browser-based Cloud Shell.
- No virtual machine was created by this failure.
- No compute billing began.
- Existing East US network, NAT, peering, and private DNS configuration remained unchanged.

## Resolution

Updated:

`deployment/azure/scripts/az05c2a5-request-eastus-daldsv7-quota.sh`

The updated script now:

- checks the current approved quota directly with an Azure CLI query;
- submits the quota request with `--no-wait`;
- performs only one short post-submission check;
- exits immediately when the request remains pending;
- treats an already-pending request as a safe continuation state;
- avoids the standard-input JSON parsing defect.

Added:

`deployment/azure/scripts/az05c2a5c-check-eastus-daldsv7-quota.sh`

The checker is read-only and completes in seconds. It reports either:

- `EASTUS DALDSV7 COMPUTE QUOTA READY`; or
- `EASTUS DALDSV7 QUOTA NOT READY`.

## Operational rule

Do not keep Azure Cloud Shell open while waiting for quota approval. Submit once, close or reconnect as needed, and run the short status checker later.
