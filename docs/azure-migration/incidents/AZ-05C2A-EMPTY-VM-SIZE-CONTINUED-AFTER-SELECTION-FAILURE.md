# AZ-05C2A: Empty VM Size Continued After Selection Failure

## Status

Resolved with continuation script on 2026-07-12.

## What happened

The initial Rocky Linux 10 restore-runner deployment successfully:

- verified all 15 PostgreSQL migration artifacts;
- verified the 3,341,746-byte dump;
- attached the West US 3 NAT Gateway to the management subnet;
- discovered and accepted the official RESF Rocky Linux 10 Marketplace image;
- created the private NIC without a public IP.

The VM-size selector did not find one of its narrowly approved v5/v6 D-series sizes. The embedded Python selector exited nonzero, but the Bash function then executed `rm -f` and returned that successful status. The calling command substitution therefore produced an empty `VM_SIZE`, and the script incorrectly continued to `az vm create --size ""`, which failed with a `NoneType` error.

## Impact

- No virtual machine was created.
- No public IP was created.
- The private NIC `nic-phd-test-db-migrate-w3` remains and is safe to reuse.
- Marketplace terms for `resf:rockylinux-x86_64:10-base` were accepted.
- The management subnet remains attached to the existing West US 3 NAT Gateway.

## Root cause

1. The approved VM-size list omitted v4 D-series sizes available to the subscription in West US 3.
2. The selector function did not preserve the embedded Python command's nonzero return status.
3. The deployment did not hard-stop on an empty `VM_SIZE` before creating the NIC and attempting VM creation.

## Resolution

Added:

`deployment/azure/scripts/az05c2a1-rocky10-restore-runner-continuation.sh`

The continuation:

- reuses the existing private NIC;
- expands the approved size list to D/E v4, v5, and v6 families;
- prioritizes `Standard_D2ds_v4` as a small compatible option;
- preserves selector failure status;
- refuses to continue when `VM_SIZE` is empty;
- dynamically selects the latest official RESF Rocky Linux 10 `10-base` image;
- validates Rocky Linux 10 and the x86-64-v3 glibc hardware capability after deployment;
- validates private PostgreSQL DNS/TCP and managed-identity token acquisition;
- writes the non-secret restore-runner configuration.

## Required success marker

`PRIVATE ROCKY 10 POSTGRESQL RESTORE RUNNER READY`
