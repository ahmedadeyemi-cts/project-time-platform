# AZ-05C2B1D — Blob DNS and Listing Ready; Original Restore Attempt Terminal

**Date:** 2026-07-12

## Verified network and storage path

The East US restore runner now resolves the storage account through the Blob private endpoint:

- storage FQDN: `stphdtest7825cc.blob.core.windows.net`
- private DNS target: `stphdtest7825cc.privatelink.blob.core.windows.net`
- private endpoint IP: `10.40.5.8`
- managed-identity storage token: success
- direct Blob REST list: HTTP 200
- source objects listed: 15
- AzCopy list: exit code 0

## Original restore attempt

The original managed Run Command:

`phd-restore-postgresql13-seed`

is now in a terminal `Failed` state.

The unique Blob probe confirmed that no original restore-related AzCopy, `pg_restore`, or `psql` process remained active.

## Safety state

The failed attempt had previously remained blocked at Blob source enumeration with zero downloaded files. Before a clean retry, final guest evidence must be collected to confirm:

1. the failure stage;
2. source and result file counts;
3. whether the target PostgreSQL database was touched;
4. whether any partial evidence exists locally;
5. the final AzCopy error details.

## Next action

Run:

`deployment/azure/scripts/az05c2b1e-collect-failed-restore-evidence.sh`

Do not update or rerun the failed managed Run Command. A clean retry must use a unique Run Command name, a new result prefix, and isolated attempt directories after the failure evidence is reviewed.
