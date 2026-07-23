# AZ-05C2B1A — Restore Still Running; Read-only Diagnostic Added

**Date:** 2026-07-12

## Observed state

- Managed Run Command: `phd-restore-postgresql13-seed`
- Execution state: `Running`
- Exit code currently displayed: `0` but not terminal while execution remains Running
- Last visible output: PostgreSQL 16.14 client and AzCopy 10.32.4 installed
- No error text reported
- Restore submitter must not be rerun
- VM must not be deallocated while restore execution remains Running

## Diagnostic action

Added `deployment/azure/scripts/az05c2b1a-diagnose-running-restore.sh`.

The diagnostic is read-only and uses a separate managed Run Command to inspect:

- active AzCopy/PostgreSQL processes;
- live guest restore log size, timestamp, and tail;
- downloaded source file count and bytes;
- generated result file count and bytes;
- latest AzCopy warning log tail;
- validation summary when present.

It does not update, stop, delete, or resubmit the restore command.
