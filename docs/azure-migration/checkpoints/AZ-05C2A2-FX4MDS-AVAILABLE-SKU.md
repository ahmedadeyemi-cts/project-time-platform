# AZ-05C2A2: FX4mds Selected as the Available Rocky Linux 10 Restore-Runner SKU

## Status

Ready to execute as of 2026-07-12.

## Discovery result

The West US 3 subscription-level SKU diagnostic showed that the practical D-series and E-series sizes evaluated for the temporary Rocky Linux 10 restore runner are restricted for this subscription.

The only unrestricted x64 D/E/F candidate returned by the diagnostic was:

- SKU: `Standard_FX4mds`
- vCPUs: 4
- Memory: 84 GiB
- Architecture: x64
- Hyper-V generation: V2
- Premium I/O: supported

The diagnostic also confirmed:

- VM `vm-phd-test-db-migrate-w3` does not exist.
- NIC `nic-phd-test-db-migrate-w3` exists successfully.
- NIC private address is `10.30.7.4`.
- No public IP is attached to the NIC.

## Decision

Use `Standard_FX4mds` only as a short-lived migration VM because it is oversized for the 3.3 MB database export but is the available compatible x64 candidate in West US 3.

The deployment script must:

1. Verify that `Standard_FX4mds` remains unrestricted before creation.
2. Query and display the current Azure Linux pay-as-you-go retail rate.
3. Use the official RESF Rocky Linux 10 `10-base` image.
4. Reuse the existing private NIC.
5. Create no public IP.
6. Validate the Rocky Linux 10 x86-64-v3 baseline.
7. Validate private PostgreSQL DNS and TCP connectivity.
8. Grant only the managed-identity roles needed for Blob and Key Vault operations.
9. Deallocate the VM immediately after validation so compute charges stop while the restore step is prepared.

## Canonical continuation

`deployment/azure/scripts/az05c2a2-rocky10-fx4mds-restore-runner.sh`

## Required success marker

`PRIVATE ROCKY 10 POSTGRESQL RESTORE RUNNER READY AND DEALLOCATED`

## Cost-control note

The VM remains a temporary migration resource. The OS disk can continue to incur a small storage charge while deallocated. The VM, disk, NIC, and temporary managed-identity role assignments must be deleted after the restore results are preserved and validated.
